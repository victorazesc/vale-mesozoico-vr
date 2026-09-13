using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal static class ForestFloorCreeperMesh
    {
        internal static bool Create(out Mesh mesh, out Material material, out Vector3[] vertices,
            out Vector3 foot, out float height)
        {
            mesh = null;
            material = null;
            vertices = Array.Empty<Vector3>();
            foot = Vector3.zero;
            height = 0f;
            Texture2D texture = Resources.Load<Texture2D>("Textures/Realistic/JungleVineLeaves_Atlas");
            if (texture == null)
            {
                Debug.LogWarning("[Forest Floor] Atlas das folhas rasteiras ausente.");
                return false;
            }

            List<Vector3> points = new(240);
            List<Vector2> uvs = new(240);
            List<int> triangles = new(720);
            float[] angles = { -4f, 43f, 91f, 134f, 184f, 228f, 279f, 319f };
            float[] lengths = { 0.45f, 0.26f, 0.41f, 0.27f, 0.45f, 0.42f, 0.26f, 0.43f };
            float[] widths = { 0.28f, 0.18f, 0.25f, 0.20f, 0.27f, 0.26f, 0.18f, 0.26f };
            float[] tilts = { 12f, 43f, 18f, 55f, 14f, 21f, 60f, 16f };
            int[] atlasTiles = { 0, 2, 0, 3, 2, 0, 2, 3 };
            for (int leaf = 0; leaf < angles.Length; leaf++)
            {
                float angle = angles[leaf] * Mathf.Deg2Rad;
                Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                bool upright = tilts[leaf] > 35f;
                Vector3 origin = direction * (upright ? 0.035f : 0.085f)
                    + Vector3.up * (upright ? 0.024f : 0.015f);
                AddStem(Vector3.zero, origin);
                AddLeaf(origin, direction, lengths[leaf], widths[leaf], tilts[leaf], atlasTiles[leaf], leaf);
            }

            mesh = new Mesh { name = "Forest Floor Curved Creeper" };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            vertices = points.ToArray();
            height = mesh.bounds.max.y;
            material = ProceduralWorld.CreateFoliageMaterial("Forest Floor Creeper Leaves", texture);
            material.SetColor("_BaseColor", new Color(0.60f, 0.71f, 0.56f));
            material.SetFloat("_Smoothness", 0.10f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            return true;

            void AddLeaf(Vector3 origin, Vector3 direction, float length, float width,
                float tiltDegrees, int tile, int leaf)
            {
                const int rows = 4;
                const int columns = 3;
                int first = points.Count;
                int firstTriangle = triangles.Count;
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                float tilt = tiltDegrees * Mathf.Deg2Rad;
                Vector2 uvOrigin = new((tile % 2) * 0.5f, (tile / 2) * 0.5f);
                for (int row = 0; row < rows; row++)
                {
                    float t = row / (float)(rows - 1);
                    float arch = Mathf.Sin(t * Mathf.PI);
                    Vector3 spine = origin
                        + direction * (length * Mathf.Cos(tilt) * t - 0.025f * t * t)
                        + Vector3.up * (length * Mathf.Sin(tilt) * t + 0.028f * arch - 0.028f * t * t);
                    for (int column = 0; column < columns; column++)
                    {
                        float u = column / (float)(columns - 1);
                        float side = u * 2f - 1f;
                        float ridge = (1f - Mathf.Abs(side)) * width * 0.09f * arch;
                        float twist = side * t * 0.012f * (leaf % 2 == 0 ? 1f : -1f);
                        points.Add(spine + across * (side * width * 0.5f)
                            + Vector3.up * (ridge + twist));
                        uvs.Add(uvOrigin + new Vector2(0.004f + u * 0.492f, 0.004f + t * 0.492f));
                    }
                }
                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < columns - 1; column++)
                    {
                        int a = first + row * columns + column;
                        triangles.Add(a); triangles.Add(a + columns); triangles.Add(a + 1);
                        triangles.Add(a + 1); triangles.Add(a + columns); triangles.Add(a + columns + 1);
                    }
                }

                // Separate back vertices allow RecalculateNormals to light both
                // sides correctly without cancelling the folded leaf's normals.
                int count = rows * columns;
                for (int vertex = first; vertex < first + count; vertex++)
                {
                    points.Add(points[vertex]);
                    uvs.Add(uvs[vertex]);
                }
                int end = triangles.Count;
                for (int triangle = firstTriangle; triangle < end; triangle += 3)
                {
                    triangles.Add(triangles[triangle] + count);
                    triangles.Add(triangles[triangle + 2] + count);
                    triangles.Add(triangles[triangle + 1] + count);
                }
            }

            void AddStem(Vector3 start, Vector3 end)
            {
                const int sides = 3;
                const float radius = 0.003f;
                int first = points.Count;
                Vector3 tangent = (end - start).normalized;
                Vector3 across = Vector3.Cross(tangent, Vector3.up).normalized;
                Vector3 normal = Vector3.Cross(across, tangent).normalized;
                for (int ring = 0; ring < 2; ring++)
                {
                    for (int side = 0; side < sides; side++)
                    {
                        float angle = side * Mathf.PI * 2f / sides;
                        points.Add((ring == 0 ? start : end)
                            + (across * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius);
                        uvs.Add(new Vector2(0.25f, 0.25f));
                    }
                }
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    triangles.Add(first + side); triangles.Add(first + side + sides); triangles.Add(first + next);
                    triangles.Add(first + next); triangles.Add(first + side + sides); triangles.Add(first + next + sides);
                }
            }
        }

        internal static bool CreateGrass(out Mesh mesh, out Material material, out Vector3[] vertices,
            out Vector3 foot, out float height)
        {
            mesh = null;
            material = null;
            vertices = Array.Empty<Vector3>();
            foot = Vector3.zero;
            height = 0f;
            Texture2D texture = Resources.Load<Texture2D>("Textures/Realistic/JungleVineLeaves_Atlas");
            if (texture == null)
            {
                Debug.LogWarning("[Forest Floor] Atlas do capim curvado ausente.");
                return false;
            }

            const int bladeCount = 20;
            const int rows = 4;
            const int faceVertices = rows * 2;
            List<Vector3> points = new(bladeCount * faceVertices * 2);
            List<Vector2> uvs = new(bladeCount * faceVertices * 2);
            List<int> triangles = new(bladeCount * 36);
            for (int blade = 0; blade < bladeCount; blade++)
            {
                float variation = ((blade * 7) % bladeCount) / (float)(bladeCount - 1);
                float angle = blade * 2.399963f;
                Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float rootRadius = 0.018f + 0.029f * Mathf.Sqrt(variation);
                Vector3 root = direction * rootRadius;
                float bladeHeight = Mathf.Lerp(0.12f, 0.28f, variation);
                float reach = 0.13f + 0.10f * (blade % 7) / 6f + 0.03f * variation;
                float width = Mathf.Lerp(0.008f, 0.018f, ((blade * 11) % bladeCount) / 19f);
                Vector3 across = Vector3.Cross(Vector3.up, direction);
                int first = points.Count;
                for (int row = 0; row < rows; row++)
                {
                    float t = row / (float)(rows - 1);
                    float remaining = 1f - t;
                    // A vertical base bends outwards, with the final segment
                    // almost horizontal and its tip below the blade's shoulder.
                    Vector3 spine = root
                        + Vector3.up * bladeHeight * (3f * remaining * remaining * t * 0.65f
                            + 3f * remaining * t * t * 1.20f + t * t * t * 0.90f)
                        + direction * reach * (3f * remaining * t * t * 0.55f + t * t * t);
                    Vector3 tangent = Vector3.up * bladeHeight * (3f * remaining * remaining * 0.65f
                            + 6f * remaining * t * 0.55f - 3f * t * t * 0.30f)
                        + direction * reach * (6f * remaining * t * 0.55f + 3f * t * t * 0.45f);
                    Vector3 twistedAcross = Quaternion.AngleAxis((blade % 5 - 2) * 14f * t,
                        tangent.normalized) * across;
                    float taper = row == 0 ? 0.60f : row == 1 ? 1f : row == 2 ? 0.55f : 0.025f;
                    for (int side = 0; side < 2; side++)
                    {
                        points.Add(spine + twistedAcross * ((side - 0.5f) * width * taper));
                        // Stay inside the opaque lower-left leaf; geometry alone
                        // defines the narrow blade, never the atlas silhouette.
                        uvs.Add(new Vector2(0.218f + variation * 0.045f + (side - 0.5f) * 0.008f,
                            0.14f + t * 0.19f));
                    }
                }
                for (int vertex = 0; vertex < faceVertices; vertex++)
                {
                    points.Add(points[first + vertex]);
                    uvs.Add(uvs[first + vertex]);
                }
                for (int row = 0; row < rows - 1; row++)
                {
                    int a = first + row * 2;
                    triangles.Add(a); triangles.Add(a + 2); triangles.Add(a + 1);
                    triangles.Add(a + 1); triangles.Add(a + 2); triangles.Add(a + 3);
                    int back = a + faceVertices;
                    triangles.Add(back); triangles.Add(back + 1); triangles.Add(back + 2);
                    triangles.Add(back + 1); triangles.Add(back + 3); triangles.Add(back + 2);
                }
            }

            mesh = new Mesh { name = "Forest Floor Curved Grass" };
            mesh.SetVertices(points);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            // Thin blades transmit skylight. Upward-biased shading keeps their
            // undersides from reading as black needles in the mobile Lit shader.
            Vector3[] normals = mesh.normals;
            for (int index = 0; index < normals.Length; index++)
                normals[index] = (normals[index] * 0.3f + Vector3.up * 0.7f).normalized;
            mesh.normals = normals;
            mesh.RecalculateBounds();
            vertices = points.ToArray();
            height = mesh.bounds.max.y;
            material = ProceduralWorld.CreateFoliageMaterial("Forest Floor Curved Grass", texture);
            material.SetColor("_BaseColor", new Color(0.72f, 0.86f, 0.60f));
            material.SetFloat("_Smoothness", 0.10f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            return true;
        }
    }
}
