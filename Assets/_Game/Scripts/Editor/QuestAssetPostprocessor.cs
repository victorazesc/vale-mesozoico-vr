#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico.Editor
{
    public sealed class QuestAssetPostprocessor : AssetPostprocessor
    {
        private const string ModelsRoot = "Assets/_Game/Resources/Models/";
        private const string RealisticTexturesRoot = "Assets/_Game/Resources/Textures/Realistic/";
        private const string SimpleLitShader = "Universal Render Pipeline/Simple Lit";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const string PcBlenderEnvironmentRoot =
            "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/";

        private static readonly string[] DinosaurTokens =
        {
            "dinosaur", "dino", "trex", "t-rex", "tyranno", "sauropod", "brachio",
            "apatosaur", "diplodocus", "raptor", "triceratops", "stegosaur", "spinosaur",
            "pteranodon", "pterosaur", "pterodactyl"
        };

        private void OnPreprocessModel()
        {
            if (!IsManagedAsset(assetPath) || assetImporter is not ModelImporter importer)
            {
                return;
            }

            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.addCollider = false;
            importer.meshCompression = IsPcBlenderEnvironment(assetPath)
                ? ModelImporterMeshCompression.Off
                : ModelImporterMeshCompression.Medium;
            importer.optimizeMeshPolygons = true;
            importer.optimizeMeshVertices = true;
            importer.isReadable = IsDinosaur(assetPath);

            bool isDinosaur = IsDinosaur(assetPath);
            importer.importAnimation = isDinosaur;
            importer.animationType = isDinosaur
                ? ModelImporterAnimationType.Legacy
                : ModelImporterAnimationType.None;
            if (isDinosaur)
            {
                importer.animationCompression = ModelImporterAnimationCompression.Optimal;
            }
        }

        private void OnPostprocessMaterial(Material material)
        {
            if (!IsManagedAsset(assetPath) || material == null)
            {
                return;
            }

            ConfigureMaterial(material, IsPcBlenderEnvironment(assetPath));
        }

        private void OnPostprocessModel(GameObject root)
        {
            if (!IsManagedAsset(assetPath) || root == null)
            {
                return;
            }

            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        ConfigureMaterial(material, IsPcBlenderEnvironment(assetPath));
                    }
                }
            }
        }

        private static void ConfigureMaterial(Material material, bool preferPcQuality)
        {
            string shaderName = preferPcQuality ? LitShader : SimpleLitShader;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[Quest Assets] Shader ausente: {shaderName}. Material mantido: {material.name}");
                return;
            }

            Texture baseMap = GetTexture(material, "_BaseMap", "_MainTex");
            Texture normalMap = GetTexture(material, "_BumpMap");
            Texture specGlossMap = GetTexture(material, "_SpecGlossMap");
            Texture occlusionMap = GetTexture(material, "_OcclusionMap");
            Color baseColor = GetColor(material, "_BaseColor", "_Color");
            material.shader = shader;
            material.enableInstancing = true;
            SetTexture(material, baseMap, "_BaseMap", "_MainTex");
            SetColor(material, baseColor, "_BaseColor", "_Color");
            SetTexture(material, normalMap, "_BumpMap");
            SetTexture(material, specGlossMap, "_SpecGlossMap");
            SetTexture(material, occlusionMap, "_OcclusionMap");
            if (normalMap != null)
            {
                material.EnableKeyword("_NORMALMAP");
            }
            if (specGlossMap != null)
            {
                material.EnableKeyword("_SPECGLOSSMAP");
            }
            if (occlusionMap != null)
            {
                material.EnableKeyword("_OCCLUSIONMAP");
            }

            if (!preferPcQuality)
            {
                return;
            }

            string lowered = material.name.ToLowerInvariant();
            bool isFoliage = lowered.Contains("leaves", StringComparison.Ordinal)
                || lowered.Contains("leaf", StringComparison.Ordinal)
                || lowered.Contains("fern", StringComparison.Ordinal)
                || lowered.Contains("anthurium", StringComparison.Ordinal)
                || lowered.Contains("calathea", StringComparison.Ordinal)
                || lowered.Contains("shrub", StringComparison.Ordinal)
                || lowered.Contains("grass", StringComparison.Ordinal)
                || lowered.Contains("coconutpalm", StringComparison.Ordinal);
            if (isFoliage)
            {
                SetFloat(material, "_Surface", 0f);
                SetFloat(material, "_AlphaClip", 1f);
                SetFloat(material, "_Cutoff", 0.38f);
                SetFloat(material, "_Cull", 0f);
                material.EnableKeyword("_ALPHATEST_ON");
                material.SetOverrideTag("RenderType", "TransparentCutout");
                material.renderQueue = (int)RenderQueue.AlphaTest;
            }

            bool isWater = lowered.Contains("water", StringComparison.Ordinal)
                || lowered.Contains("shallows", StringComparison.Ordinal);
            if (isWater)
            {
                SetFloat(material, "_Surface", 1f);
                SetFloat(material, "_Blend", 0f);
                SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                SetFloat(material, "_ZWrite", 0f);
                SetFloat(material, "_Smoothness", 0.88f);
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                material.renderQueue = (int)RenderQueue.Transparent;
            }
        }

        private void OnPreprocessTexture()
        {
            if (!IsManagedAsset(assetPath) || assetImporter is not TextureImporter importer)
            {
                return;
            }

            ConfigureTextureImporter(importer, assetPath);
        }

        [MenuItem("Vale Mesozoico/Assets/Otimizar texturas Quest %&t")]
        public static void OptimizeTexturesForQuest()
        {
            string[] searchRoots =
            {
                RealisticTexturesRoot.TrimEnd('/'),
                ModelsRoot.TrimEnd('/')
            };
            int optimized = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", searchRoots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer
                    || !ConfigureTextureImporter(importer, path))
                {
                    continue;
                }

                importer.SaveAndReimport();
                optimized++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Quest Assets] {optimized} texturas configuradas em ASTC 6x6.");
        }

        public static void ReimportPcBlenderEnvironmentTexturesFromCli()
        {
            int reimported = 0;
            foreach (string guid in AssetDatabase.FindAssets(
                "t:Texture2D",
                new[] { PcBlenderEnvironmentRoot.TrimEnd('/') }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (AssetImporter.GetAtPath(path) is not TextureImporter importer
                    || !ConfigureTextureImporter(importer, path))
                {
                    continue;
                }

                importer.SaveAndReimport();
                reimported++;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Vale Blender] {reimported} texturas reimportadas para PC/Quest.");
        }

        private static bool ConfigureTextureImporter(TextureImporter importer, string path)
        {
            string textureName = Path.GetFileNameWithoutExtension(path);
            bool isCoaster = string.Equals(textureName, "CoasterColorMap", StringComparison.OrdinalIgnoreCase);
            bool isRealistic = path.StartsWith(RealisticTexturesRoot, StringComparison.OrdinalIgnoreCase);
            bool isPolyHaven = path.IndexOf("/Models/PolyHaven/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHeroEnvironment = path.IndexOf("/Models/EnvironmentHero/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isHeroRide = path.IndexOf("/Models/Ride/AbandonedCart/", StringComparison.OrdinalIgnoreCase) >= 0;
            bool isDinosaurTexture = IsDinosaur(path);
            bool isPcBlenderEnvironment = IsPcBlenderEnvironment(path);
            if (!isCoaster && !isRealistic && !isPolyHaven && !isHeroEnvironment && !isHeroRide
                && !isDinosaurTexture && !isPcBlenderEnvironment)
            {
                return false;
            }

            importer.mipmapEnabled = true;
            importer.streamingMipmaps = !isPcBlenderEnvironment;
            importer.streamingMipmapsPriority = isHeroEnvironment || isHeroRide || isDinosaurTexture ? 2 : 0;
            importer.maxTextureSize = isHeroEnvironment || isPcBlenderEnvironment ? 2048 : 1024;
            if (isRealistic || isPolyHaven || isHeroEnvironment || isHeroRide || isDinosaurTexture
                || isPcBlenderEnvironment)
            {
                bool isNormal = textureName.EndsWith("_Normal", StringComparison.OrdinalIgnoreCase)
                    || textureName.IndexOf("_nor_gl", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isOcclusion = textureName.EndsWith("_Occlusion", StringComparison.OrdinalIgnoreCase);
                bool isMetallicSmoothness = textureName.EndsWith(
                    "_MetallicSmoothness",
                    StringComparison.OrdinalIgnoreCase);
                bool isRoughness = textureName.IndexOf("_rough", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isGeneratedHeightNormal = isNormal
                    && (textureName.StartsWith("TrackPaintedSteel", StringComparison.OrdinalIgnoreCase)
                        || textureName.StartsWith("RockBasaltMoss", StringComparison.OrdinalIgnoreCase)
                        || textureName.StartsWith("WetShore", StringComparison.OrdinalIgnoreCase));
                bool isPalmAlbedo = path.IndexOf(
                    "/Models/EnvironmentHero/CoconutPalm/",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                bool isCutout = textureName.EndsWith("_Atlas", StringComparison.OrdinalIgnoreCase)
                    || textureName.EndsWith("_Cutout", StringComparison.OrdinalIgnoreCase)
                    || textureName.EndsWith("_Alpha", StringComparison.OrdinalIgnoreCase)
                    || textureName.IndexOf("_alpha_", StringComparison.OrdinalIgnoreCase) >= 0
                    || isPalmAlbedo;
                importer.textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                importer.convertToNormalmap = isGeneratedHeightNormal;
                if (isGeneratedHeightNormal)
                {
                    importer.heightmapScale = textureName.StartsWith(
                        "RockBasaltMoss",
                        StringComparison.OrdinalIgnoreCase)
                        ? 0.052f
                        : 0.035f;
                }
                importer.sRGBTexture = !isNormal && !isOcclusion && !isMetallicSmoothness && !isRoughness;
                importer.alphaIsTransparency = isCutout;
                importer.wrapMode = isCutout || isHeroRide ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = isPcBlenderEnvironment ? 4 : 2;
            }

            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(android);
            return true;
        }

        [MenuItem("Vale Mesozoico/Assets/Auditar modelos Quest")]
        public static void AuditModels()
        {
            string[] paths = FindModelPaths();
            if (paths.Length == 0)
            {
                Debug.LogWarning($"[Quest Asset Audit] Nenhum modelo encontrado em {ModelsRoot}");
                return;
            }

            long totalVertices = 0;
            long totalTriangles = 0;
            int totalRenderers = 0;
            int totalMaterials = 0;
            int totalSimpleLit = 0;
            int totalInstanced = 0;
            int totalClips = 0;

            foreach (string path in paths)
            {
                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null)
                {
                    Debug.LogWarning($"[Quest Asset Audit] Modelo não carregado: {path}");
                    continue;
                }

                ModelStats stats = CollectStats(model, path);
                totalVertices += stats.Vertices;
                totalTriangles += stats.Triangles;
                totalRenderers += stats.Renderers;
                totalMaterials += stats.Materials;
                totalSimpleLit += stats.SimpleLitMaterials;
                totalInstanced += stats.InstancedMaterials;
                totalClips += stats.Clips;

                Debug.Log(
                    $"[Quest Asset Audit] {path} | vertices={stats.Vertices:N0} | tris={stats.Triangles:N0} " +
                    $"| renderers={stats.Renderers} | materials={stats.Materials} " +
                    $"| simpleLit={stats.SimpleLitMaterials} | instancing={stats.InstancedMaterials} | clips={stats.Clips} " +
                    $"| bounds=({stats.BoundsSize.x:F2}, {stats.BoundsSize.y:F2}, {stats.BoundsSize.z:F2})");
            }

            Debug.Log(
                $"[Quest Asset Audit] TOTAL | modelos={paths.Length} | vertices={totalVertices:N0} " +
                $"| tris={totalTriangles:N0} | renderers={totalRenderers} | materials={totalMaterials} " +
                $"| simpleLit={totalSimpleLit} | instancing={totalInstanced} | clips={totalClips}");
        }

        public static void ReimportAndAuditModels()
        {
            string[] paths = AssetDatabase.FindAssets(string.Empty, new[] { ModelsRoot.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !AssetDatabase.IsValidFolder(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in paths)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.Refresh();
            AuditModels();
        }

        private static ModelStats CollectStats(GameObject model, string path)
        {
            Renderer[] sourceRenderers = model.GetComponentsInChildren<Renderer>(true);
            long vertices = 0;
            long triangles = 0;
            HashSet<Material> materials = new();

            foreach (Renderer renderer in sourceRenderers)
            {
                Mesh mesh = GetMesh(renderer);
                if (mesh != null)
                {
                    vertices += mesh.vertexCount;
                    for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                    {
                        if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                        {
                            triangles += (long)mesh.GetIndexCount(subMesh) / 3L;
                        }
                    }
                }

                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null)
                    {
                        materials.Add(material);
                    }
                }
            }

            int clips = AssetDatabase.LoadAllAssetsAtPath(path)
                .OfType<AnimationClip>()
                .Count(clip => !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase));

            Vector3 boundsSize = CalculateBounds(model);
            int simpleLitMaterials = materials.Count(material => material.shader != null && material.shader.name == SimpleLitShader);
            int instancedMaterials = materials.Count(material => material.enableInstancing);
            return new ModelStats(
                vertices,
                triangles,
                sourceRenderers.Length,
                materials.Count,
                simpleLitMaterials,
                instancedMaterials,
                clips,
                boundsSize);
        }

        private static Vector3 CalculateBounds(GameObject model)
        {
            GameObject instance = UnityEngine.Object.Instantiate(model);
            instance.hideFlags = HideFlags.HideAndDontSave;
            try
            {
                Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0)
                {
                    return Vector3.zero;
                }

                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                return bounds.size;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static Mesh GetMesh(Renderer renderer)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                return skinned.sharedMesh;
            }

            return renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
        }

        private static string[] FindModelPaths()
        {
            return AssetDatabase.FindAssets("t:Model", new[] { ModelsRoot.TrimEnd('/') })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsManagedAsset(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            string normalized = path.Replace('\\', '/');
            return normalized.StartsWith(ModelsRoot, StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith(RealisticTexturesRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPcBlenderEnvironment(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.Replace('\\', '/').StartsWith(
                    PcBlenderEnvironmentRoot,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDinosaur(string path)
        {
            string normalized = path.Replace('\\', '/').ToLowerInvariant();
            return DinosaurTokens.Any(normalized.Contains);
        }

        private static Texture GetTexture(Material material, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (material.HasProperty(property))
                {
                    Texture texture = material.GetTexture(property);
                    if (texture != null)
                    {
                        return texture;
                    }
                }
            }

            return null;
        }

        private static Color GetColor(Material material, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (material.HasProperty(property))
                {
                    return material.GetColor(property);
                }
            }

            return Color.white;
        }

        private static void SetTexture(Material material, Texture value, params string[] properties)
        {
            if (value == null)
            {
                return;
            }

            foreach (string property in properties)
            {
                if (material.HasProperty(property))
                {
                    material.SetTexture(property, value);
                }
            }
        }

        private static void SetColor(Material material, Color value, params string[] properties)
        {
            foreach (string property in properties)
            {
                if (material.HasProperty(property))
                {
                    material.SetColor(property, value);
                }
            }
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property))
            {
                material.SetFloat(property, value);
            }
        }

        private readonly struct ModelStats
        {
            public ModelStats(
                long vertices,
                long triangles,
                int renderers,
                int materials,
                int simpleLitMaterials,
                int instancedMaterials,
                int clips,
                Vector3 boundsSize)
            {
                Vertices = vertices;
                Triangles = triangles;
                Renderers = renderers;
                Materials = materials;
                SimpleLitMaterials = simpleLitMaterials;
                InstancedMaterials = instancedMaterials;
                Clips = clips;
                BoundsSize = boundsSize;
            }

            public long Vertices { get; }
            public long Triangles { get; }
            public int Renderers { get; }
            public int Materials { get; }
            public int SimpleLitMaterials { get; }
            public int InstancedMaterials { get; }
            public int Clips { get; }
            public Vector3 BoundsSize { get; }
        }
    }
}
#endif
