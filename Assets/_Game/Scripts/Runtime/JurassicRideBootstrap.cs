using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR;

namespace ValeMesozoico
{
    internal sealed class JurassicRideBootstrap : MonoBehaviour
    {
        private static bool _created;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _created = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureBootstrap()
        {
            if (_created || FindFirstObjectByType<JurassicRideBootstrap>() != null)
            {
                return;
            }

            _created = true;
            new GameObject("Vale Mesozoico Runtime").AddComponent<JurassicRideBootstrap>();
        }

        private void Awake()
        {
            DontDestroyOnLoad(gameObject);
            ConfigureRuntime();

            foreach (Camera existingCamera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                existingCamera.gameObject.SetActive(false);
            }

            Transform worldRoot = new GameObject("Jurassic Ride").transform;
            worldRoot.SetParent(transform, false);
            RideSpline spline = new(CreateTrackPoints());
            WorldMaterials materials = ProceduralWorld.Build(worldRoot, spline);
            TrackMeshFactory.CreateTrack(worldRoot, spline, materials.Rail, materials.Sleeper, materials.Support);

            Transform cart = BuildCart(worldRoot, materials);
            Camera camera = BuildCameraRig(cart);

            RideController controller = cart.gameObject.AddComponent<RideController>();
            ComfortFade fade = camera.gameObject.AddComponent<ComfortFade>();
            controller.Initialize(spline, fade);

            RideDebugTimeline debugTimeline = cart.gameObject.AddComponent<RideDebugTimeline>();
            debugTimeline.Initialize(controller);
        }

        private static void ConfigureRuntime()
        {
            Application.runInBackground = true;
            Application.targetFrameRate = 72;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            QualitySettings.vSyncCount = 0;
            QualitySettings.antiAliasing = 4;
            QualitySettings.shadowDistance = 45f;
            QualitySettings.shadowCascades = 1;
            QualitySettings.lodBias = 0.8f;
            Time.fixedDeltaTime = 1f / 72f;

            if (XRSettings.enabled)
            {
                XRSettings.eyeTextureResolutionScale = 1f;
                List<XRDisplaySubsystem> displays = new();
                SubsystemManager.GetSubsystems(displays);
                foreach (XRDisplaySubsystem display in displays)
                {
                    if (display.running)
                    {
                        display.foveatedRenderingLevel = 0.5f;
                    }
                }
            }
        }

        private static Vector3[] CreateTrackPoints()
        {
            return new[]
            {
                new Vector3(0f, 4.2f, -56f),
                new Vector3(34f, 5f, -52f),
                new Vector3(64f, 8f, -34f),
                new Vector3(80f, 14f, -5f),
                new Vector3(76f, 24f, 27f),
                new Vector3(58f, 35f, 48f),
                new Vector3(44f, 18f, 56f),
                new Vector3(38f, 6.5f, 64f),
                new Vector3(15f, 5.5f, 76f),
                new Vector3(-22f, 7f, 72f),
                new Vector3(-54f, 10f, 50f),
                new Vector3(-72f, 7f, 16f),
                new Vector3(-68f, 5f, -20f),
                new Vector3(-49f, 4.5f, -45f),
                new Vector3(-26f, 4.2f, -57f)
            };
        }

        private static Transform BuildCart(Transform parent, WorldMaterials materials)
        {
            GameObject cart = new("Ride Cart");
            cart.transform.SetParent(parent, false);

            if (!OptimizedModelWorld.AttachCoasterTrain(cart.transform, materials.Cart, materials.CartDark))
            {
                CreatePrimitive("Cart Base", cart.transform, PrimitiveType.Cube, new Vector3(0f, 0.35f, 0f), new Vector3(1.7f, 0.28f, 2.35f), materials.Cart);
                CreatePrimitive("Front Console", cart.transform, PrimitiveType.Cube, new Vector3(0f, 0.86f, 0.78f), new Vector3(1.45f, 0.55f, 0.22f), materials.CartDark);
                CreatePrimitive("Left Side", cart.transform, PrimitiveType.Cube, new Vector3(-0.78f, 0.66f, 0f), new Vector3(0.12f, 0.48f, 2.1f), materials.Cart);
                CreatePrimitive("Right Side", cart.transform, PrimitiveType.Cube, new Vector3(0.78f, 0.66f, 0f), new Vector3(0.12f, 0.48f, 2.1f), materials.Cart);
                CreatePrimitive("Seat Back", cart.transform, PrimitiveType.Cube, new Vector3(0f, 0.84f, -0.69f), new Vector3(1.25f, 0.82f, 0.16f), materials.CartDark);
            }

            // Approved in Play Mode: persist the tested cart/camera proportion.
            cart.transform.localScale = Vector3.one * 1.6f;

            AudioSource wheels = cart.AddComponent<AudioSource>();
            AudioClip recordedWheelRoll = Resources.Load<AudioClip>("Audio/Enhanced Wheel Rail Roll");
            wheels.clip = recordedWheelRoll != null
                ? recordedWheelRoll
                : EnhancedProceduralAudio.CreateTrackLoop();
            wheels.loop = true;
            wheels.playOnAwake = true;
            wheels.spatialBlend = 0f;
            wheels.dopplerLevel = 0f;
            wheels.volume = 0.04f;
            wheels.Play();

            AudioSource liftChain = cart.AddComponent<AudioSource>();
            AudioClip liftChainStart = Resources.Load<AudioClip>("Audio/LiftChainStart");
            liftChain.clip = liftChainStart != null
                ? liftChainStart
                : EnhancedProceduralAudio.CreateLiftChainLoop();
            liftChain.loop = liftChainStart == null;
            liftChain.playOnAwake = false;
            liftChain.spatialBlend = 0f;
            liftChain.dopplerLevel = 0f;
            liftChain.volume = 0f;

            AudioClip liftChainLoop = Resources.Load<AudioClip>("Audio/LiftChainLoop");
            if (liftChainLoop != null)
            {
                AudioSource liftLoop = cart.AddComponent<AudioSource>();
                liftLoop.clip = liftChainLoop;
                liftLoop.loop = true;
                liftLoop.playOnAwake = false;
                liftLoop.spatialBlend = 0f;
                liftLoop.dopplerLevel = 0f;
                liftLoop.volume = 0f;
            }

            AudioClip liftChainEnd = Resources.Load<AudioClip>("Audio/LiftChainEnd");
            if (liftChainEnd != null)
            {
                AudioSource liftEnd = cart.AddComponent<AudioSource>();
                liftEnd.clip = liftChainEnd;
                liftEnd.loop = false;
                liftEnd.playOnAwake = false;
                liftEnd.spatialBlend = 0f;
                liftEnd.dopplerLevel = 0f;
                liftEnd.volume = 0f;
            }
            return cart.transform;
        }

        private static Camera BuildCameraRig(Transform cart)
        {
            GameObject seat = new("Seat Anchor");
            seat.transform.SetParent(cart, false);
            seat.transform.localPosition = new Vector3(0f, 0.44f, -0.42f);

            GameObject cameraObject = new("Main Camera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(seat.transform, false);
            // Seated eye point: high enough for comfort, low enough to keep the cart rim visible.
            cameraObject.transform.localPosition = new Vector3(0f, 0.52f, -0.12f);

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 310f;
            camera.fieldOfView = 75f;
            camera.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.39f, 0.55f, 0.59f);
            camera.allowHDR = false;
            camera.allowMSAA = true;
            cameraObject.AddComponent<AudioListener>();
            cameraObject.AddComponent<XRHeadTracker>();
            return camera;
        }

        private static void BuildStationSign(Transform parent, RideSpline spline)
        {
            RidePose station = spline.PoseAtDistance(2.5f);
            GameObject sign = new("Station Sign");
            sign.transform.SetParent(parent, false);
            Vector3 right = station.Rotation * Vector3.right;
            sign.transform.SetPositionAndRotation(station.Position - right * 7f + Vector3.up * 2.5f, station.Rotation * Quaternion.Euler(0f, -90f, 0f));
            TextMesh text = sign.AddComponent<TextMesh>();
            text.text = "VALE MESOZOICO VR";
            text.fontSize = 64;
            text.characterSize = 0.075f;
            text.anchor = TextAnchor.MiddleCenter;
            text.alignment = TextAlignment.Center;
            text.color = new Color(0.9f, 0.78f, 0.45f);
        }

        private static GameObject CreatePrimitive(string name, Transform parent, PrimitiveType type, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = localPosition;
            primitive.transform.localScale = localScale;
            primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
            Destroy(primitive.GetComponent<Collider>());
            return primitive;
        }

        private static AudioClip CreateTrackClip()
        {
            const int sampleRate = 22050;
            const int seconds = 2;
            float[] samples = new float[sampleRate * seconds];
            System.Random random = new(4096);
            for (int i = 0; i < samples.Length; i++)
            {
                int beat = i % (sampleRate / 4);
                float pulse = beat < 140 ? Mathf.Exp(-beat / 32f) : 0f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                samples[i] = pulse * noise * 0.42f;
            }

            AudioClip clip = AudioClip.Create("Procedural Track Clack", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    internal sealed class RideController : MonoBehaviour
    {
        private const float LiftAudioCrossfadeSeconds = 0.06f;

        private enum RideState
        {
            Boarding,
            Riding,
            Arrival,
            Resetting
        }

        private RideSpline _spline;
        private ComfortFade _fade;
        private RideState _state;
        private float _distance;
        private float _speed;
        private float _acceleration;
        private float _bankAngle;
        private float _stateTime;
        private AudioSource _trackAudio;
        private AudioSource _liftChainAudio;
        private AudioSource _liftChainLoopAudio;
        private AudioSource _liftChainEndAudio;
        private AudioClip _liftChainStartClip;
        private AudioClip _liftChainLoopClip;
        private bool _liftChainActive;
        private float _liftChainGain;
        private double _liftLoopStartDspTime = -1d;
        private bool _developerPaused;
        private bool _developerScrubbing;
        private float _developerPlaybackRate = 1f;
        private float _developerScrubStartProgress;
        private int _developerSeekVersion;
        private bool _developerLastSeekWasBackward;
        private float _nextSleeperHapticDistance;
        private float _nextLiftHapticTime;
        private float _nextBrakeHapticTime;
        private float _nextAllowedHapticTime;
        private bool _crestHapticSent;
        private InputDevice _leftController;
        private InputDevice _rightController;

        public void Initialize(RideSpline spline, ComfortFade fade)
        {
            _spline = spline;
            _fade = fade;
            _state = RideState.Boarding;
            _stateTime = 0f;
            _distance = 0f;
            _speed = 0f;
            _acceleration = 0f;
            foreach (AudioSource source in GetComponents<AudioSource>())
            {
                string clipName = source.clip != null ? source.clip.name : string.Empty;
                if (clipName == "LiftChainEnd")
                {
                    _liftChainEndAudio = source;
                }
                else if (clipName == "LiftChainLoop")
                {
                    _liftChainLoopAudio = source;
                }
                else if (clipName == "LiftChainStart" || clipName.Contains("Lift Chain"))
                {
                    _liftChainAudio = source;
                }
                else if (_trackAudio == null)
                {
                    _trackAudio = source;
                }
            }
            _liftChainStartClip = _liftChainAudio != null ? _liftChainAudio.clip : null;
            _liftChainLoopClip = _liftChainLoopAudio != null ? _liftChainLoopAudio.clip : null;
            _bankAngle = _spline.PoseAtDistance(0f).BankDegrees;
            ResetRideFeedback();
            ApplyPose(0f, true, 0f);
            _fade.SetImmediate(1f);
        }

        private void Update()
        {
            if (_spline == null)
            {
                return;
            }

            if (_developerScrubbing)
            {
                ApplyPose(_distance, true, 0f);
                return;
            }

            _stateTime += Time.deltaTime;
            switch (_state)
            {
                case RideState.Boarding:
                    ApplyPose(0f, true, 0f);
                    UpdateRideAudio();
                    _fade.SetTarget(_stateTime < 0.5f ? 1f : 0f);
                    if (_stateTime >= 4f)
                    {
                        ChangeState(RideState.Riding);
                    }
                    break;

                case RideState.Riding:
                    float frameTime = Mathf.Min(Time.deltaTime, 0.1f);
                    float simulationTime = frameTime;
                    while (simulationTime > 0f)
                    {
                        float step = Mathf.Min(simulationTime, 1f / 72f);
                        StepRide(step);
                        simulationTime -= step;
                    }

                    ApplyPose(Mathf.Min(_distance, _spline.Length - 0.02f), false, frameTime);
                    UpdateRideAudio();
                    UpdateHaptics();

                    if (_distance >= _spline.Length - 0.08f && _speed <= 0.15f)
                    {
                        _distance = _spline.Length - 0.02f;
                        _speed = 0f;
                        _acceleration = 0f;
                        ApplyPose(_distance, false, frameTime);
                        ChangeState(RideState.Arrival);
                    }
                    break;

                case RideState.Arrival:
                    _speed = 0f;
                    _acceleration = 0f;
                    ApplyPose(_spline.Length - 0.02f, false, Time.deltaTime);
                    UpdateRideAudio();
                    if (_stateTime > 3.8f)
                    {
                        _fade.SetTarget(1f);
                    }
                    if (_stateTime > 4.5f)
                    {
                        ChangeState(RideState.Resetting);
                    }
                    break;

                case RideState.Resetting:
                    _distance = 0f;
                    _speed = 0f;
                    _acceleration = 0f;
                    ApplyPose(0f, true, 0f);
                    if (_stateTime > 0.45f)
                    {
                        ChangeState(RideState.Boarding);
                    }
                    break;
            }
        }

        private void ChangeState(RideState state)
        {
            _state = state;
            _stateTime = 0f;
            if (state == RideState.Riding)
            {
                ResetRideFeedback();
            }
            else if (state == RideState.Resetting)
            {
                StopControllerHaptics();
            }
        }

        private void StepRide(float deltaTime)
        {
            RidePose pose = _spline.PoseAtDistance(_distance);
            float progress = Mathf.Clamp01(_distance / _spline.Length);
            float remainingDistance = Mathf.Max(0f, _spline.Length - _distance);
            float targetAcceleration = RideMotionProfile.TargetAcceleration(progress, pose.Tangent.y, remainingDistance, _speed);
            _acceleration = Mathf.MoveTowards(_acceleration, targetAcceleration, RideMotionProfile.MaxJerk * deltaTime);
            _acceleration = Mathf.Clamp(_acceleration, -RideMotionProfile.MaxBraking, RideMotionProfile.MaxAcceleration);
            _speed = Mathf.Clamp(_speed + _acceleration * deltaTime, 0f, RideMotionProfile.MaxSpeed);
            _distance = Mathf.Min(_spline.Length - 0.02f, _distance + _speed * deltaTime);
        }

        private void ApplyPose(float distance, bool immediateBank, float deltaTime)
        {
            RidePose pose = _spline.PoseAtDistance(distance);
            _bankAngle = immediateBank
                ? pose.BankDegrees
                : Mathf.MoveTowardsAngle(_bankAngle, pose.BankDegrees, 15f * deltaTime);
            Quaternion baseRotation = Quaternion.LookRotation(pose.Tangent, Vector3.up);
            Quaternion rideRotation = baseRotation * Quaternion.AngleAxis(_bankAngle, Vector3.forward);
            transform.SetPositionAndRotation(pose.Position + rideRotation * Vector3.up * 0.40f, rideRotation);
        }

        private void UpdateRideAudio()
        {
            float speed01 = Mathf.InverseLerp(0f, RideMotionProfile.MaxSpeed, _speed);
            RidePose pose = _spline.PoseAtDistance(_distance);
            float progress = Mathf.Clamp01(_distance / _spline.Length);
            bool liftChainActive = _state == RideState.Riding
                && RideMotionProfile.IsLiftChainActive(progress, pose.Tangent.y);
            if (_trackAudio != null)
            {
                _trackAudio.pitch = Mathf.Clamp(
                    Mathf.Lerp(0.82f, 1.18f, speed01) * _developerPlaybackRate,
                    0.1f,
                    3f);
                float rollingVolume = _state == RideState.Riding
                    ? Mathf.Lerp(0.012f, 0.24f, Mathf.Pow(speed01, 0.78f))
                    : 0f;
                _trackAudio.volume = Mathf.MoveTowards(
                    _trackAudio.volume,
                    rollingVolume,
                    Time.deltaTime * 0.75f);
            }

            if (liftChainActive && !_liftChainActive)
            {
                StartLiftChain();
            }
            else if (!liftChainActive && _liftChainActive)
            {
                FinishLiftChain();
            }
            _liftChainActive = liftChainActive;
            if (_liftChainAudio != null)
            {
                float targetVolume = _liftChainActive ? 0.30f : 0f;
                float fadeSpeed = _liftChainActive ? 2.2f : 4.8f;
                _liftChainGain = Mathf.MoveTowards(
                    _liftChainGain,
                    targetVolume,
                    Time.deltaTime * fadeSpeed);
                _liftChainAudio.pitch = _developerPlaybackRate;
                if (_liftChainLoopAudio != null)
                {
                    float blend = _liftLoopStartDspTime > 0d
                        ? Mathf.Clamp01((float)((AudioSettings.dspTime - _liftLoopStartDspTime) / LiftAudioCrossfadeSeconds))
                        : 0f;
                    float angle = blend * Mathf.PI * 0.5f;
                    _liftChainAudio.volume = _liftChainGain * Mathf.Cos(angle);
                    _liftChainLoopAudio.volume = _liftChainGain * Mathf.Sin(angle);
                    _liftChainLoopAudio.pitch = _developerPlaybackRate;
                }
                else
                {
                    _liftChainAudio.volume = _liftChainGain;
                }
            }
        }

        private void StartLiftChain()
        {
            if (_liftChainAudio == null)
            {
                return;
            }

            _liftChainEndAudio?.Stop();
            _liftChainAudio.Stop();
            _liftChainLoopAudio?.Stop();
            _liftChainAudio.clip = _liftChainStartClip;
            _liftChainAudio.loop = _liftChainLoopAudio == null;
            _liftChainGain = 0f;
            _liftChainAudio.volume = 0f;
            _liftChainAudio.pitch = _developerPlaybackRate;
            if (_liftChainLoopAudio == null || _liftChainStartClip == null)
            {
                _liftLoopStartDspTime = -1d;
                _liftChainAudio.Play();
                return;
            }

            _liftChainLoopAudio.loop = true;
            _liftChainLoopAudio.volume = 0f;
            _liftChainLoopAudio.pitch = _developerPlaybackRate;
            double startTime = AudioSettings.dspTime + 0.035d;
            float adjustedStartLength = _liftChainStartClip.length / Mathf.Max(0.1f, _developerPlaybackRate);
            _liftLoopStartDspTime = startTime + Mathf.Max(
                LiftAudioCrossfadeSeconds,
                adjustedStartLength - LiftAudioCrossfadeSeconds);
            _liftChainAudio.PlayScheduled(startTime);
            _liftChainLoopAudio.PlayScheduled(_liftLoopStartDspTime);
        }

        private void FinishLiftChain()
        {
            _liftChainAudio?.Stop();
            _liftChainLoopAudio?.Stop();
            _liftLoopStartDspTime = -1d;
            if (_liftChainEndAudio == null)
            {
                return;
            }

            _liftChainEndAudio.Stop();
            _liftChainEndAudio.pitch = _developerPlaybackRate;
            _liftChainEndAudio.volume = 0.26f;
            _liftChainEndAudio.Play();
        }

        internal bool LiftChainActive => _liftChainActive;
        internal float RideProgress => _spline != null ? Mathf.Clamp01(_distance / _spline.Length) : 0f;
        internal float RideSpeed => _speed;
        internal bool DeveloperScrubbing => _developerScrubbing;
        internal int DeveloperSeekVersion => _developerSeekVersion;
        internal bool DeveloperLastSeekWasBackward => _developerLastSeekWasBackward;
        internal float WheelRailVolume => _trackAudio != null ? _trackAudio.volume : 0f;
        internal float LiftChainVolume => _liftChainGain;
        internal bool LiftChainEndPlaying => _liftChainEndAudio != null && _liftChainEndAudio.isPlaying;

        internal void DeveloperSeekToProgress(float normalizedProgress)
        {
            BeginDeveloperScrub();
            DeveloperPreviewSeekToProgress(normalizedProgress);
            CompleteDeveloperScrub(normalizedProgress);
        }

        internal void BeginDeveloperScrub()
        {
            if (_spline == null || _developerScrubbing)
            {
                return;
            }

            _developerScrubStartProgress = RideProgress;
            _developerScrubbing = true;
            StopControllerHaptics();
            _trackAudio?.Pause();
            _liftChainAudio?.Pause();
            _liftChainLoopAudio?.Pause();
            _liftChainEndAudio?.Pause();
        }

        internal void DeveloperPreviewSeekToProgress(float normalizedProgress)
        {
            if (_spline == null)
            {
                return;
            }

            float progress = Mathf.Clamp01(normalizedProgress);
            _distance = Mathf.Min(_spline.Length - 0.02f, _spline.Length * progress);
            RidePose pose = _spline.PoseAtDistance(_distance);
            _speed = DeveloperSeekSpeed(progress, pose.Tangent.y);
            _acceleration = 0f;
            _bankAngle = pose.BankDegrees;
            _state = RideState.Riding;
            _stateTime = 0f;
            ApplyPose(_distance, true, 0f);
            _fade.SetImmediate(0f);
        }

        internal void CompleteDeveloperScrub(float normalizedProgress)
        {
            if (_spline == null)
            {
                return;
            }

            DeveloperPreviewSeekToProgress(normalizedProgress);
            float progress = RideProgress;
            _developerLastSeekWasBackward = progress < _developerScrubStartProgress - 0.0005f;
            _developerSeekVersion++;
            _developerScrubbing = false;
            ResetRideFeedback();
            if (_trackAudio != null && !_trackAudio.isPlaying)
            {
                _trackAudio.Play();
            }
            UpdateRideAudio();
            Debug.Log(
                $"[Developer Timeline] SEEK | from={_developerScrubStartProgress:F3}, "
                + $"to={progress:F3}, backward={_developerLastSeekWasBackward}");
        }

        internal void CancelDeveloperScrub()
        {
            if (!_developerScrubbing)
            {
                return;
            }

            CompleteDeveloperScrub(RideProgress);
        }

        internal void SetDeveloperPaused(bool paused)
        {
            _developerPaused = paused;
            if (paused)
            {
                StopControllerHaptics();
            }
        }

        internal void SetDeveloperPlaybackRate(float playbackRate)
        {
            _developerPlaybackRate = Mathf.Clamp(playbackRate, 0.5f, 3f);
            UpdateRideAudio();
        }

        private static float DeveloperSeekSpeed(float progress, float tangentY)
        {
            if (progress >= 0.985f)
            {
                return 0.12f;
            }

            if (RideMotionProfile.IsLiftChainActive(progress, tangentY))
            {
                return 3.4f;
            }

            if (progress >= 0.94f)
            {
                return 3.2f;
            }

            return progress < RideMotionProfile.LiftStart ? 2.6f : 6.2f;
        }

        internal void TriggerEncounterHaptic(float amplitude, float duration)
        {
            SendHapticPulse(amplitude, duration, Time.unscaledTime);
        }

        private void ResetRideFeedback()
        {
            _liftChainActive = false;
            _liftChainGain = 0f;
            _liftLoopStartDspTime = -1d;
            if (_liftChainAudio != null)
            {
                _liftChainAudio.Stop();
                _liftChainAudio.clip = _liftChainStartClip;
                _liftChainAudio.loop = _liftChainLoopClip == null;
                _liftChainAudio.volume = 0f;
            }
            if (_liftChainLoopAudio != null)
            {
                _liftChainLoopAudio.Stop();
                _liftChainLoopAudio.volume = 0f;
            }
            _liftChainEndAudio?.Stop();
            _nextSleeperHapticDistance = 1.4f;
            _nextLiftHapticTime = Time.unscaledTime;
            _nextBrakeHapticTime = Time.unscaledTime;
            _nextAllowedHapticTime = Time.unscaledTime;
            _crestHapticSent = false;
        }

        private void UpdateHaptics()
        {
            if (_developerPaused || _developerScrubbing)
            {
                return;
            }

            float progress = Mathf.Clamp01(_distance / _spline.Length);
            float now = Time.unscaledTime;
            RidePose pose = _spline.PoseAtDistance(_distance);

            if (!_crestHapticSent && progress >= RideMotionProfile.CrestStart)
            {
                _crestHapticSent = true;
                SendHapticPulse(0.24f, 0.06f, now);
                return;
            }

            if (progress >= RideMotionProfile.BrakeHapticStart && now >= _nextBrakeHapticTime)
            {
                float brakeProgress = Mathf.InverseLerp(RideMotionProfile.BrakeHapticStart, 1f, progress);
                _nextBrakeHapticTime = now + Mathf.Lerp(0.13f, 0.085f, brakeProgress);
                SendHapticPulse(Mathf.Lerp(0.12f, 0.045f, brakeProgress), 0.018f, now);
                return;
            }

            if (RideMotionProfile.IsLiftChainActive(progress, pose.Tangent.y) && now >= _nextLiftHapticTime)
            {
                _nextLiftHapticTime = now + 0.2f;
                SendHapticPulse(0.08f, 0.02f, now);
                return;
            }

            if (_distance >= _nextSleeperHapticDistance)
            {
                _nextSleeperHapticDistance = (Mathf.Floor(_distance / 1.4f) + 1f) * 1.4f;
                float amplitude = Mathf.Lerp(0.04f, 0.08f, Mathf.InverseLerp(2f, RideMotionProfile.MaxSpeed, _speed));
                SendHapticPulse(amplitude, 0.018f, now);
            }
        }

        private void SendHapticPulse(float amplitude, float duration, float now)
        {
            if (_developerPaused || _developerScrubbing)
            {
                return;
            }

            if (now < _nextAllowedHapticTime)
            {
                return;
            }

            _nextAllowedHapticTime = now + 1f / 12f;
            amplitude = Mathf.Clamp(amplitude, 0f, 0.30f);
            SendToController(ref _leftController, XRNode.LeftHand, amplitude, duration);
            SendToController(ref _rightController, XRNode.RightHand, amplitude, duration);
        }

        private static void SendToController(ref InputDevice device, XRNode node, float amplitude, float duration)
        {
            if (!device.isValid)
            {
                device = InputDevices.GetDeviceAtXRNode(node);
            }

            if (device.isValid
                && device.TryGetHapticCapabilities(out HapticCapabilities capabilities)
                && capabilities.supportsImpulse)
            {
                device.SendHapticImpulse(0u, amplitude, duration);
            }
        }

        private void OnDisable()
        {
            StopControllerHaptics();
        }

        private void StopControllerHaptics()
        {
            if (_leftController.isValid)
            {
                _leftController.StopHaptics();
            }
            if (_rightController.isValid)
            {
                _rightController.StopHaptics();
            }
        }
    }

    internal static class RideMotionProfile
    {
        internal const float MaxSpeed = 10f;
        internal const float MaxAcceleration = 1.2f;
        internal const float MaxBraking = 1.8f;
        internal const float MaxJerk = 3f;
        internal const float LiftStart = 0.03f;
        internal const float CrestStart = 0.355f;
        internal const float BrakeHapticStart = 0.93f;

        internal static bool IsLiftChainActive(float progress, float tangentY)
        {
            return progress >= LiftStart
                && progress < CrestStart
                && tangentY >= -0.025f;
        }

        internal static float TargetAcceleration(float progress, float tangentY, float remainingDistance, float speed)
        {
            float gravity = -9.81f * tangentY * 0.58f;
            float resistance = 0.08f + speed * speed * 0.0045f;
            float target;

            if (progress < LiftStart)
            {
                float launch = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0f, LiftStart, progress));
                float desiredSpeed = Mathf.Lerp(0.7f, 3.2f, launch);
                target = (desiredSpeed - speed) * 0.9f + gravity * 0.12f;
            }
            else if (IsLiftChainActive(progress, tangentY))
            {
                target = (3.4f - speed) * 1.05f + gravity * 0.12f;
            }
            else if (progress < 0.72f)
            {
                target = gravity - resistance;
                if (speed < 3.4f)
                {
                    target = Mathf.Max(target, 0.35f);
                }
                if (speed > 9.7f)
                {
                    target = Mathf.Min(target, -(speed - 9.7f) * 4f);
                }
            }
            else if (progress < 0.94f)
            {
                target = (7.2f - speed) * 0.75f + gravity * 0.1f;
            }
            else
            {
                float zoneSpeed = progress < 0.985f
                    ? Mathf.Lerp(7.2f, 2.6f, Mathf.InverseLerp(0.94f, 0.985f, progress))
                    : Mathf.Lerp(2.6f, 0.12f, Mathf.InverseLerp(0.985f, 1f, progress));
                float stoppingSpeed = Mathf.Sqrt(2f * 1.25f * Mathf.Max(0f, remainingDistance - 0.10f));
                float desiredSpeed = Mathf.Min(zoneSpeed, stoppingSpeed);
                target = (desiredSpeed - speed) * 1.35f + gravity * 0.08f;
            }

            return Mathf.Clamp(target, -MaxBraking, MaxAcceleration);
        }
    }

    internal sealed class XRHeadTracker : MonoBehaviour
    {
        private readonly List<XRInputSubsystem> _inputSubsystems = new();
        private Vector3 _seatEyePosition;
        private Vector3 _originPosition;
        private Quaternion _yawCorrection = Quaternion.identity;
        private bool _calibrated;

        private void Awake()
        {
            _seatEyePosition = transform.localPosition;
        }

        private void OnEnable()
        {
            _calibrated = false;
            _inputSubsystems.Clear();
            SubsystemManager.GetSubsystems(_inputSubsystems);
            foreach (XRInputSubsystem subsystem in _inputSubsystems)
            {
                subsystem.TrySetTrackingOriginMode(TrackingOriginModeFlags.Device);
            }
            Application.onBeforeRender += ApplyHeadPose;
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= ApplyHeadPose;
        }

        private void LateUpdate()
        {
            ApplyHeadPose();
        }

        private void ApplyHeadPose()
        {
            InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid)
            {
                return;
            }

            bool hasPosition = head.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 position)
                || head.TryGetFeatureValue(CommonUsages.devicePosition, out position);
            bool hasRotation = head.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion rotation)
                || head.TryGetFeatureValue(CommonUsages.deviceRotation, out rotation);
            if (!hasPosition || !hasRotation)
            {
                return;
            }

            if (!_calibrated)
            {
                _originPosition = position;
                float yaw = rotation.eulerAngles.y;
                _yawCorrection = Quaternion.Euler(0f, -yaw, 0f);
                _calibrated = true;
            }

            transform.localPosition = _seatEyePosition + _yawCorrection * (position - _originPosition);
            transform.localRotation = _yawCorrection * rotation;
        }
    }

    internal sealed class ComfortFade : MonoBehaviour
    {
        private Material _material;
        private MeshRenderer _renderer;
        private Transform _veil;
        private float _alpha = 1f;
        private float _target = 1f;

        private void Awake()
        {
            if (Application.isEditor && !XRSettings.isDeviceActive)
            {
                _alpha = 0f;
                _target = 0f;
                enabled = false;
                return;
            }

            GameObject veil = GameObject.CreatePrimitive(PrimitiveType.Quad);
            veil.name = "Comfort Fade";
            veil.transform.SetParent(transform, false);
            veil.transform.localPosition = new Vector3(0f, 0f, 0.31f);
            veil.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            _veil = veil.transform;
            UpdateVeilScale();
            Destroy(veil.GetComponent<Collider>());

            Material template = Resources.Load<Material>("Generated/QuestFade");
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (template == null && shader == null)
            {
                throw new InvalidOperationException("Nenhum shader de fade compatível foi incluído no player.");
            }
            _material = template != null ? new Material(template) : new Material(shader);
            _material.name = "Comfort Fade Material";
            _material.renderQueue = (int)RenderQueue.Overlay;
            if (_material.HasProperty("_Surface")) _material.SetFloat("_Surface", 1f);
            if (_material.HasProperty("_SrcBlend")) _material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (_material.HasProperty("_DstBlend")) _material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (_material.HasProperty("_ZWrite")) _material.SetFloat("_ZWrite", 0f);
            _material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            _renderer = veil.GetComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            SetColor();
        }

        public void SetTarget(float alpha)
        {
            _target = Mathf.Clamp01(alpha);
        }

        public void SetImmediate(float alpha)
        {
            _alpha = Mathf.Clamp01(alpha);
            _target = _alpha;
            SetColor();
        }

        private void Update()
        {
            _alpha = Mathf.MoveTowards(_alpha, _target, Time.unscaledDeltaTime / 0.4f);
            UpdateVeilScale();
            SetColor();
        }

        private void UpdateVeilScale()
        {
            if (_veil == null)
            {
                return;
            }

            Camera camera = GetComponent<Camera>();
            float distance = Mathf.Abs(_veil.localPosition.z);
            float height = 2f * distance * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.15f;
            _veil.localScale = new Vector3(height * camera.aspect, height, 1f);
        }

        private void SetColor()
        {
            if (_material == null)
            {
                return;
            }
            Color color = new(0.015f, 0.018f, 0.015f, _alpha);
            if (_material.HasProperty("_BaseColor")) _material.SetColor("_BaseColor", color);
            if (_material.HasProperty("_Color")) _material.SetColor("_Color", color);
            if (_renderer != null) _renderer.enabled = _alpha > 0.001f;
        }
    }
}
