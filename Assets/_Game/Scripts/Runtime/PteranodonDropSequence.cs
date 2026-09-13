using UnityEngine;

namespace ValeMesozoico
{
    // Advanced only by the ride's clock: the complete capture can pause and replay.
    internal sealed class PteranodonDropSequence : MonoBehaviour
    {
        internal enum Phase { Waiting, Approaching, Swooping, Grabbing, Lifting, Carrying, Returning,
            Releasing, Falling, Impacting, Departing, Complete }
        private const float ApproachSeconds = 1.8f;
        private const float SwoopSeconds = 3.8f;
        private const float ThrowHeight = 4f;
        private const float ThrowSeconds = .22f;
        private const float ThrowSpeed = 7f;
        private const float Gravity = 9.81f;
        private const float SuspensionSeconds = .42f;
        private RideController _controller;
        private Transform _seatAnchor;
        private Vector3 _seatPosition;
        private Quaternion _seatRotation;
        private GameObject _actor;
        private PteranodonGrabRig _rig;
        private DinosaurAudioEmitter _call;
        private AudioSource _impact;
        private Vector3 _crest, _forward, _right, _liftEnd, _departure;
        private Quaternion _crestRotation, _phaseRotation, _birdRotation;
        private Quaternion _landingRotation, _departureRotation;
        private FlightPath _pickupPath, _valleyPath, _returnPath;
        private float _elapsed, _flightClock;
        private float _fallSeconds, _departureClock;
        private bool _panelsMeasured;
        private readonly Vector3[] _rim = new Vector3[96];

        internal Phase CurrentPhase { get; private set; }
        internal int CompletedLaps { get; private set; }
        internal bool Screeched { get; private set; }
        internal bool Released { get; private set; }
        internal bool IsHolding => CurrentPhase >= Phase.Approaching && CurrentPhase <= Phase.Falling;
        internal bool ControlsCartPose => CurrentPhase >= Phase.Grabbing && CurrentPhase <= Phase.Falling;
        internal bool GripLocked => CurrentPhase >= Phase.Lifting && CurrentPhase <= Phase.Returning;
        internal bool Ready => _actor != null && _rig != null && _rig.Ready;
        internal GameObject Actor => _actor;
        internal float PhaseTime => _elapsed;
        internal float GripGap => _rig != null ? _rig.ContactGap : float.PositiveInfinity;
        internal bool PanelsMeasured => _panelsMeasured;
        internal Vector3[] GripPoints => _rig.ContactPoints;
        internal Vector3 LakeCenter { get; private set; }
        internal float AirSpeed { get; private set; }
        internal float ReturnPositionGap { get; private set; }
        internal float ReturnRotationGap { get; private set; }
        internal float ReleaseHeight { get; private set; }
        internal float ImpactSpeed { get; private set; }
        internal float LandingDistance { get; private set; }
        internal Vector3 LandingPosition { get; private set; }
        internal float LandingSlope { get; private set; }
        internal int ImpactCount { get; private set; }
        internal float MaxPickupPositionStep { get; private set; }
        internal float MaxPickupRotationStep { get; private set; }
        internal float CameraShakeOffset => _seatAnchor != null
            ? Vector3.Distance(_seatAnchor.localPosition, _seatPosition) : 0f;
        internal Vector3 CrestPosition => _crest;

        internal static PteranodonDropSequence Create(Transform parent, RideController controller, RideSpline spline)
        {
            GameObject root = new("Cave Drop Encounter");
            root.transform.SetParent(parent, false);
            var sequence = root.AddComponent<PteranodonDropSequence>();
            sequence._controller = controller;
            sequence._seatAnchor = controller.transform.Find("Seat Anchor");
            if (sequence._seatAnchor != null)
            {
                sequence._seatPosition = sequence._seatAnchor.localPosition;
                sequence._seatRotation = sequence._seatAnchor.localRotation;
            }
            sequence._actor = OptimizedModelWorld.BuildDropPteranodon(root.transform);
            if (sequence._actor != null) sequence._rig = new PteranodonGrabRig(sequence._actor);
            if (!sequence.Ready)
            {
                Debug.LogError("[Cave Drop] Pteranodon flight/leg rig missing; bypassing hold.");
                sequence.Released = true;
                if (sequence._actor != null) sequence._actor.SetActive(false);
                return sequence;
            }
            RidePose crest = spline.PoseAtDistance(spline.Length * RideMotionProfile.CrestStart);
            sequence._crestRotation = Quaternion.LookRotation(crest.Tangent, Vector3.up)
                * Quaternion.AngleAxis(crest.BankDegrees, Vector3.forward);
            sequence._crest = crest.Position + sequence._crestRotation * Vector3.up * 0.4f;
            sequence._forward = Vector3.ProjectOnPlane(crest.Tangent, Vector3.up).normalized;
            sequence._right = Vector3.Cross(Vector3.up, sequence._forward);
            sequence.ConfigureLanding(spline);
            sequence.LakeCenter = FindLake();
            sequence.BuildFlightPaths();
            sequence._call = EnhancedProceduralAudio.AttachDinosaurCall(sequence._actor, "Pteranodon", 7469);
            sequence._call.enabled = false;
            GameObject impactAudio = new("Cave Drop Impact Audio");
            impactAudio.transform.SetParent(root.transform, false);
            sequence._impact = impactAudio.AddComponent<AudioSource>();
            sequence._impact.clip = CreateImpactClip();
            sequence._impact.playOnAwake = false;
            sequence._impact.spatialBlend = 1f;
            sequence._impact.minDistance = 3f;
            sequence._impact.maxDistance = 28f;
            sequence._impact.dopplerLevel = 0f;
            sequence._impact.volume = 0.65f;
            sequence.ResetForProgress(0f);
            return sequence;
        }

        internal void BeginHold()
        {
            if (!Ready || CurrentPhase != Phase.Waiting) return;
            _crest = _controller.transform.position;
            _crestRotation = _controller.transform.rotation;
            MeasurePanels();
            _flightClock = 0f;
            _rig.SampleFlight(0f);
            BuildDirectPickupPath();
            PoseBird(_pickupPath.Position(0f), FlightDirection(_pickupPath, 0f), 0f);
            _actor.SetActive(true);
            Enter(Phase.Approaching);
            Debug.Log($"[Cave Drop] HOLD | crest={_crest.y:F2}m, directFrontPickup=True, panels={_panelsMeasured}");
        }

        internal void Tick(float deltaTime)
        {
            if (!Ready || CurrentPhase == Phase.Waiting || CurrentPhase == Phase.Complete) return;
            _elapsed += deltaTime;
            _flightClock += deltaTime * (CurrentPhase == Phase.Grabbing ? 1.65f : 1.2f);
            _rig.SampleFlight(_flightClock);
            Vector3 previous = _controller.transform.position;
            Vector3 previousBirdPosition = _actor.transform.position;
            Quaternion previousBirdRotation = _actor.transform.rotation;
            Phase previousPhase = CurrentPhase;
            switch (CurrentPhase)
            {
                case Phase.Approaching:
                    float approach = Ease(_elapsed / ApproachSeconds) * 0.3f;
                    PoseBird(_pickupPath.Position(approach), FlightDirection(_pickupPath, approach), 0f);
                    if (!Screeched && _elapsed >= 0.35f)
                    {
                        Screeched = _call.PlayNow(1.45f, 0.84f);
                    }
                    _rig.Screech(Mathf.Sin(Mathf.Clamp01((_elapsed - 0.25f) / 1.25f) * Mathf.PI));
                    if (_elapsed >= ApproachSeconds) Enter(Phase.Swooping);
                    break;
                case Phase.Swooping:
                    float swoop = Mathf.Lerp(0.3f, 1f, Ease(_elapsed / SwoopSeconds));
                    PoseBirdSmooth(_pickupPath.Position(swoop), FlightDirection(_pickupPath, swoop), 0f,
                        130f * deltaTime);
                    ReachPanels(Ease((swoop - .86f) / .14f), 0f);
                    float pickupAlignment = Vector3.Dot(
                        Vector3.ProjectOnPlane(_actor.transform.forward, Vector3.up).normalized, _forward);
                    if (_elapsed >= SwoopSeconds && pickupAlignment > .985f) Enter(Phase.Grabbing);
                    break;
                case Phase.Grabbing:
                    SetCart(_crest, _crestRotation);
                    PoseCarrier(_crest, Quaternion.LookRotation(_forward), 1f, Ease(_elapsed / 1.35f));
                    if (_elapsed >= 1.35f)
                    {
                        ContactSound(0.35f);
                        Enter(Phase.Lifting);
                    }
                    break;
                case Phase.Lifting:
                    float lift = Ease(_elapsed / 3.8f);
                    Vector3 lifted = Bezier(_crest, _crest + Vector3.up * 6f,
                        _liftEnd - _forward * 3f, _liftEnd, lift);
                    Quaternion liftBird = Quaternion.LookRotation(_forward);
                    SetCart(lifted, Quaternion.Slerp(_crestRotation, liftBird, lift));
                    PoseCarrier(lifted, liftBird, 1f, 1f);
                    if (_elapsed >= 3.8f) Enter(Phase.Carrying);
                    break;
                case Phase.Carrying:
                    float valley = Ease(_elapsed / 25f);
                    Vector3 flight = _valleyPath.Position(valley);
                    Quaternion heading = FlightRotation(_valleyPath, valley);
                    if (valley < 0.08f) heading = Quaternion.Slerp(Quaternion.LookRotation(_forward), heading, Ease(valley / 0.08f));
                    SetCart(flight, heading);
                    PoseCarrier(flight, heading, 1f, 1f);
                    if (_elapsed >= 25f) Enter(Phase.Returning);
                    break;
                case Phase.Returning:
                    float returning = Ease(_elapsed / 13f);
                    Vector3 home = _returnPath.Position(returning);
                    Quaternion homeHeading = FlightRotation(_returnPath, returning);
                    if (returning < 0.12f) homeHeading = Quaternion.Slerp(_phaseRotation, homeHeading, Ease(returning / 0.12f));
                    if (returning > 0.75f) homeHeading = Quaternion.Slerp(homeHeading, _landingRotation, Ease((returning - 0.75f) / 0.25f));
                    SetCart(home, homeHeading);
                    PoseCarrier(home, homeHeading, 1f, 1f);
                    if (_elapsed >= 13f) Enter(Phase.Releasing);
                    break;
                case Phase.Releasing:
                    // A short downward stroke launches the cart; the claws open in mid-air.
                    float stroke = Mathf.Min(_elapsed, ThrowSeconds);
                    float launchHeight = ThrowHeight - .5f * (ThrowSpeed / ThrowSeconds) * stroke * stroke;
                    Vector3 launch = LandingPosition + Vector3.up * launchHeight;
                    SetCart(launch, _landingRotation);
                    PoseCarrier(launch, _landingRotation, 1f, 1f - Ease((stroke - .14f) / .08f));
                    if (_elapsed >= ThrowSeconds)
                    {
                        ReleaseHeight = launchHeight;
                        _fallSeconds = (Mathf.Sqrt(ThrowSpeed * ThrowSpeed + 2f * Gravity * ReleaseHeight) - ThrowSpeed) / Gravity;
                        _departure = _actor.transform.position;
                        _departureRotation = _actor.transform.rotation;
                        _departureClock = 0f;
                        Enter(Phase.Falling);
                    }
                    break;
                case Phase.Falling:
                    // Ballistic descent: speed increases right up to contact, with no docking ease-out.
                    float fall = Mathf.Min(_elapsed, _fallSeconds);
                    float height = Mathf.Max(0f, ReleaseHeight - ThrowSpeed * fall - .5f * Gravity * fall * fall);
                    SetCart(LandingPosition + Vector3.up * height, _landingRotation);
                    Depart(deltaTime);
                    ReachPanels(1f - Ease(_elapsed / .12f), 0f);
                    if (_elapsed >= _fallSeconds)
                    {
                        SetCart(LandingPosition, _landingRotation);
                        ReturnPositionGap = Vector3.Distance(_controller.transform.position, LandingPosition);
                        ReturnRotationGap = Quaternion.Angle(_controller.transform.rotation, _landingRotation);
                        ImpactSpeed = ThrowSpeed + Gravity * _fallSeconds;
                        ImpactCount++;
                        ContactSound(.95f);
                        Enter(Phase.Impacting);
                        Released = true;
                        _controller.ReleaseCaveDrop();
                        Debug.Log($"[Cave Drop] IMPACT | releaseHeight={ReleaseHeight:F2}m, speed={ImpactSpeed:F2}m/s, "
                            + $"downhillSlope={LandingSlope:F3}, landingDistance={LandingDistance:F2}m");
                    }
                    break;
                case Phase.Impacting:
                    float impactTime = Mathf.Min(_elapsed, SuspensionSeconds);
                    ApplyImpactShake(impactTime);
                    Depart(deltaTime);
                    if (_elapsed >= SuspensionSeconds)
                    {
                        Enter(Phase.Departing);
                        Debug.Log($"[Cave Drop] RELEASE | directFrontPickup=True, grip={GripGap:F3}m, "
                            + $"returnGap={ReturnPositionGap:F4}m, rotation={ReturnRotationGap:F3}deg");
                    }
                    break;
                case Phase.Departing:
                    Depart(deltaTime);
                    if (_departureClock >= 5f) { _actor.SetActive(false); Enter(Phase.Complete); }
                    break;
            }
            if (previousPhase == Phase.Approaching || previousPhase == Phase.Swooping || previousPhase == Phase.Grabbing)
            {
                float positionStep = Vector3.Distance(previousBirdPosition, _actor.transform.position);
                float rotationStep = Quaternion.Angle(previousBirdRotation, _actor.transform.rotation);
                if (positionStep > MaxPickupPositionStep || rotationStep > MaxPickupRotationStep)
                {
                    MaxPickupPositionStep = Mathf.Max(MaxPickupPositionStep, positionStep);
                    MaxPickupRotationStep = Mathf.Max(MaxPickupRotationStep, rotationStep);
                    if (positionStep > .75f || rotationStep > 12f)
                    {
                        Debug.LogWarning($"[Cave Drop] PICKUP STEP | phase={previousPhase}, time={_elapsed:F3}, "
                            + $"position={positionStep:F3}m, rotation={rotationStep:F2}deg, "
                            + $"from={previousBirdPosition}, to={_actor.transform.position}");
                    }
                }
            }
            AirSpeed = ControlsCartPose ? Vector3.Distance(previous, _controller.transform.position) / Mathf.Max(0.001f, deltaTime) : 0f;
        }

        private void SetCart(Vector3 position, Quaternion rotation) => _controller.transform.SetPositionAndRotation(position, rotation);

        internal void ApplyRideImpact(ref Vector3 position, ref Quaternion rotation)
        {
            if (CurrentPhase != Phase.Impacting) return;
            float time = Mathf.Min(_elapsed, SuspensionSeconds);
            float suspension = Mathf.Exp(-10f * time) * Mathf.Sin(32f * time)
                * (1f - Ease(time / SuspensionSeconds));
            // Suspension reacts while the controller keeps advancing down the slope.
            position -= rotation * Vector3.up * (.16f * suspension);
            rotation *= Quaternion.Euler(2.2f * suspension, 0f, .7f * suspension);
        }

        private void ApplyImpactShake(float time)
        {
            if (_seatAnchor == null) return;
            // Shake the seat parent so headset tracking can still update the camera normally.
            float gain = (UnityEngine.XR.XRSettings.enabled ? .45f : 1f)
                * (1f - Ease(time / .28f)) * Mathf.Exp(-7f * time);
            _seatAnchor.localPosition = _seatPosition + new Vector3(
                Mathf.Sin(time * 91f) * .025f, -Mathf.Sin(time * 67f) * .045f,
                Mathf.Sin(time * 113f) * .018f) * gain;
            _seatAnchor.localRotation = _seatRotation * Quaternion.Euler(
                Mathf.Sin(time * 83f) * 1.6f * gain, Mathf.Sin(time * 109f) * .45f * gain,
                Mathf.Sin(time * 97f) * gain);
        }

        private void Depart(float deltaTime)
        {
            _departureClock += deltaTime;
            float depart = _departureClock / 5f;
            Vector3 direction = Vector3.Slerp(_forward, (_right + _forward + Vector3.up).normalized, Ease(depart));
            Quaternion flightRotation = Quaternion.LookRotation(direction, Vector3.up)
                * Quaternion.Euler(0f, 0f, -10f * Ease(depart));
            _birdRotation = Quaternion.Slerp(_departureRotation, flightRotation, Ease(_departureClock / .65f));
            _actor.transform.SetPositionAndRotation(
                _departure + (_right * 22f + _forward * 38f + Vector3.up * 30f) * depart * depart, _birdRotation);
        }
        private Vector3 BirdPositionForCart(Vector3 cart, Quaternion rotation)
            => cart + rotation * (Vector3.up * _rig.CarryHeight - _rig.PelvisOffset);

        private void PoseCarrier(Vector3 cartAnchor, Quaternion rotation, float reach, float curl, float loosen = 0f)
        {
            Vector3 target = BirdPositionForCart(cartAnchor, rotation);
            _actor.transform.SetPositionAndRotation(target, rotation);
            _birdRotation = rotation;
            if (loosen > 0f) _rig.RaiseWings(loosen);
            ReachPanels(reach, curl, loosen);
        }

        private void ReachPanels(float reach, float curl, float loosen = 0f)
        {
            Vector3 localRight = _controller.transform.InverseTransformDirection(_actor.transform.right);
            float angle = Mathf.Atan2(localRight.z, localRight.x);
            Vector3 a = RimPoint(angle), b = RimPoint(angle + Mathf.PI);
            _rig.Reach(_controller.transform.TransformPoint(a),
                _controller.transform.TransformPoint(b), reach, curl, loosen);
        }

        private void PoseBird(Vector3 position, Vector3 direction, float bank)
        {
            _birdRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
            _actor.transform.SetPositionAndRotation(position, _birdRotation);
        }

        private void PoseBirdSmooth(Vector3 position, Vector3 direction, float bank, float maximumRotation)
        {
            Quaternion target = Quaternion.LookRotation(direction.normalized, Vector3.up)
                * Quaternion.Euler(0f, 0f, bank);
            _birdRotation = Quaternion.RotateTowards(_birdRotation, target, maximumRotation);
            _actor.transform.SetPositionAndRotation(position, _birdRotation);
        }

        private void Enter(Phase phase)
        {
            if (phase == Phase.Returning) _phaseRotation = _birdRotation;
            CurrentPhase = phase;
            _elapsed = 0f;
        }

        private void ConfigureLanding(RideSpline spline)
        {
            float crestDistance = spline.Length * RideMotionProfile.CrestStart;
            LandingDistance = crestDistance + 8f;
            for (float distance = LandingDistance; distance <= crestDistance + 18f; distance += .5f)
            {
                LandingDistance = distance;
                if (spline.PoseAtDistance(distance).Tangent.y <= -.5f) break;
            }
            RidePose landing = spline.PoseAtDistance(LandingDistance);
            _landingRotation = Quaternion.LookRotation(landing.Tangent, Vector3.up)
                * Quaternion.AngleAxis(landing.BankDegrees, Vector3.forward);
            LandingPosition = landing.Position + _landingRotation * Vector3.up * .4f;
            LandingSlope = landing.Tangent.y;
        }

        private void BuildFlightPaths()
        {
            Vector3 lake = LakeCenter;
            _liftEnd = _crest + _forward * 12f + Vector3.up * 9f;
            _valleyPath = new FlightPath(new[] { _liftEnd,
                _liftEnd + _forward * 14f + _right * 16f + Vector3.up * 3f,
                lake + new Vector3(50f, 67f, -29f), lake + new Vector3(39f, 33f, -34f),
                lake + new Vector3(13f, 10f, -28f), lake + new Vector3(-18f, 7f, -13f),
                lake + new Vector3(-10f, 7f, 5f), lake + new Vector3(13f, 11f, 1f),
                lake + new Vector3(35f, 39f, -18f), lake + new Vector3(42f, 61f, -30f) });
            _returnPath = new FlightPath(new[] { _valleyPath.Position(1f),
                lake + new Vector3(64f, 81f, -20f), lake + new Vector3(70f, 88f, 7f),
                _crest - _forward * 26f + Vector3.up * 14f,
                LandingPosition - _forward * 12f + Vector3.up * 9f,
                LandingPosition + Vector3.up * ThrowHeight });
        }

        private void BuildDirectPickupPath()
        {
            Quaternion carrierRotation = Quaternion.LookRotation(_forward, Vector3.up);
            Vector3 catchPosition = BirdPositionForCart(_crest, carrierRotation);
            _pickupPath = new FlightPath(new[]
            {
                _crest + _forward * 38f + Vector3.up * 14f,
                _crest + _forward * 27f + Vector3.up * 11f,
                _crest + _forward * 15f + Vector3.up * 7f,
                _crest + _forward * 5f + Vector3.up * 5f,
                _crest - _forward * 5f + Vector3.up * 9f,
                _crest - _forward * 12f + Vector3.up * 11f,
                catchPosition - _forward * 10f,
                catchPosition
            });
        }

        private static Vector3 FindLake()
        {
            foreach (MeshRenderer renderer in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Vale Mesozoico/Lagoon Water")
                    return renderer.bounds.center;
            return new Vector3(39.37f, -0.82f, 30.54f);
        }

        private void MeasurePanels()
        {
            Transform visual = _controller.transform.Find("Abandoned Coaster Cart 3D");
            _panelsMeasured = visual != null;
            if (visual == null) return;
            var probes = new System.Collections.Generic.List<MeshCollider>();
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                GameObject probe = new("Temporary Claw Rim Probe");
                probe.transform.SetParent(filter.transform, false);
                MeshCollider collider = probe.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                probes.Add(collider);
            }
            Physics.SyncTransforms();
            for (int i = 0; i < _rim.Length; i++)
            {
                float angle = i * Mathf.PI * 2f / _rim.Length;
                Vector3 direction = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                float nearest = float.PositiveInfinity;
                // The hood, sides and seat back have different heights. Find
                // the upper surface of the actual cart at each grip angle.
                for (float height = 1f; height >= .4f && float.IsPositiveInfinity(nearest); height -= .02f)
                {
                    Ray ray = new(_controller.transform.TransformPoint(direction * 8f + Vector3.up * height),
                        _controller.transform.TransformDirection(-direction));
                    foreach (MeshCollider collider in probes)
                        if (collider.Raycast(ray, out RaycastHit hit, 16f) && hit.distance < nearest)
                        { nearest = hit.distance; _rim[i] = _controller.transform.InverseTransformPoint(hit.point); }
                }
                if (float.IsPositiveInfinity(nearest)) _panelsMeasured = false;
            }
            foreach (MeshCollider probe in probes) Destroy(probe.gameObject);
#if UNITY_EDITOR
            float low = float.PositiveInfinity, high = float.NegativeInfinity;
            foreach (Vector3 point in _rim) { low = Mathf.Min(low, point.y); high = Mathf.Max(high, point.y); }
            Debug.Log($"[Cave Drop] rimHeight={low:F2}..{high:F2}, sides={_rim[0]}/{_rim[_rim.Length / 2]}, headLocal={_controller.transform.InverseTransformPoint(Camera.main.transform.position)}");
#endif
        }

        private Vector3 RimPoint(float angle)
        {
            float index = Mathf.Repeat(angle / (Mathf.PI * 2f), 1f) * _rim.Length;
            int start = Mathf.FloorToInt(index) % _rim.Length;
            return Vector3.Lerp(_rim[start], _rim[(start + 1) % _rim.Length], index - Mathf.Floor(index));
        }

        private void ContactSound(float strength)
        {
            _impact.transform.position = _controller.transform.position;
            _impact.PlayOneShot(_impact.clip, Mathf.Lerp(.35f, 1.5f, Mathf.Clamp01(strength)));
            _controller.TriggerEncounterHaptic(strength, 0.18f);
        }

        internal void ResetForProgress(float progress)
        {
            CompletedLaps = 0;
            Screeched = false;
            Released = !Ready || progress > RideMotionProfile.CrestStart + 0.00001f;
            AirSpeed = 0f;
            ReleaseHeight = 0f;
            ImpactSpeed = 0f;
            ImpactCount = 0;
            MaxPickupPositionStep = 0f;
            MaxPickupRotationStep = 0f;
            ReturnPositionGap = 0f;
            ReturnRotationGap = 0f;
            _fallSeconds = 0f;
            _departureClock = 0f;
            ApplyImpactShake(SuspensionSeconds);
            _flightClock = 0f;
            Enter(Released ? Phase.Complete : Phase.Waiting);
            if (_actor != null)
            {
                foreach (AudioSource source in _actor.GetComponents<AudioSource>()) source.Stop();
                _actor.SetActive(false);
            }
            if (_impact != null) _impact.Stop();
        }

        private static float Ease(float t) { t = Mathf.Clamp01(t); return t * t * t * (10f + t * (-15f + t * 6f)); }
        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        { float u = 1f - t; return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d; }

        private static Quaternion FlightRotation(FlightPath path, float t)
        {
            Vector3 direction = FlightDirection(path, t);
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            return Quaternion.LookRotation(flat, Vector3.up)
                * Quaternion.Euler(Mathf.Clamp(-Mathf.Asin(direction.y) * Mathf.Rad2Deg * 0.3f, -14f, 14f), 0f, 0f);
        }

        private static Vector3 FlightDirection(FlightPath path, float t)
            => (path.Position(Mathf.Min(1f, t + 0.005f))
                - path.Position(Mathf.Max(0f, t - 0.005f))).normalized;

        private sealed class FlightPath
        {
            private const int Samples = 384;
            private readonly Vector3[] _points;
            private readonly float[] _lengths = new float[Samples + 1];
            internal FlightPath(Vector3[] points)
            {
                _points = points;
                Vector3 previous = Evaluate(0f);
                for (int i = 1; i <= Samples; i++)
                {
                    Vector3 p = Evaluate(i / (float)Samples);
                    _lengths[i] = _lengths[i - 1] + Vector3.Distance(previous, p);
                    previous = p;
                }
            }
            internal Vector3 Position(float t)
            {
                float distance = Mathf.Clamp01(t) * _lengths[Samples];
                int low = 0, high = Samples;
                while (high - low > 1) { int middle = (low + high) / 2; if (_lengths[middle] < distance) low = middle; else high = middle; }
                return Evaluate(Mathf.Lerp(low, high, Mathf.InverseLerp(_lengths[low], _lengths[high], distance)) / Samples);
            }
            private Vector3 Evaluate(float t)
            {
                float scaled = Mathf.Clamp01(t) * (_points.Length - 1);
                int index = Mathf.Min(_points.Length - 2, Mathf.FloorToInt(scaled));
                float u = scaled - index;
                Vector3 b = _points[index], c = _points[index + 1];
                Vector3 a = index == 0 ? b * 2f - c : _points[index - 1];
                Vector3 d = index + 2 >= _points.Length ? c * 2f - b : _points[index + 2];
                return 0.5f * ((2f * b) + (-a + c) * u + (2f * a - 5f * b + 4f * c - d) * u * u
                    + (-a + 3f * b - 3f * c + d) * u * u * u);
            }
        }
        private static AudioClip CreateImpactClip()
        {
            const int sampleRate = 22050;
            float[] samples = new float[(int)(sampleRate * 0.65f)];
            System.Random random = new(7469);
            for (int i = 0; i < samples.Length; i++)
            {
                float t = i / (float)sampleRate;
                float thump = Mathf.Sin(t * 2f * Mathf.PI * 72f) * Mathf.Exp(-t * 17f);
                float metal = (Mathf.Sin(t * 2f * Mathf.PI * 387f) * 0.23f
                    + Mathf.Sin(t * 2f * Mathf.PI * 619f) * 0.12f) * Mathf.Exp(-t * 12f);
                float scrape = ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t * 28f) * 0.28f;
                samples[i] = (thump * 0.54f + metal + scrape) * Mathf.Clamp01(t * 600f);
            }
            AudioClip clip = AudioClip.Create("Pteranodon Cart Impact", samples.Length, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private void OnDestroy()
        {
            if (_impact != null && _impact.clip != null) Destroy(_impact.clip);
            if (_actor != null)
            {
                SkinnedMeshRenderer skin = _actor.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (skin != null) Destroy(skin.sharedMaterial);
            }
        }
    }
}
