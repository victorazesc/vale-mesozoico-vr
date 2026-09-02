using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ValeMesozoico
{
    internal sealed class WorldMaterials
    {
        public Material Ground { get; set; }
        public Material Water { get; set; }
        public Material Rock { get; set; }
        public Material Trunk { get; set; }
        public Material Foliage { get; set; }
        public Material Rail { get; set; }
        public Material Sleeper { get; set; }
        public Material Support { get; set; }
        public Material Cart { get; set; }
        public Material CartDark { get; set; }
    }

    internal static class ProceduralWorld
    {
        private const float TerrainSize = 300f;

        public static WorldMaterials Build(Transform parent, RideSpline spline)
        {
            ConfigureEnvironment(parent);
            WorldMaterials materials = CreateMaterials();
            BuildTerrain(parent, materials.Ground);
            OptimizedModelWorld.BuildWater(parent, materials.Water);
            OptimizedModelWorld.BuildMountains(parent, materials.Rock, spline);
            OptimizedModelWorld.BuildRocks(parent, materials.Rock, spline);
            OptimizedModelWorld.BuildVegetation(parent, spline);
            OptimizedModelWorld.BuildDinosaurs(parent, spline);
            OptimizedModelWorld.BuildPterosaurs(parent);
            DinosaurAnimationRuntime.RestoreAndPlay(parent);
            return materials;
        }

        public static WorldMaterials BuildForImportedEnvironment(Transform parent, RideSpline spline)
        {
            ConfigureImportedEnvironment(parent);
            WorldMaterials materials = CreateMaterials();
            OptimizedModelWorld.BuildDinosaurs(parent, spline);
            OptimizedModelWorld.BuildPterosaurs(parent);
            DinosaurAnimationRuntime.RestoreAndPlay(parent);
            return materials;
        }

        private static void ConfigureImportedEnvironment(Transform parent)
        {
            ConfigureEnvironment(parent);
            ConfigureImportedSky();
            ConfigureImportedPostProcessing(parent);
            RenderSettings.fogDensity = 0.00135f;
            RenderSettings.fogColor = new Color(0.59f, 0.59f, 0.51f);
            RenderSettings.ambientSkyColor = new Color(0.46f, 0.49f, 0.38f);
            RenderSettings.ambientEquatorColor = new Color(0.30f, 0.29f, 0.21f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.115f, 0.07f);
            RenderSettings.reflectionIntensity = 0.82f;

            if (RenderSettings.sun != null)
            {
                RenderSettings.sun.color = new Color(1f, 0.86f, 0.68f);
                RenderSettings.sun.intensity = 1.78f;
                RenderSettings.sun.shadows = LightShadows.Soft;
                RenderSettings.sun.shadowStrength = 0.78f;
            }
        }

        private static void ConfigureImportedSky()
        {
            Shader shader = Shader.Find("Skybox/Procedural");
            if (shader == null)
            {
                return;
            }

            Material sky = new(shader) { name = "Runtime PC Jurassic Sky" };
            sky.SetColor("_SkyTint", new Color(0.39f, 0.52f, 0.64f));
            sky.SetColor("_GroundColor", new Color(0.47f, 0.46f, 0.39f));
            sky.SetFloat("_AtmosphereThickness", 0.92f);
            sky.SetFloat("_Exposure", 1.12f);
            sky.SetFloat("_SunSize", 0.035f);
            sky.SetFloat("_SunSizeConvergence", 5f);
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
        }

        private static void ConfigureImportedPostProcessing(Transform parent)
        {
#if UNITY_EDITOR || UNITY_STANDALONE
            GameObject volumeObject = new("PC Cinematic Color Grade");
            volumeObject.transform.SetParent(parent, false);
            Volume volume = volumeObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.profile = profile;

            Tonemapping tonemapping = profile.Add<Tonemapping>(true);
            tonemapping.mode.Override(TonemappingMode.Neutral);
            ColorAdjustments color = profile.Add<ColorAdjustments>(true);
            color.postExposure.Override(0.42f);
            color.contrast.Override(8f);
            color.saturation.Override(10f);
            color.colorFilter.Override(new Color(1f, 0.97f, 0.90f));
            Bloom bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(1.05f);
            bloom.intensity.Override(0.10f);
            bloom.scatter.Override(0.55f);
#endif
        }

        public static float HeightAt(float x, float z)
        {
            float broad = Mathf.PerlinNoise((x + 180f) * 0.0085f, (z + 210f) * 0.0085f) * 8.2f;
            float rolling = Mathf.PerlinNoise((x - 74f) * 0.019f, (z + 96f) * 0.019f) * 3.6f;
            float detail = Mathf.PerlinNoise((x - 35f) * 0.052f, (z + 10f) * 0.052f) * 1.35f;
            float ridgeNoise = Mathf.Abs(Mathf.PerlinNoise((x + 21f) * 0.014f, (z - 48f) * 0.014f) * 2f - 1f);
            float ridges = Mathf.Pow(ridgeNoise, 1.8f) * 3.8f;

            float radialDistance = new Vector2(x, z).magnitude;
            float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(72f, 148f, radialDistance)) * 18f;
            float valleyWalls = EllipticalHill(x, z, -111f, 34f, 48f, 88f, 12f)
                + EllipticalHill(x, z, 112f, 26f, 48f, 82f, 13f)
                + EllipticalHill(x, z, 22f, 126f, 94f, 43f, 17f)
                + EllipticalHill(x, z, -12f, -126f, 112f, 39f, 10f);
            float terrainHeight = broad + rolling + detail + ridges + edge + valleyWalls - 6.4f;

            // The lagoon is an actual basin shaped to the visible water instead of a plane
            // cutting through generic terrain. The wide shelf gives the wet shoreline room
            // to blend from submerged sediment into jungle ground.
            float lagoonX = (x - 38f) / 40.5f;
            float lagoonZ = (z - 28f) / 30.5f;
            float lagoonDistance = Mathf.Sqrt(lagoonX * lagoonX + lagoonZ * lagoonZ);
            float floorProgress = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 1.03f, lagoonDistance));
            float lagoonFloor = Mathf.Lerp(-4.8f, -0.62f, floorProgress);
            float basinMask = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.92f, 1.22f, lagoonDistance));
            terrainHeight = Mathf.Lerp(terrainHeight, lagoonFloor, basinMask);

            float shoreDistance = Mathf.Abs(lagoonDistance - 1.055f);
            float shoreMask = 1f - Mathf.SmoothStep(0f, 1f, shoreDistance / 0.19f);
            float shoreTarget = Mathf.Lerp(-0.48f, 0.78f, Mathf.InverseLerp(0.94f, 1.19f, lagoonDistance));
            return Mathf.Lerp(terrainHeight, shoreTarget, shoreMask * 0.78f);
        }

        private static float EllipticalHill(
            float x,
            float z,
            float centerX,
            float centerZ,
            float radiusX,
            float radiusZ,
            float height)
        {
            float normalizedX = (x - centerX) / radiusX;
            float normalizedZ = (z - centerZ) / radiusZ;
            float distance = Mathf.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
            float falloff = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance));
            return falloff * falloff * height;
        }

        private static void ConfigureEnvironment(Transform parent)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogDensity = 0.0028f;
            RenderSettings.fogColor = new Color(0.52f, 0.66f, 0.66f);
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.52f, 0.65f, 0.64f);
            RenderSettings.ambientEquatorColor = new Color(0.34f, 0.43f, 0.33f);
            RenderSettings.ambientGroundColor = new Color(0.16f, 0.20f, 0.12f);

            GameObject lightObject = new("Sun");
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            Light sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.91f, 0.76f);
            sun.intensity = 1.28f;
            sun.shadows = LightShadows.Hard;
            sun.shadowStrength = 0.72f;
            RenderSettings.sun = sun;
        }

        private static WorldMaterials CreateMaterials()
        {
            Texture2D groundTexture = Resources.Load<Texture2D>("Textures/Realistic/JungleGround_Lush_Albedo")
                ?? Resources.Load<Texture2D>("Textures/Realistic/JungleGround_Albedo")
                ?? Resources.Load<Texture2D>("Textures/JurassicGround")
                ?? CreateGroundTexture();
            Texture2D groundNormal = Resources.Load<Texture2D>("Textures/Realistic/JungleGround_Normal");
            Texture2D rockTexture = Resources.Load<Texture2D>("Textures/Realistic/MossyBasalt_Albedo")
                ?? Resources.Load<Texture2D>("Textures/VolcanicRock");
            Texture2D rockNormal = Resources.Load<Texture2D>("Textures/Realistic/MossyBasalt_Normal");
            Texture2D trackAlbedo = Resources.Load<Texture2D>("Textures/Realistic/Track/TrackPaintedSteel_Albedo");
            Texture2D trackNormal = Resources.Load<Texture2D>("Textures/Realistic/Track/TrackPaintedSteel_Normal");
            Texture2D trackSpecGloss = Resources.Load<Texture2D>("Textures/Realistic/Track/TrackPaintedSteel_SpecGloss");
            WorldMaterials materials = new WorldMaterials
            {
                Ground = CreateLit("Ground", new Color(0.82f, 0.94f, 0.78f), 0f, 0.12f, groundTexture, groundNormal, 0.72f),
                Water = CreateTransparent("Water", new Color(0.08f, 0.34f, 0.38f, 0.76f), 0.2f, 0.78f),
                Rock = CreateLit("Mossy Basalt", new Color(0.78f, 0.82f, 0.76f), 0f, 0.24f, rockTexture, rockNormal, 1.05f),
                Trunk = CreateLit("Tree Trunks", new Color(0.24f, 0.13f, 0.065f), 0f, 0.11f),
                Foliage = CreateLit("Foliage", new Color(0.12f, 0.34f, 0.12f), 0f, 0.08f),
                Rail = CreateLit("Oxide Red Running Rails", new Color(0.72f, 0.13f, 0.045f), 0.48f, 0.52f, trackAlbedo, trackNormal, 0.28f),
                Sleeper = CreateLit("Burnt Orange Steel Spine", new Color(0.84f, 0.28f, 0.045f), 0.18f, 0.42f, trackAlbedo, trackNormal, 0.34f),
                Support = CreateLit("Forest Green Tubular Supports", new Color(0.075f, 0.24f, 0.13f), 0.16f, 0.34f, trackAlbedo, trackNormal, 0.30f),
                Cart = CreateLit("Cart", new Color(0.28f, 0.055f, 0.035f), 0.46f, 0.43f),
                CartDark = CreateLit("Cart Dark", new Color(0.035f, 0.04f, 0.035f), 0.4f, 0.32f)
            };
            materials.Ground.mainTextureScale = Vector2.one;
            materials.Rock.mainTextureScale = new Vector2(2.4f, 2.4f);
            if (materials.Rock.HasProperty("_BumpMap"))
            {
                materials.Rock.SetTextureScale("_BumpMap", new Vector2(2.4f, 2.4f));
            }
            ConfigureTrackMaterial(materials.Rail, null);
            ConfigureTrackMaterial(materials.Sleeper, trackSpecGloss);
            ConfigureTrackMaterial(materials.Support, trackSpecGloss);
            return materials;
        }

        private static void ConfigureTrackMaterial(Material material, Texture2D specGloss)
        {
            Vector2 scale = new(1.15f, 1f);
            material.mainTextureScale = scale;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", scale);
            }
            if (material.HasProperty("_BumpMap"))
            {
                material.SetTextureScale("_BumpMap", scale);
            }
            if (specGloss == null || !material.HasProperty("_SpecGlossMap"))
            {
                return;
            }

            material.SetTexture("_SpecGlossMap", specGloss);
            material.SetTextureScale("_SpecGlossMap", scale);
            SetFloat(material, "_WorkflowMode", 0f);
            material.EnableKeyword("_SPECULAR_SETUP");
            material.EnableKeyword("_METALLICSPECGLOSSMAP");
            material.EnableKeyword("_SPECGLOSSMAP");
        }

        private static void BuildTerrain(Transform parent, Material material)
        {
            const int resolution = 129;
            int vertexCount = resolution * resolution;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uvs = new Vector2[vertexCount];
            int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];

            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float worldX = Mathf.Lerp(-TerrainSize * 0.5f, TerrainSize * 0.5f, x / (float)(resolution - 1));
                    float worldZ = Mathf.Lerp(-TerrainSize * 0.5f, TerrainSize * 0.5f, z / (float)(resolution - 1));
                    int index = z * resolution + x;
                    vertices[index] = new Vector3(worldX, HeightAt(worldX, worldZ), worldZ);
                    uvs[index] = new Vector2(worldX / 7f, worldZ / 7f);
                }
            }

            int triangle = 0;
            for (int z = 0; z < resolution - 1; z++)
            {
                for (int x = 0; x < resolution - 1; x++)
                {
                    int a = z * resolution + x;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    triangles[triangle++] = a;
                    triangles[triangle++] = c;
                    triangles[triangle++] = b;
                    triangles[triangle++] = b;
                    triangles[triangle++] = c;
                    triangles[triangle++] = d;
                }
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Prehistoric Valley Terrain", vertexCount);
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GameObject terrain = TrackMeshFactory.CreateMeshObject("Terrain", parent, mesh, material);
            MeshRenderer renderer = terrain.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
        }

        private static void BuildWater(Transform parent, Material material)
        {
            GameObject water = GameObject.CreatePrimitive(PrimitiveType.Plane);
            water.name = "Lagoon";
            water.transform.SetParent(parent, false);
            water.transform.SetPositionAndRotation(new Vector3(38f, -1.05f, 28f), Quaternion.identity);
            water.transform.localScale = new Vector3(4.4f, 1f, 3.4f);
            water.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.Destroy(water.GetComponent<Collider>());
        }

        private static void BuildDistantLandforms(Transform parent, Material material)
        {
            List<Vector3> vertices = new();
            List<int> triangles = new();
            AddCone(vertices, triangles, new Vector3(-108f, 2f, 98f), 43f, 54f, 18);
            AddCone(vertices, triangles, new Vector3(-86f, 0f, 118f), 34f, 38f, 16);
            AddCone(vertices, triangles, new Vector3(112f, 3f, 104f), 32f, 42f, 16);
            AddCone(vertices, triangles, new Vector3(125f, 0f, -92f), 38f, 31f, 16);
            Mesh mesh = TrackMeshFactory.NewMesh("Distant Volcanoes", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            TrackMeshFactory.CreateMeshObject("Volcanoes", parent, mesh, material);
        }

        private static void BuildRocks(Transform parent, Material material)
        {
            System.Random random = new(1729);
            List<Vector3> vertices = new();
            List<int> triangles = new();
            for (int i = 0; i < 96; i++)
            {
                float angle = Next(random, 0f, Mathf.PI * 2f);
                float radius = Next(random, 48f, 142f);
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                position.y = HeightAt(position.x, position.z) + Next(random, 0.4f, 1.4f);
                Vector3 scale = new(Next(random, 0.8f, 3.8f), Next(random, 0.8f, 3.1f), Next(random, 0.8f, 3.8f));
                AddOctahedron(vertices, triangles, position, scale, Next(random, 0f, 360f));
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Combined Valley Rocks", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            TrackMeshFactory.CreateMeshObject("Rocks", parent, mesh, material);
        }

        private static void BuildVegetation(Transform parent, Material trunk, Material foliage)
        {
            System.Random random = new(8128);
            List<Vector3> trunkVertices = new();
            List<int> trunkTriangles = new();
            List<Vector3> leafVertices = new();
            List<int> leafTriangles = new();

            for (int i = 0; i < 135; i++)
            {
                float angle = Next(random, 0f, Mathf.PI * 2f);
                float radius = Next(random, 30f, 143f);
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(38f, 28f)) < 32f)
                {
                    continue;
                }

                position.y = HeightAt(position.x, position.z);
                float height = Next(random, 3.5f, 8.2f);
                float width = Next(random, 0.22f, 0.48f);
                Quaternion rotation = Quaternion.Euler(0f, Next(random, 0f, 360f), Next(random, -4f, 4f));
                TrackMeshFactory.AddBox(trunkVertices, trunkTriangles, position + Vector3.up * height * 0.5f, rotation, new Vector3(width, height, width));
                AddPalmCrown(leafVertices, leafTriangles, position + Vector3.up * height, Next(random, 2.2f, 4.2f), rotation.eulerAngles.y);
            }

            Mesh trunkMesh = TrackMeshFactory.NewMesh("Combined Trunks", trunkVertices.Count);
            trunkMesh.SetVertices(trunkVertices);
            trunkMesh.SetTriangles(trunkTriangles, 0);
            trunkMesh.RecalculateNormals();
            trunkMesh.RecalculateBounds();
            TrackMeshFactory.CreateMeshObject("Tree Trunks", parent, trunkMesh, trunk);

            Mesh foliageMesh = TrackMeshFactory.NewMesh("Combined Palm Crowns", leafVertices.Count);
            foliageMesh.SetVertices(leafVertices);
            foliageMesh.SetTriangles(leafTriangles, 0);
            foliageMesh.RecalculateNormals();
            foliageMesh.RecalculateBounds();
            TrackMeshFactory.CreateMeshObject("Palm Crowns", parent, foliageMesh, foliage);
        }

        private static void BuildDinosaurs(Transform parent, RideSpline spline)
        {
            RidePose sauropodPose = spline.PoseAtDistance(spline.Length * 0.21f);
            Vector3 sauropodRight = sauropodPose.Rotation * Vector3.right;
            Vector3 sauropodPosition = sauropodPose.Position + sauropodRight * 22f;
            sauropodPosition.y = HeightAt(sauropodPosition.x, sauropodPosition.z);
            CreateDinosaurBillboard(
                parent,
                "Sauropod",
                "Generated/Sauropod",
                sauropodPosition,
                new Vector2(24f, 15f));

            RidePose trexPose = spline.PoseAtDistance(spline.Length * 0.63f);
            Vector3 trexRight = trexPose.Rotation * Vector3.right;
            Vector3 trexPosition = trexPose.Position - trexRight * 19f;
            trexPosition.y = HeightAt(trexPosition.x, trexPosition.z);
            CreateDinosaurBillboard(
                parent,
                "Tyrannosaurus",
                "Generated/Trex",
                trexPosition,
                new Vector2(17f, 11f));
        }

        private static void BuildAmbientAudio(Transform parent)
        {
            GameObject audioObject = new("Valley Ambience");
            audioObject.transform.SetParent(parent, false);
            AudioSource source = audioObject.AddComponent<AudioSource>();
            source.clip = EnhancedProceduralAudio.CreateJungleAmbience();
            source.loop = true;
            source.playOnAwake = true;
            source.spatialBlend = 0f;
            source.volume = 0.14f;
            source.Play();
        }

        private static void CreateDinosaurBillboard(Transform parent, string name, string resourcePath, Vector3 position, Vector2 size)
        {
            Texture2D texture = Resources.Load<Texture2D>(resourcePath);
            if (texture == null)
            {
                Debug.LogWarning($"Textura de dinossauro não encontrada: {resourcePath}");
                return;
            }

            Mesh mesh = new() { name = $"{name} Billboard Mesh" };
            mesh.vertices = new[]
            {
                new Vector3(-size.x * 0.5f, 0f, 0f),
                new Vector3(size.x * 0.5f, 0f, 0f),
                new Vector3(-size.x * 0.5f, size.y, 0f),
                new Vector3(size.x * 0.5f, size.y, 0f)
            };
            mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            Material material = CreateCutout(name, texture);
            GameObject actor = TrackMeshFactory.CreateMeshObject(name, parent, mesh, material);
            actor.transform.position = position;
            actor.AddComponent<BillboardActor>();
        }

        private static Material CreateLit(
            string name,
            Color color,
            float metallic,
            float smoothness,
            Texture texture = null,
            Texture normal = null,
            float normalScale = 1f)
        {
            Material template = Resources.Load<Material>("Generated/QuestLit");
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (template == null && shader == null)
            {
                throw new InvalidOperationException("Nenhum shader Lit compatível foi incluído no player.");
            }
            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = name;
            material.enableInstancing = true;
            SetColor(material, color);
            SetFloat(material, "_Metallic", metallic);
            SetFloat(material, "_Smoothness", smoothness);
            if (texture != null)
            {
                SetTexture(material, texture);
                material.mainTextureScale = new Vector2(6f, 6f);
            }
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", normalScale);
                material.EnableKeyword("_NORMALMAP");
            }

            return material;
        }

        internal static Material CreateFoliageMaterial(string name, Texture texture)
        {
            Material material = CreateLit(name, Color.white, 0f, 0.16f, texture);
            material.mainTextureScale = Vector2.one;
            if (material.HasProperty("_BaseMap"))
            {
                material.SetTextureScale("_BaseMap", Vector2.one);
            }
            SetFloat(material, "_AlphaClip", 1f);
            SetFloat(material, "_Cutoff", 0.22f);
            SetFloat(material, "_Cull", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            return material;
        }

        private static Material CreateTransparent(string name, Color color, float metallic, float smoothness)
        {
            Material material = CreateLit(name, color, metallic, smoothness);
            SetFloat(material, "_Surface", 1f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Material CreateCutout(string name, Texture texture)
        {
            Material template = Resources.Load<Material>("Generated/QuestCutout");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Transparent Cutout");
            if (template == null && shader == null)
            {
                throw new InvalidOperationException("Nenhum shader cutout compatível foi incluído no player.");
            }
            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = $"{name} Cutout";
            material.enableInstancing = true;
            SetTexture(material, texture);
            SetColor(material, Color.white);
            SetFloat(material, "_AlphaClip", 1f);
            SetFloat(material, "_Cutoff", 0.1f);
            SetFloat(material, "_Cull", 0f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.AlphaTest;
            return material;
        }

        private static Texture2D CreateGroundTexture()
        {
            const int size = 256;
            Texture2D texture = new(size, size, TextureFormat.RGB24, true, true)
            {
                name = "Procedural Ground Albedo",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 2
            };

            Color[] pixels = new Color[size * size];
            Color moss = new(0.14f, 0.23f, 0.085f);
            Color soil = new(0.27f, 0.20f, 0.10f);
            Color stone = new(0.32f, 0.31f, 0.24f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float low = Mathf.PerlinNoise(x * 0.035f, y * 0.035f);
                    float high = Mathf.PerlinNoise(x * 0.12f + 19f, y * 0.12f + 41f);
                    Color baseColor = Color.Lerp(soil, moss, Mathf.SmoothStep(0.32f, 0.72f, low));
                    pixels[y * size + x] = Color.Lerp(baseColor, stone, Mathf.Clamp01((high - 0.72f) * 2.2f));
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static AudioClip CreateWindClip()
        {
            const int sampleRate = 22050;
            const int seconds = 4;
            float[] samples = new float[sampleRate * seconds];
            System.Random random = new(64);
            float lowPass = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                lowPass = Mathf.Lerp(lowPass, noise, 0.018f);
                float swell = 0.55f + Mathf.Sin(i / (float)sampleRate * Mathf.PI * 0.45f) * 0.25f;
                samples[i] = lowPass * swell * 0.35f;
            }

            AudioClip clip = AudioClip.Create("Procedural Valley Wind", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void AddCone(List<Vector3> vertices, List<int> triangles, Vector3 center, float radius, float height, int sides)
        {
            int start = vertices.Count;
            vertices.Add(center + Vector3.up * height);
            vertices.Add(center);
            for (int i = 0; i < sides; i++)
            {
                float angle = i * Mathf.PI * 2f / sides;
                vertices.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            for (int i = 0; i < sides; i++)
            {
                int current = start + 2 + i;
                int next = start + 2 + (i + 1) % sides;
                triangles.Add(start);
                triangles.Add(next);
                triangles.Add(current);
                triangles.Add(start + 1);
                triangles.Add(current);
                triangles.Add(next);
            }
        }

        private static void AddOctahedron(List<Vector3> vertices, List<int> triangles, Vector3 center, Vector3 scale, float yaw)
        {
            int start = vertices.Count;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3[] points =
            {
                Vector3.up, Vector3.down, Vector3.left, Vector3.right, Vector3.forward, Vector3.back
            };
            foreach (Vector3 point in points)
            {
                vertices.Add(center + rotation * Vector3.Scale(point, scale));
            }

            int[] indices =
            {
                0, 4, 3, 0, 2, 4, 0, 5, 2, 0, 3, 5,
                1, 3, 4, 1, 4, 2, 1, 2, 5, 1, 5, 3
            };
            foreach (int index in indices)
            {
                triangles.Add(start + index);
            }
        }

        private static void AddPalmCrown(List<Vector3> vertices, List<int> triangles, Vector3 center, float radius, float yaw)
        {
            for (int i = 0; i < 7; i++)
            {
                float angle = yaw * Mathf.Deg2Rad + i * Mathf.PI * 2f / 7f;
                Vector3 side = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 perpendicular = Vector3.Cross(Vector3.up, side) * 0.36f;
                Vector3 inner = center + side * 0.25f + Vector3.up * 0.2f;
                Vector3 outer = center + side * radius - Vector3.up * 0.55f;
                int start = vertices.Count;
                vertices.Add(inner - perpendicular);
                vertices.Add(inner + perpendicular);
                vertices.Add(outer - perpendicular * 0.24f);
                vertices.Add(outer + perpendicular * 0.24f);
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        private static float Next(System.Random random, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetTexture(Material material, Texture texture)
        {
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", texture);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", texture);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }

    internal sealed class BillboardActor : MonoBehaviour
    {
        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null)
            {
                return;
            }

            Vector3 direction = camera.transform.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                transform.rotation = Quaternion.LookRotation(-direction.normalized, Vector3.up);
            }
        }
    }

    internal sealed class PterosaurOrbit : MonoBehaviour
    {
        private Vector3 _center;
        private float _radius;
        private float _speed;
        private float _phase;

        public void Initialize(Vector3 center, float radius, float speed, float phase)
        {
            _center = center;
            _radius = radius;
            _speed = speed;
            _phase = phase;
            ApplyPose(Time.time);
        }

        private void Update()
        {
            ApplyPose(Time.time);
        }

        private void ApplyPose(float time)
        {
            float angle = time * _speed / Mathf.Max(1f, _radius) + _phase;
            float verticalAngle = angle * 2f + _phase * 0.35f;
            Vector3 next = _center + new Vector3(
                Mathf.Cos(angle) * _radius,
                Mathf.Sin(verticalAngle) * 1.2f,
                Mathf.Sin(angle) * _radius);
            Vector3 tangent = new Vector3(
                -Mathf.Sin(angle) * _radius,
                Mathf.Cos(verticalAngle) * 2.4f,
                Mathf.Cos(angle) * _radius).normalized;
            Vector3 airflow = MesozoicWind.DirectionAt(next);
            airflow.y = tangent.y;
            tangent = Vector3.Slerp(tangent, airflow.normalized, 0.16f).normalized;
            float gust = MesozoicWind.Gust(time, _phase);
            float bank = -9f - Mathf.Sin(angle * 1.4f + _phase) * 3.2f - gust * 1.8f;
            Quaternion rotation = Quaternion.LookRotation(tangent, Vector3.up)
                * Quaternion.Euler(0f, 0f, bank);
            transform.SetPositionAndRotation(next, rotation);
        }
    }
}
