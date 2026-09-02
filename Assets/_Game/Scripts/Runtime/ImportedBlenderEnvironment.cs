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

                if (IsWindReactive(instance.name))
                {
                    TropicalWindSway sway = instance.AddComponent<TropicalWindSway>();
                    float phase = placement.position.x * 0.071f + placement.position.z * 0.053f;
                    sway.Initialize(phase, 0.72f, 2.4f);
                }
            }

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
                SetColor(material, "_BaseColor", definition.baseColor);
                SetColor(material, "_Color", definition.baseColor);
                SetFloat(material, "_Metallic", Mathf.Clamp01(definition.metallic));
                SetFloat(material, "_Smoothness", Mathf.Clamp01(definition.smoothness));
                SetFloat(material, "_TileScale", 1f / 14f);
                if (definition.name == "M_Terrain_Jungle_PBR")
                {
                    SetFloat(material, "_Cull", (float)CullMode.Off);
                }

                Texture2D baseTexture = LoadTexture(definition.baseTexture);
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
                Material animatedWater = new(renderer.sharedMaterial)
                {
                    name = $"{renderer.sharedMaterial.name} Runtime",
                    enableInstancing = true
                };
                renderer.sharedMaterial = animatedWater;
                WaterSurfaceAnimator animator = renderer.gameObject.AddComponent<WaterSurfaceAnimator>();
                animator.Initialize(animatedWater, false);
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
