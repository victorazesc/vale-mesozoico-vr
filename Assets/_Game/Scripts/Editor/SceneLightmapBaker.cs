#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ValeMesozoico.Editor
{
    [InitializeOnLoad]
    public static class SceneLightmapBaker
    {
        private const string Capturing = "Vale.Lightmap.Capturing";
        private const string Pending = "Vale.Lightmap.Pending";
        private const string Baking = "Vale.Lightmap.Baking";
        private const string FolderKey = "Vale.Lightmap.Folder";
        private const string ScenePath = "Assets/_Game/Scenes/JurassicRide.unity";
        private const string SettingsPath = "Assets/_Game/Settings/JurassicRide.lighting";

        static SceneLightmapBaker()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Lightmapping.bakeCompleted += OnBakeCompleted;
        }

        [MenuItem("Vale Mesozoico/Iluminacao/Gerar Lightmap")]
        public static void Generate()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || Lightmapping.isRunning
                || SceneManager.GetActiveScene().path != ScenePath)
            {
                Debug.LogWarning("[Vale Lightmap] Abra JurassicRide fora do Play Mode e aguarde qualquer bake ativo.");
                return;
            }
            if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene()))
                throw new InvalidOperationException("Não foi possível salvar a cena antes do bake.");

            string folder = "Assets/_Game/Lighting/Bake-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            SessionState.SetString(FolderKey, folder);
            SessionState.SetBool(Capturing, true);
            Debug.Log("[Vale Lightmap] Capturando a geometria fixa do passeio.");
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(Capturing, false))
            {
                // Capture before the ride or any moving prop advances.
                try
                {
                    CaptureStaticGeometry();
                    SessionState.SetBool(Pending, true);
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                    SessionState.SetBool(Pending, false);
                }
                finally
                {
                    SessionState.SetBool(Capturing, false);
                    EditorApplication.ExitPlaymode();
                }
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Pending, false))
            {
                SessionState.SetBool(Pending, false);
                EditorApplication.delayCall += PrepareAndBake;
            }
        }

        private static bool IsFixed(MeshRenderer renderer, Transform world)
        {
            if (!renderer.enabled || !renderer.gameObject.activeInHierarchy
                || renderer.sharedMaterials.Any(m => m == null || m.renderQueue >= 3000)) return false;
            for (Transform item = renderer.transform; item != null && item != world; item = item.parent)
            {
                if (item.GetComponent<Animator>() != null || item.GetComponent<Animation>() != null
                    || item.GetComponent<Rigidbody>() != null || item.GetComponents<MonoBehaviour>().Length > 0)
                    return false;
            }
            return renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null;
        }

        private static void CaptureStaticGeometry()
        {
            GameObject worldObject = GameObject.Find("Jurassic Ride")
                ?? throw new InvalidOperationException("O passeio não criou o cenário.");
            Transform world = worldObject.transform;
            string folder = SessionState.GetString(FolderKey, "");
            GameObject root = new("Baked Static Environment");
            BakedSceneLighting baked = root.AddComponent<BakedSceneLighting>();
            Dictionary<Transform, Transform> transforms = new() { [world] = root.transform };
            Dictionary<Mesh, Mesh> meshes = new();
            Dictionary<Material, Material> materials = new();
            List<BakedSceneLighting.Entry> entries = new();
            int excluded = 0;

            Transform CloneTransform(Transform original)
            {
                if (transforms.TryGetValue(original, out Transform existing)) return existing;
                Transform parent = CloneTransform(original.parent);
                Transform clone = new GameObject(original.name).transform;
                clone.SetParent(parent, false);
                clone.localPosition = original.localPosition;
                clone.localRotation = original.localRotation;
                clone.localScale = original.localScale;
                transforms.Add(original, clone);
                return clone;
            }

            foreach (MeshRenderer source in world.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!IsFixed(source, world)) { excluded++; continue; }
                Mesh original = source.GetComponent<MeshFilter>().sharedMesh;
                if (!meshes.TryGetValue(original, out Mesh mesh))
                {
                    mesh = Object.Instantiate(original);
                    mesh.name = original.name + " Lightmap UV";
                    AssetDatabase.CreateAsset(mesh, $"{folder}/Mesh-{meshes.Count:D3}.asset");
                    meshes.Add(original, mesh);
                }
                Transform transform = CloneTransform(source.transform);
                transform.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                MeshRenderer target = transform.gameObject.AddComponent<MeshRenderer>();
                target.sharedMaterials = source.sharedMaterials.Select(originalMaterial =>
                {
                    if (AssetDatabase.Contains(originalMaterial)) return originalMaterial;
                    if (materials.TryGetValue(originalMaterial, out Material existing)) return existing;
                    Material material = new(originalMaterial) { name = originalMaterial.name };
                    AssetDatabase.CreateAsset(material, $"{folder}/Material-{materials.Count:D3}.mat");
                    materials.Add(originalMaterial, material);
                    return material;
                }).ToArray();
                target.shadowCastingMode = source.shadowCastingMode;
                target.receiveShadows = source.receiveShadows;
                target.receiveGI = ReceiveGI.Lightmaps;
                target.scaleInLightmap = source.bounds.size.magnitude > 160f ? 0.35f : 1f;
                GameObjectUtility.SetStaticEditorFlags(target.gameObject,
                    StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic
                    | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic
                    | StaticEditorFlags.ReflectionProbeStatic);
                entries.Add(new BakedSceneLighting.Entry
                {
                    path = BakedSceneLighting.PathOf(source.transform, world), renderer = target, mesh = mesh,
                    sourceVertexCount = original.vertexCount, sourceMeshName = original.name
                });
            }
            if (entries.Count == 0) throw new InvalidOperationException("Nenhuma malha fixa encontrada.");
            baked.entries = entries.ToArray();

            Light sourceSun = RenderSettings.sun;
            if (sourceSun == null) throw new InvalidOperationException("Directional Light não encontrada.");
            GameObject sunObject = new("Directional Light");
            sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.rotation = sourceSun.transform.rotation;
            baked.sun = sunObject.AddComponent<Light>();
            EditorUtility.CopySerialized(sourceSun, baked.sun);
            baked.sun.lightmapBakeType = LightmapBakeType.Mixed;

            Material sky = new(RenderSettings.skybox) { name = "Jurassic Sky" };
            AssetDatabase.CreateAsset(sky, folder + "/Sky.mat");
            PrefabUtility.SaveAsPrefabAsset(root, folder + "/StaticEnvironment.prefab");
            AssetDatabase.SaveAssets();
            Debug.Log($"[Vale Lightmap] Captura salva: {entries.Count} malhas estáticas, "
                + $"{meshes.Count} meshes únicos, {excluded} renderers dinâmicos/inativos excluídos.");
        }

        private static void PrepareAndBake()
        {
            try
            {
                string folder = SessionState.GetString(FolderKey, "");
                foreach (BakedSceneLighting previous in Object.FindObjectsByType<BakedSceneLighting>(FindObjectsSortMode.None))
                    Undo.DestroyObjectImmediate(previous.gameObject);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/StaticEnvironment.prefab");
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
                Undo.RegisterCreatedObjectUndo(root, "Preparar lightmap");
                BakedSceneLighting baked = root.GetComponent<BakedSceneLighting>();
                Mesh[] meshes = baked.entries.Select(e => e.renderer.GetComponent<MeshFilter>().sharedMesh).Distinct().ToArray();
                foreach (Mesh mesh in meshes)
                {
                    if (!Unwrapping.GenerateSecondaryUVSet(mesh))
                        throw new InvalidOperationException($"Falha ao gerar UV2: {mesh.name}");
                    EditorUtility.SetDirty(mesh);
                }
                RenderSettings.skybox = AssetDatabase.LoadAssetAtPath<Material>(folder + "/Sky.mat");
                RenderSettings.sun = baked.sun;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(0.46f, 0.49f, 0.38f);
                RenderSettings.ambientEquatorColor = new Color(0.30f, 0.29f, 0.21f);
                RenderSettings.ambientGroundColor = new Color(0.12f, 0.115f, 0.07f);

                LightingSettings settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(SettingsPath);
                if (settings == null)
                {
                    settings = new LightingSettings();
                    AssetDatabase.CreateAsset(settings, SettingsPath);
                }
                settings.bakedGI = true;
                settings.realtimeGI = false;
                settings.mixedBakeMode = MixedLightingMode.IndirectOnly;
                settings.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
                settings.directionalityMode = LightmapsMode.NonDirectional;
                settings.lightmapResolution = 4f;
                settings.lightmapMaxSize = 1024;
                settings.lightmapPadding = 4;
                settings.directSampleCount = 32;
                settings.indirectSampleCount = 128;
                settings.environmentSampleCount = 64;
                settings.maxBounces = 2;
                settings.ao = false;
                Lightmapping.lightingSettings = settings;
                EditorUtility.SetDirty(settings);
                AssetDatabase.SaveAssets();
                EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
                SessionState.SetBool(Baking, true);
                if (!Lightmapping.BakeAsync()) throw new InvalidOperationException("Unity não iniciou o bake.");
                Debug.Log("[Vale Lightmap] Bake GPU iniciado: Mixed/Baked Indirect, 4 texels/m, atlas 1024.");
            }
            catch (Exception exception)
            {
                SessionState.SetBool(Baking, false);
                Debug.LogException(exception);
            }
        }

        private static void OnBakeCompleted()
        {
            if (!SessionState.GetBool(Baking, false)) return;
            SessionState.SetBool(Baking, false);
            EditorApplication.delayCall += ValidateAndSave;
        }

        [MenuItem("Vale Mesozoico/Iluminacao/Validar Lightmap")]
        public static void ValidateAndSave()
        {
            BakedSceneLighting baked = Object.FindFirstObjectByType<BakedSceneLighting>();
            if (baked == null) throw new InvalidOperationException("Cenário de bake ausente.");
            int count = LightmapSettings.lightmaps.Length;
            int mapped = baked.entries.Count(e => e.renderer != null
                && e.renderer.lightmapIndex >= 0 && e.renderer.lightmapIndex < count);
            if (count == 0 || mapped != baked.entries.Length || Lightmapping.lightingDataAsset == null)
                throw new InvalidOperationException($"Bake incompleto: {mapped}/{baked.entries.Length} renderers, {count} mapas.");
            foreach (BakedSceneLighting.Entry entry in baked.entries)
            {
                entry.mesh = entry.renderer.GetComponent<MeshFilter>().sharedMesh;
                entry.lightmapIndex = entry.renderer.lightmapIndex;
                entry.lightmapScaleOffset = entry.renderer.lightmapScaleOffset;
            }
            EditorUtility.SetDirty(baked);
            PrefabUtility.RecordPrefabInstancePropertyModifications(baked);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            string report = $"[Vale Lightmap] CONCLUIDO: {mapped}/{baked.entries.Length} renderers estáticos, "
                + $"{count} lightmaps, Directional Light={baked.sun.lightmapBakeType}, "
                + $"bake={baked.sun.bakingOutput.isBaked}, dados={AssetDatabase.GetAssetPath(Lightmapping.lightingDataAsset)}";
            Debug.Log(report);
            File.WriteAllText(SessionState.GetString(FolderKey, "Assets/_Game/Lighting") + "/Validation.txt", report);
        }
    }
}
#endif
