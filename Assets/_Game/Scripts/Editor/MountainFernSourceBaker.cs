using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;

namespace ValeMesozoico.Editor
{
    public static class MountainFernSourceBaker
    {
        private const string ModelPath = "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.fbx";
        private const string OutputPath = "Assets/_Game/Resources/Models/Environment/MountainFernSources.bytes";

        [Serializable]
        private sealed class SourceBake
        {
            public string bakeId = "mountain-fern-sources-v1";
            public BakedMesh[] sources;
        }

        [Serializable]
        private sealed class BakedMesh
        {
            public string templateId;
            public string meshName;
            public int vertexCount;
            public int triangleCount;
            public Vector3[] vertices;
        }

        [MenuItem("Vale Mesozoico/Environment/Bake Mountain Fern Sources")]
        public static void RunFromCli()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Gere as fontes de samambaias fora de Play Mode.");
            }

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
            {
                throw new FileNotFoundException("Biblioteca de vegetação ausente.", ModelPath);
            }

            List<BakedMesh> sources = new();
            int totalVertices = 0;
            int totalTriangles = 0;
            MeshFilter[] filters = model.GetComponentsInChildren<MeshFilter>(true);
            foreach (string templateId in new[] { "VM_ENV_021", "VM_ENV_029", "VM_ENV_031" })
            {
                MeshFilter filter = Array.Find(filters, candidate => candidate.name == templateId);
                if (filter == null || filter.sharedMesh == null)
                {
                    throw new InvalidOperationException("Template de samambaia ausente: " + templateId);
                }

                Mesh mesh = filter.sharedMesh;
                // Editor-only API reads imported data without enabling Read/Write
                // or keeping the complete environment vertex buffer on the CPU.
                using Mesh.MeshDataArray data = MeshUtility.AcquireReadOnlyMeshData(mesh);
                using NativeArray<Vector3> vertices = new(mesh.vertexCount, Allocator.Temp);
                data[0].GetVertices(vertices);
                int triangles = 0;
                for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
                {
                    if (mesh.GetTopology(submesh) != MeshTopology.Triangles)
                    {
                        throw new InvalidOperationException("Topologia inválida para " + templateId);
                    }
                    triangles += (int)mesh.GetIndexCount(submesh) / 3;
                }
                sources.Add(new BakedMesh
                {
                    templateId = templateId,
                    meshName = mesh.name,
                    vertexCount = mesh.vertexCount,
                    triangleCount = triangles,
                    vertices = vertices.ToArray()
                });
                totalVertices += mesh.vertexCount;
                totalTriangles += triangles;
            }

            File.WriteAllText(OutputPath, JsonUtility.ToJson(new SourceBake { sources = sources.ToArray() }));
            AssetDatabase.ImportAsset(OutputPath, ImportAssetOptions.ForceSynchronousImport);
            Debug.Log($"[Mountain Fern Source Bake] PASS | sources={sources.Count}, vertices={totalVertices}, triangles={totalTriangles}, output={OutputPath}");
        }
    }
}
