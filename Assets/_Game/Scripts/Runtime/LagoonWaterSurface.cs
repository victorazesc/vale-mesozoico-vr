using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValeMesozoico
{
    internal static class LagoonWaterSurface
    {
        private const string MeshName = "Lagoon Connected Water Surface";
        private const float GridStep = 1f;
        private const float BankOverlap = 0.16f;
        private const float WaveDepth = 0.7f;

        public static bool Rebuild(MeshRenderer renderer)
        {
            if (renderer == null || !renderer.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
                return Reject("Renderer ou malha original ausente.");
            if (filter.sharedMesh.name == MeshName) return true;

            ForestFloorSurface terrain = ForestFloorSurface.Load();
            if (terrain == null) return Reject("Bake do terreno indisponível.");
            Bounds originalBounds = renderer.bounds;
            Vector3 min = originalBounds.min;
            Vector3 max = originalBounds.max;
            float waterLevel = originalBounds.center.y;
            if (!Finite(min) || !Finite(max) || originalBounds.size.x < 0.01f || originalBounds.size.z < 0.01f
                || originalBounds.size.x > 64000f || originalBounds.size.z > 64000f
                || Mathf.Abs(renderer.localToWorldMatrix.determinant) < 0.000001f)
                return Reject("Bounds ou transformação da água inválidos.");

            int columns = Mathf.CeilToInt(originalBounds.size.x / GridStep);
            int rows = Mathf.CeilToInt(originalBounds.size.z / GridStep);
            int stride = columns + 1;
            long vertexCount = (long)stride * (rows + 1);
            if (vertexCount >= 65000) return Reject($"Grid excede o limite de vértices: {vertexCount}.");

            Vector3[] vertices = new Vector3[(int)vertexCount];
            Vector2[] waveMask = new Vector2[vertices.Length];
            float[] heights = new float[vertices.Length];
            Transform geometry = renderer.transform;
            for (int row = 0; row <= rows; row++)
            {
                for (int column = 0; column <= columns; column++)
                {
                    int index = row * stride + column;
                    float x = Mathf.Lerp(min.x, max.x, column / (float)columns);
                    float z = Mathf.Lerp(min.z, max.z, row / (float)rows);
                    if (!terrain.Sample(x, z, out Vector3 ground, out _) || !Finite(ground))
                        return Reject($"Terreno indisponível no grid ({column},{row}).");
                    vertices[index] = geometry.InverseTransformPoint(new Vector3(x, waterLevel, z));
                    if (!Finite(vertices[index])) return Reject("Transformação produziu vértices inválidos.");
                    waveMask[index] = new Vector2(Mathf.Clamp01((waterLevel - ground.y) / WaveDepth), 0f);
                    heights[index] = ground.y;
                }
            }

            bool[] eligible = new bool[columns * rows];
            bool[] connected = new bool[eligible.Length];
            int seed = -1;
            int eligibleCount = 0;
            float nearestSeed = float.PositiveInfinity;
            Vector2 lakeCenter = new(38f, 28f);
            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int vertex = row * stride + column;
                    float lowestCorner = Mathf.Min(Mathf.Min(heights[vertex], heights[vertex + 1]),
                        Mathf.Min(heights[vertex + stride], heights[vertex + stride + 1]));
                    int cell = row * columns + column;
                    if (lowestCorner > waterLevel + BankOverlap) continue;
                    eligible[cell] = true;
                    eligibleCount++;
                    // Start in genuinely submerged terrain; the overlap strip must not seed a dry basin.
                    if (lowestCorner > waterLevel) continue;
                    float x = Mathf.Lerp(min.x, max.x, (column + 0.5f) / columns);
                    float z = Mathf.Lerp(min.z, max.z, (row + 0.5f) / rows);
                    float distance = (new Vector2(x, z) - lakeCenter).sqrMagnitude;
                    if (distance >= nearestSeed) continue;
                    nearestSeed = distance;
                    seed = cell;
                }
            }
            if (seed < 0) return Reject("Nenhuma célula submersa encontrada para o lago.");

            Queue<int> pending = new();
            pending.Enqueue(seed);
            connected[seed] = true;
            int connectedCount = 0;
            while (pending.Count > 0)
            {
                int cell = pending.Dequeue();
                connectedCount++;
                int column = cell % columns;
                int row = cell / columns;
                if (column > 0) Visit(cell - 1);
                if (column + 1 < columns) Visit(cell + 1);
                if (row > 0) Visit(cell - columns);
                if (row + 1 < rows) Visit(cell + columns);
            }

            List<int> triangles = new(connectedCount * 6);
            for (int cell = 0; cell < connected.Length; cell++)
            {
                if (!connected[cell]) continue;
                int a = (cell / columns) * stride + cell % columns;
                int b = a + 1;
                int c = a + stride;
                int d = c + 1;
                // Upward world-space winding. InverseTransformPoint preserves the final world winding even with mirror X.
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }
            if (triangles.Count == 0) return Reject("A superfície conectada não contém triângulos.");

            // Keep the original bounds: the reflection probe and submerged-ground
            // material use them. Only vertices unreferenced by triangles are removed.
            Bounds surfaceBounds = new(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices) surfaceBounds.Encapsulate(vertex);
            surfaceBounds.Expand(0.12f);
            int[] remap = new int[vertices.Length];
            Array.Fill(remap, -1);
            List<Vector3> compactVertices = new(vertices.Length);
            List<Vector2> compactWaveMask = new(vertices.Length);
            for (int index = 0; index < triangles.Count; index++)
            {
                int source = triangles[index];
                if (remap[source] < 0)
                {
                    remap[source] = compactVertices.Count;
                    compactVertices.Add(vertices[source]);
                    compactWaveMask.Add(waveMask[source]);
                }
                triangles[index] = remap[source];
            }

            Mesh mesh = null;
            try
            {
                mesh = new Mesh { name = MeshName };
                mesh.SetVertices(compactVertices);
                mesh.SetUVs(1, compactWaveMask);
                mesh.SetTriangles(triangles, 0, false);
                mesh.bounds = surfaceBounds;
                // Release the player copy; editor geometry QA can still build temporary colliders.
                mesh.UploadMeshData(!Application.isEditor);
                filter.sharedMesh = mesh;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException
                || exception is UnityException)
            {
                if (mesh != null) UnityEngine.Object.Destroy(mesh);
                return Reject("Falha ao gerar a superfície: " + exception.Message);
            }
            Debug.Log($"[Lagoon Water Surface] PASS | vertices={compactVertices.Count}/{vertices.Length}, triangles={triangles.Count / 3}, "
                + $"waterLevel={waterLevel:F3}, cells={connectedCount}/{eligibleCount}, "
                + $"grid={columns}x{rows}, disconnectedCells={eligibleCount - connectedCount}, "
                + $"vertexBytes={compactVertices.Count * 20}/{vertices.Length * 40}, cpuMeshCopy={mesh.isReadable}");
            return true;

            void Visit(int cell)
            {
                if (!eligible[cell] || connected[cell]) return;
                connected[cell] = true;
                pending.Enqueue(cell);
            }
        }

        private static bool Finite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x)
            && !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        private static bool Reject(string reason)
        {
            Debug.LogWarning("[Lagoon Water Surface] Original preservado: " + reason);
            return false;
        }
    }
}
