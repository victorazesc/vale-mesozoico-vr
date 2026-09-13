using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ValeMesozoico
{
    internal sealed class RideGameFlow : MonoBehaviour
    {
        private const float ExitDelay = 4.5f;

        private enum GamePhase
        {
            Intro,
            Riding,
            Finished
        }

        private RideController _controller;
        private ComfortFade _fade;
        private GamePhase _phase;
        private float _phaseTime;
        private InputDevice _leftController;
        private InputDevice _rightController;
        private bool _xrButtonWasPressed;
        private GameObject _worldMenu;
        private Material _panelMaterial;
        private Material _buttonMaterial;
        private GUIStyle _eyebrowStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _bodyStyle;
        private GUIStyle _buttonStyle;

        internal void Initialize(RideController controller, ComfortFade fade)
        {
            _controller = controller;
            _fade = fade;
            _phase = GamePhase.Intro;
            _controller.RideCompleted += HandleRideCompleted;
            ShowWorldMenu(
                "VALE MESOZOICO",
                "UMA AVENTURA SOBRE TRILHOS",
                "START",
                "Gatilho ou A para iniciar");
            Cursor.visible = true;
        }

        private void OnDestroy()
        {
            if (_controller != null)
            {
                _controller.RideCompleted -= HandleRideCompleted;
            }

            Destroy(_panelMaterial);
            Destroy(_buttonMaterial);
        }

        private void Update()
        {
            _phaseTime += Time.unscaledDeltaTime;

            if (_phase == GamePhase.Intro)
            {
                if (!_controller.WaitingToStart)
                {
                    _phase = GamePhase.Riding;
                    Cursor.visible = false;
                    DestroyWorldMenu();
                    return;
                }

                bool xrPressed = IsXrStartPressed();
                bool keyboardPressed = IsKeyboardStartPressed();
                if (keyboardPressed || (xrPressed && !_xrButtonWasPressed))
                {
                    StartGame();
                }
                _xrButtonWasPressed = xrPressed;
            }
            else if (_phase == GamePhase.Finished && _phaseTime >= ExitDelay)
            {
                ExitGame();
            }
        }

        private void OnGUI()
        {
            if (_phase == GamePhase.Riding || _controller == null)
            {
                return;
            }

            EnsureGuiStyles();
            int previousDepth = GUI.depth;
            GUI.depth = -100;

            Color previousColor = GUI.color;
            GUI.color = new Color(0.015f, 0.035f, 0.027f, 0.94f);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1200f, Screen.height / 720f), 0.72f, 1.55f);
            float width = Mathf.Min(620f * scale, Screen.width - 40f);
            float height = Mathf.Min(390f * scale, Screen.height - 40f);
            Rect panel = new((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.color = new Color(0.025f, 0.12f, 0.085f, 1f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            if (_phase == GamePhase.Intro)
            {
                DrawIntro(panel, scale);
            }
            else
            {
                DrawFinished(panel, scale);
            }

            GUI.color = previousColor;
            GUI.depth = previousDepth;
        }

        private void DrawIntro(Rect panel, float scale)
        {
            GUI.Label(new Rect(panel.x, panel.y + 58f * scale, panel.width, 24f * scale),
                "EXPEDIÇÃO JURÁSSICA", _eyebrowStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 91f * scale, panel.width - 48f, 64f * scale),
                "VALE MESOZOICO", _titleStyle);
            GUI.Label(new Rect(panel.x + 55f, panel.y + 166f * scale, panel.width - 110f, 54f * scale),
                "Uma volta completa pela montanha.\nMantenha-se sentado e aproveite a viagem.", _bodyStyle);

            Rect button = new(panel.center.x - 142f * scale, panel.yMax - 118f * scale,
                284f * scale, 62f * scale);
            if (GUI.Button(button, "START", _buttonStyle))
            {
                StartGame();
            }

            GUI.Label(new Rect(panel.x, panel.yMax - 42f * scale, panel.width, 18f * scale),
                "ENTER / ESPAÇO  •  QUEST: GATILHO OU A", _eyebrowStyle);
        }

        private void DrawFinished(Rect panel, float scale)
        {
            GUI.Label(new Rect(panel.x, panel.y + 82f * scale, panel.width, 24f * scale),
                "EXPEDIÇÃO CONCLUÍDA", _eyebrowStyle);
            GUI.Label(new Rect(panel.x + 24f, panel.y + 116f * scale, panel.width - 48f, 64f * scale),
                "FIM DA VIAGEM", _titleStyle);
            GUI.Label(new Rect(panel.x + 55f, panel.y + 201f * scale, panel.width - 110f, 52f * scale),
                "A volta pelo Vale Mesozoico terminou.\nAté a próxima aventura!", _bodyStyle);
            GUI.Label(new Rect(panel.x, panel.yMax - 60f * scale, panel.width, 22f * scale),
                "ENCERRANDO...", _eyebrowStyle);
        }

        private void StartGame()
        {
            if (_phase != GamePhase.Intro || !_controller.StartRide())
            {
                return;
            }

            _phase = GamePhase.Riding;
            _phaseTime = 0f;
            Cursor.visible = false;
            DestroyWorldMenu();
        }

        private void HandleRideCompleted()
        {
            _phase = GamePhase.Finished;
            _phaseTime = 0f;
            Cursor.visible = true;
            _fade.SetImmediate(0f);
            ShowWorldMenu(
                "FIM DA VIAGEM",
                "EXPEDIÇÃO CONCLUÍDA",
                "ATÉ A PRÓXIMA",
                "Encerrando...");
        }

        private bool IsXrStartPressed()
        {
            return DeviceButtonPressed(ref _leftController, XRNode.LeftHand)
                || DeviceButtonPressed(ref _rightController, XRNode.RightHand);
        }

        private static bool IsKeyboardStartPressed()
        {
#if ENABLE_INPUT_SYSTEM
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            return keyboard != null
                && (keyboard.enterKey.wasPressedThisFrame
                    || keyboard.numpadEnterKey.wasPressedThisFrame
                    || keyboard.spaceKey.wasPressedThisFrame);
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space);
#else
            return false;
#endif
        }

        private static bool DeviceButtonPressed(ref InputDevice device, XRNode node)
        {
            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(node);
            }

            if (!device.isValid)
            {
                return false;
            }

            return (device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger) && trigger)
                || (device.TryGetFeatureValue(CommonUsages.primaryButton, out bool primary) && primary);
        }

        private void ShowWorldMenu(string title, string subtitle, string button, string hint)
        {
            DestroyWorldMenu();
            _worldMenu = new GameObject("Ride Menu World Space");
            _worldMenu.transform.SetParent(transform, false);
            _worldMenu.transform.localPosition = new Vector3(0f, 0f, 1.4f);

            _panelMaterial ??= CreateUnlitMaterial("Ride Menu Panel", new Color(0.018f, 0.075f, 0.055f, 1f));
            _buttonMaterial ??= CreateUnlitMaterial("Ride Menu Start", new Color(0.83f, 0.55f, 0.13f, 1f));
            CreateQuad("Panel", _worldMenu.transform, Vector3.zero, new Vector3(1.62f, 0.92f, 1f), _panelMaterial);
            CreateText("Title", _worldMenu.transform, title, new Vector3(0f, 0.22f, -0.012f), 64, 0.022f,
                new Color(0.94f, 0.84f, 0.55f));
            CreateText("Subtitle", _worldMenu.transform, subtitle, new Vector3(0f, 0.08f, -0.014f), 44, 0.012f,
                new Color(0.79f, 0.9f, 0.82f));
            CreateQuad("Start Button", _worldMenu.transform, new Vector3(0f, -0.16f, -0.016f),
                new Vector3(0.64f, 0.18f, 1f), _buttonMaterial);
            CreateText("Button Label", _worldMenu.transform, button, new Vector3(0f, -0.16f, -0.026f), 54, 0.018f,
                new Color(0.055f, 0.045f, 0.025f));
            CreateText("Hint", _worldMenu.transform, hint, new Vector3(0f, -0.34f, -0.014f), 38, 0.01f,
                new Color(0.73f, 0.82f, 0.76f));
        }

        private static void CreateQuad(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = position;
            quad.transform.localScale = scale;
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(quad.GetComponent<Collider>());
        }

        private static void CreateText(string name, Transform parent, string value, Vector3 position,
            int fontSize, float characterSize, Color color)
        {
            GameObject textObject = new(name);
            textObject.transform.SetParent(parent, false);
            textObject.transform.localPosition = position;
            TextMesh text = textObject.AddComponent<TextMesh>();
            text.text = value;
            text.fontSize = fontSize;
            text.characterSize = characterSize;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = color;
            text.fontStyle = FontStyle.Bold;
        }

        private static Material CreateUnlitMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            Material material = new(shader)
            {
                name = name,
                color = color,
                renderQueue = (int)RenderQueue.Geometry
            };
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private void DestroyWorldMenu()
        {
            if (_worldMenu != null)
            {
                Destroy(_worldMenu);
                _worldMenu = null;
            }
        }

        private void EnsureGuiStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _eyebrowStyle = CenteredStyle(14, FontStyle.Bold, new Color(0.78f, 0.64f, 0.32f));
            _titleStyle = CenteredStyle(38, FontStyle.Bold, new Color(0.96f, 0.9f, 0.69f));
            _bodyStyle = CenteredStyle(17, FontStyle.Normal, new Color(0.81f, 0.89f, 0.84f));
            _bodyStyle.wordWrap = true;
            _buttonStyle = CenteredStyle(23, FontStyle.Bold, new Color(0.08f, 0.06f, 0.025f));
            _buttonStyle.normal.background = Texture2D.whiteTexture;
            _buttonStyle.hover.background = Texture2D.whiteTexture;
            _buttonStyle.active.background = Texture2D.whiteTexture;
        }

        private static GUIStyle CenteredStyle(int fontSize, FontStyle fontStyle, Color color)
        {
            GUIStyle style = new(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = fontSize,
                fontStyle = fontStyle
            };
            style.normal.textColor = color;
            return style;
        }

        private static void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
