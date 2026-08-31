#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
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
        private const int Width = 1920;
        private const int Height = 1080;

        private static Camera _camera;
        private static RenderTexture _renderTarget;
        private static Shot[] _shots;
        private static int _shotIndex;
        private static int _nextFrame;
        private static bool _shotPrepared;
        private static bool _capturePending;
        private static double _waitStartedAt;
        private static double _captureRequestedAt;

        static ScenePreviewCapture()
        {
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
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
                PrepareCurrentShot();
                _shotPrepared = true;
                _nextFrame = Time.frameCount + 2;
                return;
            }

            _capturePending = true;
            _captureRequestedAt = EditorApplication.timeSinceStartup;
        }

        private static bool TryInitializeCapture()
        {
            if (GameObject.Find("Jurassic Ride") == null || Camera.main == null)
            {
                return false;
            }

            _camera = Camera.main;
            DisableRuntimeBehaviour("RideController");
            DisableRuntimeBehaviour("XRHeadTracker");
            DisableRuntimeBehaviour("ComfortFade");

            Transform fade = _camera.transform.Find("Comfort Fade");
            if (fade != null)
            {
                fade.gameObject.SetActive(false);
            }

            _camera.transform.SetParent(null, true);
            _camera.enabled = true;
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.allowHDR = false;
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

            _shots = BuildShots();
            _shotIndex = 0;
            _shotPrepared = false;
            _capturePending = false;
            _nextFrame = Time.frameCount + 60;
            Debug.Log($"[Vale Preview] Mundo pronto. {_shots.Length} capturas serão renderizadas.");
            return true;
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

        private static void PrepareCurrentShot()
        {
            Shot shot = _shots[_shotIndex];
            Vector3 direction = shot.Target - shot.Position;
            if (direction.sqrMagnitude < 0.001f)
            {
                Fail($"Enquadramento inválido para {shot.FileName}.");
                return;
            }

            _camera.fieldOfView = shot.FieldOfView;
            _camera.transform.SetPositionAndRotation(
                shot.Position,
                Quaternion.LookRotation(direction.normalized, Vector3.up));
            Debug.Log($"[Vale Preview] Preparando {_shotIndex + 1}/{_shots.Length}: {shot.FileName}");
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
            SessionState.SetBool(RunningKey, false);
            SessionState.SetBool(FinishingKey, false);
            SessionState.SetBool(PlayModeExitRequestedKey, false);
            SessionState.SetString(OutputPathKey, string.Empty);
            EditorApplication.Exit(exitCode);
        }

        private static void CleanupRenderState()
        {
            _capturePending = false;
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
            {
                FileName = fileName;
                Position = position;
                Target = target;
                FieldOfView = fieldOfView;
            }

            public string FileName { get; }
            public Vector3 Position { get; }
            public Vector3 Target { get; }
            public float FieldOfView { get; }
        }
    }
}
#endif
