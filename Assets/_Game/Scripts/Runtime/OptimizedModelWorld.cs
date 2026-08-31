using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal static class OptimizedModelWorld
    {
        private const string PteranodonResourcePath = "Models/Dinosaurs/Pteranodon/Pteranodon";
        private const string CoasterCartResourcePath = "Models/Ride/AbandonedCart/AbandonedCoasterCart";
        private const string HeroBoulderResourcePath = "Models/EnvironmentHero/Boulder01/Boulder01";
        private const string HeroMountainsideResourcePath = "Models/EnvironmentHero/Mountainside/Mountainside";
        private static Material _pteranodonMaterial;
        private static Material _coasterCartBodyMaterial;
        private static Material _coasterCartFrameMaterial;
        private static Material _coasterCartTrimMaterial;
        private static Material _heroPalmBarkMaterial;
        private static Material _heroPalmLeafMaterial;
        private static Material _heroBoulderMaterial;
        private static Material _heroMountainsideMaterial;
        private static Material _closedRockMaterial;
        private static Material _shorelineMaterial;
        private static readonly Mesh[,] ClosedRockLods = new Mesh[6, 3];

        public static void BuildWater(Transform parent, Material sourceMaterial)
        {
            Material waterMaterial = new(sourceMaterial)
            {
                name = "Animated Lagoon Water",
                mainTexture = CreateWaterTexture(),
                mainTextureScale = new Vector2(1.8f, 1.4f),
                enableInstancing = true
            };
            if (waterMaterial.HasProperty("_BumpMap"))
            {
                waterMaterial.SetTexture("_BumpMap", CreateWaterNormalTexture());
                waterMaterial.SetTextureScale("_BumpMap", new Vector2(2.8f, 2.3f));
                waterMaterial.SetFloat("_BumpScale", 0.31f);
                waterMaterial.EnableKeyword("_NORMALMAP");
            }
            SetFloat(waterMaterial, "_Smoothness", 0.88f);
            SetFloat(waterMaterial, "_EnvironmentReflections", 1f);
            SetFloat(waterMaterial, "_SpecularHighlights", 1f);
            SetFloat(waterMaterial, "_Cull", 0f);

            Vector3 lagoonCenter = new(38f, -0.48f, 28f);
            Mesh mesh = CreateLagoonMesh(72, 8, 40.5f, 30.5f);
            GameObject water = TrackMeshFactory.CreateMeshObject("Lagoon", parent, mesh, waterMaterial);
            water.transform.position = lagoonCenter;
            water.layer = 4;
            MeshRenderer waterRenderer = water.GetComponent<MeshRenderer>();
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = false;
            WaterSurfaceAnimator animator = water.AddComponent<WaterSurfaceAnimator>();
            animator.Initialize(waterMaterial);

            ReflectionProbe probe = water.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = true;
            probe.shadowDistance = 0f;
            probe.cullingMask = ~(1 << water.layer);
            probe.size = new Vector3(96f, 35f, 76f);
            probe.center = new Vector3(0f, 8f, 0f);
            probe.blendDistance = 12f;
            probe.intensity = 0.9f;
            LagoonReflectionCapture capture = water.AddComponent<LagoonReflectionCapture>();
            capture.Initialize(probe);

            BuildLagoonShoreline(parent, waterMaterial, lagoonCenter);
            BuildWaterfallCliff(parent);
            BuildWaterfall(parent, waterMaterial);

            AudioSource lagoonAudio = water.AddComponent<AudioSource>();
            lagoonAudio.clip = EnhancedProceduralAudio.CreateWaterAmbience();
            lagoonAudio.loop = true;
            lagoonAudio.playOnAwake = true;
            lagoonAudio.spatialBlend = 1f;
            lagoonAudio.rolloffMode = AudioRolloffMode.Linear;
            lagoonAudio.minDistance = 4f;
            lagoonAudio.maxDistance = 26f;
            lagoonAudio.volume = 0.08f;
            lagoonAudio.Play();

            ConfigureSky();
        }

        private static void BuildLagoonShoreline(
            Transform parent,
            Material waterMaterial,
            Vector3 lagoonCenter)
        {
            Material shoreMaterial = GetShorelineMaterial();
            if (shoreMaterial != null)
            {
                GameObject shore = TrackMeshFactory.CreateMeshObject(
                    "Wet Shoreline Transition 3D",
                    parent,
                    CreateTerrainShorelineMesh(
                        80,
                        5,
                        new Vector2(lagoonCenter.x, lagoonCenter.z),
                        39.6f,
                        29.6f,
                        44.8f,
                        34.8f,
                        lagoonCenter.y),
                    shoreMaterial);
                shore.transform.position = new Vector3(lagoonCenter.x, 0f, lagoonCenter.z);
                MeshRenderer renderer = shore.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;

                float[] blendInnerX = { 44.1f, 46.0f };
                float[] blendInnerZ = { 34.1f, 35.8f };
                float[] blendOuterX = { 46.8f, 48.8f };
                float[] blendOuterZ = { 36.7f, 38.5f };
                float[] blendAlpha = { 0.58f, 0.27f };
                for (int blend = 0; blend < 2; blend++)
                {
                    Material blendMaterial = CreateTransparentShoreMaterial(
                        shoreMaterial,
                        $"Wet Shoreline Blend {blend + 1}",
                        blendAlpha[blend]);
                    GameObject blendObject = TrackMeshFactory.CreateMeshObject(
                        $"Wet Shoreline Blend 3D {blend + 1}",
                        parent,
                        CreateTerrainOverlayRingMesh(
                            80,
                            2,
                            new Vector2(lagoonCenter.x, lagoonCenter.z),
                            blendInnerX[blend],
                            blendInnerZ[blend],
                            blendOuterX[blend],
                            blendOuterZ[blend]),
                        blendMaterial);
                    blendObject.transform.position = new Vector3(lagoonCenter.x, 0f, lagoonCenter.z);
                    MeshRenderer blendRenderer = blendObject.GetComponent<MeshRenderer>();
                    blendRenderer.shadowCastingMode = ShadowCastingMode.Off;
                    blendRenderer.receiveShadows = true;
                }
            }

            float[] innerRadiusX = { 39.15f, 41.15f, 42.85f };
            float[] innerRadiusZ = { 29.15f, 31.05f, 32.75f };
            float[] outerRadiusX = { 41.9f, 43.65f, 44.75f };
            float[] outerRadiusZ = { 31.9f, 33.65f, 34.75f };
            float[] alpha = { 0.67f, 0.52f, 0.36f };
            Color[] colors =
            {
                new(0.11f, 0.38f, 0.40f, alpha[0]),
                new(0.14f, 0.40f, 0.40f, alpha[1]),
                new(0.18f, 0.41f, 0.36f, alpha[2])
            };

            for (int band = 0; band < 3; band++)
            {
                Material shallowMaterial = new(waterMaterial)
                {
                    name = $"Shallow Lagoon Water {band + 1}",
                    enableInstancing = true
                };
                SetColor(shallowMaterial, "_BaseColor", colors[band]);
                SetColor(shallowMaterial, "_Color", colors[band]);
                SetFloat(shallowMaterial, "_Smoothness", 0.82f + band * 0.025f);
                SetFloat(shallowMaterial, "_EnvironmentReflections", 1f);

                GameObject transition = TrackMeshFactory.CreateMeshObject(
                    $"Shallow Water Transition 3D {band + 1}",
                    parent,
                    CreateLagoonRingMesh(
                        80,
                        2,
                        innerRadiusX[band],
                        innerRadiusZ[band],
                        outerRadiusX[band],
                        outerRadiusZ[band],
                        0.018f + band * 0.012f),
                    shallowMaterial);
                transition.transform.position = lagoonCenter;
                transition.layer = 4;
                MeshRenderer renderer = transition.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            BuildShorelinePebbles(parent, lagoonCenter);
        }

        private static void BuildShorelinePebbles(Transform parent, Vector3 lagoonCenter)
        {
            GameObject root = new("Shoreline Pebbles 3D");
            root.transform.SetParent(parent, false);
            Material rockMaterial = GetClosedRockMaterial();
            Material heroBoulderMaterial = GetHeroBoulderMaterial();
            const int pebbleCount = 18;
            for (int index = 0; index < pebbleCount; index++)
            {
                float angle = index * Mathf.PI * 2f / pebbleCount + Mathf.Sin(index * 2.17f) * 0.11f;
                float radialJitter = Mathf.Sin(index * 4.73f) * 0.75f;
                Vector3 position = new(
                    lagoonCenter.x + Mathf.Cos(angle) * (44.8f + radialJitter),
                    0f,
                    lagoonCenter.z + Mathf.Sin(angle) * (34.7f + radialJitter * 0.65f));
                position.y = ProceduralWorld.HeightAt(position.x, position.z) - 0.08f;
                float height = 0.34f + Mathf.Abs(Mathf.Sin(index * 1.91f)) * 0.52f;
                Quaternion rotation = Quaternion.Euler(
                    index % 3 * 7f,
                    index * 67f,
                    (index % 2 == 0 ? -1f : 1f) * 9f);
                if (index % 3 == 0 && heroBoulderMaterial != null)
                {
                    InstantiateEnvironmentLod(
                        root.transform,
                        $"Photogrammetry Shore Boulder {index + 1:00}",
                        HeroBoulderResourcePath,
                        position,
                        rotation,
                        height * 1.12f,
                        heroBoulderMaterial,
                        false,
                        index < 6);
                }
                else
                {
                    InstantiateClosedRockLod(
                        root.transform,
                        $"Shoreline Pebble 3D {index + 1:00}",
                        position,
                        rotation,
                        height,
                        new Vector2(1.35f + index % 3 * 0.15f, 0.84f + index % 2 * 0.18f),
                        index,
                        rockMaterial,
                        false);
                }
            }
        }

        private static void BuildWaterfall(Transform parent, Material sourceMaterial)
        {
            GameObject waterfallRoot = new("Volumetric 3D Waterfall");
            waterfallRoot.transform.SetParent(parent, false);
            waterfallRoot.transform.position = new Vector3(41f, -0.48f, 57.5f);

            Texture2D flowTexture = CreateWaterfallTexture();
            Texture2D flowNormal = CreateWaterfallNormalTexture();
            float[] offsets = { -3.15f, 0f, 3f };
            float[] widths = { 3f, 4.9f, 2.9f };
            float[] heights = { 18.8f, 20.5f, 18.4f };
            float[] depths = { 0.38f, 0.58f, 0.36f };
            float[] alpha = { 0.34f, 0.52f, 0.32f };

            for (int layer = 0; layer < offsets.Length; layer++)
            {
                Material flowMaterial = new(sourceMaterial)
                {
                    name = $"Waterfall Flow PBR {layer + 1}",
                    mainTexture = flowTexture,
                    mainTextureScale = Vector2.one,
                    enableInstancing = true
                };
                SetTexture(flowMaterial, flowTexture, "_BaseMap", "_MainTex");
                SetTexture(flowMaterial, flowNormal, "_BumpMap");
                SetColor(flowMaterial, "_BaseColor", new Color(0.92f, 0.97f, 1f, alpha[layer]));
                SetColor(flowMaterial, "_Color", new Color(0.92f, 0.97f, 1f, alpha[layer]));
                SetFloat(flowMaterial, "_BumpScale", 0.66f);
                SetFloat(flowMaterial, "_Metallic", 0f);
                SetFloat(flowMaterial, "_Smoothness", 0.86f);
                SetFloat(flowMaterial, "_EnvironmentReflections", 1f);
                SetFloat(flowMaterial, "_SpecularHighlights", 1f);
                SetFloat(flowMaterial, "_Cull", 2f);
                flowMaterial.EnableKeyword("_NORMALMAP");

                Mesh flowMesh = CreateVolumetricWaterfallMesh(
                    10,
                    18,
                    widths[layer],
                    heights[layer],
                    depths[layer],
                    4.7f + layer * 3.1f);
                GameObject flow = TrackMeshFactory.CreateMeshObject(
                    $"Waterfall Flow 3D {layer + 1}",
                    waterfallRoot.transform,
                    flowMesh,
                    flowMaterial);
                flow.transform.localPosition = new Vector3(offsets[layer], 0f, layer == 1 ? 0f : 0.08f);
                flow.layer = 4;
                MeshRenderer flowRenderer = flow.GetComponent<MeshRenderer>();
                flowRenderer.shadowCastingMode = ShadowCastingMode.Off;
                flowRenderer.receiveShadows = false;
                WaterfallAnimator animator = flow.AddComponent<WaterfallAnimator>();
                animator.Initialize(flowMaterial, 0.31f + layer * 0.027f, layer * 0.37f);
            }

            Material foamMaterial = new(sourceMaterial)
            {
                name = "Volumetric Waterfall Foam",
                mainTexture = flowTexture,
                mainTextureScale = new Vector2(2.4f, 2.4f),
                enableInstancing = true
            };
            SetTexture(foamMaterial, flowTexture, "_BaseMap", "_MainTex");
            SetTexture(foamMaterial, flowNormal, "_BumpMap");
            SetColor(foamMaterial, "_BaseColor", new Color(0.88f, 0.98f, 1f, 0.72f));
            SetColor(foamMaterial, "_Color", new Color(0.88f, 0.98f, 1f, 0.72f));
            SetFloat(foamMaterial, "_BumpScale", 0.44f);
            SetFloat(foamMaterial, "_Metallic", 0f);
            SetFloat(foamMaterial, "_Smoothness", 0.58f);
            SetFloat(foamMaterial, "_Cull", 2f);
            foamMaterial.EnableKeyword("_NORMALMAP");

            GameObject foam = TrackMeshFactory.CreateMeshObject(
                "Waterfall Foam Volume 3D",
                waterfallRoot.transform,
                CreateFoamVolumeMesh(36, 4, 6.3f, 3.7f, 0.24f, 2.7f),
                foamMaterial);
            foam.transform.localPosition = new Vector3(0f, -0.07f, -2.35f);
            foam.layer = 4;
            ConfigureWaterfallRenderer(foam.GetComponent<MeshRenderer>());

            GameObject crest = TrackMeshFactory.CreateMeshObject(
                "Waterfall Crest Volume 3D",
                waterfallRoot.transform,
                CreateFoamVolumeMesh(28, 3, 4.6f, 1.35f, 0.16f, 7.9f),
                foamMaterial);
            crest.transform.localPosition = new Vector3(0f, 20.12f, 0.82f);
            crest.layer = 4;
            ConfigureWaterfallRenderer(crest.GetComponent<MeshRenderer>());

            GameObject splash = TrackMeshFactory.CreateMeshObject(
                "Waterfall Splash Jets 3D",
                waterfallRoot.transform,
                CreateSplashJetMesh(14, 6, 6, 3.8f, 2.15f),
                foamMaterial);
            splash.transform.localPosition = new Vector3(0f, 0.04f, -1.82f);
            splash.layer = 4;
            ConfigureWaterfallRenderer(splash.GetComponent<MeshRenderer>());

            Material sourcePoolMaterial = new(sourceMaterial)
            {
                name = "Waterfall Source Water PBR",
                enableInstancing = true
            };
            SetColor(sourcePoolMaterial, "_BaseColor", new Color(0.28f, 0.62f, 0.66f, 0.76f));
            SetColor(sourcePoolMaterial, "_Color", new Color(0.28f, 0.62f, 0.66f, 0.76f));
            SetFloat(sourcePoolMaterial, "_Smoothness", 0.9f);
            SetFloat(sourcePoolMaterial, "_EnvironmentReflections", 1f);
            SetFloat(sourcePoolMaterial, "_SpecularHighlights", 1f);

            GameObject sourceStream = TrackMeshFactory.CreateMeshObject(
                "Waterfall Source Stream 3D",
                waterfallRoot.transform,
                CreateWaterfallSourceStreamMesh(9, 6.4f, 7.2f, 0.42f, 0.42f),
                sourcePoolMaterial);
            sourceStream.transform.localPosition = new Vector3(0f, 20.28f, 0f);
            sourceStream.layer = 4;
            ConfigureWaterfallRenderer(sourceStream.GetComponent<MeshRenderer>());
            WaterSurfaceAnimator sourceAnimator = sourceStream.AddComponent<WaterSurfaceAnimator>();
            sourceAnimator.Initialize(sourcePoolMaterial, false);

            GameObject sourceFoam = TrackMeshFactory.CreateMeshObject(
                "Waterfall Source Lip Foam 3D",
                waterfallRoot.transform,
                CreateFoamVolumeMesh(30, 3, 4.25f, 1.35f, 0.12f, 11.4f),
                foamMaterial);
            sourceFoam.transform.localPosition = new Vector3(0f, 20.18f, 1.08f);
            sourceFoam.layer = 4;
            ConfigureWaterfallRenderer(sourceFoam.GetComponent<MeshRenderer>());

            WaterSurfaceAnimator foamAnimator = foam.AddComponent<WaterSurfaceAnimator>();
            foamAnimator.Initialize(foamMaterial, false);

            AudioSource audio = waterfallRoot.AddComponent<AudioSource>();
            audio.clip = EnhancedProceduralAudio.CreateWaterAmbience();
            audio.loop = true;
            audio.playOnAwake = true;
            audio.spatialBlend = 1f;
            audio.rolloffMode = AudioRolloffMode.Linear;
            audio.minDistance = 5f;
            audio.maxDistance = 30f;
            audio.volume = 0.20f;
            audio.Play();
        }

        private static void BuildWaterfallCliff(Transform parent)
        {
            Material rockMaterial = GetClosedRockMaterial();
            Material mountainsideMaterial = GetHeroMountainsideMaterial() ?? rockMaterial;
            Material boulderMaterial = GetHeroBoulderMaterial() ?? rockMaterial;
            Vector3[] heroCliffPositions =
            {
                new(26.8f, -3.8f, 63.7f),
                new(55.2f, -3.6f, 63.5f)
            };
            float[] heroCliffHeights = { 25.5f, 25.2f };
            for (int i = 0; i < heroCliffPositions.Length; i++)
            {
                GameObject heroCliff = InstantiateEnvironmentLod(
                    parent,
                    $"Photogrammetry Waterfall Cliff {i + 1}",
                    HeroMountainsideResourcePath,
                    heroCliffPositions[i],
                    Quaternion.Euler(-4f, 151f + i * 47f, i % 2 == 0 ? -8f : 8f),
                    heroCliffHeights[i],
                    mountainsideMaterial,
                    false,
                    true);
                if (heroCliff != null)
                {
                    heroCliff.transform.localScale = new Vector3(1.45f, 1f, 1.16f);
                }
            }

            Vector3[] cliffPositions =
            {
                new(29.7f, -4.7f, 61.4f),
                new(52.1f, -4.5f, 61.2f)
            };
            float[] cliffHeights = { 23.8f, 23.3f };
            float[] cliffYaw = { 166f, 194f };
            for (int i = 0; i < cliffPositions.Length; i++)
            {
                InstantiateClosedRockLod(
                    parent,
                    $"Waterfall Rock Cliff 3D {i + 1}",
                    cliffPositions[i],
                    Quaternion.Euler(-4f + i * 7f, cliffYaw[i], i == 0 ? -7f : 7f),
                    cliffHeights[i],
                    new Vector2(1.28f, 0.92f),
                    i + 1,
                    rockMaterial,
                    i == 0);
            }

            Vector3[] boulderPositions =
            {
                new(33.1f, -0.75f, 56.7f),
                new(44.6f, -0.68f, 56.5f),
                new(35.7f, 15.2f, 59.5f),
                new(47.2f, 15.4f, 59.4f),
                new(31.5f, 1.2f, 58.6f),
                new(46.7f, 1.0f, 58.4f),
                new(41.0f, 8.0f, 61.7f),
                new(37.5f, 17.2f, 62.5f),
                new(45.5f, 17.4f, 62.2f),
                new(41.5f, 19.0f, 65.0f)
            };
            float[] boulderHeights = { 4.8f, 5.2f, 6.4f, 6.1f, 5.4f, 5.1f, 9.7f, 4.5f, 4.8f, 4.1f };
            for (int i = 0; i < boulderPositions.Length; i++)
            {
                Quaternion rotation = Quaternion.Euler(
                    (i % 3 - 1) * 8f,
                    31f + i * 53f,
                    (i % 2 == 0 ? -1f : 1f) * 7f);
                if (i < 2)
                {
                    InstantiateEnvironmentLod(
                        parent,
                        $"Photogrammetry Waterfall Boulder {i + 1}",
                        HeroBoulderResourcePath,
                        boulderPositions[i],
                        rotation,
                        boulderHeights[i],
                        boulderMaterial,
                        false,
                        true);
                }
                else
                {
                    InstantiateClosedRockLod(
                        parent,
                        $"Waterfall Rock Boulder 3D {i + 1}",
                        boulderPositions[i],
                        rotation,
                        boulderHeights[i],
                        new Vector2(1.12f + (i % 2) * 0.16f, 0.88f + (i % 3) * 0.11f),
                        i,
                        rockMaterial,
                        false);
                }
            }
        }

        private static void ConfigureWaterfallRenderer(MeshRenderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static Mesh CreateVolumetricWaterfallMesh(
            int columns,
            int rows,
            float width,
            float height,
            float thickness,
            float seed)
        {
            int stride = columns + 1;
            int planeVertexCount = stride * (rows + 1);
            List<Vector3> vertices = new(planeVertexCount * 2);
            List<Vector2> uvs = new(planeVertexCount * 2);
            List<int> triangles = new(columns * rows * 12 + (columns + rows) * 12);

            for (int side = 0; side < 2; side++)
            {
                float depthSign = side == 0 ? -1f : 1f;
                for (int row = 0; row <= rows; row++)
                {
                    float v = row / (float)rows;
                    float fall = 1f - v;
                    for (int column = 0; column <= columns; column++)
                    {
                        float u = column / (float)columns;
                        float edge = Mathf.Sin(u * Mathf.PI);
                        float turbulence = Mathf.Sin(u * 19f + v * 31f + seed) * 0.07f
                            + Mathf.Sin(u * 37f - v * 17f + seed * 1.7f) * 0.032f;
                        float forward = -Mathf.Sin(fall * Mathf.PI * 0.5f) * 1.08f
                            + turbulence * edge * (0.35f + fall * 0.65f);
                        float localThickness = thickness
                            * Mathf.Lerp(0.68f, 1f, edge)
                            * (0.90f + Mathf.Sin(v * 23f + seed) * 0.10f);
                        float widthScale = Mathf.Lerp(0.82f, 1.05f, fall)
                            * (0.94f + Mathf.Sin(v * 11f + seed * 0.6f) * 0.06f);
                        float edgeDistance = Mathf.Abs(u - 0.5f) * 2f;
                        float x = (u - 0.5f) * width * widthScale
                            + turbulence
                            * (0.28f + edgeDistance * 0.92f)
                            * (0.42f + fall * 0.58f);
                        float y = v * height + Mathf.Sin(u * 13f + v * 27f + seed) * 0.035f * edge;
                        vertices.Add(new Vector3(x, y, forward + depthSign * localThickness * 0.5f));
                        uvs.Add(new Vector2(u, v * 4.6f));
                    }
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int a = row * stride + column;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);

                    int back = planeVertexCount;
                    triangles.Add(back + a); triangles.Add(back + b); triangles.Add(back + c);
                    triangles.Add(back + b); triangles.Add(back + d); triangles.Add(back + c);
                }
            }

            for (int row = 0; row < rows; row++)
            {
                int leftFront = row * stride;
                int leftFrontUpper = leftFront + stride;
                int leftBack = planeVertexCount + leftFront;
                int leftBackUpper = leftBack + stride;
                triangles.Add(leftFront); triangles.Add(leftBack); triangles.Add(leftFrontUpper);
                triangles.Add(leftFrontUpper); triangles.Add(leftBack); triangles.Add(leftBackUpper);

                int rightFront = row * stride + columns;
                int rightFrontUpper = rightFront + stride;
                int rightBack = planeVertexCount + rightFront;
                int rightBackUpper = rightBack + stride;
                triangles.Add(rightFront); triangles.Add(rightFrontUpper); triangles.Add(rightBack);
                triangles.Add(rightFrontUpper); triangles.Add(rightBackUpper); triangles.Add(rightBack);
            }

            for (int column = 0; column < columns; column++)
            {
                int bottomFront = column;
                int bottomFrontNext = bottomFront + 1;
                int bottomBack = planeVertexCount + bottomFront;
                int bottomBackNext = bottomBack + 1;
                triangles.Add(bottomFront); triangles.Add(bottomFrontNext); triangles.Add(bottomBack);
                triangles.Add(bottomFrontNext); triangles.Add(bottomBackNext); triangles.Add(bottomBack);

                int topFront = rows * stride + column;
                int topFrontNext = topFront + 1;
                int topBack = planeVertexCount + topFront;
                int topBackNext = topBack + 1;
                triangles.Add(topFront); triangles.Add(topBack); triangles.Add(topFrontNext);
                triangles.Add(topFrontNext); triangles.Add(topBack); triangles.Add(topBackNext);
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Volumetric Waterfall Shell", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateFoamVolumeMesh(
            int segments,
            int rings,
            float radiusX,
            float radiusZ,
            float thickness,
            float seed)
        {
            List<Vector3> vertices = new(2 + segments * (rings + 1));
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(segments * (rings * 6 + 9));
            vertices.Add(new Vector3(0f, 0.06f, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int ring = 1; ring <= rings; ring++)
            {
                float ratio = ring / (float)rings;
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float wave = Mathf.Sin(angle * 5f + seed) * 0.055f
                        + Mathf.Sin(angle * 9f - seed * 0.7f) * 0.025f;
                    float x = Mathf.Cos(angle) * radiusX * ratio;
                    float z = Mathf.Sin(angle) * radiusZ * ratio;
                    float y = wave * ratio + (1f - ratio) * 0.06f;
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(x / (radiusX * 2f) + 0.5f, z / (radiusZ * 2f) + 0.5f));
                }
            }

            int bottomRing = vertices.Count;
            for (int segment = 0; segment < segments; segment++)
            {
                float angle = segment * Mathf.PI * 2f / segments;
                vertices.Add(new Vector3(
                    Mathf.Cos(angle) * radiusX,
                    -thickness,
                    Mathf.Sin(angle) * radiusZ));
                uvs.Add(new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f));
            }
            int bottomCenter = vertices.Count;
            vertices.Add(new Vector3(0f, -thickness, 0f));
            uvs.Add(new Vector2(0.5f, 0.5f));

            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(0); triangles.Add(1 + next); triangles.Add(1 + segment);
            }
            for (int ring = 1; ring < rings; ring++)
            {
                int inner = 1 + (ring - 1) * segments;
                int outer = inner + segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles.Add(inner + segment); triangles.Add(inner + next); triangles.Add(outer + segment);
                    triangles.Add(inner + next); triangles.Add(outer + next); triangles.Add(outer + segment);
                }
            }

            int topOuter = 1 + (rings - 1) * segments;
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(topOuter + segment); triangles.Add(topOuter + next); triangles.Add(bottomRing + segment);
                triangles.Add(topOuter + next); triangles.Add(bottomRing + next); triangles.Add(bottomRing + segment);
                triangles.Add(bottomCenter); triangles.Add(bottomRing + segment); triangles.Add(bottomRing + next);
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Volumetric Waterfall Foam", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateSplashJetMesh(
            int jetCount,
            int pathSegments,
            int radialSegments,
            float spreadX,
            float spreadZ)
        {
            List<Vector3> vertices = new(jetCount * (pathSegments + 1) * radialSegments);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(jetCount * pathSegments * radialSegments * 6);
            System.Random random = new(7319);

            for (int jet = 0; jet < jetCount; jet++)
            {
                float angle = Next(random, 0f, Mathf.PI * 2f);
                Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float length = Next(random, 0.75f, 1.9f);
                float height = Next(random, 0.45f, 1.35f);
                float radius = Next(random, 0.045f, 0.095f);
                Vector3 origin = new(
                    Mathf.Cos(angle) * Next(random, 0.15f, spreadX * 0.42f),
                    0f,
                    Mathf.Sin(angle) * Next(random, 0.12f, spreadZ * 0.42f));
                int firstVertex = vertices.Count;

                for (int step = 0; step <= pathSegments; step++)
                {
                    float t = step / (float)pathSegments;
                    Vector3 center = origin
                        + direction * length * t
                        + Vector3.up * (height * 4f * t * (1f - t));
                    Vector3 tangent = (direction * length
                        + Vector3.up * (height * 4f * (1f - 2f * t))).normalized;
                    Vector3 side = Vector3.Cross(Vector3.up, tangent).normalized;
                    if (side.sqrMagnitude < 0.01f)
                    {
                        side = Vector3.right;
                    }
                    Vector3 up = Vector3.Cross(tangent, side).normalized;
                    float localRadius = radius * Mathf.Lerp(1f, 0.42f, t);
                    for (int radial = 0; radial < radialSegments; radial++)
                    {
                        float ringAngle = radial * Mathf.PI * 2f / radialSegments;
                        vertices.Add(center + (side * Mathf.Cos(ringAngle) + up * Mathf.Sin(ringAngle)) * localRadius);
                        uvs.Add(new Vector2(radial / (float)radialSegments, t * 2f));
                    }
                }

                for (int step = 0; step < pathSegments; step++)
                {
                    int current = firstVertex + step * radialSegments;
                    int nextRing = current + radialSegments;
                    for (int radial = 0; radial < radialSegments; radial++)
                    {
                        int next = (radial + 1) % radialSegments;
                        triangles.Add(current + radial); triangles.Add(nextRing + radial); triangles.Add(current + next);
                        triangles.Add(current + next); triangles.Add(nextRing + radial); triangles.Add(nextRing + next);
                    }
                }
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Volumetric Waterfall Splash Jets", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static void BuildMountains(Transform parent, Material material, RideSpline spline)
        {
            _ = spline;
            const int segments = 96;
            const int bands = 6;
            List<Vector3> vertices = new((segments + 1) * bands);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(segments * (bands - 1) * 6);

            for (int segment = 0; segment <= segments; segment++)
            {
                float t = segment / (float)segments;
                float angle = t * Mathf.PI * 2f;
                float directionX = Mathf.Cos(angle);
                float directionZ = Mathf.Sin(angle);
                float broad = Mathf.PerlinNoise(directionX * 1.35f + 4.1f, directionZ * 1.35f + 7.7f);
                float detail = Mathf.PerlinNoise(directionX * 4.8f + 18.3f, directionZ * 4.8f + 2.4f);
                float ridgeHeight = 27f
                    + broad * 23f
                    + detail * 10f
                    + Mathf.Abs(Mathf.Sin(angle * 7f + broad * 3f)) * 6f;
                float radialJitter = (detail - 0.5f) * 15f
                    + Mathf.Sin(angle * 5f + broad * 2f) * 4f;
                float[] radii =
                {
                    126f + radialJitter * 0.15f,
                    141f + radialJitter * 0.35f,
                    157f + radialJitter * 0.72f,
                    177f + radialJitter,
                    204f + radialJitter * 0.62f,
                    232f + radialJitter * 0.28f
                };
                float[] heightFactors = { 0f, 0.24f, 0.68f, 1f, 0.57f, 0.12f };

                for (int band = 0; band < bands; band++)
                {
                    float radius = radii[band];
                    float x = directionX * radius;
                    float z = directionZ * radius;
                    float baseHeight = ProceduralWorld.HeightAt(x, z);
                    float crag = (Mathf.PerlinNoise(t * 19f + band * 2.7f, 0.37f + band) - 0.5f)
                        * (band >= 2 && band <= 4 ? 6f : 2f);
                    vertices.Add(new Vector3(x, baseHeight + ridgeHeight * heightFactors[band] + crag, z));
                    uvs.Add(new Vector2(t * 10f, band * 0.72f));
                }
            }

            for (int segment = 0; segment < segments; segment++)
            {
                int current = segment * bands;
                int next = (segment + 1) * bands;
                for (int band = 0; band < bands - 1; band++)
                {
                    int a = current + band;
                    int b = a + 1;
                    int c = next + band;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            Mesh mountainMesh = TrackMeshFactory.NewMesh("Continuous Jurassic Mountain Ring", vertices.Count);
            mountainMesh.SetVertices(vertices);
            mountainMesh.SetUVs(0, uvs);
            mountainMesh.SetTriangles(triangles, 0);
            mountainMesh.RecalculateNormals();
            mountainMesh.RecalculateTangents();
            mountainMesh.RecalculateBounds();
            GameObject mountains = TrackMeshFactory.CreateMeshObject(
                "Weathered Mountain Range",
                parent,
                mountainMesh,
                material);
            MeshRenderer renderer = mountains.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            SetFloat(material, "_Cull", 0f);
        }

        public static void BuildRocks(Transform parent, Material material, RideSpline spline)
        {
            BuildHeroRockLandscape(parent, spline, material);
            BuildWaterfallTrackCave(parent, spline, material);
        }

        private static void BuildWaterfallTrackCave(Transform parent, RideSpline spline, Material fallbackMaterial)
        {
            GameObject caveRoot = new("Waterfall Track Cave 3D");
            caveRoot.transform.SetParent(parent, false);
            Material caveSource = GetClosedRockMaterial() ?? fallbackMaterial;
            Material caveMaterial = new(caveSource)
            {
                name = "Tileable Waterfall Cave Interior PBR",
                enableInstancing = true
            };
            Vector2 caveTiling = new(1.25f, 0.9f);
            caveMaterial.mainTextureScale = caveTiling;
            if (caveMaterial.HasProperty("_BaseMap")) caveMaterial.SetTextureScale("_BaseMap", caveTiling);
            if (caveMaterial.HasProperty("_BumpMap")) caveMaterial.SetTextureScale("_BumpMap", caveTiling);
            SetFloat(caveMaterial, "_Smoothness", 0.18f);
            SetFloat(caveMaterial, "_BumpScale", 1.02f);
            SetFloat(caveMaterial, "_Cull", 0f);

            Mesh caveMesh = CreateTrackCaveMesh(spline, 0.398f, 0.475f, 24, 16);
            GameObject cave = TrackMeshFactory.CreateMeshObject(
                "Rock Cave Tunnel Interior 3D",
                caveRoot.transform,
                caveMesh,
                caveMaterial);
            MeshRenderer caveRenderer = cave.GetComponent<MeshRenderer>();
            caveRenderer.shadowCastingMode = ShadowCastingMode.On;
            caveRenderer.receiveShadows = true;

            BuildCavePortal(caveRoot.transform, spline, 0.398f, "Waterfall Cave Entrance", false);
            BuildCavePortal(caveRoot.transform, spline, 0.475f, "Waterfall Cave Exit", true);

            RidePose atmospherePose = spline.PoseAtDistance(spline.Length * 0.435f);
            GameObject atmosphere = new("Cave Atmosphere");
            atmosphere.transform.SetParent(caveRoot.transform, false);
            atmosphere.transform.position = atmospherePose.Position + Vector3.up * 2.3f;
            Light caveLight = atmosphere.AddComponent<Light>();
            caveLight.type = LightType.Point;
            caveLight.color = new Color(0.22f, 0.43f, 0.34f);
            caveLight.intensity = 0.46f;
            caveLight.range = 18f;
            caveLight.shadows = LightShadows.None;
            AudioReverbZone reverb = atmosphere.AddComponent<AudioReverbZone>();
            reverb.reverbPreset = AudioReverbPreset.Cave;
            reverb.minDistance = 4f;
            reverb.maxDistance = 21f;
        }

        private static void BuildCavePortal(
            Transform parent,
            RideSpline spline,
            float progress,
            string name,
            bool exit)
        {
            GameObject root = new(name);
            root.transform.SetParent(parent, false);
            RidePose pose = spline.PoseAtDistance(spline.Length * progress);
            Vector3 forward = Vector3.ProjectOnPlane(pose.Tangent, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.5f)
            {
                forward = Vector3.forward;
            }
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 center = pose.Position + forward * (exit ? 1.1f : -1.1f);
            float ground = ProceduralWorld.HeightAt(center.x, center.z) - 0.9f;
            Material fallbackMaterial = GetClosedRockMaterial();
            Material mountainsideMaterial = GetHeroMountainsideMaterial() ?? fallbackMaterial;
            Material boulderMaterial = GetHeroBoulderMaterial() ?? fallbackMaterial;

            for (int sideIndex = 0; sideIndex < 2; sideIndex++)
            {
                float side = sideIndex == 0 ? -1f : 1f;
                Vector3 position = center + right * side * 5.15f - forward * 0.35f;
                position.y = ground;
                Quaternion rotation = Quaternion.LookRotation(-right * side, Vector3.up)
                    * Quaternion.Euler(side * 5f, exit ? 18f : -18f, side * 7f);
                GameObject sideRock = InstantiateEnvironmentLod(
                    root.transform,
                    $"{name} Side {sideIndex + 1}",
                    HeroMountainsideResourcePath,
                    position,
                    rotation,
                    9.4f + sideIndex * 0.8f,
                    mountainsideMaterial,
                    false,
                    true);
                if (sideRock != null)
                {
                    sideRock.transform.localScale = new Vector3(1.38f, 1f, 1.12f);
                }
            }

            Vector3 crownPosition = center + Vector3.up * 4.65f - forward * 0.2f;
            GameObject crown = InstantiateEnvironmentLod(
                root.transform,
                $"{name} Crown",
                HeroBoulderResourcePath,
                crownPosition,
                pose.Rotation * Quaternion.Euler(86f, exit ? 21f : -17f, 12f),
                5.4f,
                boulderMaterial,
                false,
                true);
            if (crown != null)
            {
                crown.transform.localScale = new Vector3(2.15f, 0.72f, 1.28f);
            }
        }

        private static Mesh CreateTrackCaveMesh(
            RideSpline spline,
            float startProgress,
            float endProgress,
            int pathSegments,
            int ringSegments)
        {
            int verticesPerRing = ringSegments + 1;
            List<Vector3> vertices = new((pathSegments + 1) * verticesPerRing);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(pathSegments * ringSegments * 6);
            for (int path = 0; path <= pathSegments; path++)
            {
                float pathT = path / (float)pathSegments;
                float progress = Mathf.Lerp(startProgress, endProgress, pathT);
                RidePose pose = spline.PoseAtDistance(spline.Length * progress);
                Vector3 up = Vector3.up;
                Vector3 horizontalForward = Vector3.ProjectOnPlane(pose.Tangent, up).normalized;
                if (horizontalForward.sqrMagnitude < 0.5f)
                {
                    horizontalForward = Vector3.forward;
                }
                Vector3 right = Vector3.Cross(up, horizontalForward).normalized;
                Vector3 center = pose.Position + up * 0.72f;
                for (int ring = 0; ring <= ringSegments; ring++)
                {
                    float ringT = ring / (float)ringSegments;
                    float angle = Mathf.Lerp(-0.22f, Mathf.PI + 0.22f, ringT);
                    float rockVariation = 1f
                        + Mathf.Sin(path * 1.73f + ring * 2.41f) * 0.055f
                        + Mathf.Sin(path * 0.47f - ring * 1.19f) * 0.035f;
                    float horizontalRadius = 5.75f * rockVariation;
                    float verticalRadius = 4.85f * (1f + (rockVariation - 1f) * 0.8f);
                    vertices.Add(center
                        + right * Mathf.Cos(angle) * horizontalRadius
                        + up * Mathf.Sin(angle) * verticalRadius);
                    uvs.Add(new Vector2(ringT * 3f, pathT * 9f));
                }
            }

            for (int path = 0; path < pathSegments; path++)
            {
                int current = path * verticesPerRing;
                int nextRing = current + verticesPerRing;
                for (int ring = 0; ring < ringSegments; ring++)
                {
                    int a = current + ring;
                    int b = a + 1;
                    int c = nextRing + ring;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Spline Aligned Waterfall Cave", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildHeroRockLandscape(Transform parent, RideSpline spline, Material fallbackMaterial)
        {
            GameObject root = new("Closed 3D Rock Landscape");
            root.transform.SetParent(parent, false);
            Material rockMaterial = GetClosedRockMaterial() ?? fallbackMaterial;
            Material mountainsideMaterial = GetHeroMountainsideMaterial() ?? rockMaterial;
            Material boulderMaterial = GetHeroBoulderMaterial() ?? rockMaterial;
            System.Random random = new(9631);

            const int cliffCount = 9;
            for (int index = 0; index < cliffCount; index++)
            {
                float fraction = Mathf.Lerp(0.06f, 0.94f, index / (cliffCount - 1f));
                RidePose pose = spline.PoseAtDistance(spline.Length * fraction);
                Vector3 right = pose.Rotation * Vector3.right;
                float side = index % 2 == 0 ? -1f : 1f;
                Vector3 position = pose.Position
                    + right * side * Next(random, 21f, 29f)
                    + pose.Tangent * Next(random, -4.5f, 4.5f);
                float targetHeight = Next(random, 8.8f, 13.5f);
                position.y = ProceduralWorld.HeightAt(position.x, position.z) - targetHeight * 0.24f;
                Vector3 faceDirection = -right * side;
                faceDirection.y = 0f;
                Quaternion rotation = Quaternion.LookRotation(faceDirection.normalized, Vector3.up)
                    * Quaternion.Euler(Next(random, -8f, 8f), Next(random, -24f, 24f), Next(random, -11f, 11f));
                GameObject hero = InstantiateEnvironmentLod(
                    root.transform,
                    $"Photogrammetry 3D Cliff {index + 1:00}",
                    HeroMountainsideResourcePath,
                    position,
                    rotation,
                    targetHeight,
                    mountainsideMaterial,
                    false,
                    index < 4);
                if (hero != null)
                {
                    hero.transform.localScale = new Vector3(
                        Next(random, 2.15f, 2.9f),
                        1f,
                        Next(random, 1.08f, 1.42f));
                    Vector3 foundationPosition = position;
                    foundationPosition.y = ProceduralWorld.HeightAt(position.x, position.z) - targetHeight * 0.34f;
                    InstantiateClosedRockLod(
                        root.transform,
                        $"Cliff Foundation 3D {index + 1:00}",
                        foundationPosition,
                        rotation * Quaternion.Euler(0f, 28f, 0f),
                        targetHeight * 0.56f,
                        new Vector2(2.25f, 1.32f),
                        index + 3,
                        rockMaterial,
                        false);
                }
                else
                {
                    InstantiateClosedRockLod(
                        root.transform,
                        $"Closed 3D Cliff {index + 1:00}",
                        position,
                        rotation,
                        targetHeight,
                        new Vector2(Next(random, 1.45f, 2.05f), Next(random, 0.78f, 1.20f)),
                        index,
                        rockMaterial,
                        index < 4);
                }
            }

            const int boulderCount = 14;
            for (int index = 0; index < boulderCount; index++)
            {
                float distance = Next(random, spline.Length * 0.04f, spline.Length * 0.96f);
                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = pose.Rotation * Vector3.right;
                float side = index % 2 == 0 ? -1f : 1f;
                Vector3 position = pose.Position
                    + right * side * Next(random, 9f, 16.5f)
                    + pose.Tangent * Next(random, -3f, 3f);
                position.y = ProceduralWorld.HeightAt(position.x, position.z) - Next(random, 0.28f, 0.82f);
                Quaternion rotation = Quaternion.Euler(
                    Next(random, -8f, 8f),
                    Next(random, 0f, 360f),
                    Next(random, -8f, 8f));
                float targetHeight = Next(random, 2.1f, 4.6f);
                GameObject hero = InstantiateEnvironmentLod(
                    root.transform,
                    $"Photogrammetry 3D Boulder {index + 1:00}",
                    HeroBoulderResourcePath,
                    position,
                    rotation,
                    targetHeight,
                    boulderMaterial,
                    false,
                    index < 6);
                if (hero != null)
                {
                    hero.transform.localScale = new Vector3(
                        Next(random, 1.04f, 1.26f),
                        1f,
                        Next(random, 0.96f, 1.18f));
                }
                else
                {
                    InstantiateClosedRockLod(
                        root.transform,
                        $"Closed 3D Boulder {index + 1:00}",
                        position,
                        rotation,
                        targetHeight,
                        new Vector2(Next(random, 0.92f, 1.38f), Next(random, 0.82f, 1.24f)),
                        index + 2,
                        rockMaterial,
                        index < 6);
                }
            }
        }

        private static void BuildLegacyRocks(Transform parent, Material material, RideSpline spline)
        {
            System.Random random = new(1729);
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            for (int i = 0; i < 74; i++)
            {
                float angle = Next(random, 0f, Mathf.PI * 2f);
                float radius = Next(random, 34f, 145f);
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(38f, 28f)) < 28f)
                {
                    i--;
                    continue;
                }

                Vector3 scale = new(
                    Next(random, 0.7f, 3.7f),
                    Next(random, 0.65f, 2.8f),
                    Next(random, 0.75f, 3.8f));
                position.y = ProceduralWorld.HeightAt(position.x, position.z) + scale.y * Next(random, 0.48f, 0.64f);
                AddBoulder(vertices, uvs, triangles, position, scale, Next(random, 0f, 360f), random);
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Rounded Volcanic Boulders", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            GameObject rocks = TrackMeshFactory.CreateMeshObject("Textured Volcanic Rocks", parent, mesh, material);
            MeshRenderer renderer = rocks.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;

            string[] heroRockResources =
            {
                "Models/Environment/RockLargeA",
                "Models/Environment/RockLargeD",
                "Models/Environment/RockLargeF"
            };
            GameObject heroRoot = new("Detailed 3D Rocks");
            heroRoot.transform.SetParent(parent, false);
            BuildPolyHavenRockCorridor(heroRoot.transform, spline, material);
            System.Random heroRandom = new(5491);
            for (int i = 0; i < 8; i++)
            {
                float angle = Next(heroRandom, 0f, Mathf.PI * 2f);
                float radius = Next(heroRandom, 28f, 118f);
                Vector3 position = new(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (Vector2.Distance(new Vector2(position.x, position.z), new Vector2(38f, 28f)) < 33f)
                {
                    i--;
                    continue;
                }
                position.y = ProceduralWorld.HeightAt(position.x, position.z);
                GameObject rock = InstantiateSizedModel(
                    heroRoot.transform,
                    $"Detailed Rock {i + 1:00}",
                    heroRockResources[i % heroRockResources.Length],
                    position,
                    Quaternion.Euler(0f, Next(heroRandom, 0f, 360f), 0f),
                    Next(heroRandom, 1.7f, 4.4f),
                    i < 7,
                    0.012f);
                if (rock == null)
                {
                    continue;
                }
                foreach (MeshRenderer rockRenderer in rock.GetComponentsInChildren<MeshRenderer>(true))
                {
                    Material[] shared = rockRenderer.sharedMaterials;
                    for (int materialIndex = 0; materialIndex < shared.Length; materialIndex++)
                    {
                        shared[materialIndex] = material;
                    }
                    rockRenderer.sharedMaterials = shared;
                }
            }
        }

        private static void BuildPolyHavenRockCorridor(Transform parent, RideSpline spline, Material fallback)
        {
            GameObject rockFacePrefab = Resources.Load<GameObject>("Models/PolyHaven/RockFace/RockFace_Quest");
            GameObject mossSetPrefab = Resources.Load<GameObject>("Models/PolyHaven/RockMossSet/RockMossSet_Quest");
            if (rockFacePrefab == null && mossSetPrefab == null)
            {
                return;
            }

            Material rockFaceMaterial = CreatePolyHavenMaterial(
                fallback,
                "Poly Haven Rock Face",
                "Models/PolyHaven/RockFace/textures/RockFace_Albedo",
                "Models/PolyHaven/RockFace/textures/RockFace_Normal",
                0.24f);
            Material mossMaterial = CreatePolyHavenMaterial(
                fallback,
                "Poly Haven Mossy Rocks",
                "Models/PolyHaven/RockMossSet/textures/RockMossSet_Albedo",
                "Models/PolyHaven/RockMossSet/textures/RockMossSet_Normal",
                0.20f);

            System.Random random = new(9631);
            const int corridorSections = 16;
            for (int section = 0; section < corridorSections; section++)
            {
                float fraction = Mathf.Lerp(0.07f, 0.93f, section / (corridorSections - 1f));
                RidePose pose = spline.PoseAtDistance(spline.Length * fraction);
                Vector3 right = pose.Rotation * Vector3.right;
                for (int sideIndex = 0; sideIndex < 2; sideIndex++)
                {
                    bool useMossSet = (section == 3 && sideIndex == 0)
                        || (section == 9 && sideIndex == 0)
                        || (section == 13 && sideIndex == 1);
                    string resourcePath = useMossSet
                        ? "Models/PolyHaven/RockMossSet/RockMossSet_Quest"
                        : "Models/PolyHaven/RockFace/RockFace_Quest";
                    if ((useMossSet && mossSetPrefab == null) || (!useMossSet && rockFacePrefab == null))
                    {
                        continue;
                    }

                    float side = sideIndex == 0 ? -1f : 1f;
                    float offset = useMossSet ? Next(random, 9.5f, 12f) : Next(random, 13f, 18.5f);
                    Vector3 position = pose.Position
                        + right * side * offset
                        + pose.Tangent * Next(random, -2.8f, 2.8f);
                    position.y = ProceduralWorld.HeightAt(position.x, position.z);
                    Vector3 faceDirection = -right * side;
                    faceDirection.y = 0f;
                    Quaternion rotation = Quaternion.LookRotation(faceDirection.normalized, Vector3.up)
                        * Quaternion.Euler(0f, Next(random, -20f, 20f), Next(random, -4f, 4f));
                    GameObject rock = InstantiateSizedModel(
                        parent,
                        $"Canyon Rock {section + 1:00}-{sideIndex + 1}",
                        resourcePath,
                        position,
                        rotation,
                        useMossSet ? Next(random, 2.8f, 4.4f) : Next(random, 9f, 13.5f),
                        section < 4,
                        0.015f);
                    if (rock != null)
                    {
                        OverrideMaterials(rock, useMossSet ? mossMaterial : rockFaceMaterial);
                    }
                }
            }
        }

        public static void BuildVegetation(Transform parent, RideSpline spline)
        {
            BuildHeroVegetation(parent, spline);
        }

        private static void BuildHeroVegetation(Transform parent, RideSpline spline)
        {
            GameObject vegetation = new("Wind Animated 3D Vegetation");
            vegetation.transform.SetParent(parent, false);
            Material barkMaterial = GetHeroPalmBarkMaterial();
            Material leafMaterial = GetHeroPalmLeafMaterial();
            System.Random random = new(8128);

            const int palmCount = 48;
            for (int index = 0; index < palmCount; index++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 12 && !placed; attempt++)
                {
                    Vector3 position;
                    if (index < 10)
                    {
                        float angle = index / 10f * Mathf.PI * 2f + Next(random, -0.28f, 0.28f);
                        float radius = Next(random, 34f, 49f);
                        position = new Vector3(
                            38f + Mathf.Cos(angle) * radius,
                            0f,
                            28f + Mathf.Sin(angle) * radius * 0.78f);
                    }
                    else
                    {
                        float distance = Next(random, spline.Length * 0.04f, spline.Length * 0.94f);
                        RidePose pose = spline.PoseAtDistance(distance);
                        Vector3 right = pose.Rotation * Vector3.right;
                        float side = index % 2 == 0 ? -1f : 1f;
                        position = pose.Position
                            + right * side * Next(random, 8.5f, 16f)
                            + pose.Tangent * Next(random, -4f, 4f);
                    }

                    if (IsNearTrack(position, spline, 6f))
                    {
                        continue;
                    }

                    position.y = ProceduralWorld.HeightAt(position.x, position.z);
                    GameObject palm = InstantiateEnvironmentLod(
                        vegetation.transform,
                        $"Hero Coconut Palm {index + 1:00}",
                        "Models/EnvironmentHero/CoconutPalm/CoconutPalm",
                        position,
                        Quaternion.Euler(Next(random, -2f, 2f), Next(random, 0f, 360f), Next(random, -2f, 2f)),
                        Next(random, 8.5f, 13.5f),
                        barkMaterial,
                        true,
                        index < 10,
                        leafMaterial);
                    if (palm == null)
                    {
                        break;
                    }

                    TropicalWindSway sway = palm.AddComponent<TropicalWindSway>();
                    sway.Initialize(Next(random, 0f, Mathf.PI * 2f), Next(random, 1.35f, 2.25f), 3.2f);
                    placed = true;
                }

                if (!placed)
                {
                    Debug.LogWarning($"Não foi possível posicionar a palmeira 3D {index + 1:00} com distância segura da pista.");
                }
            }

            BuildDetailedUnderstory(vegetation.transform, spline);
            BuildCanopyDiversity(vegetation.transform, spline, barkMaterial, leafMaterial);
        }

        private static void BuildCanopyDiversity(
            Transform parent,
            RideSpline spline,
            Material barkMaterial,
            Material leafMaterial)
        {
            string[] typeNames = { "Fan Palm", "Tall Jungle Palm", "Bent River Palm" };
            System.Random random = new(7319);
            const int varietyCount = 36;
            for (int index = 0; index < varietyCount; index++)
            {
                bool placed = false;
                for (int attempt = 0; attempt < 14 && !placed; attempt++)
                {
                    float distance = Next(random, spline.Length * 0.025f, spline.Length * 0.965f);
                    RidePose pose = spline.PoseAtDistance(distance);
                    Vector3 right = pose.Rotation * Vector3.right;
                    float side = index % 2 == 0 ? -1f : 1f;
                    Vector3 position = pose.Position
                        + right * side * Next(random, 9f, 17.5f)
                        + pose.Tangent * Next(random, -5f, 5f);
                    if (IsNearTrack(position, spline, 8.5f))
                    {
                        continue;
                    }

                    position.y = ProceduralWorld.HeightAt(position.x, position.z);
                    int type = index % typeNames.Length;
                    float targetHeight = type switch
                    {
                        0 => Next(random, 5.4f, 7.4f),
                        1 => Next(random, 11f, 14.5f),
                        _ => Next(random, 7.8f, 10.8f)
                    };
                    GameObject plant = InstantiateEnvironmentLod(
                        parent,
                        $"Canopy {typeNames[type]} {index + 1:00}",
                        "Models/EnvironmentHero/CoconutPalm/CoconutPalm",
                        position,
                        Quaternion.Euler(
                            Next(random, -2f, 2f),
                            Next(random, 0f, 360f),
                            type == 2 ? Next(random, -5f, 5f) : Next(random, -2.5f, 2.5f)),
                        targetHeight,
                        barkMaterial,
                        true,
                        index < 12,
                        leafMaterial);
                    if (plant == null)
                    {
                        break;
                    }

                    TropicalWindSway sway = plant.AddComponent<TropicalWindSway>();
                    sway.Initialize(Next(random, 0f, Mathf.PI * 2f), Next(random, 0.85f, 1.65f), 3.7f);
                    placed = true;
                }
            }
        }

        private static void BuildLegacyVegetation(Transform parent, RideSpline spline)
        {
            GameObject vegetation = new("Realistic Optimized Vegetation");
            vegetation.transform.SetParent(parent, false);
            System.Random random = new(8128);
            Mesh palmMesh = CreateDetailedPalmMesh();
            Material barkMaterial = CreateOpaquePalmMaterial(
                "Palm Fibrous Bark",
                "Textures/Realistic/PalmBark_Albedo",
                new Color(0.68f, 0.61f, 0.48f),
                0.14f);
            Material leafMaterial = CreateOpaquePalmMaterial(
                "Palm Natural Fronds",
                "Textures/Realistic/PalmLeaf_Albedo",
                new Color(0.52f, 0.68f, 0.34f),
                0.12f);
            Material coconutMaterial = CreateOpaquePalmMaterial(
                "Palm Coconuts",
                null,
                new Color(0.22f, 0.12f, 0.055f),
                0.20f);

            for (int i = 0; i < 30; i++)
            {
                Vector3 position;
                if (i < 10)
                {
                    float angle = Next(random, 0f, Mathf.PI * 2f);
                    float radius = Next(random, 31f, 43f);
                    position = new Vector3(38f + Mathf.Cos(angle) * radius, 0f, 28f + Mathf.Sin(angle) * radius * 0.78f);
                }
                else
                {
                    float distance = Next(random, spline.Length * 0.035f, spline.Length * 0.94f);
                    RidePose pose = spline.PoseAtDistance(distance);
                    Vector3 right = pose.Rotation * Vector3.right;
                    float side = i % 2 == 0 ? -1f : 1f;
                    position = pose.Position
                        + right * side * Next(random, 10f, 22f)
                        + pose.Tangent * Next(random, -4f, 4f);
                }

                if (IsNearTrack(position, spline, 8.5f))
                {
                    i--;
                    continue;
                }

                position.y = ProceduralWorld.HeightAt(position.x, position.z);
                GameObject palm = new($"Detailed Coconut Palm {i + 1:00}");
                palm.transform.SetParent(vegetation.transform, false);
                palm.transform.SetPositionAndRotation(
                    position,
                    Quaternion.Euler(Next(random, -2f, 2f), Next(random, 0f, 360f), Next(random, -3f, 3f)));
                float height = Next(random, 6.5f, 11.5f);
                float scale = height / 1.18f;
                float widthVariation = Next(random, 0.88f, 1.12f);
                palm.transform.localScale = new Vector3(scale * widthVariation, scale, scale * widthVariation);

                MeshFilter filter = palm.AddComponent<MeshFilter>();
                filter.sharedMesh = palmMesh;
                MeshRenderer renderer = palm.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = new[] { barkMaterial, leafMaterial, coconutMaterial };
                renderer.shadowCastingMode = i < 8 ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                LODGroup lod = palm.AddComponent<LODGroup>();
                lod.fadeMode = LODFadeMode.CrossFade;
                lod.animateCrossFading = false;
                lod.SetLODs(new[] { new LOD(0.025f, new Renderer[] { renderer }) });
                lod.RecalculateBounds();
                if (i >= 8)
                {
                    lod.fadeMode = LODFadeMode.None;
                }
            }

            BuildDetailedUnderstory(vegetation.transform, spline);
            BuildUnderstory(vegetation.transform, spline);
        }

        private static Mesh CreateDetailedPalmMesh()
        {
            List<Vector3> vertices = new(2200);
            List<Vector2> uvs = new(2200);
            List<int> trunkTriangles = new(900);
            List<int> leafTriangles = new(2800);
            List<int> coconutTriangles = new(900);
            AddPalmTrunk(vertices, uvs, trunkTriangles);
            AddPalmFronds(vertices, uvs, leafTriangles);
            AddPalmCoconuts(vertices, uvs, coconutTriangles);

            Mesh mesh = TrackMeshFactory.NewMesh("Optimized Detailed Coconut Palm", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = 3;
            mesh.SetTriangles(trunkTriangles, 0);
            mesh.SetTriangles(leafTriangles, 1);
            mesh.SetTriangles(coconutTriangles, 2);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            mesh.UploadMeshData(true);
            return mesh;
        }

        private static void AddPalmTrunk(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            const int rings = 16;
            const int sides = 10;
            int start = vertices.Count;
            for (int ring = 0; ring < rings; ring++)
            {
                float t = ring / (float)(rings - 1);
                Vector3 center = new(
                    Mathf.Sin(t * 1.7f) * 0.035f * t,
                    t,
                    Mathf.Sin(t * 1.15f + 0.7f) * 0.025f * t);
                float radius = Mathf.Lerp(0.105f, 0.052f, t) * (1f + Mathf.Sin(t * 28f) * 0.025f);
                for (int side = 0; side < sides; side++)
                {
                    float angle = side * Mathf.PI * 2f / sides;
                    vertices.Add(center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
                    uvs.Add(new Vector2(side / (float)sides * 1.8f, t * 6f));
                }
            }
            for (int ring = 0; ring < rings - 1; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    int a = start + ring * sides + side;
                    int b = start + ring * sides + next;
                    int c = start + (ring + 1) * sides + side;
                    int d = start + (ring + 1) * sides + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        private static void AddPalmFronds(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            const int frondCount = 14;
            const int segments = 12;
            Vector3 crown = new(0.03f, 1f, 0.02f);
            for (int frond = 0; frond < frondCount; frond++)
            {
                float angle = frond * Mathf.PI * 2f / frondCount + Mathf.Sin(frond * 2.17f) * 0.08f;
                Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                Vector3 side = new(-direction.z, 0f, direction.x);
                int ribbonStart = vertices.Count;
                for (int segment = 0; segment < segments; segment++)
                {
                    float t = segment / (float)(segments - 1);
                    Vector3 center = crown
                        + direction * (0.08f + t * 0.66f)
                        + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.12f - t * t * 0.26f + Mathf.Sin(frond * 1.31f) * 0.015f);
                    float halfWidth = Mathf.Lerp(0.012f, 0.003f, t);
                    vertices.Add(center - side * halfWidth);
                    vertices.Add(center + side * halfWidth);
                    uvs.Add(new Vector2(0f, t * 2.4f));
                    uvs.Add(new Vector2(1f, t * 2.4f));
                }
                for (int segment = 0; segment < segments - 1; segment++)
                {
                    int a = ribbonStart + segment * 2;
                    int b = a + 1;
                    int c = a + 2;
                    int d = a + 3;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }

                for (int segment = 1; segment < segments - 1; segment++)
                {
                    float t = segment / (float)(segments - 1);
                    Vector3 center = crown
                        + direction * (0.08f + t * 0.66f)
                        + Vector3.up * (Mathf.Sin(t * Mathf.PI) * 0.12f - t * t * 0.26f + Mathf.Sin(frond * 1.31f) * 0.015f);
                    float leafletLength = Mathf.Pow(Mathf.Sin(t * Mathf.PI), 0.58f) * Mathf.Lerp(0.18f, 0.11f, t);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        int start = vertices.Count;
                        Vector3 basePoint = center + direction * 0.01f;
                        Vector3 tip = center
                            + side * sign * leafletLength
                            + direction * 0.045f
                            - Vector3.up * Mathf.Lerp(0.015f, 0.065f, t);
                        vertices.Add(basePoint - direction * 0.016f);
                        vertices.Add(basePoint + direction * 0.016f);
                        vertices.Add(tip - direction * 0.010f);
                        vertices.Add(tip + direction * 0.010f);
                        uvs.Add(new Vector2(0f, t));
                        uvs.Add(new Vector2(1f, t));
                        uvs.Add(new Vector2(0f, t + 0.55f));
                        uvs.Add(new Vector2(1f, t + 0.55f));
                        triangles.Add(start);
                        triangles.Add(start + 2);
                        triangles.Add(start + 1);
                        triangles.Add(start + 1);
                        triangles.Add(start + 2);
                        triangles.Add(start + 3);
                    }
                }
            }
        }

        private static void AddPalmCoconuts(List<Vector3> vertices, List<Vector2> uvs, List<int> triangles)
        {
            for (int coconut = 0; coconut < 5; coconut++)
            {
                float angle = coconut * Mathf.PI * 2f / 5f + 0.35f;
                Vector3 center = new(Mathf.Cos(angle) * 0.075f, 0.95f - (coconut % 2) * 0.025f, Mathf.Sin(angle) * 0.075f);
                AddLowPolySphere(vertices, uvs, triangles, center, 0.052f, 8, 5);
            }
        }

        private static void AddLowPolySphere(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            float radius,
            int sides,
            int rings)
        {
            int start = vertices.Count;
            for (int ring = 0; ring <= rings; ring++)
            {
                float v = ring / (float)rings;
                float latitude = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);
                float ringRadius = Mathf.Cos(latitude) * radius;
                for (int side = 0; side < sides; side++)
                {
                    float u = side / (float)sides;
                    float angle = u * Mathf.PI * 2f;
                    vertices.Add(center + new Vector3(Mathf.Cos(angle) * ringRadius, Mathf.Sin(latitude) * radius, Mathf.Sin(angle) * ringRadius));
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (int ring = 0; ring < rings; ring++)
            {
                for (int side = 0; side < sides; side++)
                {
                    int next = (side + 1) % sides;
                    int a = start + ring * sides + side;
                    int b = start + ring * sides + next;
                    int c = start + (ring + 1) * sides + side;
                    int d = start + (ring + 1) * sides + next;
                    triangles.Add(a); triangles.Add(c); triangles.Add(b);
                    triangles.Add(b); triangles.Add(c); triangles.Add(d);
                }
            }
        }

        private static void BuildDetailedUnderstory(Transform parent, RideSpline spline)
        {
            const string resourcePath = "Models/PolyHaven/Fern/Fern_Quest";
            if (Resources.Load<GameObject>(resourcePath) == null)
            {
                return;
            }

            Material fernMaterial = CreatePolyHavenFoliageMaterial(
                "Detailed Fern",
                "Models/PolyHaven/Fern/textures/Fern_Cutout",
                "Models/PolyHaven/Fern/textures/Fern_Normal");
            Material youngFernMaterial = new(fernMaterial)
            {
                name = "Young Fern PBR",
                enableInstancing = true
            };
            Material cycadMaterial = new(fernMaterial)
            {
                name = "Deep Green Cycad PBR",
                enableInstancing = true
            };
            SetColor(youngFernMaterial, "_BaseColor", new Color(0.72f, 0.92f, 0.62f));
            SetColor(youngFernMaterial, "_Color", new Color(0.72f, 0.92f, 0.62f));
            SetColor(cycadMaterial, "_BaseColor", new Color(0.34f, 0.58f, 0.31f));
            SetColor(cycadMaterial, "_Color", new Color(0.34f, 0.58f, 0.31f));
            Material[] plantMaterials = { fernMaterial, youngFernMaterial, cycadMaterial };
            string[] plantTypes = { "Giant Fern", "Young Fern", "Cycad" };
            System.Random random = new(6353);
            for (int i = 0; i < 240; i++)
            {
                float distance = Next(random, spline.Length * 0.05f, spline.Length * 0.91f);
                RidePose pose = spline.PoseAtDistance(distance);
                float side = i % 2 == 0 ? -1f : 1f;
                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 position = pose.Position
                    + right * side * Next(random, 4.8f, 10.5f)
                    + pose.Tangent * Next(random, -2.5f, 2.5f);
                position.y = ProceduralWorld.HeightAt(position.x, position.z);
                int type = i % plantTypes.Length;
                float targetHeight = type switch
                {
                    0 => Next(random, 1.35f, 2.15f),
                    1 => Next(random, 0.62f, 1.05f),
                    _ => Next(random, 0.95f, 1.55f)
                };
                GameObject plant = InstantiateSizedModel(
                    parent,
                    $"Detailed Plant {i + 1:000} {plantTypes[type]}",
                    resourcePath,
                    position,
                    Quaternion.Euler(0f, Next(random, 0f, 360f), 0f),
                    targetHeight,
                    i < 12,
                    0.028f);
                if (plant != null)
                {
                    OverrideMaterials(plant, plantMaterials[type]);
                    TropicalWindSway sway = plant.AddComponent<TropicalWindSway>();
                    sway.Initialize(Next(random, 0f, Mathf.PI * 2f), Next(random, 0.65f, 1.1f), 4.1f);
                }
            }
        }

        private static void BuildUnderstory(Transform parent, RideSpline spline)
        {
            Texture2D atlas = Resources.Load<Texture2D>("Textures/Realistic/PrehistoricFoliage_Atlas");
            if (atlas == null)
            {
                Debug.LogWarning("Atlas de vegetação realista não encontrado.");
                return;
            }

            Material material = ProceduralWorld.CreateFoliageMaterial("Prehistoric Understory", atlas);
            const int sectorCount = 12;
            List<Vector3>[] verticesBySector = new List<Vector3>[sectorCount];
            List<Vector2>[] uvsBySector = new List<Vector2>[sectorCount];
            List<int>[] trianglesBySector = new List<int>[sectorCount];
            for (int sector = 0; sector < sectorCount; sector++)
            {
                verticesBySector[sector] = new List<Vector3>(80 * 8);
                uvsBySector[sector] = new List<Vector2>(80 * 8);
                trianglesBySector[sector] = new List<int>(80 * 12);
            }

            System.Random random = new(1847);
            int placed = 0;
            for (int attempt = 0; attempt < 2400 && placed < 960; attempt++)
            {
                float distance = Next(random, 0f, spline.Length);
                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 forward = pose.Tangent;
                float side = random.Next(0, 2) == 0 ? -1f : 1f;
                Vector3 position = pose.Position
                    + right * side * Next(random, 4.2f, 18f)
                    + forward * Next(random, -3.5f, 3.5f);
                position.y = ProceduralWorld.HeightAt(position.x, position.z);
                if (position.y < -0.55f)
                {
                    continue;
                }

                float height = Next(random, 1.15f, 3.25f);
                float width = height * Next(random, 0.76f, 1.08f);
                int sectorIndex = Mathf.Clamp(
                    Mathf.FloorToInt(distance / spline.Length * sectorCount),
                    0,
                    sectorCount - 1);
                AddFoliageCross(
                    verticesBySector[sectorIndex],
                    uvsBySector[sectorIndex],
                    trianglesBySector[sectorIndex],
                    position,
                    width,
                    height,
                    Next(random, 0f, 180f),
                    placed % 4);
                placed++;
            }

            for (int sector = 0; sector < sectorCount; sector++)
            {
                if (verticesBySector[sector].Count == 0)
                {
                    continue;
                }

                Mesh mesh = TrackMeshFactory.NewMesh($"Trackside Foliage {sector + 1:00}", verticesBySector[sector].Count);
                mesh.SetVertices(verticesBySector[sector]);
                mesh.SetUVs(0, uvsBySector[sector]);
                mesh.SetTriangles(trianglesBySector[sector], 0);
                mesh.RecalculateNormals();
                mesh.RecalculateTangents();
                mesh.RecalculateBounds();
                GameObject understory = TrackMeshFactory.CreateMeshObject(
                    $"Dense Understory Sector {sector + 1:00}",
                    parent,
                    mesh,
                    material);
                MeshRenderer renderer = understory.GetComponent<MeshRenderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
            }
        }

        private static void AddFoliageCross(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 position,
            float width,
            float height,
            float yaw,
            int atlasIndex)
        {
            int column = atlasIndex % 2;
            int row = atlasIndex / 2;
            float uMin = column * 0.5f + 0.012f;
            float uMax = (column + 1) * 0.5f - 0.012f;
            float vMin = row == 0 ? 0.512f : 0.012f;
            float vMax = row == 0 ? 0.988f : 0.488f;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);

            for (int plane = 0; plane < 2; plane++)
            {
                Vector3 axis = rotation * Quaternion.Euler(0f, plane * 90f, 0f) * Vector3.right * (width * 0.5f);
                int start = vertices.Count;
                vertices.Add(position - axis);
                vertices.Add(position + axis);
                vertices.Add(position - axis + Vector3.up * height);
                vertices.Add(position + axis + Vector3.up * height);
                uvs.Add(new Vector2(uMin, vMin));
                uvs.Add(new Vector2(uMax, vMin));
                uvs.Add(new Vector2(uMin, vMax));
                uvs.Add(new Vector2(uMax, vMax));
                triangles.Add(start);
                triangles.Add(start + 2);
                triangles.Add(start + 1);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        private static bool IsNearTrack(Vector3 position, RideSpline spline, float clearance)
        {
            Vector2 candidate = new(position.x, position.z);
            int samples = Mathf.Max(96, Mathf.CeilToInt(spline.Length / 2f));
            float clearanceSquared = clearance * clearance;
            for (int sample = 0; sample < samples; sample++)
            {
                Vector3 trackPosition = spline.PoseAtDistance(spline.Length * sample / samples).Position;
                Vector2 delta = candidate - new Vector2(trackPosition.x, trackPosition.z);
                if (delta.sqrMagnitude < clearanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        public static void BuildDinosaurs(Transform parent, RideSpline spline)
        {
            GameObject dinosaurs = new("Animated 3D Dinosaurs");
            dinosaurs.transform.SetParent(parent, false);

            GameObject apatosaurus = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Apatosaurus",
                "Models/Dinosaurs/Apatosaurus",
                0.18f,
                24f,
                14.5f,
                0.007f);
            AddDinosaurRoam(apatosaurus, 3.2f, 0.54f, 0.3f);

            GameObject youngApatosaurus = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Apatosaurus Young",
                "Models/Dinosaurs/Apatosaurus",
                0.27f,
                -22f,
                9.2f,
                0.008f);
            AddDinosaurRoam(youngApatosaurus, 2.4f, 0.68f, 2.1f);

            GameObject triceratops = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Triceratops",
                "Models/Dinosaurs/Triceratops",
                0.50f,
                -18f,
                5.4f,
                0.009f);
            AddDinosaurRoam(triceratops, 3f, 0.92f, 4.4f);

            GameObject herdLeader = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Triceratops Herd Leader",
                "Models/Dinosaurs/Triceratops",
                0.56f,
                18f,
                4.8f,
                0.009f);
            AddDinosaurRoam(herdLeader, 2.7f, 0.84f, 1.4f);

            GameObject herdYoung = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Triceratops Herd Young",
                "Models/Dinosaurs/Triceratops",
                0.59f,
                22f,
                4.1f,
                0.01f);
            AddDinosaurRoam(herdYoung, 2.2f, 0.96f, 3.2f);

            GameObject tyrannosaurus = CreateDinosaur(
                dinosaurs.transform,
                spline,
                "Tyrannosaurus Rex Chase",
                "Models/Dinosaurs/Trex",
                0.64f,
                4.5f,
                9.5f,
                0.004f);
            if (tyrannosaurus != null)
            {
                DinosaurIdleMotion idleMotion = tyrannosaurus.GetComponent<DinosaurIdleMotion>();
                if (idleMotion != null)
                {
                    UnityEngine.Object.Destroy(idleMotion);
                }

                tyrannosaurus.SetActive(false);
                TyrannosaurusChaseSequence sequence = dinosaurs.AddComponent<TyrannosaurusChaseSequence>();
                sequence.Initialize(spline, tyrannosaurus);
            }
        }

        private static void AddDinosaurRoam(GameObject actor, float radius, float speed, float phase)
        {
            if (actor == null)
            {
                return;
            }

            DinosaurRoamMotion motion = actor.AddComponent<DinosaurRoamMotion>();
            motion.Initialize(actor.transform.position, radius, speed, phase);
        }

        public static void BuildPterosaurs(Transform parent)
        {
            Transform group = parent.Find("Animated 3D Dinosaurs");
            if (group == null)
            {
                GameObject dinosaurs = new("Animated 3D Dinosaurs");
                dinosaurs.transform.SetParent(parent, false);
                group = dinosaurs.transform;
            }

            Vector3[] centers =
            {
                new(28f, 27f, 22f),
                new(29f, 31f, 23f),
                new(27f, 34f, 21f)
            };
            float[] radii = { 13f, 18.5f, 24f };
            float[] speeds = { 5.6f, 6.2f, 6.8f };
            float[] phases = { 0.2f, 2.25f, 4.45f };
            float[] wingspans = { 6.4f, 5.6f, 4.9f };

            for (int i = 0; i < centers.Length; i++)
            {
                GameObject actor = InstantiateSizedModel(
                    group,
                    $"Pteranodon {i + 1}",
                    PteranodonResourcePath,
                    centers[i],
                    Quaternion.identity,
                    wingspans[i],
                    false,
                    0.0045f,
                    true,
                    false);
                if (actor == null)
                {
                    continue;
                }

                PterosaurOrbit orbit = actor.AddComponent<PterosaurOrbit>();
                orbit.Initialize(centers[i], radii[i], speeds[i], phases[i]);
                if (i == 0)
                {
                    EnhancedProceduralAudio.AttachDinosaurCall(actor, "Pteranodon", 7401);
                }
            }
        }

        public static bool AttachCoasterTrain(Transform cart, Material fallbackBody, Material fallbackDark)
        {
            GameObject model = InstantiateSizedModel(
                cart,
                "Abandoned Coaster Cart 3D",
                CoasterCartResourcePath,
                cart.position + Vector3.down * 0.22f,
                cart.rotation * Quaternion.Euler(0f, 90f, 0f),
                0.92f,
                true,
                0f,
                false);

            bool usesDetailedCart = model != null;
            if (model == null)
            {
                model = InstantiateSizedModel(
                    cart,
                    "Coaster Train 3D",
                    "Models/Ride/CoasterTrainFront",
                    cart.position + Vector3.down * 0.42f,
                    cart.rotation * Quaternion.Euler(0f, 180f, 0f),
                    0.92f,
                    true,
                    0f,
                    true);
            }

            if (model == null)
            {
                return false;
            }

            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            if (usesDetailedCart)
            {
                ApplyCoasterCartMaterials(renderers);
                Debug.Log($"[Coaster Cart QA] PASS renderers={renderers.Length}, model=AbandonedCoasterCart, textures=1K PBR");
                return true;
            }

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] == null)
                    {
                        materials[i] = i == 0 ? fallbackBody : fallbackDark;
                    }
                }
                renderer.sharedMaterials = materials;
            }

            return true;
        }

        private static void ApplyCoasterCartMaterials(Renderer[] renderers)
        {
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int index = 0; index < materials.Length; index++)
                {
                    string materialName = materials[index] != null
                        ? materials[index].name
                        : renderer.name;
                    Material replacement = GetCoasterCartMaterial(materialName);
                    if (replacement != null)
                    {
                        materials[index] = replacement;
                    }
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material GetCoasterCartMaterial(string materialName)
        {
            if (materialName.IndexOf("Frame", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return _coasterCartFrameMaterial ??= CreateCoasterCartMaterial("CartFrame", 0.42f);
            }
            if (materialName.IndexOf("Trim", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return _coasterCartTrimMaterial ??= CreateCoasterCartMaterial("CartTrim", 0.34f);
            }

            return _coasterCartBodyMaterial ??= CreateCoasterCartMaterial("CartBody", 0.28f);
        }

        private static Material CreateCoasterCartMaterial(string prefix, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning($"Shader URP Lit ausente para {prefix}.");
                return null;
            }

            const string textureRoot = "Models/Ride/AbandonedCart/";
            Texture2D baseMap = Resources.Load<Texture2D>(textureRoot + prefix + "_BaseColor");
            Texture2D normalMap = Resources.Load<Texture2D>(textureRoot + prefix + "_Normal");
            Texture2D metallicSmoothness = Resources.Load<Texture2D>(textureRoot + prefix + "_MetallicSmoothness");
            Texture2D occlusionMap = Resources.Load<Texture2D>(textureRoot + prefix + "_Occlusion");

            Material material = new(shader)
            {
                name = prefix + " PBR Quest",
                enableInstancing = true
            };
            SetTexture(material, baseMap, "_BaseMap", "_MainTex");
            SetColor(material, "_BaseColor", Color.white);
            SetColor(material, "_Color", Color.white);
            if (normalMap != null)
            {
                SetTexture(material, normalMap, "_BumpMap");
                SetFloat(material, "_BumpScale", 0.88f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (metallicSmoothness != null)
            {
                SetTexture(material, metallicSmoothness, "_MetallicGlossMap");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
            }
            if (occlusionMap != null)
            {
                SetTexture(material, occlusionMap, "_OcclusionMap");
                SetFloat(material, "_OcclusionStrength", 0.86f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
            SetFloat(material, "_Metallic", 1f);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_SpecularHighlights", 1f);
            SetFloat(material, "_EnvironmentReflections", 1f);
            return material;
        }

        private static GameObject CreateDinosaur(
            Transform parent,
            RideSpline spline,
            string name,
            string resourcePath,
            float trackFraction,
            float sideOffset,
            float targetHeight,
            float cullHeight)
        {
            RidePose pose = spline.PoseAtDistance(spline.Length * trackFraction);
            Vector3 right = pose.Rotation * Vector3.right;
            Vector3 position = pose.Position + right * sideOffset;
            position.y = ProceduralWorld.HeightAt(position.x, position.z);
            Vector3 lookDirection = pose.Position - position;
            lookDirection.y = 0f;
            Quaternion rotation = lookDirection.sqrMagnitude > 0.01f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                : Quaternion.identity;

            GameObject actor = InstantiateSizedModel(
                parent,
                name,
                resourcePath,
                position,
                rotation,
                targetHeight,
                true,
                cullHeight);

            if (actor != null)
            {
                EnhancedProceduralAudio.AttachDinosaurCall(actor, name, Mathf.RoundToInt(trackFraction * 10000f));
            }

            return actor;
        }

        private static GameObject InstantiateClosedRockLod(
            Transform parent,
            string name,
            Vector3 position,
            Quaternion rotation,
            float targetHeight,
            Vector2 horizontalRatios,
            int variant,
            Material material,
            bool castShadows)
        {
            if (material == null)
            {
                Debug.LogWarning($"Material 3D de rocha ausente para {name}.");
                return null;
            }

            int normalizedVariant = ((variant % ClosedRockLods.GetLength(0)) + ClosedRockLods.GetLength(0))
                % ClosedRockLods.GetLength(0);
            Mesh[] meshes = new Mesh[3];
            for (int level = 0; level < meshes.Length; level++)
            {
                meshes[level] = GetClosedRockMesh(normalizedVariant, level);
            }

            GameObject actor = new(name);
            actor.transform.SetParent(parent, true);
            actor.transform.SetPositionAndRotation(position, rotation);
            GameObject scaleRoot = new("Closed Rock Scale");
            scaleRoot.transform.SetParent(actor.transform, false);

            Bounds sourceBounds = meshes[0].bounds;
            Vector3 scale = new(
                targetHeight * horizontalRatios.x / Mathf.Max(sourceBounds.size.x, 0.001f),
                targetHeight / Mathf.Max(sourceBounds.size.y, 0.001f),
                targetHeight * horizontalRatios.y / Mathf.Max(sourceBounds.size.z, 0.001f));
            scaleRoot.transform.localScale = scale;
            scaleRoot.transform.localPosition = new Vector3(
                -sourceBounds.center.x * scale.x,
                -sourceBounds.min.y * scale.y,
                -sourceBounds.center.z * scale.z);

            Renderer[][] lodRenderers = new Renderer[3][];
            for (int level = 0; level < meshes.Length; level++)
            {
                GameObject lodObject = new($"LOD{level}");
                lodObject.transform.SetParent(scaleRoot.transform, false);
                MeshFilter filter = lodObject.AddComponent<MeshFilter>();
                filter.sharedMesh = meshes[level];
                MeshRenderer renderer = lodObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows && level == 0
                    ? ShadowCastingMode.On
                    : ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                lodRenderers[level] = new Renderer[] { renderer };
            }

            LODGroup lodGroup = actor.AddComponent<LODGroup>();
            lodGroup.animateCrossFading = false;
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.SetLODs(new[]
            {
                new LOD(0.11f, lodRenderers[0]),
                new LOD(0.04f, lodRenderers[1]),
                new LOD(0.012f, lodRenderers[2])
            });
            lodGroup.RecalculateBounds();
            return actor;
        }

        private static Mesh GetClosedRockMesh(int variant, int lodLevel)
        {
            Mesh cached = ClosedRockLods[variant, lodLevel];
            if (cached != null)
            {
                return cached;
            }

            int[] longitudeSegments = { 56, 32, 18 };
            int[] latitudeSegments = { 32, 18, 10 };
            float seed = 2.71f + variant * 4.37f;
            cached = CreateClosedRockMesh(
                $"Closed Mossy Rock V{variant + 1} LOD{lodLevel}",
                seed,
                longitudeSegments[lodLevel],
                latitudeSegments[lodLevel]);
            ClosedRockLods[variant, lodLevel] = cached;
            return cached;
        }

        private static Mesh CreateClosedRockMesh(
            string name,
            float seed,
            int longitudeSegments,
            int latitudeSegments)
        {
            int stride = longitudeSegments + 1;
            int ringCount = latitudeSegments - 1;
            List<Vector3> vertices = new(2 + ringCount * stride);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(longitudeSegments * (latitudeSegments - 1) * 6);

            vertices.Add(CreateClosedRockVertex(Vector3.up, 0f, 0f, seed));
            uvs.Add(new Vector2(0.5f, 1f));

            for (int latitude = 1; latitude < latitudeSegments; latitude++)
            {
                float v = latitude / (float)latitudeSegments;
                float phi = v * Mathf.PI;
                for (int longitude = 0; longitude <= longitudeSegments; longitude++)
                {
                    float u = longitude / (float)longitudeSegments;
                    float theta = u * Mathf.PI * 2f;
                    Vector3 direction = new(
                        Mathf.Sin(phi) * Mathf.Cos(theta),
                        Mathf.Cos(phi),
                        Mathf.Sin(phi) * Mathf.Sin(theta));
                    vertices.Add(CreateClosedRockVertex(direction, theta, phi, seed));
                    uvs.Add(new Vector2(u, 1f - v));
                }
            }

            int bottom = vertices.Count;
            vertices.Add(CreateClosedRockVertex(Vector3.down, 0f, Mathf.PI, seed));
            uvs.Add(new Vector2(0.5f, 0f));

            int firstRing = 1;
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
            {
                triangles.Add(0);
                triangles.Add(firstRing + longitude + 1);
                triangles.Add(firstRing + longitude);
            }

            for (int ring = 0; ring < ringCount - 1; ring++)
            {
                int upper = firstRing + ring * stride;
                int lower = upper + stride;
                for (int longitude = 0; longitude < longitudeSegments; longitude++)
                {
                    int a = upper + longitude;
                    int b = a + 1;
                    int c = lower + longitude;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }

            int lastRing = firstRing + (ringCount - 1) * stride;
            for (int longitude = 0; longitude < longitudeSegments; longitude++)
            {
                triangles.Add(lastRing + longitude);
                triangles.Add(lastRing + longitude + 1);
                triangles.Add(bottom);
            }

            Mesh mesh = TrackMeshFactory.NewMesh(name, vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            Vector3[] recalculatedNormals = mesh.normals;
            for (int ring = 0; ring < ringCount; ring++)
            {
                int first = firstRing + ring * stride;
                int last = first + longitudeSegments;
                Vector3 seamNormal = (recalculatedNormals[first] + recalculatedNormals[last]).normalized;
                recalculatedNormals[first] = seamNormal;
                recalculatedNormals[last] = seamNormal;
            }
            mesh.normals = recalculatedNormals;
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 CreateClosedRockVertex(
            Vector3 direction,
            float theta,
            float phi,
            float seed)
        {
            float broad = Mathf.Sin(theta * 3.1f + phi * 1.7f + seed) * 0.14f;
            float broken = Mathf.Sin(theta * 7.3f - phi * 4.1f + seed * 1.9f) * 0.082f;
            float strata = Mathf.Sin(phi * 10.8f + theta * 1.7f + seed * 0.6f) * 0.052f;
            float ridge = Mathf.Pow(
                Mathf.Abs(Mathf.Sin(theta * 2.45f + phi * 0.73f + seed * 0.37f)),
                9f) * 0.095f;
            float detail = Mathf.Sin(theta * 15.7f + phi * 11.2f - seed * 0.8f) * 0.034f;
            float asymmetry = direction.x * 0.075f - direction.z * 0.055f;
            float radius = 1f + broad + broken + strata + ridge + detail + asymmetry;
            radius = Mathf.Round(radius * 24f) / 24f;
            Vector3 vertex = direction * radius;
            vertex.y *= 0.88f + Mathf.Sin(theta * 2f + seed) * 0.045f;
            vertex.x += Mathf.Sin(phi * 3.4f + seed) * direction.z * 0.095f;
            vertex.z += Mathf.Cos(phi * 2.8f - seed) * direction.x * 0.085f;
            if (vertex.y < -0.52f)
            {
                vertex.y = Mathf.Lerp(vertex.y, -0.72f, 0.48f);
            }
            return vertex;
        }

        private static Material GetClosedRockMaterial()
        {
            if (_closedRockMaterial != null)
            {
                return _closedRockMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            Texture2D albedo = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Albedo")
                ?? Resources.Load<Texture2D>("Textures/Realistic/MossyBasalt_Albedo")
                ?? Resources.Load<Texture2D>("Textures/VolcanicRock");
            Texture2D normal = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Normal")
                ?? Resources.Load<Texture2D>("Textures/Realistic/MossyBasalt_Normal");
            _closedRockMaterial = new Material(shader)
            {
                name = "Closed 3D Mossy Rock PBR",
                enableInstancing = true,
                mainTexture = albedo,
                mainTextureScale = new Vector2(2.2f, 1.85f)
            };
            SetTexture(_closedRockMaterial, albedo, "_BaseMap", "_MainTex");
            SetColor(_closedRockMaterial, "_BaseColor", new Color(0.96f, 0.98f, 0.93f, 1f));
            SetColor(_closedRockMaterial, "_Color", new Color(0.96f, 0.98f, 0.93f, 1f));
            SetFloat(_closedRockMaterial, "_Metallic", 0f);
            SetFloat(_closedRockMaterial, "_Smoothness", 0.21f);
            SetFloat(_closedRockMaterial, "_SpecularHighlights", 1f);
            SetFloat(_closedRockMaterial, "_EnvironmentReflections", 1f);
            if (normal != null)
            {
                SetTexture(_closedRockMaterial, normal, "_BumpMap");
                SetFloat(_closedRockMaterial, "_BumpScale", 1.08f);
                _closedRockMaterial.EnableKeyword("_NORMALMAP");
            }

            Vector2 textureScale = new(2.2f, 1.85f);
            if (_closedRockMaterial.HasProperty("_BaseMap"))
            {
                _closedRockMaterial.SetTextureScale("_BaseMap", textureScale);
            }
            if (_closedRockMaterial.HasProperty("_BumpMap"))
            {
                _closedRockMaterial.SetTextureScale("_BumpMap", textureScale);
            }
            return _closedRockMaterial;
        }

        private static Material GetShorelineMaterial()
        {
            if (_shorelineMaterial != null)
            {
                return _shorelineMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }

            Texture2D albedo = Resources.Load<Texture2D>("Textures/Realistic/Shoreline/WetShore_Albedo");
            Texture2D normal = Resources.Load<Texture2D>("Textures/Realistic/Shoreline/WetShore_Normal");
            _shorelineMaterial = new Material(shader)
            {
                name = "Wet Tropical Shoreline PBR",
                enableInstancing = true,
                mainTexture = albedo
            };
            SetTexture(_shorelineMaterial, albedo, "_BaseMap", "_MainTex");
            SetColor(_shorelineMaterial, "_BaseColor", new Color(0.86f, 0.82f, 0.74f, 1f));
            SetColor(_shorelineMaterial, "_Color", new Color(0.86f, 0.82f, 0.74f, 1f));
            SetFloat(_shorelineMaterial, "_Metallic", 0f);
            SetFloat(_shorelineMaterial, "_Smoothness", 0.24f);
            SetFloat(_shorelineMaterial, "_EnvironmentReflections", 1f);
            if (normal != null)
            {
                SetTexture(_shorelineMaterial, normal, "_BumpMap");
                SetFloat(_shorelineMaterial, "_BumpScale", 0.62f);
                _shorelineMaterial.EnableKeyword("_NORMALMAP");
            }
            return _shorelineMaterial;
        }

        private static Material CreateTransparentShoreMaterial(
            Material source,
            string name,
            float alpha)
        {
            Material material = new(source)
            {
                name = name,
                enableInstancing = true,
                renderQueue = (int)RenderQueue.Transparent
            };
            Color color = new(0.86f, 0.82f, 0.74f, alpha);
            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            SetFloat(material, "_Surface", 1f);
            SetFloat(material, "_Blend", 0f);
            SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
            SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            SetFloat(material, "_ZWrite", 0f);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            return material;
        }

        private static GameObject InstantiateEnvironmentLod(
            Transform parent,
            string name,
            string resourcePrefix,
            Vector3 position,
            Quaternion rotation,
            float targetHeight,
            Material primaryMaterial,
            bool palmMaterials,
            bool castShadows,
            Material secondaryMaterial = null)
        {
            GameObject[] prefabs = new GameObject[3];
            for (int level = 0; level < prefabs.Length; level++)
            {
                prefabs[level] = Resources.Load<GameObject>($"{resourcePrefix}_LOD{level}");
                if (prefabs[level] == null)
                {
                    Debug.LogWarning($"LOD 3D não encontrado: {resourcePrefix}_LOD{level}");
                    return null;
                }
            }

            GameObject actor = new(name);
            actor.transform.SetParent(parent, true);
            actor.transform.SetPositionAndRotation(position, rotation);
            GameObject scaleRoot = new("Model Scale");
            scaleRoot.transform.SetParent(actor.transform, false);
            Renderer[][] lodRenderers = new Renderer[3][];

            for (int level = 0; level < prefabs.Length; level++)
            {
                GameObject lodRoot = new($"LOD{level}");
                lodRoot.transform.SetParent(scaleRoot.transform, false);
                GameObject visual = UnityEngine.Object.Instantiate(prefabs[level], lodRoot.transform);
                visual.name = "Visual";
                visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
                visual.transform.localScale = Vector3.one;
                lodRenderers[level] = visual.GetComponentsInChildren<Renderer>(true);

                foreach (Renderer renderer in lodRenderers[level])
                {
                    if (palmMaterials)
                    {
                        bool isLeaf = IsPalmLeafRenderer(renderer);
                        renderer.sharedMaterial = isLeaf && secondaryMaterial != null
                            ? secondaryMaterial
                            : primaryMaterial;
                    }
                    else
                    {
                        renderer.sharedMaterial = primaryMaterial;
                    }

                    renderer.shadowCastingMode = castShadows && level == 0
                        ? ShadowCastingMode.On
                        : ShadowCastingMode.Off;
                    renderer.receiveShadows = true;
                }
            }

            if (lodRenderers[0].Length == 0
                || !TryGetBounds(lodRenderers[0], out Bounds bounds)
                || bounds.size.y < 0.0001f)
            {
                UnityEngine.Object.Destroy(actor);
                Debug.LogWarning($"Modelo LOD sem renderer utilizável: {resourcePrefix}");
                return null;
            }

            scaleRoot.transform.localScale = Vector3.one * (targetHeight / bounds.size.y);
            TryGetBounds(lodRenderers[0], out bounds);
            scaleRoot.transform.position += new Vector3(
                position.x - bounds.center.x,
                position.y - bounds.min.y,
                position.z - bounds.center.z);

            LODGroup lodGroup = actor.AddComponent<LODGroup>();
            lodGroup.animateCrossFading = false;
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.SetLODs(new[]
            {
                new LOD(0.12f, lodRenderers[0]),
                new LOD(0.045f, lodRenderers[1]),
                new LOD(0.012f, lodRenderers[2])
            });
            lodGroup.RecalculateBounds();
            return actor;
        }

        private static bool IsPalmLeafRenderer(Renderer renderer)
        {
            if (renderer.name.IndexOf("leav", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            if (renderer is SkinnedMeshRenderer skinned
                && skinned.sharedMesh != null
                && skinned.sharedMesh.name.IndexOf("leav", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            return filter != null
                && filter.sharedMesh != null
                && filter.sharedMesh.name.IndexOf("leav", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Material GetHeroPalmBarkMaterial()
        {
            return _heroPalmBarkMaterial ??= CreateHeroPbrMaterial(
                "Hero Coconut Palm Bark",
                "Models/EnvironmentHero/CoconutPalm/CoconutPalm_Albedo",
                null,
                null,
                null,
                0.16f,
                false);
        }

        private static Material GetHeroPalmLeafMaterial()
        {
            return _heroPalmLeafMaterial ??= CreateHeroPbrMaterial(
                "Hero Coconut Palm Leaves",
                "Models/EnvironmentHero/CoconutPalm/CoconutPalm_Albedo",
                null,
                null,
                null,
                0.12f,
                true);
        }

        private static Material GetHeroBoulderMaterial()
        {
            return _heroBoulderMaterial ??= CreateHeroPbrMaterial(
                "Photogrammetry Boulder PBR",
                "Models/EnvironmentHero/Boulder01/Boulder01_Albedo",
                "Models/EnvironmentHero/Boulder01/Boulder01_Normal",
                "Models/EnvironmentHero/Boulder01/Boulder01_SpecGloss",
                "Models/EnvironmentHero/Boulder01/Boulder01_Occlusion",
                0.22f,
                false);
        }

        private static Material GetHeroMountainsideMaterial()
        {
            return _heroMountainsideMaterial ??= CreateHeroPbrMaterial(
                "Photogrammetry Mountainside PBR",
                "Models/EnvironmentHero/Mountainside/Mountainside_Albedo",
                "Models/EnvironmentHero/Mountainside/Mountainside_Normal",
                "Models/EnvironmentHero/Mountainside/Mountainside_SpecGloss",
                "Models/EnvironmentHero/Mountainside/Mountainside_Occlusion",
                0.18f,
                false);
        }

        private static Material CreateHeroPbrMaterial(
            string name,
            string albedoPath,
            string normalPath,
            string specGlossPath,
            string occlusionPath,
            float smoothness,
            bool alphaClip)
        {
            bool fullPbr = !string.IsNullOrEmpty(normalPath)
                || !string.IsNullOrEmpty(specGlossPath)
                || !string.IsNullOrEmpty(occlusionPath);
            Shader shader = Shader.Find(fullPbr
                ? "Universal Render Pipeline/Lit"
                : "Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning($"Shader URP Simple Lit ausente para {name}.");
                return null;
            }

            Material material = new(shader)
            {
                name = name,
                enableInstancing = true
            };
            Texture2D albedo = Resources.Load<Texture2D>(albedoPath);
            Texture2D normal = string.IsNullOrEmpty(normalPath) ? null : Resources.Load<Texture2D>(normalPath);
            Texture2D specGloss = string.IsNullOrEmpty(specGlossPath) ? null : Resources.Load<Texture2D>(specGlossPath);
            Texture2D occlusion = string.IsNullOrEmpty(occlusionPath) ? null : Resources.Load<Texture2D>(occlusionPath);
            SetTexture(material, albedo, "_BaseMap", "_MainTex");
            SetColor(material, "_BaseColor", Color.white);
            SetColor(material, "_Color", Color.white);
            if (normal != null)
            {
                SetTexture(material, normal, "_BumpMap");
                SetFloat(material, "_BumpScale", 0.82f);
                material.EnableKeyword("_NORMALMAP");
            }
            if (specGloss != null)
            {
                SetTexture(material, specGloss, "_SpecGlossMap");
                SetFloat(material, "_WorkflowMode", 0f);
                material.EnableKeyword("_SPECULAR_SETUP");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            if (occlusion != null)
            {
                SetTexture(material, occlusion, "_OcclusionMap");
                SetFloat(material, "_OcclusionStrength", 0.78f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", smoothness);
            SetFloat(material, "_SpecularHighlights", 1f);
            SetFloat(material, "_EnvironmentReflections", 1f);
            if (alphaClip)
            {
                SetFloat(material, "_AlphaClip", 1f);
                SetFloat(material, "_Cutoff", 0.36f);
                SetFloat(material, "_Cull", 0f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }
            return material;
        }

        private static GameObject InstantiateSizedModel(
            Transform parent,
            string name,
            string resourcePath,
            Vector3 position,
            Quaternion rotation,
            float targetHeight,
            bool castShadows,
            float cullHeight,
            bool scaleByLargestDimension = false,
            bool alignToGround = true)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogWarning($"Modelo 3D não encontrado: {resourcePath}");
                return null;
            }

            GameObject actor = new(name);
            actor.transform.SetParent(parent, true);
            actor.transform.SetPositionAndRotation(position, rotation);

            GameObject normalizationRoot = new("Model Scale");
            normalizationRoot.transform.SetParent(actor.transform, false);
            GameObject visual = UnityEngine.Object.Instantiate(prefab, normalizationRoot.transform);
            visual.name = "Visual";
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            visual.transform.localScale = Vector3.one;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0 || !TryGetBounds(renderers, out Bounds bounds) || bounds.size.y < 0.0001f)
            {
                UnityEngine.Object.Destroy(actor);
                Debug.LogWarning($"Modelo sem renderer utilizável: {resourcePath}");
                return null;
            }

            float sourceSize = scaleByLargestDimension
                ? Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z))
                : bounds.size.y;
            normalizationRoot.transform.localScale = Vector3.one * (targetHeight / sourceSize);
            TryGetBounds(renderers, out bounds);
            Vector3 alignment = alignToGround
                ? new Vector3(
                    position.x - bounds.center.x,
                    position.y - bounds.min.y,
                    position.z - bounds.center.z)
                : position - bounds.center;
            normalizationRoot.transform.position += alignment;

            ConfigureRenderers(renderers, castShadows);
            if (string.Equals(resourcePath, PteranodonResourcePath, StringComparison.OrdinalIgnoreCase))
            {
                ApplyPteranodonMaterial(renderers);
            }
            if (cullHeight > 0f)
            {
                AddCullLod(actor, renderers, cullHeight);
            }
            return actor;
        }

        private static void ApplyPteranodonMaterial(Renderer[] renderers)
        {
            Material material = GetPteranodonMaterial();
            if (material == null)
            {
                return;
            }

            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static Material GetPteranodonMaterial()
        {
            if (_pteranodonMaterial != null)
            {
                return _pteranodonMaterial;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null)
            {
                Debug.LogWarning("Shader URP Simple Lit ausente para o Pteranodon.");
                return null;
            }

            const string textureRoot = "Models/Dinosaurs/Pteranodon/";
            Texture2D baseMap = Resources.Load<Texture2D>(textureRoot + "Pteranodon_BaseColor");
            Texture2D normalMap = Resources.Load<Texture2D>(textureRoot + "Pteranodon_Normal");
            Texture2D specGloss = Resources.Load<Texture2D>(textureRoot + "Pteranodon_SpecGloss");
            Texture2D occlusion = Resources.Load<Texture2D>(textureRoot + "Pteranodon_Occlusion");

            _pteranodonMaterial = new Material(shader)
            {
                name = "Pteranodon PBR Quest",
                enableInstancing = true
            };
            if (_pteranodonMaterial.HasProperty("_BaseMap"))
            {
                _pteranodonMaterial.SetTexture("_BaseMap", baseMap);
            }
            if (_pteranodonMaterial.HasProperty("_BaseColor"))
            {
                _pteranodonMaterial.SetColor("_BaseColor", Color.white);
            }
            if (normalMap != null && _pteranodonMaterial.HasProperty("_BumpMap"))
            {
                _pteranodonMaterial.SetTexture("_BumpMap", normalMap);
                _pteranodonMaterial.SetFloat("_BumpScale", 0.82f);
                _pteranodonMaterial.EnableKeyword("_NORMALMAP");
            }
            if (specGloss != null && _pteranodonMaterial.HasProperty("_SpecGlossMap"))
            {
                _pteranodonMaterial.SetTexture("_SpecGlossMap", specGloss);
                _pteranodonMaterial.EnableKeyword("_SPECGLOSSMAP");
            }
            if (occlusion != null && _pteranodonMaterial.HasProperty("_OcclusionMap"))
            {
                _pteranodonMaterial.SetTexture("_OcclusionMap", occlusion);
                _pteranodonMaterial.SetFloat("_OcclusionStrength", 0.72f);
                _pteranodonMaterial.EnableKeyword("_OCCLUSIONMAP");
            }
            SetFloat(_pteranodonMaterial, "_Smoothness", 0.34f);
            SetFloat(_pteranodonMaterial, "_SpecularHighlights", 1f);
            SetFloat(_pteranodonMaterial, "_EnvironmentReflections", 1f);
            SetFloat(_pteranodonMaterial, "_Cull", 0f);
            return _pteranodonMaterial;
        }

        private static void ConfigureRenderers(Renderer[] renderers, bool castShadows)
        {
            foreach (Renderer renderer in renderers)
            {
                renderer.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
                renderer.receiveShadows = castShadows;
                renderer.forceRenderingOff = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = castShadows;
                    skinned.quality = SkinQuality.Bone2;
                }

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        material.enableInstancing = true;
                        if (castShadows && material.HasProperty("_Cull"))
                        {
                            material.SetFloat("_Cull", 0f);
                        }
                    }
                }
            }
        }

        private static void AddCullLod(GameObject root, Renderer[] renderers, float cullHeight)
        {
            LODGroup lodGroup = root.AddComponent<LODGroup>();
            lodGroup.animateCrossFading = false;
            lodGroup.fadeMode = LODFadeMode.None;
            lodGroup.SetLODs(new[] { new LOD(cullHeight, renderers) });
            lodGroup.RecalculateBounds();
        }

        private static void PlayIdleAnimation(GameObject actor, string resourcePath)
        {
            Animation animation = actor.GetComponentInChildren<Animation>();
            if (animation == null)
            {
                Transform visual = actor.transform.Find("Model Scale/Visual");
                animation = visual != null ? visual.gameObject.AddComponent<Animation>() : actor.AddComponent<Animation>();
            }

            AnimationClip[] clips = Resources.LoadAll<AnimationClip>(resourcePath);
            AnimationClip selected = null;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (animation.GetClip(clip.name) == null)
                {
                    animation.AddClip(clip, clip.name);
                }
                selected ??= clip;
                if (clip.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    selected = clip;
                    break;
                }
            }

            if (selected == null)
            {
                foreach (AnimationState state in animation)
                {
                    selected = animation.GetClip(state.name);
                    if (state.name.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        break;
                    }
                }
            }

            if (selected != null)
            {
                AnimationState state = animation[selected.name];
                state.wrapMode = WrapMode.Loop;
                state.speed = UnityEngine.Random.Range(0.82f, 1.06f);
                animation.Play(selected.name);
            }
        }

        private static bool TryGetBounds(Renderer[] renderers, out Bounds bounds)
        {
            bounds = default;
            bool initialized = false;
            foreach (Renderer renderer in renderers)
            {
                if (!renderer.enabled)
                {
                    continue;
                }

                if (!initialized)
                {
                    bounds = renderer.bounds;
                    initialized = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return initialized;
        }

        private static Mesh CreateLagoonMesh(int segments, int rings, float radiusX, float radiusZ)
        {
            List<Vector3> vertices = new(1 + segments * rings) { Vector3.zero };
            List<Vector2> uvs = new(1 + segments * rings) { new Vector2(0.5f, 0.5f) };
            List<int> triangles = new(segments * (rings * 6 - 3));

            for (int ring = 1; ring <= rings; ring++)
            {
                float ratio = ring / (float)rings;
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float contour = Mathf.Lerp(1f, LagoonContour(angle), ratio);
                    float x = Mathf.Cos(angle) * radiusX * ratio * contour;
                    float z = Mathf.Sin(angle) * radiusZ * ratio * contour;
                    vertices.Add(new Vector3(x, Mathf.Sin(angle * 3f) * 0.025f * ratio, z));
                    uvs.Add(new Vector2(x / (radiusX * 2f) + 0.5f, z / (radiusZ * 2f) + 0.5f));
                }
            }

            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(0);
                triangles.Add(1 + next);
                triangles.Add(1 + segment);
            }

            for (int ring = 1; ring < rings; ring++)
            {
                int inner = 1 + (ring - 1) * segments;
                int outer = 1 + ring * segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles.Add(inner + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);
                    triangles.Add(outer + segment);
                }
            }

            Mesh mesh = TrackMeshFactory.NewMesh("Optimized Elliptical Lagoon", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float LagoonContour(float angle)
        {
            return 1f
                + Mathf.Sin(angle * 3f + 0.42f) * 0.034f
                + Mathf.Sin(angle * 7f - 1.18f) * 0.017f
                + Mathf.Sin(angle * 11f + 2.07f) * 0.008f;
        }

        private static Mesh CreateLagoonRingMesh(
            int segments,
            int radialBands,
            float innerRadiusX,
            float innerRadiusZ,
            float outerRadiusX,
            float outerRadiusZ,
            float heightOffset)
        {
            List<Vector3> vertices = new((radialBands + 1) * segments);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(radialBands * segments * 6);

            for (int band = 0; band <= radialBands; band++)
            {
                float t = band / (float)radialBands;
                float radiusX = Mathf.Lerp(innerRadiusX, outerRadiusX, t);
                float radiusZ = Mathf.Lerp(innerRadiusZ, outerRadiusZ, t);
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float contour = LagoonContour(angle);
                    float x = Mathf.Cos(angle) * radiusX * contour;
                    float z = Mathf.Sin(angle) * radiusZ * contour;
                    float wave = Mathf.Sin(angle * 4f + t * 2.3f) * 0.014f;
                    vertices.Add(new Vector3(x, heightOffset + wave, z));
                    uvs.Add(new Vector2(x / 18f, z / 18f));
                }
            }

            AddRingTriangles(triangles, segments, radialBands);
            Mesh mesh = TrackMeshFactory.NewMesh("Layered Shallow Lagoon Ring", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTerrainShorelineMesh(
            int segments,
            int radialBands,
            Vector2 worldCenter,
            float innerRadiusX,
            float innerRadiusZ,
            float outerRadiusX,
            float outerRadiusZ,
            float waterHeight)
        {
            List<Vector3> vertices = new((radialBands + 1) * segments);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(radialBands * segments * 6);

            for (int band = 0; band <= radialBands; band++)
            {
                float t = band / (float)radialBands;
                float smoothT = Mathf.SmoothStep(0f, 1f, t);
                float radiusX = Mathf.Lerp(innerRadiusX, outerRadiusX, t);
                float radiusZ = Mathf.Lerp(innerRadiusZ, outerRadiusZ, t);
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float contour = LagoonContour(angle);
                    float x = Mathf.Cos(angle) * radiusX * contour;
                    float z = Mathf.Sin(angle) * radiusZ * contour;
                    float worldX = worldCenter.x + x;
                    float worldZ = worldCenter.y + z;
                    float terrainHeight = ProceduralWorld.HeightAt(worldX, worldZ) + 0.055f;
                    float y = Mathf.Lerp(waterHeight - 0.055f, terrainHeight, smoothT);
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(worldX / 5.4f, worldZ / 5.4f));
                }
            }

            AddRingTriangles(triangles, segments, radialBands);
            Mesh mesh = TrackMeshFactory.NewMesh("Terrain Matched Wet Shoreline", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateTerrainOverlayRingMesh(
            int segments,
            int radialBands,
            Vector2 worldCenter,
            float innerRadiusX,
            float innerRadiusZ,
            float outerRadiusX,
            float outerRadiusZ)
        {
            List<Vector3> vertices = new((radialBands + 1) * segments);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(radialBands * segments * 6);

            for (int band = 0; band <= radialBands; band++)
            {
                float t = band / (float)radialBands;
                float radiusX = Mathf.Lerp(innerRadiusX, outerRadiusX, t);
                float radiusZ = Mathf.Lerp(innerRadiusZ, outerRadiusZ, t);
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float contour = LagoonContour(angle);
                    float x = Mathf.Cos(angle) * radiusX * contour;
                    float z = Mathf.Sin(angle) * radiusZ * contour;
                    float worldX = worldCenter.x + x;
                    float worldZ = worldCenter.y + z;
                    float y = ProceduralWorld.HeightAt(worldX, worldZ) + 0.065f + t * 0.004f;
                    vertices.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(worldX / 5.4f, worldZ / 5.4f));
                }
            }

            AddRingTriangles(triangles, segments, radialBands);
            Mesh mesh = TrackMeshFactory.NewMesh("Terrain Matched Shoreline Blend", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddRingTriangles(List<int> triangles, int segments, int radialBands)
        {
            for (int band = 0; band < radialBands; band++)
            {
                int inner = band * segments;
                int outer = (band + 1) * segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles.Add(inner + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);
                    triangles.Add(outer + segment);
                }
            }
        }

        private static Mesh CreateWaterfallSourceStreamMesh(
            int segments,
            float width,
            float length,
            float depth,
            float rise)
        {
            List<Vector3> vertices = new((segments + 1) * 4);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(segments * 24 + 12);

            for (int segment = 0; segment <= segments; segment++)
            {
                float t = segment / (float)segments;
                float localWidth = width
                    * Mathf.Lerp(0.72f, 1f, t)
                    * (0.96f + Mathf.Sin(t * 15f + 1.7f) * 0.04f);
                float halfWidth = localWidth * 0.5f;
                float z = 0.85f + length * t;
                float top = rise * t + Mathf.Sin(t * 19f) * 0.025f;
                int first = vertices.Count;
                vertices.Add(new Vector3(-halfWidth, top, z));
                vertices.Add(new Vector3(halfWidth, top, z));
                vertices.Add(new Vector3(-halfWidth, top - depth, z));
                vertices.Add(new Vector3(halfWidth, top - depth, z));
                uvs.Add(new Vector2(0f, t * 2.4f));
                uvs.Add(new Vector2(1f, t * 2.4f));
                uvs.Add(new Vector2(0f, t * 2.4f));
                uvs.Add(new Vector2(1f, t * 2.4f));

                if (segment == segments)
                {
                    continue;
                }

                int next = first + 4;
                triangles.Add(first); triangles.Add(next); triangles.Add(first + 1);
                triangles.Add(first + 1); triangles.Add(next); triangles.Add(next + 1);
                triangles.Add(first + 2); triangles.Add(first + 3); triangles.Add(next + 2);
                triangles.Add(first + 3); triangles.Add(next + 3); triangles.Add(next + 2);
                triangles.Add(first); triangles.Add(first + 2); triangles.Add(next);
                triangles.Add(first + 2); triangles.Add(next + 2); triangles.Add(next);
                triangles.Add(first + 1); triangles.Add(next + 1); triangles.Add(first + 3);
                triangles.Add(first + 3); triangles.Add(next + 1); triangles.Add(next + 3);
            }

            triangles.Add(0); triangles.Add(1); triangles.Add(2);
            triangles.Add(1); triangles.Add(3); triangles.Add(2);
            int end = segments * 4;
            triangles.Add(end); triangles.Add(end + 2); triangles.Add(end + 1);
            triangles.Add(end + 1); triangles.Add(end + 2); triangles.Add(end + 3);

            Mesh mesh = TrackMeshFactory.NewMesh("Closed 3D Waterfall Source Stream", vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddMountain(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            float radiusX,
            float radiusZ,
            float height,
            float seed)
        {
            const int segments = 32;
            const int rings = 7;
            Vector3 peakOffset = new(
                Mathf.Sin(seed * 1.7f) * radiusX * 0.14f,
                0f,
                Mathf.Cos(seed * 1.3f) * radiusZ * 0.14f);
            int top = vertices.Count;
            vertices.Add(center + peakOffset + Vector3.up * height);
            uvs.Add(new Vector2(0.5f, 1f));

            for (int ring = 1; ring <= rings; ring++)
            {
                float t = ring / (float)rings;
                float radial = Mathf.Pow(t, 0.79f);
                float y = height * (1f - Mathf.Pow(t, 1.18f));
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    float ridge = Mathf.Sin(angle * 3f + seed) * 0.105f
                                  + Mathf.Sin(angle * 7f - seed * 0.7f) * 0.045f;
                    float erosion = (Mathf.PerlinNoise(seed + segment * 0.21f, ring * 0.31f) - 0.5f) * 0.105f;
                    float radius = radial * (1f + ridge + erosion);
                    float ledge = (Mathf.PerlinNoise(seed * 0.3f + segment * 0.09f, ring * 0.54f) - 0.5f) * height * 0.035f * t;
                    Vector3 ringCenter = center + peakOffset * (1f - t);
                    Vector3 point = ringCenter + new Vector3(
                        Mathf.Cos(angle) * radiusX * radius,
                        y + ledge,
                        Mathf.Sin(angle) * radiusZ * radius);
                    vertices.Add(point);
                    uvs.Add(new Vector2(segment / (float)segments * 3f, t * 3f));
                }
            }

            int firstRing = top + 1;
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(top);
                triangles.Add(firstRing + next);
                triangles.Add(firstRing + segment);
            }

            for (int ring = 1; ring < rings; ring++)
            {
                int inner = firstRing + (ring - 1) * segments;
                int outer = firstRing + ring * segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles.Add(inner + segment);
                    triangles.Add(outer + next);
                    triangles.Add(outer + segment);
                    triangles.Add(inner + segment);
                    triangles.Add(inner + next);
                    triangles.Add(outer + next);
                }
            }
        }

        private static void AddBoulder(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            Vector3 scale,
            float yaw,
            System.Random random)
        {
            const int segments = 8;
            const int rings = 3;
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            float[] radialNoise = new float[segments];
            for (int segment = 0; segment < segments; segment++)
            {
                radialNoise[segment] = Next(random, 0.78f, 1.18f);
            }

            int top = vertices.Count;
            vertices.Add(center + rotation * Vector3.Scale(Vector3.up * Next(random, 0.86f, 1.08f), scale));
            uvs.Add(new Vector2(0.5f, 1f));

            for (int ring = 0; ring < rings; ring++)
            {
                float latitude = Mathf.Lerp(0.56f, -0.48f, ring / (float)(rings - 1));
                float horizontal = Mathf.Sqrt(1f - latitude * latitude);
                for (int segment = 0; segment < segments; segment++)
                {
                    float angle = segment * Mathf.PI * 2f / segments;
                    Vector3 unit = new(
                        Mathf.Cos(angle) * horizontal * radialNoise[segment],
                        latitude * Next(random, 0.88f, 1.08f),
                        Mathf.Sin(angle) * horizontal * radialNoise[segment]);
                    vertices.Add(center + rotation * Vector3.Scale(unit, scale));
                    uvs.Add(new Vector2(segment / (float)segments * 2f, (1f - latitude) * 0.85f));
                }
            }

            int bottom = vertices.Count;
            vertices.Add(center + rotation * Vector3.Scale(Vector3.down * Next(random, 0.72f, 0.92f), scale));
            uvs.Add(new Vector2(0.5f, 0f));
            int firstRing = top + 1;
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(top);
                triangles.Add(firstRing + segment);
                triangles.Add(firstRing + next);
            }

            for (int ring = 0; ring < rings - 1; ring++)
            {
                int upper = firstRing + ring * segments;
                int lower = upper + segments;
                for (int segment = 0; segment < segments; segment++)
                {
                    int next = (segment + 1) % segments;
                    triangles.Add(upper + segment);
                    triangles.Add(lower + segment);
                    triangles.Add(upper + next);
                    triangles.Add(upper + next);
                    triangles.Add(lower + segment);
                    triangles.Add(lower + next);
                }
            }

            int lastRing = firstRing + (rings - 1) * segments;
            for (int segment = 0; segment < segments; segment++)
            {
                int next = (segment + 1) % segments;
                triangles.Add(lastRing + segment);
                triangles.Add(bottom);
                triangles.Add(lastRing + next);
            }
        }

        private static Texture2D CreateWaterTexture()
        {
            const int size = 128;
            Texture2D texture = new(size, size, TextureFormat.RGB24, true, true)
            {
                name = "Procedural Lagoon Ripples",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };
            Color[] pixels = new Color[size * size];
            Color deep = new(0.70f, 0.79f, 0.80f);
            Color crest = new(0.91f, 0.97f, 0.97f);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float broad = Mathf.PerlinNoise(x * 0.052f, y * 0.052f);
                    float detail = Mathf.PerlinNoise(x * 0.16f + 17f, y * 0.16f + 29f);
                    float wave = Mathf.Clamp01(0.43f + broad * 0.36f + detail * 0.12f);
                    pixels[y * size + x] = Color.Lerp(deep, crest, wave);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D CreateWaterfallTexture()
        {
            const int size = 256;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Procedural Waterfall Streaks",
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };
            texture.wrapModeU = TextureWrapMode.Clamp;
            texture.wrapModeV = TextureWrapMode.Mirror;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)(size - 1);
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.02f, 0.14f, u))
                        * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.98f, u)));
                    float broad = Mathf.PerlinNoise(u * 5.5f + 8f, v * 2.2f + 17f);
                    float streak = Mathf.PerlinNoise(u * 24f + broad * 2.4f, v * 5f + 31f);
                    float channels = Mathf.SmoothStep(0.22f, 0.84f, broad * 0.66f + streak * 0.44f);
                    float alpha = edge
                        * Mathf.SmoothStep(0.34f, 0.72f, channels)
                        * Mathf.Lerp(0.35f, 0.82f, streak);
                    float brightness = Mathf.Lerp(0.64f, 1f, streak * 0.55f + channels * 0.45f);
                    pixels[y * size + x] = new Color(
                        0.86f * brightness,
                        0.95f * brightness,
                        1f * brightness,
                        alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D CreateWaterfallNormalTexture()
        {
            const int size = 128;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Procedural Waterfall Flow Normal",
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };
            texture.wrapModeU = TextureWrapMode.Clamp;
            texture.wrapModeV = TextureWrapMode.Mirror;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                float v = y / (float)size;
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)size;
                    float phaseA = (u * 9f + v * 2.2f) * Mathf.PI * 2f;
                    float phaseB = (u * 21f - v * 3.6f + 0.37f) * Mathf.PI * 2f;
                    float phaseC = (u * 4f + v * 8f + 0.71f) * Mathf.PI * 2f;
                    float dx = Mathf.Cos(phaseA) * 0.20f
                        + Mathf.Cos(phaseB) * 0.09f
                        + Mathf.Cos(phaseC) * 0.045f;
                    float dy = Mathf.Sin(phaseA) * 0.035f
                        + Mathf.Sin(phaseB) * 0.025f
                        + Mathf.Sin(phaseC) * 0.08f;
                    Vector3 normal = new Vector3(-dx, -dy, 1f).normalized;
                    pixels[y * size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static Texture2D CreateWaterNormalTexture()
        {
            const int size = 128;
            Texture2D texture = new(size, size, TextureFormat.RGBA32, true, true)
            {
                name = "Procedural Lagoon Normal",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 1
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float angleA = (x * 5f + y * 3f) * Mathf.PI * 2f / size;
                    float angleB = (x * -3f + y * 7f) * Mathf.PI * 2f / size + 1.8f;
                    float angleC = (x * 9f + y * -4f) * Mathf.PI * 2f / size + 0.6f;
                    float dx = Mathf.Cos(angleA) * 0.08f + Mathf.Cos(angleB) * -0.045f + Mathf.Cos(angleC) * 0.035f;
                    float dy = Mathf.Cos(angleA) * 0.048f + Mathf.Cos(angleB) * 0.105f + Mathf.Cos(angleC) * -0.018f;
                    Vector3 normal = new Vector3(-dx, -dy, 1f).normalized;
                    pixels[y * size + x] = new Color(
                        normal.x * 0.5f + 0.5f,
                        normal.y * 0.5f + 0.5f,
                        normal.z * 0.5f + 0.5f,
                        1f);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(true, true);
            return texture;
        }

        private static void ConfigureSky()
        {
            Material sky = Resources.Load<Material>("Generated/QuestSky");
            if (sky == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");
                if (shader == null)
                {
                    return;
                }
                sky = new Material(shader) { name = "Runtime Jurassic Sky" };
            }

            SetColor(sky, "_SkyTint", new Color(0.28f, 0.48f, 0.53f));
            SetColor(sky, "_GroundColor", new Color(0.20f, 0.25f, 0.16f));
            SetFloat(sky, "_AtmosphereThickness", 0.82f);
            SetFloat(sky, "_Exposure", 1.18f);
            RenderSettings.skybox = sky;
            DynamicGI.UpdateEnvironment();
        }

        private static Material CreatePolyHavenMaterial(
            Material fallback,
            string name,
            string albedoResource,
            string normalResource,
            float smoothness)
        {
            Material material = new(fallback)
            {
                name = name,
                enableInstancing = true
            };
            Texture2D albedo = Resources.Load<Texture2D>(albedoResource);
            Texture2D normal = Resources.Load<Texture2D>(normalResource);
            if (albedo != null)
            {
                material.mainTexture = albedo;
                material.mainTextureScale = Vector2.one;
                if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", albedo);
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
                if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
            }
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 0.78f);
                material.EnableKeyword("_NORMALMAP");
            }
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", smoothness);
            return material;
        }

        private static Material CreatePolyHavenFoliageMaterial(
            string name,
            string albedoResource,
            string normalResource)
        {
            Texture2D albedo = Resources.Load<Texture2D>(albedoResource);
            Material material = ProceduralWorld.CreateFoliageMaterial(name, albedo);
            Texture2D normal = Resources.Load<Texture2D>(normalResource);
            if (normal != null && material.HasProperty("_BumpMap"))
            {
                material.SetTexture("_BumpMap", normal);
                material.SetFloat("_BumpScale", 0.52f);
                material.EnableKeyword("_NORMALMAP");
            }
            SetFloat(material, "_Smoothness", 0.12f);
            SetFloat(material, "_Cutoff", 0.30f);
            return material;
        }

        private static Material CreateOpaquePalmMaterial(
            string name,
            string textureResource,
            Color color,
            float smoothness)
        {
            Texture2D texture = string.IsNullOrEmpty(textureResource)
                ? null
                : Resources.Load<Texture2D>(textureResource);
            Material material = ProceduralWorld.CreateFoliageMaterial(name, texture);
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Geometry;
            material.mainTextureScale = Vector2.one;
            SetFloat(material, "_AlphaClip", 0f);
            SetFloat(material, "_Cull", 0f);
            SetFloat(material, "_Metallic", 0f);
            SetFloat(material, "_Smoothness", smoothness);
            SetColor(material, "_BaseColor", color);
            SetColor(material, "_Color", color);
            return material;
        }

        private static void OverrideMaterials(GameObject root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    materials[i] = material;
                }
                renderer.sharedMaterials = materials;
            }
        }

        private static void SetColor(Material material, string property, Color color)
        {
            if (material.HasProperty(property)) material.SetColor(property, color);
        }

        private static void SetTexture(Material material, Texture texture, params string[] properties)
        {
            if (material == null || texture == null)
            {
                return;
            }

            foreach (string property in properties)
            {
                if (material.HasProperty(property)) material.SetTexture(property, texture);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }

        private static float Next(System.Random random, float minimum, float maximum)
        {
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }
    }

    internal sealed class DinosaurIdleMotion : MonoBehaviour
    {
        private Quaternion _baseRotation;
        private float _phase;

        public void Initialize(float phase)
        {
            _baseRotation = transform.rotation;
            _phase = phase;
        }

        private void Update()
        {
            float yaw = Mathf.Sin(Time.time * 0.32f + _phase) * 2.2f;
            transform.rotation = _baseRotation * Quaternion.Euler(0f, yaw, 0f);
        }
    }

    internal sealed class WaterSurfaceAnimator : MonoBehaviour
    {
        private Material _material;
        private bool _animateColor;
        private readonly Color _deep = new(0.07f, 0.36f, 0.40f, 0.84f);
        private readonly Color _sunlit = new(0.13f, 0.48f, 0.52f, 0.84f);

        public void Initialize(Material material, bool animateColor = true)
        {
            _material = material;
            _animateColor = animateColor;
        }

        private void Update()
        {
            if (_material == null)
            {
                return;
            }

            _material.mainTextureOffset = new Vector2(Time.time * 0.0045f, Time.time * 0.0022f);
            if (_material.HasProperty("_BumpMap"))
            {
                _material.SetTextureOffset("_BumpMap", new Vector2(-Time.time * 0.0031f, Time.time * 0.0017f));
            }
            if (_animateColor)
            {
                Color color = Color.Lerp(_deep, _sunlit, Mathf.Sin(Time.time * 0.42f) * 0.5f + 0.5f);
                if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
                if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
            }
        }
    }

    internal sealed class WaterfallAnimator : MonoBehaviour
    {
        private Material _material;
        private float _speed;
        private float _phase;

        public void Initialize(Material material, float speed = 0.31f, float phase = 0f)
        {
            _material = material;
            _speed = speed;
            _phase = phase;
        }

        private void Update()
        {
            if (_material == null)
            {
                return;
            }

            Vector2 flowOffset = new(
                Mathf.Sin(Time.time * 0.35f + _phase) * 0.018f,
                Time.time * _speed + _phase);
            _material.mainTextureOffset = flowOffset;
            if (_material.HasProperty("_BaseMap"))
            {
                _material.SetTextureOffset("_BaseMap", flowOffset);
            }
            if (_material.HasProperty("_BumpMap"))
            {
                _material.SetTextureOffset("_BumpMap", new Vector2(
                    -flowOffset.x * 0.74f,
                    flowOffset.y * 1.13f));
            }
        }
    }

    internal sealed class LagoonReflectionCapture : MonoBehaviour
    {
        private ReflectionProbe _probe;

        public void Initialize(ReflectionProbe probe)
        {
            _probe = probe;
        }

        private void Start()
        {
            Invoke(nameof(CaptureOnce), 0.45f);
        }

        private void CaptureOnce()
        {
            if (_probe != null)
            {
                _probe.RenderProbe();
            }
        }
    }
}
