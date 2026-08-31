#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace ValeMesozoico.Editor
{
    public static class QuestProjectBuilder
    {
        private const string ScenePath = "Assets/_Game/Scenes/JurassicRide.unity";
        private const string SettingsFolder = "Assets/_Game/Settings";
        private const string RendererAssetPath = SettingsFolder + "/QuestForwardRenderer.asset";
        private const string PipelineAssetPath = SettingsFolder + "/QuestURP.asset";
        private const string XrGeneralSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        private const string MetaFeatureSetId = "com.unity.openxr.featureset.meta";
        private const string ApkPath = "Builds/Quest/ValeMesozoicoVR.apk";

        [MenuItem("Vale Mesozoico/Preparar projeto")]
        public static void ConfigureProject()
        {
            if (!BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Android Build Support não está instalado.");
            }

            EnsureFolder("Assets/_Game/Scenes");
            EnsureFolder(SettingsFolder);
            EnsureFolder("Assets/_Game/Resources/Generated");
            EnsureFolder("Assets/XR");

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                throw new InvalidOperationException("Não foi possível ativar o target Android.");
            }

            ConfigureAndroid();
            ConfigureUrp();
            ConfigureOpenXr();
            ConfigureGeneratedTextures();
            QuestAssetPostprocessor.OptimizeTexturesForQuest();
            EnsureShaderMaterials();
            EnsureScene();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("Vale Mesozoico configurado para Quest 3: Vulkan, ARM64, IL2CPP, OpenXR e API 34.");
        }

        [MenuItem("Vale Mesozoico/Gerar APK Quest %&j")]
        public static void BuildQuestApk()
        {
            ConfigureProject();
            string absoluteOutput = Path.GetFullPath(ApkPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absoluteOutput) ?? throw new InvalidOperationException("Pasta de build inválida."));

            BuildPlayerOptions options = new()
            {
                scenes = new[] { ScenePath },
                locationPathName = absoluteOutput,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Build Quest falhou: {report.summary.result}, {report.summary.totalErrors} erro(s).");
            }

            long apkBytes = new FileInfo(absoluteOutput).Length;
            Debug.Log($"APK gerada em {absoluteOutput} ({apkBytes / 1024f / 1024f:0.0} MiB).");
        }

        private static void ConfigureAndroid()
        {
            PlayerSettings.companyName = "Azevedo Labs";
            PlayerSettings.productName = "Vale Mesozoico VR";
            PlayerSettings.bundleVersion = "0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.victorazevedo.valemesozoicovr");
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.useCustomKeystore = false;
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            EditorUserBuildSettings.androidBuildSystem = AndroidBuildSystem.Gradle;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            EditorUserBuildSettings.buildAppBundle = false;
        }

        private static void ConfigureUrp()
        {
            UniversalRenderPipelineAsset pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(PipelineAssetPath);
            if (pipeline == null)
            {
                UniversalRendererData renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(RendererAssetPath);
                if (renderer == null)
                {
                    renderer = ScriptableObject.CreateInstance<UniversalRendererData>();
                    renderer.name = "Quest Forward Renderer";
                    renderer.renderingMode = RenderingMode.Forward;
                    AssetDatabase.CreateAsset(renderer, RendererAssetPath);
                }

                pipeline = UniversalRenderPipelineAsset.Create(renderer);
                pipeline.name = "Quest URP";
                AssetDatabase.CreateAsset(pipeline, PipelineAssetPath);
            }

            pipeline.supportsHDR = false;
            pipeline.supportsCameraOpaqueTexture = false;
            pipeline.supportsCameraDepthTexture = false;
            pipeline.msaaSampleCount = 4;
            pipeline.renderScale = 1f;
            pipeline.shadowDistance = 40f;
            pipeline.shadowCascadeCount = 1;
            pipeline.mainLightShadowmapResolution = 1024;
            pipeline.maxAdditionalLightsCount = 0;
            pipeline.useSRPBatcher = true;
            SerializedObject pipelineSettings = new(pipeline);
            SerializedProperty boxProjection = pipelineSettings.FindProperty("m_ReflectionProbeBoxProjection");
            if (boxProjection != null)
            {
                boxProjection.boolValue = true;
                pipelineSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            GraphicsSettings.defaultRenderPipeline = pipeline;
            QualitySettings.renderPipeline = pipeline;
            EditorUtility.SetDirty(pipeline);
        }

        private static void ConfigureOpenXr()
        {
            XRGeneralSettingsPerBuildTarget perTarget = GetOrCreateXrGeneralSettings();
            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
            {
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            }

            XRGeneralSettings general = perTarget.SettingsForBuildTarget(BuildTargetGroup.Android);
            general.InitManagerOnStart = true;
            string loaderTypeName = typeof(OpenXRLoader).FullName;
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, loaderTypeName, BuildTargetGroup.Android)
                || !XRPackageMetadataStore.IsLoaderAssigned(loaderTypeName, BuildTargetGroup.Android))
            {
                throw new InvalidOperationException("Não foi possível registrar OpenXRLoader no Android.");
            }

            OpenXRFeatureSetManager.InitializeFeatureSets();
            var metaFeatureSet = OpenXRFeatureSetManager.GetFeatureSetWithId(BuildTargetGroup.Android, MetaFeatureSetId);
            if (metaFeatureSet != null)
            {
                metaFeatureSet.isEnabled = false;
                OpenXRFeatureSetManager.SetFeaturesFromEnabledFeatureSets(BuildTargetGroup.Android);
            }

            OpenXRSettings settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
            {
                throw new InvalidOperationException("OpenXRSettings do Android não foi criado.");
            }

            foreach (OpenXRFeature openXrFeature in settings.GetFeatures())
            {
                if (openXrFeature == null)
                {
                    continue;
                }

                openXrFeature.enabled = false;
                EditorUtility.SetDirty(openXrFeature);
            }

            MetaQuestFeature feature = settings.GetFeature<MetaQuestFeature>();
            if (feature == null)
            {
                throw new InvalidOperationException("MetaQuestFeature não está disponível no OpenXR instalado.");
            }

            feature.enabled = true;
            ConfigureQuest3Target(feature);
            FoveatedRenderingFeature foveatedRendering = settings.GetFeature<FoveatedRenderingFeature>();
            if (foveatedRendering == null)
            {
                throw new InvalidOperationException("Foveated Rendering não está disponível no OpenXR instalado.");
            }

            foveatedRendering.enabled = true;
            MetaQuestTouchPlusControllerProfile touchPlus =
                settings.GetFeature<MetaQuestTouchPlusControllerProfile>();
            OculusTouchControllerProfile oculusTouch =
                settings.GetFeature<OculusTouchControllerProfile>();
            if (touchPlus == null || oculusTouch == null)
            {
                throw new InvalidOperationException("Perfis Touch OpenXR não estão disponíveis.");
            }

            touchPlus.enabled = true;
            oculusTouch.enabled = true;
            settings.renderMode = OpenXRSettings.RenderMode.SinglePassInstanced;
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(foveatedRendering);
            EditorUtility.SetDirty(touchPlus);
            EditorUtility.SetDirty(oculusTouch);
            EditorUtility.SetDirty(settings);
            EditorUtility.SetDirty(general);
            EditorUtility.SetDirty(perTarget);
        }

        private static void ConfigureQuest3Target(MetaQuestFeature feature)
        {
            SerializedObject serializedFeature = new(feature);
            SerializedProperty targetDevices = serializedFeature.FindProperty("targetDevices");
            if (targetDevices == null || !targetDevices.isArray)
            {
                throw new InvalidOperationException("Lista de dispositivos Meta Quest não está disponível.");
            }

            bool quest3Found = false;
            for (int index = 0; index < targetDevices.arraySize; index++)
            {
                SerializedProperty targetDevice = targetDevices.GetArrayElementAtIndex(index);
                string manifestName = targetDevice.FindPropertyRelative("manifestName").stringValue;
                bool isQuest3 = manifestName == "eureka";
                targetDevice.FindPropertyRelative("enabled").boolValue = isQuest3;
                quest3Found |= isQuest3;
            }

            if (!quest3Found)
            {
                throw new InvalidOperationException("Meta Quest 3 não está disponível na lista de dispositivos OpenXR.");
            }

            serializedFeature.FindProperty("forceRemoveInternetPermission").boolValue = true;
            serializedFeature.ApplyModifiedPropertiesWithoutUndo();
        }

        private static XRGeneralSettingsPerBuildTarget GetOrCreateXrGeneralSettings()
        {
            EditorBuildSettings.TryGetConfigObject(
                XRGeneralSettings.k_SettingsKey,
                out XRGeneralSettingsPerBuildTarget perTarget);
            if (perTarget != null)
            {
                return perTarget;
            }

            perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrGeneralSettingsPath);
            if (perTarget == null)
            {
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                perTarget.name = "XR General Settings Per Build Target";
                AssetDatabase.CreateAsset(perTarget, XrGeneralSettingsPath);
            }

            EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            return perTarget;
        }

        private static void ConfigureGeneratedTextures()
        {
            ConfigureTexture("Assets/_Game/Resources/Generated/Trex.png");
            ConfigureTexture("Assets/_Game/Resources/Generated/Sauropod.png");
            ConfigureSurfaceTexture("Assets/_Game/Resources/Textures/JurassicGround.png");
            ConfigureSurfaceTexture("Assets/_Game/Resources/Textures/VolcanicRock.png");
            ConfigureSurfaceTexture("Assets/_Game/Resources/Models/Environment/PalmHero/PalmHeroAtlas.png");
        }

        private static void ConfigureSurfaceTexture(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                throw new InvalidOperationException($"Textura de superfície ausente: {path}");
            }

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = false;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 2;
            importer.maxTextureSize = 512;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 512;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void ConfigureTexture(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
            {
                throw new InvalidOperationException($"Textura ausente: {path}");
            }

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.sRGBTexture = true;
            importer.maxTextureSize = 1024;
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(android);
            importer.SaveAndReimport();
        }

        private static void EnsureShaderMaterials()
        {
            EnsureMaterial(
                "Assets/_Game/Resources/Generated/QuestLit.mat",
                "Universal Render Pipeline/Lit",
                material => SetColor(material, Color.white));
            EnsureMaterial(
                "Assets/_Game/Resources/Generated/QuestCutout.mat",
                "Universal Render Pipeline/Unlit",
                material =>
                {
                    SetColor(material, Color.white);
                    SetFloat(material, "_AlphaClip", 1f);
                    SetFloat(material, "_Cutoff", 0.1f);
                    SetFloat(material, "_Cull", 0f);
                    material.EnableKeyword("_ALPHATEST_ON");
                    material.renderQueue = (int)RenderQueue.AlphaTest;
                });
            EnsureMaterial(
                "Assets/_Game/Resources/Generated/QuestFade.mat",
                "Universal Render Pipeline/Unlit",
                material =>
                {
                    SetColor(material, new Color(0f, 0f, 0f, 1f));
                    SetFloat(material, "_Surface", 1f);
                    SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
                    SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    SetFloat(material, "_ZWrite", 0f);
                    material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    material.renderQueue = (int)RenderQueue.Overlay;
                });
            EnsureMaterial(
                "Assets/_Game/Resources/Generated/QuestSky.mat",
                "Skybox/Procedural",
                material =>
                {
                    SetColorProperty(material, "_SkyTint", new Color(0.28f, 0.48f, 0.53f));
                    SetColorProperty(material, "_GroundColor", new Color(0.20f, 0.25f, 0.16f));
                    SetFloat(material, "_AtmosphereThickness", 0.82f);
                    SetFloat(material, "_Exposure", 1.18f);
                });
        }

        private static void SetColorProperty(Material material, string property, Color color)
        {
            if (material.HasProperty(property)) material.SetColor(property, color);
        }

        private static void EnsureMaterial(string path, string shaderName, Action<Material> configure)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Shader ausente: {shaderName}");
            }

            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            configure(material);
            EditorUtility.SetDirty(material);
        }

        private static void EnsureScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new InvalidOperationException($"Falha ao salvar {ScenePath}.");
                }
            }

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }

        private static void EnsureFolder(string assetPath)
        {
            string[] parts = assetPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void SetColor(Material material, Color color)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        }

        private static void SetFloat(Material material, string property, float value)
        {
            if (material.HasProperty(property)) material.SetFloat(property, value);
        }
    }
}
#endif
