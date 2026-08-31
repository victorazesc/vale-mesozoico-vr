#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ValeMesozoico.Editor
{
    internal static class PteranodonQaCapture
    {
        private static readonly string CaptureMarkerPath = Path.Combine(
            Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty,
            "Builds/Previews/.capture-pteranodon");
        private static bool _markerTriggered;

        [InitializeOnLoadMethod]
        private static void InitializeAutoCapture()
        {
            EditorApplication.update -= AutoCaptureTick;
            EditorApplication.update += AutoCaptureTick;
        }

        private static void AutoCaptureTick()
        {
            if (_markerTriggered || !EditorApplication.isPlaying || Time.time < 3f
                || !File.Exists(CaptureMarkerPath))
            {
                return;
            }

            File.Delete(CaptureMarkerPath);
            _markerTriggered = true;
            AuditRuntime();
            CaptureCloseup();
        }

        [MenuItem("Vale Mesozoico/QA/Auditar Pteranodon em Play Mode %&u")]
        public static void AuditRuntime()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Pteranodon QA] Entre em Play Mode antes da auditoria.");
                return;
            }

            StringBuilder report = new();
            int count = 0;
            for (int index = 1; index <= 3; index++)
            {
                GameObject actor = GameObject.Find($"Pteranodon {index}");
                if (actor == null)
                {
                    continue;
                }

                count++;
                SkinnedMeshRenderer renderer = actor.GetComponentInChildren<SkinnedMeshRenderer>(true);
                Animation animation = actor.GetComponentInChildren<Animation>(true);
                AnimationState playingState = null;
                if (animation != null)
                {
                    foreach (AnimationState state in animation)
                    {
                        if (animation.IsPlaying(state.name))
                        {
                            playingState = state;
                            break;
                        }
                    }
                }

                int triangles = 0;
                if (renderer != null && renderer.sharedMesh != null)
                {
                    for (int subMesh = 0; subMesh < renderer.sharedMesh.subMeshCount; subMesh++)
                    {
                        triangles += (int)renderer.sharedMesh.GetIndexCount(subMesh) / 3;
                    }
                }

                Material material = renderer != null ? renderer.sharedMaterial : null;
                report.AppendLine(
                    $"{actor.name}: tris={triangles}, bones={renderer?.bones.Length ?? 0}, " +
                    $"clip={playingState?.name ?? "none"}, speed={playingState?.speed ?? 0f:0.00}, " +
                    $"material={material?.name ?? "none"}, normal={HasTexture(material, "_BumpMap")}, " +
                    $"specGloss={HasTexture(material, "_SpecGlossMap")}, position={actor.transform.position}");
            }

            Debug.Log($"[Pteranodon QA] time={Time.time:0.00}s, count={count}\n{report}");
        }

        [MenuItem("Vale Mesozoico/QA/Capturar Pteranodon 3D %&o")]
        public static void CaptureCloseup()
        {
            if (!EditorApplication.isPlaying || Camera.main == null)
            {
                Debug.LogWarning("[Pteranodon QA] Play Mode e Main Camera são necessários para a captura.");
                return;
            }

            GameObject subject = GameObject.Find("Pteranodon 1");
            if (subject == null)
            {
                Debug.LogWarning("[Pteranodon QA] Pteranodon 1 não encontrado.");
                return;
            }

            PteranodonQaCaptureRunner runner = Camera.main.GetComponent<PteranodonQaCaptureRunner>();
            if (runner == null)
            {
                runner = Camera.main.gameObject.AddComponent<PteranodonQaCaptureRunner>();
            }
            runner.Begin(Camera.main, subject);
        }

        private static bool HasTexture(Material material, string property)
        {
            return material != null && material.HasProperty(property) && material.GetTexture(property) != null;
        }
    }

    internal sealed class PteranodonQaCaptureRunner : MonoBehaviour
    {
        private Camera _camera;
        private GameObject _subject;
        private bool _running;

        public void Begin(Camera camera, GameObject subject)
        {
            if (_running)
            {
                return;
            }

            _camera = camera;
            _subject = subject;
            _running = true;
            StartCoroutine(CaptureRoutine());
        }

        private IEnumerator CaptureRoutine()
        {
            Transform cameraTransform = _camera.transform;
            Transform originalParent = cameraTransform.parent;
            Vector3 originalLocalPosition = cameraTransform.localPosition;
            Quaternion originalLocalRotation = cameraTransform.localRotation;
            float originalFieldOfView = _camera.fieldOfView;
            StereoTargetEyeMask originalStereoTarget = _camera.stereoTargetEye;
            EditorWindow gameView = null;
            PropertyInfo showGizmosProperty = null;
            bool originalShowGizmos = true;

            Type gameViewType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            if (gameViewType != null)
            {
                gameView = EditorWindow.GetWindow(gameViewType);
                showGizmosProperty = gameViewType.GetProperty(
                    "showGizmos",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (showGizmosProperty?.CanRead == true && showGizmosProperty.CanWrite)
                {
                    originalShowGizmos = (bool)showGizmosProperty.GetValue(gameView);
                    showGizmosProperty.SetValue(gameView, false);
                }
            }

            Renderer[] renderers = _subject.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0)
            {
                Debug.LogWarning("[Pteranodon QA] Modelo sem renderer para enquadramento.");
                UnityEngine.Object.Destroy(this);
                yield break;
            }

            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            cameraTransform.SetParent(null, true);
            _camera.stereoTargetEye = StereoTargetEyeMask.None;
            _camera.fieldOfView = 36f;
            float largestSize = Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z));
            Vector3 subjectForward = _subject.transform.forward.normalized;
            Vector3 cameraPosition = bounds.center
                + subjectForward * Mathf.Max(3.5f, largestSize * 0.48f)
                + _subject.transform.right * largestSize * 0.38f
                + Vector3.up * bounds.extents.y * 0.12f;
            cameraTransform.SetPositionAndRotation(
                cameraPosition,
                Quaternion.LookRotation((bounds.center - cameraPosition).normalized, Vector3.up));

            yield return null;
            yield return new WaitForEndOfFrame();
            yield return new WaitForEndOfFrame();

            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string outputDirectory = Path.Combine(projectRoot, "Builds/Previews");
            Directory.CreateDirectory(outputDirectory);
            string outputPath = Path.Combine(outputDirectory, "08-pteranodon-3d-qa.png");
            ScreenCapture.CaptureScreenshot(outputPath, 2);
            Debug.Log($"[Pteranodon QA] Captura solicitada: {outputPath}");

            yield return new WaitForEndOfFrame();
            yield return null;

            cameraTransform.SetParent(originalParent, false);
            cameraTransform.SetLocalPositionAndRotation(originalLocalPosition, originalLocalRotation);
            _camera.fieldOfView = originalFieldOfView;
            _camera.stereoTargetEye = originalStereoTarget;
            if (showGizmosProperty?.CanWrite == true && gameView != null)
            {
                showGizmosProperty.SetValue(gameView, originalShowGizmos);
            }
            _running = false;
            UnityEngine.Object.Destroy(this);
        }
    }
}
#endif
