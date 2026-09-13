using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal static class MountainTrackCave
    {
        public const float BaseStartProgress = 0.465f;
        public const float BaseEndProgress = 0.515f;
        public static float StartProgress { get; private set; } = BaseStartProgress;
        public static float EndProgress { get; private set; } = BaseEndProgress;

        internal static void Configure(RideSpline spline)
        {
            StartProgress = spline.MapBaseProgress(BaseStartProgress);
            EndProgress = spline.MapBaseProgress(BaseEndProgress);
        }
        private const string ResourcePath = "Models/Environment/TrackMountainCave";

        [Serializable]
        private sealed class CaveGeometry
        {
            public Vector3[] vertices;
            public Vector3[] normals;
            public Vector2[] uvs;
            public int[] triangles;
            public int[] surface;
        }

        public static void Build(Transform environment, RideSpline spline)
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError("[Mountain Cave] Modelo ausente: " + ResourcePath);
                return;
            }

            CaveGeometry geometry = JsonUtility.FromJson<CaveGeometry>(asset.text);
            if (geometry?.vertices == null || geometry.vertices.Length < 3
                || geometry.uvs == null || geometry.uvs.Length != geometry.vertices.Length
                || geometry.triangles == null || geometry.triangles.Length < 3)
            {
                Debug.LogError("[Mountain Cave] Geometria inválida.");
                return;
            }

            Shader shader = Resources.Load<Shader>("Shaders/MountainRock");
            Texture2D albedo = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Albedo");
            Texture2D normal = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Normal");
            Texture2D moss = Resources.Load<Texture2D>("Textures/Realistic/JungleGround_Lush_Albedo");
            if (shader == null || albedo == null || normal == null || moss == null)
            {
                Debug.LogError("[Mountain Cave] Material de rocha PBR ausente.");
                return;
            }

            // The camera-facing Blender cave has a sealed end and a different path.
            // Keep its source instances intact; only the playable replacement is shown.
            foreach (Transform instance in environment)
            {
                string name = instance.name;
                if (name.StartsWith("PC_FINAL_Cave_", StringComparison.Ordinal)
                    || name.StartsWith("PC_REFINE_Cave", StringComparison.Ordinal)
                    || name.StartsWith("PC_Cave_", StringComparison.Ordinal)
                    // These shoreline plants are buried under the replacement
                    // mountain; their leaf tips otherwise pierce the cave floor.
                    || name == "PC_REFINE_ShorePlant_046"
                    || name == "PC_REFINE_ShorePlant_047")
                {
                    instance.gameObject.SetActive(false);
                }
            }

            Mesh mesh = new()
            {
                name = "Spline Carved Rock Mountain",
                indexFormat = geometry.vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                vertices = geometry.vertices,
                uv = geometry.uvs,
                triangles = geometry.triangles
            };
            if (geometry.normals != null && geometry.normals.Length == geometry.vertices.Length)
            {
                mesh.normals = geometry.normals;
            }
            else
            {
                mesh.RecalculateNormals();
            }
            mesh.colors = CreateSurfaceColors(geometry, mesh.normals);
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();

            // Use a seamless surface texture: the imported boulders use a UV atlas
            // with empty areas which cannot be repeated over a continuous mountain.
            Material material = new(shader)
            {
                name = "Mountain Cave Rock PBR",
                enableInstancing = true
            };
            material.SetTexture("_BaseMap", albedo);
            material.SetTexture("_BumpMap", normal);
            material.SetTexture("_MossMap", moss);
            material.EnableKeyword("_NORMALMAP");
            material.SetFloat("_BumpScale", 0.20f);
            material.SetFloat("_WorldScale", 0.11f);
            material.SetColor("_BaseColor", Color.white);
            material.SetColor("_StoneColor", new Color(0.53f, 0.50f, 0.44f));
            material.SetColor("_MossColor", new Color(0.19f, 0.29f, 0.065f));
            material.SetFloat("_Smoothness", 0.12f);
            material.SetFloat("_Cull", (float)CullMode.Back);
            material.DisableKeyword("_EMISSION");

            GameObject root = new("Mountain Track Cave");
            root.transform.SetParent(environment, false);
            GameObject mountain = TrackMeshFactory.CreateMeshObject(
                "Mountain Cave Interior", root.transform, mesh, material);
            mountain.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            MountainRockVegetation.Build(environment, root.transform, mesh, spline);
            CaveEntranceFoliage.Build(root.transform, mesh, spline, geometry.surface);

            for (int index = 0; index < 3; index++)
            {
                float progress = Mathf.Lerp(StartProgress, EndProgress, (index + 1f) / 4f);
                RidePose pose = spline.PoseAtDistance(spline.Length * progress);
                GameObject atmosphere = new("Cave Ambient Light " + (index + 1));
                atmosphere.transform.SetParent(root.transform, false);
                atmosphere.transform.localPosition = pose.Position + pose.Rotation * Vector3.up * 3.8f;
                Light light = atmosphere.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.64f, 0.74f, 0.65f);
                light.intensity = 0.65f;
                light.range = 12f;
                light.shadows = LightShadows.None;
                if (index == 1)
                {
                    AudioReverbZone reverb = atmosphere.AddComponent<AudioReverbZone>();
                    reverb.reverbPreset = AudioReverbPreset.Cave;
                    reverb.minDistance = 4f;
                    reverb.maxDistance = 16f;
                }
            }

            Debug.Log($"[Mountain Cave] Túnel integrado | length={(EndProgress - StartProgress) * spline.Length:F1}m, "
                + $"triangles={mesh.triangles.Length / 3}, entrance={StartProgress:F3}, exit={EndProgress:F3}");
        }

        private static Color[] CreateSurfaceColors(CaveGeometry geometry, Vector3[] normals)
        {
            Color[] colors = new Color[geometry.vertices.Length];
            bool hasSurface = geometry.surface != null && geometry.surface.Length == colors.Length;
            float summit = 1f;
            foreach (Vector3 vertex in geometry.vertices)
            {
                summit = Mathf.Max(summit, vertex.y);
            }
            for (int index = 0; index < colors.Length; index++)
            {
                Vector3 point = geometry.vertices[index];
                bool interior = hasSurface && geometry.surface[index] == 1;
                float weathering = Mathf.PerlinNoise(point.x * 0.071f + 13.7f, point.z * 0.071f + point.y * 0.029f);
                float mineral = Mathf.PerlinNoise(point.x * 0.17f + point.y * 0.043f, point.z * 0.14f + 7.3f);
                float exposure = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.25f, 0.75f, weathering));
                float shade = 0.86f + exposure * 0.18f;
                Color stone = Color.Lerp(new Color(0.83f, 0.81f, 0.77f), new Color(1.01f, 0.99f, 0.93f), mineral);
                stone *= shade * (interior ? 0.66f : 1f);

                // Dense groundcover joins the exterior ledges and climbing vines.
                // Explicit surface tags keep every tunnel face free of growth.
                float slope = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.65f, normals[index].y));
                float growth = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.66f, weathering));
                float rootedCover = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, summit * 0.12f, point.y));
                // Long connected patches reach the lower faces with runoff.
                // Their vertical frequency is low so they never form horizontal rings.
                float streak = Mathf.PerlinNoise(point.x * 0.24f + point.z * 0.07f,
                    point.z * 0.21f + point.y * 0.018f + 4.7f);
                float runoff = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.46f, streak)) * rootedCover;
                float summitCover = Mathf.InverseLerp(summit * 0.62f, summit * 0.82f, point.y);
                stone.a = interior ? 0f : Mathf.Clamp01(Mathf.Max(summitCover,
                    Mathf.Max(slope * (0.80f + growth * 0.20f) * rootedCover, runoff)));
                colors[index] = stone;
            }
            return colors;
        }
    }
}
