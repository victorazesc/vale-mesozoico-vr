using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class RideDebugTimeline : MonoBehaviour
    {
        private static readonly float[] PlaybackRates = { 0.5f, 1f, 2f, 3f };

        private RideController _controller;
        private GUIStyle _labelStyle;
        private GUIStyle _buttonStyle;
        private bool _paused;
        private bool _scrubbing;
        private float _scrubProgress;
        private float _lastScrubInputTime;
        private int _playbackRateIndex = 1;

        internal static bool DeveloperUiAllowed => Application.isEditor || Debug.isDebugBuild;
        internal bool IsPaused => _paused;
        internal float PlaybackRate => PlaybackRates[_playbackRateIndex];

        private static float UiScale => Mathf.Clamp(Screen.width / 1200f, 0.78f, 1.25f);

        private static Rect GetPanelRect(float uiScale)
        {
            float virtualWidth = Screen.width / uiScale;
            float virtualHeight = Screen.height / uiScale;
            float panelWidth = Mathf.Min(820f, virtualWidth - 32f);
            return new Rect((virtualWidth - panelWidth) * 0.5f, virtualHeight - 112f, panelWidth, 92f);
        }

        internal static bool ContainsScreenPoint(Vector2 point)
        {
            return DeveloperUiAllowed && GetPanelRect(UiScale).Contains(point / UiScale);
        }

        internal void Initialize(RideController controller)
        {
            _controller = controller;
            enabled = DeveloperUiAllowed;
            _controller.SetDeveloperPlaybackRate(PlaybackRate);
        }

        private void Awake()
        {
            enabled = DeveloperUiAllowed;
        }

        private void OnGUI()
        {
            if (!DeveloperUiAllowed || _controller == null)
            {
                return;
            }

            EnsureStyles();
            Matrix4x4 previousMatrix = GUI.matrix;
            Color previousColor = GUI.color;
            float uiScale = UiScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(uiScale, uiScale, 1f));

            Rect panel = GetPanelRect(uiScale);

            GUI.color = new Color(0.025f, 0.035f, 0.035f, 0.92f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = Color.white;

            float progress = _controller.RideProgress;
            GUI.Label(
                new Rect(panel.x + 16f, panel.y + 8f, panel.width - 32f, 22f),
                $"DEV  |  LINHA DO TEMPO  |  {progress * 100f:0.0}%  |  {_controller.RideSpeed:0.0} m/s",
                _labelStyle);

            bool previousChanged = GUI.changed;
            GUI.changed = false;
            float requestedProgress = GUI.HorizontalSlider(
                new Rect(panel.x + 18f, panel.y + 34f, panel.width - 36f, 18f),
                progress,
                0f,
                1f);
            bool sliderChanged = GUI.changed;
            GUI.changed |= previousChanged;
            if (sliderChanged && Mathf.Abs(requestedProgress - progress) > 0.0005f)
            {
                if (!_scrubbing)
                {
                    _scrubbing = true;
                    _controller.BeginDeveloperScrub();
                }
                _scrubProgress = requestedProgress;
                _lastScrubInputTime = Time.unscaledTime;
                _controller.DeveloperPreviewSeekToProgress(requestedProgress);
            }

            float buttonY = panel.y + 57f;
            if (GUI.Button(new Rect(panel.x + 18f, buttonY, 78f, 25f), "- 5%", _buttonStyle))
            {
                _controller.DeveloperSeekToProgress(_controller.RideProgress - 0.05f);
            }

            if (GUI.Button(new Rect(panel.x + 104f, buttonY, 92f, 25f), _paused ? "CONTINUAR" : "PAUSAR", _buttonStyle))
            {
                SetPaused(!_paused);
            }

            if (GUI.Button(new Rect(panel.x + 204f, buttonY, 78f, 25f), "+ 5%", _buttonStyle))
            {
                _controller.DeveloperSeekToProgress(_controller.RideProgress + 0.05f);
            }

            if (panel.width >= 760f && !UnityEngine.XR.XRSettings.isDeviceActive
                && !UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.Head).isValid)
            {
                GUI.Label(new Rect(panel.x + 296f, buttonY, panel.width - 448f, 25f),
                    "Arraste: olhar  |  R: centralizar", _labelStyle);
            }

            if (GUI.Button(
                    new Rect(panel.x + panel.width - 138f, buttonY, 120f, 25f),
                    $"VELOCIDADE {PlaybackRate:0.#}x",
                    _buttonStyle))
            {
                CyclePlaybackRate();
            }

            GUI.color = previousColor;
            GUI.matrix = previousMatrix;
        }

        private void EnsureStyles()
        {
            if (_labelStyle != null)
            {
                return;
            }

            _labelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                fontSize = 13,
                fontStyle = FontStyle.Bold
            };
            _labelStyle.normal.textColor = new Color(0.82f, 0.94f, 0.89f);

            _buttonStyle = new GUIStyle(GUI.skin.button)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
        }

        private void Update()
        {
            if (_scrubbing && Time.unscaledTime - _lastScrubInputTime >= 0.08f)
            {
                _scrubbing = false;
                _controller.CompleteDeveloperScrub(_scrubProgress);
            }
        }

        private void SetPaused(bool paused)
        {
            CompleteScrubIfNeeded();
            _paused = paused;
            _controller.SetDeveloperPaused(paused);
            Time.timeScale = paused ? 0f : PlaybackRate;
            AudioListener.pause = paused;
            Debug.Log($"[Developer Timeline] {(paused ? "PAUSE" : "PLAY")} | progress={_controller.RideProgress:F3}");
        }

        private void CyclePlaybackRate()
        {
            CompleteScrubIfNeeded();
            _playbackRateIndex = (_playbackRateIndex + 1) % PlaybackRates.Length;
            _controller.SetDeveloperPlaybackRate(PlaybackRate);
            if (!_paused)
            {
                Time.timeScale = PlaybackRate;
            }
            Debug.Log($"[Developer Timeline] SPEED | rate={PlaybackRate:0.#}x");
        }

        private void CompleteScrubIfNeeded()
        {
            if (!_scrubbing)
            {
                return;
            }

            _scrubbing = false;
            _controller.CompleteDeveloperScrub(_scrubProgress);
        }

        private void OnDisable()
        {
            if (_scrubbing)
            {
                _controller?.CancelDeveloperScrub();
            }
            _scrubbing = false;
            _paused = false;
            _playbackRateIndex = 1;
            _controller?.SetDeveloperPaused(false);
            _controller?.SetDeveloperPlaybackRate(1f);
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }
    }

#if UNITY_EDITOR
    internal sealed class RideDebugTimelineQa : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Ride Debug Timeline QA").AddComponent<RideDebugTimelineQa>();
        }

        private System.Collections.IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.8f);

            RideController controller = FindFirstObjectByType<RideController>();
            RideDebugTimeline timeline = FindFirstObjectByType<RideDebugTimeline>();
            bool pass = controller != null
                && timeline != null
                && timeline.enabled
                && RideDebugTimeline.DeveloperUiAllowed
                && Mathf.Approximately(timeline.PlaybackRate, 1f)
                && !timeline.IsPaused;
            Debug.Log(
                $"[Developer Timeline QA] {(pass ? "PASS" : "FAIL")} | "
                + $"controller={controller != null}, timeline={timeline != null}, "
                + $"allowed={RideDebugTimeline.DeveloperUiAllowed}, defaultRate={(timeline != null ? timeline.PlaybackRate : 0f):0.#}x");
            Destroy(gameObject);
        }
    }
#endif
}
