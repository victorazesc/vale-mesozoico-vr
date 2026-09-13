#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace ValeMesozoico.Editor
{
    [InitializeOnLoad]
    public static class ScenePreviewCapture
    {
        private const string ScenePath = "Assets/_Game/Scenes/JurassicRide.unity";
        private const string RunningKey = "ValeMesozoico.PreviewCapture.Running";
        private const string FinishingKey = "ValeMesozoico.PreviewCapture.Finishing";
        private const string PlayModeExitRequestedKey = "ValeMesozoico.PreviewCapture.PlayModeExitRequested";
        private const string ExitCodeKey = "ValeMesozoico.PreviewCapture.ExitCode";
        private const string OutputPathKey = "ValeMesozoico.PreviewCapture.OutputPath";
        private const string ForestFloorEditorPreviewKey = "ValeMesozoico.PreviewCapture.ForestFloorEditor";
        private const string LagoonEditorPreviewKey = "ValeMesozoico.PreviewCapture.LagoonEditor";
        private const string TrackEditorPreviewKey = "ValeMesozoico.PreviewCapture.TrackEditor";
        private const string BlenderManifestResourcePath =
            "Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export";
        private static readonly bool ReferencePerspective =
            Environment.GetCommandLineArgs().Contains("-referencePerspectivePreview");
        private static int Width => ReferencePerspective ? 2160 : 1920;
        private static int Height => ReferencePerspective ? 918 : 1080;
        private const string TriceratopsPreviewKey = "ValeMesozoico.TriceratopsPreview";
        private static bool TriceratopsPreview => SessionState.GetBool(TriceratopsPreviewKey, false)
            || Environment.GetCommandLineArgs().Contains("-triceratopsPreview");
        private static bool ForestFloorEditorPreview => SessionState.GetBool(ForestFloorEditorPreviewKey, false);
        private static bool LagoonEditorPreview => SessionState.GetBool(LagoonEditorPreviewKey, false);
        private static bool TrackEditorPreview => SessionState.GetBool(TrackEditorPreviewKey, false);
        private static bool CaveFoliagePreview => (ForestFloorEditorPreview && !TriceratopsPreview && !LagoonEditorPreview && !TrackEditorPreview)
            || Environment.GetCommandLineArgs().Contains("-caveFoliagePreview");

        [Serializable]
        private sealed class BlenderPreviewManifest
        {
            public BlenderReferenceCamera referenceCamera;
        }

        [Serializable]
        private sealed class BlenderReferenceCamera
        {
            public Vector3 position;
            public Vector3 forward;
            public Vector3 up;
            public float verticalFovDegrees;
        }

        private static Camera _camera;
        private static MonoBehaviour _rideController;
        private static MethodInfo _seekRide;
        private static Transform _riderCameraParent;
        private static Vector3 _riderCameraPosition;
        private static Quaternion _riderCameraRotation;
        private static float _riderFieldOfView;
        private static RenderTexture _renderTarget;
        private static Shot[] _shots;
        private static int _shotIndex;
        private static int _nextFrame;
        private static bool _shotPrepared;
        private static bool _capturePending;
        private static double _waitStartedAt;
        private static double _captureRequestedAt;
        private static Material _lagoonMaterial;
        private static Renderer _lagoonRenderer;

        static ScenePreviewCapture()
        {
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void CaptureFromCli()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Fail("O Editor já está entrando ou executando em Play Mode.");
                return;
            }

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Não foi possível resolver a raiz do projeto.");
            string sceneAbsolutePath = Path.Combine(projectRoot, ScenePath);
            if (!File.Exists(sceneAbsolutePath))
            {
                Fail($"Cena de preview não encontrada: {sceneAbsolutePath}");
                return;
            }

            string requestedOutput = ReadCommandLineArgument("-previewOutput");
            string outputPath = string.IsNullOrWhiteSpace(requestedOutput)
                ? Path.Combine(projectRoot, "Builds/Previews")
                : Path.GetFullPath(requestedOutput);
            Directory.CreateDirectory(outputPath);

            ResetRuntimeState();
            SessionState.SetBool(ForestFloorEditorPreviewKey, false);
            SessionState.SetBool(LagoonEditorPreviewKey, false);
            SessionState.SetBool(TrackEditorPreviewKey, false);
            SessionState.SetString(OutputPathKey, outputPath);
            SessionState.SetInt(ExitCodeKey, 0);
            SessionState.SetBool(FinishingKey, false);
            SessionState.SetBool(PlayModeExitRequestedKey, false);
            SessionState.SetBool(RunningKey, true);

            try
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                Debug.Log($"[Vale Preview] Iniciando captura em {outputPath}");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Fail($"Não foi possível iniciar o Play Mode: {exception}");
            }
        }

        [MenuItem("Vale Mesozoico/Preview/Forest Floor and Rocks")]
        public static void CaptureForestFloorInEditor()
            => StartEditorPreview(false);

        [MenuItem("Vale Mesozoico/Preview/Lagoon Water")]
        public static void CaptureLagoonInEditor()
            => StartEditorPreview(true);

        [MenuItem("Vale Mesozoico/Preview/Track Wear and Supports")]
        public static void CaptureTrackInEditor()
            => StartEditorPreview(false, true);

        [MenuItem("Vale Mesozoico/Preview/Triceratops")]
        public static void CaptureTriceratopsInEditor() => StartEditorPreview(false, false, true);

        private static void StartEditorPreview(bool lagoon, bool track = false, bool triceratops = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Vale Preview] Saia do Play Mode antes de iniciar esta captura.");
                return;
            }

            if (SceneManager.GetActiveScene().path != ScenePath)
            {
                Debug.LogWarning("[Vale Preview] Abra a cena JurassicRide antes desta captura.");
                return;
            }

            SessionState.SetBool(ForestFloorEditorPreviewKey, true);
            SessionState.SetBool(TriceratopsPreviewKey, triceratops);
            SessionState.SetBool(LagoonEditorPreviewKey, lagoon);
            SessionState.SetBool(TrackEditorPreviewKey, track);
            try
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                    ?? throw new InvalidOperationException("Não foi possível resolver a raiz do projeto.");
                string outputPath = triceratops ? Path.Combine(projectRoot, "work/triceratops-realism/unity")
                    : Path.Combine(projectRoot, "Builds/Previews", track ? "Track" : lagoon ? "Lagoon" : "ForestFloor");
                Directory.CreateDirectory(outputPath);
                ResetRuntimeState();
                SessionState.SetString(OutputPathKey, outputPath);
                SessionState.SetInt(ExitCodeKey, 0);
                SessionState.SetBool(FinishingKey, false);
                SessionState.SetBool(PlayModeExitRequestedKey, false);
                SessionState.SetBool(RunningKey, true);
                Debug.Log($"[Vale Preview] Iniciando captura na cena aberta em {outputPath}");
                EditorApplication.EnterPlaymode();
            }
            catch (Exception exception)
            {
                Fail($"Não foi possível iniciar o preview: {exception}");
            }
        }

        private static void EditorTick()
        {
            if (SessionState.GetBool(FinishingKey, false))
            {
                if (EditorApplication.isPlaying)
                {
                    if (!SessionState.GetBool(PlayModeExitRequestedKey, false))
                    {
                        SessionState.SetBool(PlayModeExitRequestedKey, true);
                        EditorApplication.ExitPlaymode();
                    }

                    return;
                }

                ExitEditorWhenReady();
                return;
            }

            if (!SessionState.GetBool(RunningKey, false) || !EditorApplication.isPlaying)
            {
                return;
            }

            if (_waitStartedAt <= 0d)
            {
                _waitStartedAt = EditorApplication.timeSinceStartup;
            }

            if (_camera == null)
            {
                if (!TryInitializeCapture())
                {
                    if (EditorApplication.timeSinceStartup - _waitStartedAt > 90d)
                    {
                        Fail("Tempo esgotado aguardando o mundo procedural e a Main Camera.");
                    }

                    return;
                }
            }

            if (_capturePending)
            {
                if (EditorApplication.timeSinceStartup - _captureRequestedAt > 30d)
                {
                    Fail("Tempo esgotado aguardando a renderização URP da câmera de preview.");
                }

                return;
            }

            if (Time.frameCount < _nextFrame)
            {
                return;
            }

            if (!_shotPrepared)
            {
                if (!PrepareCurrentShot())
                {
                    return;
                }
                _shotPrepared = true;
                _nextFrame = Time.frameCount + 2;
                return;
            }

            _capturePending = true;
            _captureRequestedAt = EditorApplication.timeSinceStartup;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && ForestFloorEditorPreview
                && SessionState.GetBool(RunningKey, false))
            {
                Finish(1, "Captura interrompida ao sair do Play Mode.");
            }
        }

        private static bool TryInitializeCapture()
        {
            if (TriceratopsPreview && Time.time < 24f)
                return false;
            if (GameObject.Find("Jurassic Ride") == null || Camera.main == null)
            {
                return false;
            }

            _camera = Camera.main;
            _riderCameraParent = _camera.transform.parent;
            _riderCameraPosition = _camera.transform.localPosition;
            _riderCameraRotation = _camera.transform.localRotation;
            _riderFieldOfView = _camera.fieldOfView;
            _rideController = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                .FirstOrDefault(behaviour => behaviour != null && behaviour.GetType().Name == "RideController");
            _seekRide = _rideController?.GetType().GetMethod(
                "DeveloperSeekToProgress", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            DisableRuntimeBehaviour("RideController");
            DisableRuntimeBehaviour("XRHeadTracker");
            DisableRuntimeBehaviour("ComfortFade");

            if (!LagoonEditorPreview && !TrackEditorPreview && !TriceratopsPreview
                && GameObject.Find("Mountain Track Cave") != null && !ValidateMountainCave())
            {
                return false;
            }
            if (ForestFloorEditorPreview && !TriceratopsPreview && !LagoonEditorPreview && !TrackEditorPreview && !ValidateForestFloor())
            {
                return false;
            }
            if (LagoonEditorPreview && !ValidateLagoonWater())
            {
                return false;
            }

            Transform fade = _camera.transform.Find("Comfort Fade");
            if (fade != null)
            {
                fade.gameObject.SetActive(false);
            }

            _camera.transform.SetParent(null, true);
            _camera.enabled = true;
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.allowHDR = GameObject.Find("Blender Environment") != null;
            _camera.allowMSAA = true;
            _camera.aspect = (float)Width / Height;
            _camera.nearClipPlane = 0.05f;
            _camera.farClipPlane = 500f;

            _renderTarget = new RenderTexture(
                Width,
                Height,
                24,
                RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB)
            {
                name = "Vale Mesozoico Preview",
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _renderTarget.Create();
            _camera.targetTexture = _renderTarget;

            _shots = LagoonEditorPreview ? BuildLagoonShots() : BuildShots();
            if (!LagoonEditorPreview && CaveFoliagePreview)
            {
                MonoBehaviour foliage = Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
                    .FirstOrDefault(behaviour => behaviour.GetType().Name == "CaveEntranceFoliage");
                if (foliage == null)
                {
                    Fail("Folhas da entrada ausentes.");
                    return false;
                }
                Type type = foliage.GetType();
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                float entrance = (float)type.GetProperty("EntranceProgress", flags).GetValue(foliage);
                float first = (float)type.GetProperty("FirstDistance", flags).GetValue(foliage)
                    / (float)type.GetProperty("RouteLength", flags).GetValue(foliage);
                _shots = new[]
                {
                    new Shot("01-montanha-vegetacao.png", new Vector3(67f, 42f, 37f),
                        new Vector3(44f, 18f, 58f), 68f, Vector3.up, first - 0.035f),
                    Shot.Rider("02-entrada-folhas.png", first - 0.024f),
                    Shot.Rider("03-aproximacao.png", first - 0.012f),
                    Shot.Rider("04-passagem-folhas.png", entrance),
                    Shot.Rider("05-dentro-caverna.png", 0.443f),
                    new Shot("06-folhas-abertas.png", new Vector3(67f, 42f, 37f),
                        new Vector3(44f, 18f, 58f), 68f, Vector3.up, entrance),
                    new Shot("07-topo-e-encosta.png", new Vector3(54f, 41f, 43f),
                        new Vector3(41f, 25f, 60f), 57f, Vector3.up, first - 0.035f),
                    new Shot("08-encosta-lago.png", new Vector3(14f, 26f, 43f),
                        new Vector3(37f, 17f, 62f), 55f, Vector3.up, first - 0.035f)
                };
            }
            if (!TriceratopsPreview && !LagoonEditorPreview && (ForestFloorEditorPreview || Environment.GetCommandLineArgs().Any(argument =>
                    string.Equals(argument, "-routePreview", StringComparison.OrdinalIgnoreCase))))
            {
                int[] routePercentages = { 3, 10, 17, 24, 31, 35, 43, 51, 58, 65, 71, 80, 87, 94 };
                _shots = _shots.Concat(routePercentages.Select(progress =>
                    Shot.Rider($"route-rider-{progress:000}.png", progress / 100f))).ToArray();
            }
            if (ForestFloorEditorPreview && !TriceratopsPreview && !LagoonEditorPreview)
            {
                _shots = new[]
                {
                    Shot.Rider("solo-rider-006.png", 0.006f),
                    Shot.Rider("pedra-rider-3p2.png", 0.032f)
                }.Concat(_shots).ToArray();
            }
            if (TrackEditorPreview) _shots = BuildTrackShots();
            _shotIndex = 0;
            _shotPrepared = false;
            _capturePending = false;
            _nextFrame = Time.frameCount + 60;
            Debug.Log($"[Vale Preview] Mundo pronto. {_shots.Length} capturas serão renderizadas.");
            return true;
        }

        private static Shot[] BuildTrackShots()
        {
            List<Shot> shots = new();
            MeshFilter rails = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                .First(filter => filter.name == "Polished Running Rails");
            Vector3[] railVertices = rails.sharedMesh.vertices;
            Vector3 rail = rails.transform.TransformPoint(railVertices[28 * 11 + 2]);
            Vector3 forward = (rails.transform.TransformPoint(railVertices[29 * 11 + 2]) - rail).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            shots.Add(new Shot("01-rolamento-detalhe.png", rail - right * 0.55f + Vector3.up * 0.40f - forward * 0.55f,
                rail + forward * 0.30f, 52f, Vector3.up, 0.6f));
            shots.Add(new Shot("02-trilho-sem-aneis.png", rail - right * 1.5f + Vector3.up * 0.45f - forward * 1.3f,
                rail + right * 0.55f - Vector3.up * 0.25f, 58f, Vector3.up, 0.6f));
            MeshFilter supports = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                .First(filter => filter.name == "Tubular Ground Supports");
            Vector3[] vertices = supports.sharedMesh.vertices;
            Vector3 foot = Vector3.zero;
            Vector3 top = Vector3.zero;
            float tallest = -1f;
            for (int support = 0; support + 82 <= vertices.Length; support += 82)
            {
                Vector3 candidateFoot = Vector3.zero;
                Vector3 candidateTop = Vector3.zero;
                for (int index = 26; index < 36; index++) candidateFoot += vertices[support + index] / 10f;
                for (int index = 8; index < 16; index++) candidateTop += vertices[support + index] / 8f;
                candidateFoot = supports.transform.TransformPoint(candidateFoot);
                candidateTop = supports.transform.TransformPoint(candidateTop);
                float height = candidateTop.y - candidateFoot.y;
                if (candidateFoot.y < 0.5f || height <= tallest) continue;
                tallest = height;
                foot = candidateFoot;
                top = candidateTop;
            }
            if (tallest > 0f)
            {
                shots.Add(new Shot("03-apoio-base.png", foot + new Vector3(1.8f, 1.6f, 2f),
                    foot + Vector3.up * 0.2f, 52f, Vector3.up, 0.6f));
                shots.Add(new Shot("04-apoio-encaixe-superior.png", top + new Vector3(2.2f, -0.8f, 2.3f),
                    top, 52f, Vector3.up, 0.6f));
                Vector3 middle = (foot + top) * 0.5f;
                shots.Add(new Shot("05-apoio-completo.png", middle + new Vector3(tallest * 0.70f, 0f, tallest * 0.70f),
                    middle, 55f, Vector3.up, 0.6f));
            }
            shots.Add(Shot.Rider("06-percurso-inicio.png", 0.006f));
            shots.Add(Shot.Rider("07-percurso-subida.png", 0.24f));
            shots.Add(Shot.Rider("08-percurso-curva.png", 0.71f));
            return shots.ToArray();
        }

        private static bool ValidateForestFloor()
        {
            try
            {
                Type qaType = _rideController?.GetType().Assembly.GetType("ValeMesozoico.EnvironmentRuntimeQa");
                MethodInfo validate = qaType?.GetMethod("ValidateForestFloorForPreview",
                    BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object[] arguments = { null };
                if (validate == null || !(bool)validate.Invoke(null, arguments))
                {
                    Fail($"Validação do chão da floresta falhou: {arguments[0] ?? "método de QA indisponível"}");
                    return false;
                }
                Debug.Log($"[Vale Preview] Apoio das plantas validado: {arguments[0]}");
                return true;
            }
            catch (Exception exception)
            {
                Fail($"Falha ao validar o chão da floresta: {exception.GetBaseException().Message}");
                return false;
            }
        }

        private static bool ValidateLagoonWater()
        {
            try
            {
                GameObject environment = GameObject.Find("Blender Environment");
                GameObject lagoon = environment != null
                    ? FindDescendant(environment.transform, "Lagoon_Water_Editable")
                    : GameObject.Find("Lagoon_Water_Editable");
                Renderer water = lagoon != null
                    ? lagoon.transform.Find("Geometry")?.GetComponent<Renderer>()
                    : null;
                Material material = water != null ? water.sharedMaterial : null;
                if (water == null || !water.enabled || !water.gameObject.activeInHierarchy
                    || material == null || material.shader == null
                    || material.shader.name != "Vale Mesozoico/Lagoon Water" || !material.shader.isSupported)
                {
                    Fail("Lago sem renderer ativo com o shader Vale Mesozoico/Lagoon Water suportado.");
                    return false;
                }

                Component cameraData = _camera.GetComponent("UniversalAdditionalCameraData");
                object requiresDepth = cameraData?.GetType().GetProperty("requiresDepthTexture")?.GetValue(cameraData);
                if (!(requiresDepth is bool depthEnabled) || !depthEnabled)
                {
                    Fail("A câmera do lago precisa de requiresDepthTexture habilitado.");
                    return false;
                }

                string[] requiredProperties =
                {
                    "_BumpMap", "_NormalStrength", "_RippleScale", "_FlowSpeed", "_Absorption",
                    "_AlphaMax", "_EdgeFade", "_WaveAmplitude", "_WaveLength", "_WaveSpeed", "_WaveTime"
                };
                if (requiredProperties.Any(property => !material.HasProperty(property)))
                {
                    Fail("O material do lago não expõe todas as propriedades de transparência e movimento.");
                    return false;
                }

                float alpha = material.GetFloat("_AlphaMax");
                float edgeFade = material.GetFloat("_EdgeFade");
                if (!(alpha > 0f && alpha < 1f) || !(edgeFade > 0f)
                    || !(material.GetFloat("_Absorption") > 0f)
                    || !(material.GetFloat("_WaveAmplitude") > 0f)
                    || !(material.GetFloat("_WaveLength") > 0f)
                    || !(material.GetFloat("_WaveSpeed") > 0f)
                    || !(material.GetFloat("_FlowSpeed") > 0f)
                    || material.GetTexture("_BumpMap") == null
                    || material.renderQueue < (int)RenderQueue.Transparent)
                {
                    Fail("Parâmetros de transparência, borda ou movimento do lago inválidos.");
                    return false;
                }

                _lagoonMaterial = material;
                _lagoonRenderer = water;
                Debug.Log($"[Vale Preview] Lago: shader ativo, depth texture habilitada, "
                    + $"alpha máximo={alpha:F2}, faixa da borda={edgeFade:F2}m. Inspeção dos PNGs pendente.");
                return true;
            }
            catch (Exception exception)
            {
                Fail($"Falha ao validar a água do lago: {exception.GetBaseException().Message}");
                return false;
            }
        }

        private static bool ValidateMountainCave()
        {
            try
            {
                if (_rideController == null || _seekRide == null || _riderCameraParent == null)
                {
                    Fail("Controlador ou câmera real do carrinho indisponível para validar a caverna.");
                    return false;
                }

                Type qaType = _rideController.GetType().Assembly.GetType("ValeMesozoico.EnvironmentRuntimeQa");
                MethodInfo validate = qaType?.GetMethod(
                    "ValidateMountainCaveForPreview", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object[] arguments = { null };
                if (validate == null || !(bool)validate.Invoke(null, arguments))
                {
                    Fail($"Validação geométrica da caverna falhou: {arguments[0] ?? "método de QA indisponível"}");
                    return false;
                }

                Debug.Log($"[Vale Preview] Caverna validada: {arguments[0]}");
                if (CaveFoliagePreview)
                {
                    MethodInfo foliageValidation = qaType?.GetMethod("ValidateCaveFoliageForPreview",
                        BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    arguments[0] = null;
                    if (foliageValidation == null || !(bool)foliageValidation.Invoke(null, arguments))
                    {
                        Fail($"Validação das folhas falhou: {arguments[0]}");
                        return false;
                    }
                }
                return true;
            }
            catch (Exception exception)
            {
                Fail($"Falha ao validar a caverna: {exception.GetBaseException().Message}");
                return false;
            }
        }

        private static void DisableRuntimeBehaviour(string typeName)
        {
            foreach (MonoBehaviour behaviour in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None))
            {
                if (behaviour != null && behaviour.GetType().Name == typeName)
                {
                    behaviour.enabled = false;
                }
            }
        }

        private static Shot[] BuildShots()
        {
            if (TriceratopsPreview)
            {
                GameObject animal = GameObject.Find("Triceratops");
                Vector3 center = animal.transform.position + Vector3.up * 1.6f;
                Vector3 right = animal.transform.right;
                Vector3 forward = animal.transform.forward;
                return new[]
                {
                    new Shot("01-triceratops-corpo.png", center + right * 9f + forward * 7f + Vector3.up * 2f,
                        center, 52f, Vector3.up, 0.08f),
                    new Shot("02-triceratops-patas.png", center + right * 5.5f + forward * 2f - Vector3.up * 0.2f,
                        center - Vector3.up * 0.5f, 55f, Vector3.up, 0.08f),
                    new Shot("03-triceratops-cabeca.png", center + right * 4.5f + forward * 6.5f + Vector3.up * 0.5f,
                        center + forward * 2.3f + Vector3.up * 0.25f, 48f, Vector3.up, 0.08f),
                    new Shot("04-manada.png", GameObject.Find("Triceratops Herd Leader").transform.position + new Vector3(10f, 5f, 10f),
                        GameObject.Find("Triceratops Herd Leader").transform.position + Vector3.up * 1.5f, 58f, Vector3.up, 0.38f),
                    new Shot("05-triceratops-pele.png", center - right * 6f + forward * 3f + Vector3.up * 0.7f,
                        center + forward, 53f, Vector3.up, 0.08f)
                };
            }
            GameObject importedEnvironment = GameObject.Find("Blender Environment");
            if (importedEnvironment != null)
            {
                return BuildImportedEnvironmentShots(importedEnvironment);
            }

            List<Shot> shots = new(5)
            {
                CreateSubjectShot(
                    "01-tyrannosaurus.png",
                    GameObject.Find("Tyrannosaurus Rex"),
                    new Vector3(-0.75f, 0.28f, -1f),
                    new Vector3(-42f, 8f, 42f),
                    new Vector3(-20f, 5f, 58f),
                    62f),
                CreateSubjectShot(
                    "02-sauropod.png",
                    GameObject.Find("Apatosaurus"),
                    new Vector3(1f, 0.30f, 0.72f),
                    new Vector3(58f, 12f, -8f),
                    new Vector3(39f, 7f, 14f),
                    62f),
                CreateSubjectShot(
                    "03-lagoon-and-track.png",
                    GameObject.Find("Lagoon"),
                    new Vector3(-0.72f, 0.42f, -1f),
                    new Vector3(5f, 18f, -15f),
                    new Vector3(38f, -0.4f, 28f),
                    58f),
                CreateSubjectShot(
                    "04-triceratops.png",
                    GameObject.Find("Triceratops"),
                    new Vector3(-0.85f, 0.26f, 0.65f),
                    new Vector3(-35f, 6f, -15f),
                    new Vector3(-16f, 3f, 3f),
                    60f),
                new Shot(
                    "05-station-and-track.png",
                    new Vector3(-18f, 10f, -75f),
                    new Vector3(17f, 6f, -48f),
                    66f)
            };
            return shots.ToArray();
        }

        private static Shot[] BuildLagoonShots()
        {
            Shot[] stills =
            {
                new Shot("lago-margem.png", new Vector3(0f, 4f, 5f),
                    new Vector3(18f, -0.82f, 14f), 60f),
                new Shot("lago-amplo.png", new Vector3(12f, 13f, 18f),
                    new Vector3(40f, -0.82f, 30f), 65f),
                new Shot("ondas-a.png", new Vector3(18f, 7f, 25f),
                    new Vector3(42f, -0.82f, 32f), 60f, Vector3.up, waveTime: 0f),
                new Shot("ondas-b.png", new Vector3(18f, 7f, 25f),
                    new Vector3(42f, -0.82f, 32f), 60f, Vector3.up, waveTime: 2.5f),
                new Shot("ondas-sem-agua.png", new Vector3(18f, 7f, 25f),
                    new Vector3(42f, -0.82f, 32f), 60f, Vector3.up, waveTime: 0f, hideWater: true),
                Shot.Rider("route-rider-010.png", 0.10f),
                Shot.Rider("route-rider-017.png", 0.17f)
            };
            return stills.Concat(Enumerable.Range(0, 24).Select(frame =>
                new Shot($"lago-mov-{frame:000}.png", new Vector3(18f, 7f, 25f),
                    new Vector3(42f, -0.82f, 32f), 60f, Vector3.up, waveTime: frame / 12f))).ToArray();
        }

        private static Shot[] BuildImportedEnvironmentShots(GameObject environment)
        {
            if (ReferencePerspective)
            {
                // Panoramic view aligned to the supplied image: foreground track,
                // central lagoon and the rock/crest on the right side of the valley.
                return new[]
                {
                    new Shot("17-perspectiva-referencia-elevada.png",
                        new Vector3(28f, 65f, -100f), new Vector3(-25f, 12f, 22f),
                        36f, Vector3.up, 0.10f),
                    new Shot("18-perspectiva-vale.png",
                        new Vector3(45f, 65f, -100f), new Vector3(-25f, 12f, 22f),
                        38f, Vector3.up, 0.10f),
                    Shot.Rider("19-caverna-validacao.png", 0.443f),
                    new Shot("20-rocha-detalhe.png",
                        new Vector3(-10f, 17f, 36f), new Vector3(36f, 14f, 64f),
                        54f, Vector3.up, 0.10f)
                };
            }
            List<Shot> shots = new(5);
            TextAsset manifestAsset = Resources.Load<TextAsset>(BlenderManifestResourcePath);
            BlenderPreviewManifest manifest = manifestAsset != null
                ? JsonUtility.FromJson<BlenderPreviewManifest>(manifestAsset.text)
                : null;
            BlenderReferenceCamera reference = manifest?.referenceCamera;
            if (reference != null && reference.forward.sqrMagnitude > 0.5f)
            {
                shots.Add(new Shot(
                    "01-blender-reference.png",
                    reference.position,
                    reference.position + reference.forward.normalized * 120f,
                    Mathf.Clamp(reference.verticalFovDegrees, 20f, 80f),
                    reference.up.sqrMagnitude > 0.5f ? reference.up.normalized : Vector3.up));
            }
            else
            {
                shots.Add(new Shot(
                    "01-blender-reference.png",
                    new Vector3(-138f, 54f, 158f),
                    new Vector3(0f, 7f, 0f),
                    34f));
            }

            GameObject lagoon = FindDescendant(environment.transform, "Lagoon_Water_Editable");
            shots.Add(CreateSubjectShot(
                "02-lagoon-close.png",
                lagoon,
                new Vector3(-0.75f, 0.48f, 0.72f),
                new Vector3(-55f, 34f, 72f),
                new Vector3(18f, 1f, 15f),
                52f));

            shots.Add(new Shot(
                "03-cave-and-track.png",
                new Vector3(-14f, 17f, 111f),
                new Vector3(43.7f, 5.4f, 59.7f),
                51f));

            shots.Add(new Shot(
                "04-rider-start.png",
                new Vector3(0f, 6.15f, -56.7f),
                new Vector3(34f, 5.1f, -52f),
                76f));
            shots.Add(new Shot(
                "05-high-overview.png",
                new Vector3(-118f, 98f, 145f),
                new Vector3(0f, 6f, 0f),
                52f));

            if (GameObject.Find("Mountain Track Cave") != null)
            {
                shots.Add(new Shot(
                    "06-mountain-cave-with-cart.png",
                    new Vector3(67f, 42f, 37f),
                    new Vector3(44f, 18f, 58f),
                    68f, Vector3.up, 0.396f));
                shots.Add(Shot.Rider("07-rider-cave-entrance.png", 0.396f));
                shots.Add(Shot.Rider("08-rider-cave-middle.png", 0.443f));
                shots.Add(Shot.Rider("09-rider-cave-exit.png", 0.488f));
                shots.Add(Shot.Rider("10-rider-after-cave.png", 0.503f));
                if (Environment.GetCommandLineArgs().Contains("-mountainDetailPreview"))
                {
                    shots.Add(new Shot(
                        "11-mountain-profile.png",
                        new Vector3(93f, 49f, 24f),
                        new Vector3(40f, 17f, 61f),
                        50f, Vector3.up, 0.378f));
                    shots.Add(new Shot(
                        "12-lagoon-rock-profile.png",
                        new Vector3(-10f, 17f, 36f),
                        new Vector3(36f, 17f, 64f),
                        54f, Vector3.up, 0.378f));
                    shots.Add(new Shot(
                        "13-paredao-com-trilho.png",
                        new Vector3(-26f, 36f, 22f),
                        new Vector3(36f, 15f, 64f),
                        48f, Vector3.up, 0.396f));
                }
            }
            return shots.ToArray();
        }

        private static GameObject FindDescendant(Transform root, string expectedName)
        {
            Transform match = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate != null
                    && (string.Equals(candidate.name, expectedName, StringComparison.Ordinal)
                        || candidate.name.StartsWith(expectedName, StringComparison.Ordinal)));
            return match != null ? match.gameObject : null;
        }

        private static Shot CreateSubjectShot(
            string fileName,
            GameObject subject,
            Vector3 viewDirection,
            Vector3 fallbackPosition,
            Vector3 fallbackTarget,
            float fieldOfView)
        {
            if (subject == null || !TryGetRendererBounds(subject, out Bounds bounds))
            {
                Debug.LogWarning($"[Vale Preview] Sujeito ausente em {fileName}; usando enquadramento de fallback.");
                return new Shot(fileName, fallbackPosition, fallbackTarget, fieldOfView);
            }

            Debug.Log($"[Vale Preview] {subject.name} bounds center={bounds.center} size={bounds.size}");

            float verticalExtent = Mathf.Max(bounds.extents.y, bounds.extents.x / ((float)Width / Height));
            float distance = verticalExtent / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad) * 2.05f;
            distance = Mathf.Max(distance, 14f);
            Vector3 target = bounds.center + Vector3.up * bounds.extents.y * 0.04f;
            Vector3 position = target + viewDirection.normalized * distance;
            return new Shot(fileName, position, target, fieldOfView);
        }

        private static bool TryGetRendererBounds(GameObject subject, out Bounds bounds)
        {
            Renderer[] renderers = subject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return true;
        }

        private static bool PrepareCurrentShot()
        {
            Shot shot = _shots[_shotIndex];
            if (LagoonEditorPreview && _lagoonMaterial != null)
            {
                _lagoonMaterial.SetFloat("_WaveTime", shot.WaveTime);
            }
            if (LagoonEditorPreview && _lagoonRenderer != null)
            {
                _lagoonRenderer.enabled = !shot.HideWater;
            }
            if (shot.RideProgress >= 0f)
            {
                try
                {
                    if (_rideController == null || _seekRide == null || _riderCameraParent == null)
                    {
                        Fail($"Câmera real do carrinho indisponível para {shot.FileName}.");
                        return false;
                    }

                    _camera.transform.SetParent(_riderCameraParent, false);
                    _camera.transform.SetLocalPositionAndRotation(_riderCameraPosition, _riderCameraRotation);
                    _seekRide.Invoke(_rideController, new object[] { shot.RideProgress });
                    if (shot.RiderCamera)
                    {
                        _camera.fieldOfView = _riderFieldOfView;
                        if (shot.FileName == "pedra-rider-3p2.png")
                        {
                            LogRockShotMaterials();
                        }
                        Debug.Log($"[Vale Preview] Rider real {shot.FileName}: progress={shot.RideProgress:F3}, "
                            + $"camera={_camera.transform.position}, cart={_rideController.transform.position}");
                        return true;
                    }
                }
                catch (Exception exception)
                {
                    Fail($"Falha ao posicionar carrinho: {exception.GetBaseException().Message}");
                    return false;
                }
            }

            _camera.transform.SetParent(null, true);
            Vector3 direction = shot.Target - shot.Position;
            if (direction.sqrMagnitude < 0.001f)
            {
                Fail($"Enquadramento inválido para {shot.FileName}.");
                return false;
            }

            _camera.fieldOfView = shot.FieldOfView;
            _camera.transform.SetPositionAndRotation(
                shot.Position,
                Quaternion.LookRotation(direction.normalized, shot.Up));
            Debug.Log($"[Vale Preview] Preparando {_shotIndex + 1}/{_shots.Length}: {shot.FileName}");
            return true;
        }

        private static void LogRockShotMaterials()
        {
            Ray ray = _camera.ViewportPointToRay(new Vector3(0.25f, 0.50f, 0f));
            List<(float distance, MeshRenderer renderer)> hits = new();
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy)
                {
                    continue;
                }

                bool rockMaterial = renderer.sharedMaterials.Any(material => material != null
                    && (material.name.IndexOf("Boulder", StringComparison.OrdinalIgnoreCase) >= 0
                        || material.name.IndexOf("Mountainside", StringComparison.OrdinalIgnoreCase) >= 0
                        || material.name.IndexOf("RockMoss", StringComparison.OrdinalIgnoreCase) >= 0));
                bool pcParent = renderer.transform.parent != null
                    && renderer.transform.parent.name.StartsWith("PC", StringComparison.OrdinalIgnoreCase);
                if ((rockMaterial || pcParent) && renderer.bounds.IntersectRay(ray, out float distance))
                {
                    hits.Add((distance, renderer));
                }
            }

            Debug.Log($"[Vale Preview] Pedra 3.2%: {hits.Count} bounds no raio viewport (0.25, 0.50).");
            foreach (var hit in hits.OrderBy(hit => hit.distance).Take(5))
            {
                MeshRenderer renderer = hit.renderer;
                string materials = string.Join("; ", renderer.sharedMaterials.Select(material =>
                {
                    if (material == null)
                    {
                        return "material ausente";
                    }
                    Texture baseTexture = material.HasProperty("_BaseMap")
                        ? material.GetTexture("_BaseMap")
                        : material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") : null;
                    return $"material={material.name}, shader={material.shader?.name}, baseTexture={baseTexture?.name}";
                }));
                Debug.Log($"[Vale Preview] Pedra 3.2% bounds hit: distance={hit.distance:F2}, "
                    + $"parent={renderer.transform.parent?.name}, renderer={renderer.name}, "
                    + $"mesh={renderer.GetComponent<MeshFilter>()?.sharedMesh?.name}, {materials}, "
                    + $"bounds={renderer.bounds}");
            }
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera renderedCamera)
        {
            if (!_capturePending || renderedCamera != _camera || _renderTarget == null)
            {
                return;
            }

            try
            {
                Texture2D image = new(Width, Height, TextureFormat.RGB24, false, false);
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = _renderTarget;
                image.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0, false);
                image.Apply(false, false);
                RenderTexture.active = previous;

                string outputPath = SessionState.GetString(OutputPathKey, string.Empty);
                string filePath = Path.Combine(outputPath, _shots[_shotIndex].FileName);
                File.WriteAllBytes(filePath, image.EncodeToPNG());
                Object.Destroy(image);
                Debug.Log($"[Vale Preview] PNG salvo: {filePath}");

                _shotIndex++;
                _capturePending = false;
                _shotPrepared = false;
                _nextFrame = Time.frameCount + 2;
                if (_shotIndex >= _shots.Length)
                {
                    Finish(0, $"Captura concluída: {_shots.Length} PNGs em {outputPath}");
                }
            }
            catch (Exception exception)
            {
                Fail($"Falha ao salvar o preview: {exception}");
            }
        }

        private static void Finish(int exitCode, string message)
        {
            if (exitCode == 0 && TriceratopsPreview)
            {
                Type qa = _rideController?.GetType().Assembly.GetType("ValeMesozoico.TriceratopsRuntimeQa");
                PropertyInfo passed = qa?.GetProperty("Passed", BindingFlags.Static | BindingFlags.NonPublic);
                if (passed == null || !(bool)passed.GetValue(null))
                {
                    exitCode = 1;
                    message += " | QA dos triceratops falhou; consulte work/triceratops-realism/runtime-qa.txt.";
                }
            }
            if (exitCode == 0)
            {
                Debug.Log($"[Vale Preview] {message}");
            }
            else
            {
                Debug.LogError($"[Vale Preview] {message}");
            }

            CleanupRenderState();
            SessionState.SetBool(RunningKey, false);
            SessionState.SetInt(ExitCodeKey, exitCode);
            SessionState.SetBool(FinishingKey, true);
            SessionState.SetBool(PlayModeExitRequestedKey, false);
        }

        private static void Fail(string message)
        {
            Finish(1, message);
        }

        private static void ExitEditorWhenReady()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            int exitCode = SessionState.GetInt(ExitCodeKey, 1);
            bool keepEditorOpen = ForestFloorEditorPreview;
            SessionState.SetBool(TriceratopsPreviewKey, false);
            SessionState.SetBool(TrackEditorPreviewKey, false);
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(FinishingKey, false);
            SessionState.SetBool(PlayModeExitRequestedKey, false);
            SessionState.SetString(OutputPathKey, string.Empty);
            SessionState.SetBool(ForestFloorEditorPreviewKey, false);
            SessionState.SetBool(LagoonEditorPreviewKey, false);
            if (!keepEditorOpen)
            {
                EditorApplication.Exit(exitCode);
            }
        }

        private static void CleanupRenderState()
        {
            _capturePending = false;
            if (_lagoonRenderer != null)
            {
                _lagoonRenderer.enabled = true;
                _lagoonRenderer = null;
            }
            if (_lagoonMaterial != null)
            {
                _lagoonMaterial.SetFloat("_WaveTime", -1f);
                _lagoonMaterial = null;
            }
            if (_camera != null)
            {
                _camera.targetTexture = null;
            }

            if (_renderTarget != null)
            {
                _renderTarget.Release();
                Object.Destroy(_renderTarget);
            }

            _camera = null;
            _rideController = null;
            _seekRide = null;
            _riderCameraParent = null;
            _renderTarget = null;
            _shots = null;
        }

        private static void ResetRuntimeState()
        {
            CleanupRenderState();
            _shotIndex = 0;
            _nextFrame = 0;
            _shotPrepared = false;
            _waitStartedAt = 0d;
            _captureRequestedAt = 0d;
        }

        private static string ReadCommandLineArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                {
                    return arguments[index + 1];
                }
            }

            return null;
        }

        private readonly struct Shot
        {
            public Shot(string fileName, Vector3 position, Vector3 target, float fieldOfView)
                : this(fileName, position, target, fieldOfView, Vector3.up)
            {
            }

            public Shot(
                string fileName,
                Vector3 position,
                Vector3 target,
                float fieldOfView,
                Vector3 up,
                float rideProgress = -1f,
                bool riderCamera = false,
                float waveTime = -1f,
                bool hideWater = false)
            {
                FileName = fileName;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
                Up = up;
                RideProgress = rideProgress;
                RiderCamera = riderCamera;
                WaveTime = waveTime;
                HideWater = hideWater;
            }

            public static Shot Rider(string fileName, float progress)
                => new(fileName, Vector3.zero, Vector3.forward, 75f, Vector3.up, progress, true);

            public string FileName { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
            public Vector3 Up { get; }
            public float RideProgress { get; }
            public bool RiderCamera { get; }
            public float WaveTime { get; }
            public bool HideWater { get; }
        }
    }
}
#endif
