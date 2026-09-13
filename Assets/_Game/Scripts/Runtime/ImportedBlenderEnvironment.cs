using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal static class ImportedBlenderEnvironment
    {
        public const string ResourcePath =
            "Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment";
        private const string ManifestResourcePath =
            "Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export";
        private const string TextureResourcePath =
            "Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.fbm/";

        [Serializable]
        private sealed class EnvironmentManifest
        {
            public int objectCount;
            public EnvironmentTemplate[] templates;
            public EnvironmentMaterial[] materials;
            public EnvironmentInstance[] instances;
        }

        [Serializable]
        private sealed class EnvironmentTemplate
        {
            public string id;
            public string[] materials;
        }

        [Serializable]
        private sealed class EnvironmentMaterial
        {
            public string name;
            public Color baseColor = Color.white;
            public float metallic;
            public float smoothness = 0.5f;
            public string baseTexture;
            public string normalTexture;
            public string roughnessTexture;
            public string occlusionTexture;
            public string maskTexture;
            public string alphaTexture;
            public bool alphaClip;
            public bool transparent;
            public bool doubleSided;
            public float cutoff = 0.5f;
        }

        [Serializable]
        private sealed class EnvironmentInstance
        {
            public string template;
            public string name;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
        }

        private readonly struct TemplateMesh
        {
            public TemplateMesh(
                Mesh mesh,
                Material[] materials,
                Vector3 localPosition,
                Quaternion localRotation,
                Vector3 localScale)
            {
                Mesh = mesh;
                Materials = materials;
                LocalPosition = localPosition;
                LocalRotation = localRotation;
                LocalScale = localScale;
            }

            public Mesh Mesh { get; }
            public Material[] Materials { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }
        }

        public static bool TryAttach(Transform parent, out GameObject environment)
        {
            GameObject library = Resources.Load<GameObject>(ResourcePath);
            TextAsset manifestAsset = Resources.Load<TextAsset>(ManifestResourcePath);
            if (library == null || manifestAsset == null)
            {
                environment = null;
                return false;
            }

            EnvironmentManifest manifest = JsonUtility.FromJson<EnvironmentManifest>(manifestAsset.text);
            if (manifest?.instances == null || manifest.instances.Length == 0)
            {
                Debug.LogError("[Vale Blender] Manifesto de instâncias vazio ou inválido.");
                environment = null;
                return false;
            }

            Dictionary<string, EnvironmentTemplate> templateDefinitions = new(StringComparer.Ordinal);
            if (manifest.templates != null)
            {
                foreach (EnvironmentTemplate definition in manifest.templates)
                {
                    if (definition != null && !string.IsNullOrEmpty(definition.id))
                    {
                        templateDefinitions[definition.id] = definition;
                    }
                }
            }

            Dictionary<string, Material> runtimeMaterials = BuildMaterials(manifest.materials);
            Dictionary<string, TemplateMesh> templates = new(StringComparer.Ordinal);
            foreach (MeshFilter filter in library.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null || !filter.TryGetComponent(out MeshRenderer renderer))
                {
                    continue;
                }

                Material[] materials = renderer.sharedMaterials;
                if (templateDefinitions.TryGetValue(filter.name, out EnvironmentTemplate definition)
                    && definition.materials != null)
                {
                    materials = new Material[definition.materials.Length];
                    for (int index = 0; index < definition.materials.Length; index++)
                    {
                        string materialName = definition.materials[index];
                        if (!string.IsNullOrEmpty(materialName)
                            && runtimeMaterials.TryGetValue(materialName, out Material runtimeMaterial))
                        {
                            materials[index] = runtimeMaterial;
                        }
                        else if (index < renderer.sharedMaterials.Length)
                        {
                            materials[index] = renderer.sharedMaterials[index];
                        }
                    }
                }

                templates[filter.name] = new TemplateMesh(
                    filter.sharedMesh,
                    materials,
                    filter.transform.localPosition,
                    filter.transform.localRotation,
                    filter.transform.localScale);
            }

            environment = new GameObject("Blender Environment");
            environment.transform.SetParent(parent, false);
            EnvironmentPlacementCorrections corrections = EnvironmentPlacementCorrections.LoadForAttachment();
            int missingTemplates = 0;
            foreach (EnvironmentInstance placement in manifest.instances)
            {
                if (placement == null || !templates.TryGetValue(placement.template, out TemplateMesh template))
                {
                    missingTemplates++;
                    continue;
                }

                GameObject instance = new(string.IsNullOrEmpty(placement.name)
                    ? placement.template
                    : placement.name);
                instance.transform.SetParent(environment.transform, false);
                instance.transform.SetLocalPositionAndRotation(placement.position, placement.rotation);
                instance.transform.localScale = placement.scale;

                GameObject geometry = new("Geometry");
                geometry.transform.SetParent(instance.transform, false);
                geometry.transform.SetLocalPositionAndRotation(
                    template.LocalPosition,
                    template.LocalRotation);
                // Unity's FBX importer mirrors mesh vertices on X while converting
                // Blender's right-handed coordinates. The manifest already carries
                // the authored Unity-space transforms, so cancel that mesh-only mirror.
                geometry.transform.localScale = new Vector3(
                    -template.LocalScale.x,
                    template.LocalScale.y,
                    template.LocalScale.z);
                geometry.AddComponent<MeshFilter>().sharedMesh = template.Mesh;
                geometry.AddComponent<MeshRenderer>().sharedMaterials = template.Materials;

                corrections.Apply(instance);
                if (IsWindReactive(instance.name))
                {
                    TropicalWindSway sway = instance.AddComponent<TropicalWindSway>();
                    float phase = placement.position.x * 0.071f + placement.position.z * 0.053f;
                    sway.Initialize(phase, 0.72f, 2.4f);
                }
            }

            corrections.Report();
            if (missingTemplates > 0 || environment.transform.childCount != manifest.objectCount)
            {
                Debug.LogError(
                    $"[Vale Blender] Falha ao reconstruir cenário: "
                    + $"esperado={manifest.objectCount}, criado={environment.transform.childCount}, "
                    + $"templatesAusentes={missingTemplates}.");
                UnityEngine.Object.Destroy(environment);
                environment = null;
                return false;
            }

            ConfigureRenderers(environment);
            ConfigureLagoon(environment);
            ConfigureWaterfall(environment);
            Debug.Log(
                $"[Vale Blender] Cenário importado carregado: {environment.transform.childCount:N0} instâncias.");
            return true;
        }

        private static bool IsWindReactive(string objectName)
        {
            if (string.IsNullOrEmpty(objectName))
            {
                return false;
            }

            string lowered = objectName.ToLowerInvariant();
            return lowered.Contains("tree", StringComparison.Ordinal)
                || lowered.Contains("canopy", StringComparison.Ordinal)
                || lowered.Contains("palm", StringComparison.Ordinal);
        }

        private static Dictionary<string, Material> BuildMaterials(EnvironmentMaterial[] definitions)
        {
            Dictionary<string, Material> materials = new(StringComparer.Ordinal);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            Shader groundShader = Resources.Load<Shader>("Shaders/WorldProjectedGround");
            if (definitions == null || shader == null)
            {
                return materials;
            }

            foreach (EnvironmentMaterial definition in definitions)
            {
                if (definition == null || string.IsNullOrEmpty(definition.name))
                {
                    continue;
                }

                Shader selectedShader = definition.name == "M_Terrain_Jungle_PBR"
                    && groundShader != null
                        ? groundShader
                        : shader;
                Material material = new(selectedShader)
                {
                    name = definition.name,
                    enableInstancing = true
                };
                // Match the established mountain greens without grading the whole
                // scene. Fern02 is shared with its approved groundcover and keeps
                // its source palette; each other species retains a distinct tint.
                (Color tint, float smoothness) = definition.name switch
                {
                    "M_Terrain_Jungle_PBR" => (new Color(0.82f, 0.87f, 0.81f), definition.smoothness),
                    "PC_PolyHaven_PachiraLeaves_CC0" => (new Color(0.72f, 0.94f, 0.75f), 0.22f),
                    "PC_PolyHaven_Shrub02_CC0" => (new Color(0.76f, 0.96f, 0.74f), 0.18f),
                    "PC_PolyHaven_Calathea_CC0" => (new Color(0.83f, 0.97f, 0.82f), 0.28f),
                    "PC_PolyHaven_Anthurium_CC0" => (new Color(0.80f, 0.96f, 0.77f), 0.30f),
                    "PC_Jurassic_Meadow_Grass_PBR" => (new Color(0.24f, 0.37f, 0.19f), 0.10f),
                    // Palm leaves and bark share an atlas: only soften highlights.
                    "PC_CoconutPalm_LOD0" => (definition.baseColor, 0.18f),
                    "PC_Boulder_LOD0" => (new Color(0.84f, 0.90f, 0.96f), 0.18f),
                    "PC_Mountainside_LOD0" => (new Color(0.90f, 0.94f, 0.98f), 0.18f),
                    "PC_PolyHaven_RockMossSet01_CC0" => (new Color(0.90f, 0.94f, 0.98f), 0.18f),
                    _ => (definition.baseColor, definition.smoothness)
                };
                tint.a = definition.baseColor.a;
                SetColor(material, "_BaseColor", tint);
                SetColor(material, "_Color", tint);
                SetFloat(material, "_Metallic", Mathf.Clamp01(definition.metallic));
                SetFloat(material, "_Smoothness", Mathf.Clamp01(smoothness));
                SetFloat(material, "_TileScale", 1f / 14f);
                if (definition.name == "M_Terrain_Jungle_PBR")
                {
                    SetFloat(material, "_Cull", (float)CullMode.Off);
                    SetFloat(material, "_TileScale", 1f / 7f);
                    SetFloat(material, "_BumpScale", 0.32f);
                }

                Texture2D baseTexture = LoadTexture(definition.baseTexture);
                if (definition.name == "M_Terrain_Jungle_PBR")
                {
                    // Soil, fallen litter and sparse moss sit beneath the living
                    // plants; the lush texture printed whole plants onto the floor.
                    baseTexture = Resources.Load<Texture2D>("Textures/Realistic/JungleGround_Albedo")
                        ?? baseTexture;
                }
                Texture2D normalTexture = LoadTexture(definition.normalTexture);
                Texture2D occlusionTexture = LoadTexture(definition.occlusionTexture);
                Texture2D maskTexture = LoadTexture(definition.maskTexture);
                SetTexture(material, "_BaseMap", baseTexture);
                SetTexture(material, "_MainTex", baseTexture);
                SetTexture(material, "_BumpMap", normalTexture);
                SetTexture(material, "_OcclusionMap", occlusionTexture);
                SetTexture(material, "_MetallicGlossMap", maskTexture);
                if (normalTexture != null)
                {
                    material.EnableKeyword("_NORMALMAP");
                }
                if (occlusionTexture != null)
                {
                    material.EnableKeyword("_OCCLUSIONMAP");
                }
                if (maskTexture != null)
                {
                    material.EnableKeyword("_METALLICSPECGLOSSMAP");
                }
                ConfigureRockSurface(material, definition.name);

                if (definition.transparent)
                {
                    SetFloat(material, "_Surface", 1f);
                    SetFloat(material, "_Blend", 0f);
                    SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                    SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    SetFloat(material, "_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.SetOverrideTag("RenderType", "Transparent");
                    material.renderQueue = (int)RenderQueue.Transparent;
                }
                else if (definition.alphaClip)
                {
                    SetFloat(material, "_AlphaClip", 1f);
                    SetFloat(material, "_Cutoff", Mathf.Clamp01(definition.cutoff));
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.SetOverrideTag("RenderType", "TransparentCutout");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                }

                if (definition.doubleSided || definition.transparent)
                {
                    SetFloat(material, "_Cull", (float)CullMode.Off);
                }

                materials[definition.name] = material;
            }

            return materials;
        }

        private static void ConfigureRockSurface(Material material, string materialName)
        {
            bool boulder = materialName == "PC_Boulder_LOD0";
            bool mountainside = materialName == "PC_Mountainside_LOD0";
            if (!boulder && !mountainside && materialName != "PC_PolyHaven_RockMossSet01_CC0")
            {
                return;
            }
            if (!mountainside && ConfigureRockDetail(material, boulder ? 0.55f : 0.82f)) return;
            SetFloat(material, "_BumpScale", 0.82f);
            Texture2D detailNormal = Resources.Load<Texture2D>(
                "Textures/Realistic/Rocks/RockBasaltMoss_Normal");
            if (detailNormal != null && material.HasProperty("_DetailNormalMap")
                && material.HasProperty("_DetailAlbedoMap"))
            {
                // URP shares the detail-albedo UV transform with its normal map.
                // Scale zero with _DETAIL_SCALED keeps the original albedo intact.
                SetTexture(material, "_DetailNormalMap", detailNormal);
                SetFloat(material, "_DetailNormalMapScale", mountainside ? 0.08f : boulder ? 0.07f : 0.06f);
                SetFloat(material, "_DetailAlbedoMapScale", 0f);
                float detailTiling = mountainside ? 12f : 10f;
                material.SetTextureScale("_DetailAlbedoMap", Vector2.one * detailTiling);
                material.DisableKeyword("_DETAIL_MULX2");
                material.EnableKeyword("_DETAIL_SCALED");
            }
            if (!boulder && !mountainside)
            {
                return;
            }

            // The imported rocks retain the hero assets' UVs and albedos. Restore
            // their existing PBR maps instead of applying uniform gloss everywhere.
            string family = boulder ? "Boulder01" : "Mountainside";
            string resource = $"Models/EnvironmentHero/{family}/{family}";
            Texture2D specGloss = Resources.Load<Texture2D>(resource + "_SpecGloss");
            Texture2D occlusion = Resources.Load<Texture2D>(resource + "_Occlusion");
            if (specGloss != null)
            {
                SetTexture(material, "_SpecGlossMap", specGloss);
                SetFloat(material, "_WorkflowMode", 0f);
                material.EnableKeyword("_SPECULAR_SETUP");
                material.EnableKeyword("_METALLICSPECGLOSSMAP");
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            if (occlusion != null)
            {
                SetTexture(material, "_OcclusionMap", occlusion);
                SetFloat(material, "_OcclusionStrength", 0.78f);
                material.EnableKeyword("_OCCLUSIONMAP");
            }
        }

        private static bool ConfigureRockDetail(Material material, float surfaceDetail)
        {
            Shader shader = Resources.Load<Shader>("Shaders/MountainRock");
            Texture atlas = material.GetTexture("_BaseMap");
            Color tint = material.GetColor("_BaseColor");
            Texture2D detail = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Albedo");
            Texture2D normal = Resources.Load<Texture2D>("Textures/Realistic/Rocks/RockBasaltMoss_Normal");
            if (shader == null || atlas == null || detail == null || normal == null) return false;

            // Share the mountain's mineral palette and world-space fractures;
            // retain a little of each scanned rock's original color variation.
            material.shader = shader;
            material.shaderKeywords = Array.Empty<string>();
            material.EnableKeyword("_ROCK_ATLAS_DETAIL");
            material.SetTexture("_RockAtlasMap", atlas);
            material.SetTexture("_BaseMap", detail);
            material.SetTexture("_BumpMap", normal);
            material.SetColor("_BaseColor", tint);
            material.SetColor("_StoneColor", new Color(0.53f, 0.50f, 0.44f));
            material.SetFloat("_WorldScale", 0.25f);
            material.SetFloat("_RockDetailStrength", surfaceDetail);
            material.SetFloat("_RockAtlasDesaturation", 0.22f);
            material.SetFloat("_BumpScale", 0.22f);
            material.SetFloat("_Smoothness", 0.12f);
            material.enableInstancing = true;
            return true;
        }

        private static Texture2D LoadTexture(string textureName)
        {
            return string.IsNullOrEmpty(textureName)
                ? null
                : Resources.Load<Texture2D>($"{TextureResourcePath}{textureName}");
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (texture != null && material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void SetColor(Material material, string property, Color color)
        {
            if (material.HasProperty(property))
            {
                material.SetColor(property, color);
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private static void ConfigureRenderers(GameObject environment)
        {
            foreach (Renderer renderer in environment.GetComponentsInChildren<Renderer>(true))
            {
                renderer.allowOcclusionWhenDynamic = true;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
                renderer.receiveShadows = true;

                string lowered = renderer.transform.parent != null
                    ? renderer.transform.parent.name.ToLowerInvariant()
                    : renderer.name.ToLowerInvariant();
                if (lowered.Contains("undergrowth", StringComparison.Ordinal)
                    || lowered.Contains("grass", StringComparison.Ordinal)
                    || lowered.Contains("shore_detail", StringComparison.Ordinal))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        material.enableInstancing = true;
                    }
                }
            }
        }

        private static void ConfigureLagoon(GameObject environment)
        {
            Transform lagoon = FindDescendant(environment.transform, "Lagoon_Water_Editable");
            MeshRenderer renderer = lagoon != null
                ? lagoon.GetComponentInChildren<MeshRenderer>(true)
                : null;
            if (lagoon == null || renderer == null)
            {
                Debug.LogWarning("[Vale Blender] Malha Lagoon_Water_Editable não encontrada.");
                return;
            }

            lagoon.gameObject.layer = 4;
            renderer.gameObject.layer = 4;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            if (renderer.sharedMaterial != null)
            {
                Shader waterShader = Shader.Find("Vale Mesozoico/Lagoon Water");
                if (waterShader == null)
                {
                    Debug.LogWarning("[Lagoon Water] Shader de água indisponível; material original preservado.");
                }
                else
                {
                    Material animatedWater = new(waterShader)
                    {
                        name = "M_Lagoon_Water Runtime",
                        enableInstancing = true
                    };
                    animatedWater.SetTexture("_BumpMap", OptimizedModelWorld.CreateWaterNormalTexture());
                    bool rebuilt = LagoonWaterSurface.Rebuild(renderer);
                    if (!rebuilt) animatedWater.SetFloat("_WaveAmplitude", 0f);
                    renderer.sharedMaterial = animatedWater;
                    Transform terrain = FindDescendant(environment.transform, "Terrain_Editable_129x129");
                    MeshRenderer terrainRenderer = terrain != null
                        ? terrain.GetComponentInChildren<MeshRenderer>(true) : null;
                    if (terrainRenderer != null && terrainRenderer.sharedMaterial != null
                        && terrainRenderer.sharedMaterial.HasProperty("_LagoonLevel"))
                    {
                        Bounds waterBounds = renderer.bounds;
                        terrainRenderer.sharedMaterial.SetFloat("_LagoonLevel", waterBounds.center.y);
                        terrainRenderer.sharedMaterial.SetVector("_LagoonBounds", new Vector4(
                            waterBounds.min.x, waterBounds.min.z, waterBounds.max.x, waterBounds.max.z));
                    }
                    // Depth fade follows the actual basin. These legacy overlays
                    // floated above it and obscured the bottom near the shore.
                    foreach (string name in new[] { "PC_Lagoon_Shallows", "PC_REFINE_Shoreline_Gradient" })
                    {
                        Transform overlay = FindDescendant(environment.transform, name);
                        if (overlay == null) continue;
                        foreach (Renderer overlayRenderer in overlay.GetComponentsInChildren<Renderer>(true))
                            overlayRenderer.enabled = false;
                    }
                    Debug.Log($"[Lagoon Water] PASS shader={waterShader.name}, depthTransparency=true, "
                        + $"animatedNormals=2, waveMesh={rebuilt}, oldShoreOverlays=disabled");
                }
            }

            Bounds bounds = renderer.bounds;
            GameObject probeObject = new("Imported Lagoon Reflection Probe");
            probeObject.transform.SetParent(environment.transform, true);
            probeObject.transform.position = bounds.center + Vector3.up * 5f;
            ReflectionProbe probe = probeObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
            probe.resolution = 128;
            probe.hdr = false;
            probe.boxProjection = true;
            probe.shadowDistance = 0f;
            probe.cullingMask = ~(1 << lagoon.gameObject.layer);
            probe.size = new Vector3(
                Mathf.Max(20f, bounds.size.x + 12f),
                28f,
                Mathf.Max(20f, bounds.size.z + 12f));
            probe.blendDistance = 10f;
            probe.intensity = 0.9f;
            probeObject.AddComponent<LagoonReflectionCapture>().Initialize(probe);

            AudioSource ambience = renderer.gameObject.AddComponent<AudioSource>();
            ambience.clip = EnhancedProceduralAudio.CreateWaterAmbience();
            ambience.loop = true;
            ambience.playOnAwake = true;
            ambience.spatialBlend = 1f;
            ambience.rolloffMode = AudioRolloffMode.Linear;
            ambience.minDistance = 4f;
            ambience.maxDistance = 26f;
            ambience.volume = 0.07f;
            ambience.Play();
        }

        private static void ConfigureWaterfall(GameObject environment)
        {
            Transform waterfall = FindDescendant(environment.transform, "Waterfall_Editable");
            MeshRenderer renderer = waterfall != null
                ? waterfall.GetComponentInChildren<MeshRenderer>(true)
                : null;
            if (waterfall == null || renderer == null || renderer.sharedMaterial == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            Material animatedWaterfall = new(renderer.sharedMaterial)
            {
                name = $"{renderer.sharedMaterial.name} Runtime",
                enableInstancing = true
            };
            renderer.sharedMaterial = animatedWaterfall;
            WaterfallAnimator animator = renderer.gameObject.AddComponent<WaterfallAnimator>();
            animator.Initialize(animatedWaterfall, 0.17f);
        }

        private static Transform FindDescendant(Transform root, string expectedName)
        {
            foreach (Transform candidate in root.GetComponentsInChildren<Transform>(true))
            {
                if (string.Equals(candidate.name, expectedName, StringComparison.Ordinal)
                    || candidate.name.StartsWith(expectedName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
