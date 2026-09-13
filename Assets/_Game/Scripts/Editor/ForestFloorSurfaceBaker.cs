using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico.Editor
{
    public static class ForestFloorSurfaceBaker
    {
        private const string ModelPath = "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.fbx";
        private const string ManifestPath = "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export.json";
        private const string OutputPath = "Assets/_Game/Resources/Models/Environment/ForestFloorSurface.bytes";
        private const string TemplateId = "VM_ENV_043";
        private const int Resolution = 129;
        private const float MaxGridJitterFraction = 0.02f;
        private const float SeamHeightTolerance = 0.001f;
        private const uint Magic = 0x53464D56;
        private const int Version = 1;

        [Serializable]
        private sealed class Manifest
        {
            public Template[] templates;
            public Instance[] instances;
        }

        [Serializable]
        private sealed class Template
        {
            public string id;
            public int vertexCount;
            public int triangleCount;
        }

        [Serializable]
        private sealed class Instance
        {
            public string template;
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        [MenuItem("Tools/Vale Mesozoico/Bake Forest Floor Surface")]
        public static void BakeFromCli()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Gere a superfície do chão fora de Play Mode.");
            }
            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null || !File.Exists(ManifestPath))
            {
                throw new FileNotFoundException("FBX ou manifest do ambiente ausente.");
            }
            Manifest manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(ManifestPath));
            Template template = manifest?.templates == null ? null
                : Array.Find(manifest.templates, item => item != null && item.id == TemplateId);
            Instance[] placements = manifest?.instances == null ? Array.Empty<Instance>()
                : Array.FindAll(manifest.instances, item => item != null && item.template == TemplateId);
            MeshFilter filter = Array.Find(model.GetComponentsInChildren<MeshFilter>(true), item => item.name == TemplateId);
            if (template == null || placements.Length != 1 || filter == null || filter.sharedMesh == null)
            {
                throw new InvalidDataException("É necessário um único terreno VM_ENV_043 com mesh e placement válidos.");
            }

            Mesh source = filter.sharedMesh;
            Instance placement = placements[0];
            Transform geometry = filter.transform;
            // Match ImportedBlenderEnvironment exactly, including its FBX mirror fix.
            Vector3 geometryScale = geometry.localScale;
            geometryScale.x = -geometryScale.x;
            Matrix4x4 transform = Matrix4x4.TRS(placement.position, placement.rotation, placement.scale)
                * Matrix4x4.TRS(geometry.localPosition, geometry.localRotation, geometryScale);
            Vector3[] vertices;
            List<int> triangles = new();
            using (Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(source))
            {
                using NativeArray<Vector3> nativeVertices = new(source.vertexCount, Allocator.Temp);
                data[0].GetVertices(nativeVertices);
                vertices = new Vector3[nativeVertices.Length];
                for (int index = 0; index < vertices.Length; index++)
                {
                    vertices[index] = transform.MultiplyPoint3x4(nativeVertices[index]);
                    if (!Finite(vertices[index].x) || !Finite(vertices[index].y) || !Finite(vertices[index].z))
                    {
                        throw new InvalidDataException("O terreno contém vértices não finitos.");
                    }
                }
                for (int submesh = 0; submesh < source.subMeshCount; submesh++)
                {
                    SubMeshDescriptor descriptor = data[0].GetSubMesh(submesh);
                    if (descriptor.topology != MeshTopology.Triangles)
                    {
                        throw new InvalidDataException("A superfície deve usar topologia de triângulos.");
                    }
                    using NativeArray<int> indices = new(descriptor.indexCount, Allocator.Temp);
                    data[0].GetIndices(indices, submesh, true);
                    foreach (int index in indices) triangles.Add(index);
                }
            }
            if (triangles.Count % 3 != 0 || triangles.Count / 3 != template.triangleCount
                || vertices.Length < 4 || triangles.Count < 6)
            {
                throw new InvalidDataException($"Contagens inválidas: vértices importados={vertices.Length}, "
                    + $"manifest={template.vertexCount}; índices={triangles.Count}, triângulos esperados={template.triangleCount}.");
            }
            foreach (int index in triangles)
                if (index < 0 || index >= vertices.Length)
                    throw new InvalidDataException($"Índice de vértice inválido no terreno: {index}.");
            Bounds bounds = new(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) bounds.Encapsulate(vertex);
            if (bounds.size.x <= 0.01f || bounds.size.z <= 0.01f)
            {
                throw new InvalidDataException("Bounds XZ inválidos para um heightfield.");
            }
            bool regular = TryRegularGrid(vertices, triangles, bounds, out float[] heights, out int uniqueVertices,
                out string gridRejection);
            if (!regular)
            {
                uniqueVertices = new HashSet<Vector3>(vertices).Count;
                Debug.LogWarning($"[Forest Floor Surface Bake] Grid fallback | reason={gridRejection}, "
                    + $"{DescribeGrid(vertices, bounds)}, uniquePositions={uniqueVertices}, "
                    + $"transformDeterminant={transform.determinant:G9}");
            }
            else
            {
                Debug.Log($"[Forest Floor Surface Bake] Verified grid | {DescribeGrid(vertices, bounds)}, "
                    + $"maxJitterFractionPerAxis={MaxGridJitterFraction}, seamHeightTolerance={SeamHeightTolerance}m");
            }
            if (uniqueVertices != template.vertexCount)
            {
                throw new InvalidDataException($"Contagem geométrica divergente: vértices importados={vertices.Length}, "
                    + $"posições únicas={uniqueVertices}, manifest={template.vertexCount}; "
                    + $"triângulos={triangles.Count / 3}/{template.triangleCount}.");
            }
            if (!regular) heights = RaycastGrid(vertices, triangles, bounds);
            foreach (float height in heights)
            {
                if (!Finite(height) || height < bounds.min.y - 0.001f || height > bounds.max.y + 0.001f)
                {
                    throw new InvalidDataException("Alturas amostradas inválidas; bake anterior preservado.");
                }
            }

            using MemoryStream buffer = new();
            using (BinaryWriter writer = new(buffer, System.Text.Encoding.UTF8, true))
            {
                // VMFS v1: seven 32-bit integers, six bounds floats, then row-major heights.
                writer.Write(Magic);
                writer.Write(Version);
                writer.Write(regular ? 1 : 2);
                writer.Write(Resolution);
                writer.Write(Resolution);
                writer.Write(vertices.Length);
                writer.Write(triangles.Count / 3);
                writer.Write(bounds.min.x);
                writer.Write(bounds.min.z);
                writer.Write(bounds.max.x);
                writer.Write(bounds.max.z);
                writer.Write(bounds.min.y);
                writer.Write(bounds.max.y);
                foreach (float height in heights) writer.Write(height);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllBytes(OutputPath, buffer.ToArray());
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Forest Floor Surface Bake] PASS | version={Version}, mode={(regular ? "verified-regular-grid" : "raycast")}, "
                + $"grid={Resolution}x{Resolution}, bounds={bounds}, sourceVertices={vertices.Length}, "
                + $"uniqueVertices={uniqueVertices}/{template.vertexCount}, seamDuplicates={vertices.Length - uniqueVertices}, "
                + $"sourceTriangles={triangles.Count / 3}, bytes={buffer.Length}, output={OutputPath}");
        }

        private static bool TryRegularGrid(Vector3[] vertices, List<int> triangles, Bounds bounds,
            out float[] heights, out int uniqueVertices, out string rejection)
        {
            heights = new float[Resolution * Resolution];
            uniqueVertices = 0;
            rejection = string.Empty;
            if (vertices.Length < heights.Length)
            {
                rejection = $"insufficient vertices: {vertices.Length}/{heights.Length}";
                return false;
            }
            bool[] occupied = new bool[heights.Length];
            int[] vertexCells = new int[vertices.Length];
            float stepX = bounds.size.x / (Resolution - 1);
            float stepZ = bounds.size.z / (Resolution - 1);
            // FBX compression perturbs XZ slightly; topology still has to prove a complete grid.
            float toleranceX = stepX * MaxGridJitterFraction;
            float toleranceZ = stepZ * MaxGridJitterFraction;
            for (int vertexIndex = 0; vertexIndex < vertices.Length; vertexIndex++)
            {
                Vector3 vertex = vertices[vertexIndex];
                float gridX = (vertex.x - bounds.min.x) / stepX;
                float gridZ = (vertex.z - bounds.min.z) / stepZ;
                int column = Mathf.RoundToInt(gridX);
                int row = Mathf.RoundToInt(gridZ);
                if (column < 0 || column >= Resolution || row < 0 || row >= Resolution
                    || Mathf.Abs(vertex.x - (bounds.min.x + column * stepX)) > toleranceX
                    || Mathf.Abs(vertex.z - (bounds.min.z + row * stepZ)) > toleranceZ)
                {
                    rejection = $"vertex outside grid or above the 2% cell jitter limit: "
                        + $"toleranceXZ=({toleranceX:G9},{toleranceZ:G9})";
                    return false;
                }
                int index = row * Resolution + column;
                vertexCells[vertexIndex] = index;
                if (occupied[index])
                {
                    // UV/normal seams split imported vertices without changing the surface.
                    if (Mathf.Abs(heights[index] - vertex.y) > SeamHeightTolerance)
                        throw new InvalidDataException($"Alturas conflitantes no grid ({column},{row}): "
                            + $"{heights[index]} e {vertex.y}.");
                    continue;
                }
                occupied[index] = true;
                heights[index] = vertex.y;
                uniqueVertices++;
            }
            if (uniqueVertices != heights.Length)
            {
                rejection = $"incomplete grid: {uniqueVertices}/{heights.Length} occupied cells";
                return false;
            }

            // Prove that each quad has exactly two complementary triangles, including across seams.
            int[] quadMasks = new int[(Resolution - 1) * (Resolution - 1)];
            for (int index = 0; index < triangles.Count; index += 3)
            {
                int a = vertexCells[triangles[index]];
                int b = vertexCells[triangles[index + 1]];
                int c = vertexCells[triangles[index + 2]];
                int minX = Mathf.Min(a % Resolution, Mathf.Min(b % Resolution, c % Resolution));
                int minZ = Mathf.Min(a / Resolution, Mathf.Min(b / Resolution, c / Resolution));
                int maxX = Mathf.Max(a % Resolution, Mathf.Max(b % Resolution, c % Resolution));
                int maxZ = Mathf.Max(a / Resolution, Mathf.Max(b / Resolution, c / Resolution));
                if (maxX - minX != 1 || maxZ - minZ != 1)
                    throw new InvalidDataException($"Triângulo {index / 3} não pertence a uma célula regular do terreno.");
                int cornerA = a % Resolution - minX + 2 * (a / Resolution - minZ);
                int cornerB = b % Resolution - minX + 2 * (b / Resolution - minZ);
                int cornerC = c % Resolution - minX + 2 * (c / Resolution - minZ);
                int missingCorner = 15 ^ ((1 << cornerA) | (1 << cornerB) | (1 << cornerC));
                int quad = minZ * (Resolution - 1) + minX;
                if ((missingCorner & (missingCorner - 1)) != 0 || (quadMasks[quad] & missingCorner) != 0)
                    throw new InvalidDataException($"Triângulo degenerado ou duplicado na célula ({minX},{minZ}).");
                quadMasks[quad] |= missingCorner;
            }
            for (int index = 0; index < quadMasks.Length; index++)
                if (quadMasks[index] != 6 && quadMasks[index] != 9)
                    throw new InvalidDataException($"Cobertura incompleta ou sobreposta na célula "
                        + $"({index % (Resolution - 1)},{index / (Resolution - 1)}).");
            return true;
        }

        private static string DescribeGrid(Vector3[] vertices, Bounds bounds)
        {
            HashSet<float> uniqueX = new();
            HashSet<float> uniqueZ = new();
            float stepX = bounds.size.x / (Resolution - 1);
            float stepZ = bounds.size.z / (Resolution - 1);
            float maxErrorX = 0f;
            float maxErrorZ = 0f;
            foreach (Vector3 vertex in vertices)
            {
                uniqueX.Add(vertex.x);
                uniqueZ.Add(vertex.z);
                float snappedX = bounds.min.x + Mathf.Round((vertex.x - bounds.min.x) / stepX) * stepX;
                float snappedZ = bounds.min.z + Mathf.Round((vertex.z - bounds.min.z) / stepZ) * stepZ;
                maxErrorX = Mathf.Max(maxErrorX, Mathf.Abs(vertex.x - snappedX));
                maxErrorZ = Mathf.Max(maxErrorZ, Mathf.Abs(vertex.z - snappedZ));
            }
            return $"min={bounds.min.ToString("F5")}, max={bounds.max.ToString("F5")}, "
                + $"uniqueX={uniqueX.Count}, uniqueZ={uniqueZ.Count}, "
                + $"stepXZ=({stepX:G9},{stepZ:G9}), maxGridErrorXZ=({maxErrorX:G9},{maxErrorZ:G9})";
        }

        private static float[] RaycastGrid(Vector3[] vertices, List<int> sourceTriangles, Bounds bounds)
        {
            Mesh mesh = new()
            {
                name = "Forest Floor Surface Editor Probe",
                indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = vertices
            };
            GameObject probe = new("Forest Floor Surface Editor Probe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            bool previousBackfaces = Physics.queriesHitBackfaces;
            try
            {
                Physics.queriesHitBackfaces = true;
                int[] triangles = sourceTriangles.ToArray();
                int reversedTriangles = 0;
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]];
                    Vector3 b = vertices[triangles[index + 1]];
                    Vector3 c = vertices[triangles[index + 2]];
                    if (Vector3.Cross(b - a, c - a).y < 0f)
                    {
                        (triangles[index + 1], triangles[index + 2]) = (triangles[index + 2], triangles[index + 1]);
                        reversedTriangles++;
                    }
                }
                mesh.triangles = triangles;
                mesh.RecalculateBounds();
                MeshCollider surface = probe.AddComponent<MeshCollider>();
                surface.sharedMesh = mesh;
                Physics.SyncTransforms();
                float minStep = Mathf.Min(bounds.size.x, bounds.size.z) / (Resolution - 1);
                float edgeInset = Mathf.Min(0.005f, minStep * 0.01f);
                float retryInset = Mathf.Min(0.025f, minStep * 0.02f);
                Debug.Log($"[Forest Floor Surface Bake] Raycast | upwardWindingFlips={reversedTriangles}, "
                    + $"backfaces=true, edgeInset={edgeInset:G9}m, retryInset={retryInset:G9}m, "
                    + $"colliderMin={surface.bounds.min.ToString("F5")}, colliderMax={surface.bounds.max.ToString("F5")}");
                float[] heights = new float[Resolution * Resolution];
                for (int row = 0; row < Resolution; row++)
                {
                    for (int column = 0; column < Resolution; column++)
                    {
                        float x = Mathf.Lerp(bounds.min.x, bounds.max.x, column / (float)(Resolution - 1));
                        float z = Mathf.Lerp(bounds.min.z, bounds.max.z, row / (float)(Resolution - 1));
                        // Only boundary samples move inward, by at most 2.5 cm on retry.
                        Vector3 origin = new(Mathf.Clamp(x, bounds.min.x + edgeInset, bounds.max.x - edgeInset),
                            bounds.max.y + 3f, Mathf.Clamp(z, bounds.min.z + edgeInset, bounds.max.z - edgeInset));
                        if (!surface.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, bounds.size.y + 6f))
                        {
                            origin.x = Mathf.Clamp(x, bounds.min.x + retryInset, bounds.max.x - retryInset);
                            origin.z = Mathf.Clamp(z, bounds.min.z + retryInset, bounds.max.z - retryInset);
                            if (!surface.Raycast(new Ray(origin, Vector3.down), out hit, bounds.size.y + 6f))
                            {
                                throw new InvalidDataException($"Heightfield contém vazio na célula ({column},{row}), "
                                    + $"rayOrigin={origin.ToString("F5")}, distance={bounds.size.y + 6f:G9}, "
                                    + $"edgeInset={retryInset:G9}, backfaces=true.");
                            }
                        }
                        heights[row * Resolution + column] = hit.point.y;
                    }
                }
                return heights;
            }
            finally
            {
                Physics.queriesHitBackfaces = previousBackfaces;
                UnityEngine.Object.DestroyImmediate(probe);
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
