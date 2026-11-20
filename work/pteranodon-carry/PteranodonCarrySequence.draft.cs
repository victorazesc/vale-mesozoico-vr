using UnityEngine;

namespace ValeMesozoico
{
    // Advanced only by the ride's clock: the complete capture can pause and replay.
    internal sealed class PteranodonDropSequence : MonoBehaviour
    {
        internal enum Phase { Waiting, Approaching, Circling, Screeching, Swooping,
            Grabbing, Lifting, Carrying, Threatening, Returning, Docking, Releasing, Departing, Complete }
        internal const float OrbitSeconds = 6.5f;
        private const float OrbitRadius = 14f;
        private RideController _controller;
        private GameObject _actor;
        private QuetzalcoatlusGrabRig _rig;
        private DinosaurAudioEmitter _call;
        private AudioSource _impact;
        private Vector3 _crest, _forward, _right, _center, _liftEnd, _threatEnd, _departure;
        private Vector3 _leftPanel, _rightPanel, _birdStart;
        private Quaternion _crestRotation, _phaseRotation, _birdRotation;
        private FlightPath _valleyPath, _returnPath;
        private float _elapsed, _flightClock;
        private bool _panelsMeasured;

        internal Phase CurrentPhase { get; private set; }
        internal int CompletedLaps { get; private set; }
        internal bool Screeched { get; private set; }
        internal bool Released { get; private set; }
        internal bool IsHolding => CurrentPhase >= Phase.Approaching && CurrentPhase <= Phase.Releasing;
        internal bool ControlsCartPose => CurrentPhase >= Phase.Grabbing && CurrentPhase <= Phase.Releasing;
        internal bool GripLocked => CurrentPhase >= Phase.Lifting && CurrentPhase <= Phase.Docking
            && (CurrentPhase != Phase.Threatening || _elapsed < 1.3f || _elapsed > 4.3f);
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
        internal Vector3 CrestPosition => _crest;

        internal static PteranodonDropSequence Create(Transform parent, RideController controller, RideSpline spline)
        {
            GameObject root = new("Cave Drop Encounter");
            root.transform.SetParent(parent, false);
            var sequence = root.AddComponent<PteranodonDropSequence>();
            sequence._controller = controller;
            sequence._actor = OptimizedModelWorld.BuildDropPteranodon(root.transform);
            if (sequence._actor != null) sequence._rig = new QuetzalcoatlusGrabRig(sequence._actor);
            if (!sequence.Ready)
            {
                Debug.LogError("[Cave Drop] Quetzalcoatlus flight/leg rig missing; bypassing hold.");
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
            sequence._center = sequence._crest + Vector3.up * 8f;
            sequence.LakeCenter = FindLake();
            sequence.BuildFlightPaths();
            sequence._call = EnhancedProceduralAudio.AttachDinosaurCall(sequence._actor, "Pteranodon", 7469);
            sequence._call.enabled = false;
            sequence._impact = root.AddComponent<AudioSource>();
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
            _actor.SetActive(true);
            _flightClock = 0f;
            Enter(Phase.Approaching);
            Debug.Log($"[Cave Drop] HOLD | crest={_crest.y:F2}m, span=14m, panels={_panelsMeasured}");
        }

        internal void Tick(float deltaTime)
        {
            if (!Ready || CurrentPhase == Phase.Waiting || CurrentPhase == Phase.Complete) return;
            _elapsed += deltaTime;
            _flightClock += deltaTime * (CurrentPhase == Phase.Grabbing || CurrentPhase == Phase.Threatening ? 1.65f : 1.2f);
            _rig.SampleFlight(_flightClock);
            Vector3 previous = _controller.transform.position;
            switch (CurrentPhase)
            {
                case Phase.Approaching:
                    float approach = Ease(_elapsed / 3f);
                    Vector3 end = OrbitPosition(0f);
                    PoseBird(Vector3.Lerp(end - _right * 34f + Vector3.up * 12f, end, approach), _right, -10f);
                    if (_elapsed >= 3f) Enter(Phase.Circling);
                    break;
                case Phase.Circling:
                    float angle = Mathf.Min(_elapsed / OrbitSeconds, 2f) * Mathf.PI * 2f;
                    PoseBird(OrbitPosition(angle), OrbitPosition(angle + 0.01f) - OrbitPosition(angle), -14f);
                    CompletedLaps = Mathf.Min(2, Mathf.FloorToInt((_elapsed + 0.00001f) / OrbitSeconds));
                    if (_elapsed >= OrbitSeconds * 2f)
                    {
                        CompletedLaps = 2;
                        Screeched = _call.PlayNow(1.45f, 0.84f);
                        _birdStart = _actor.transform.position;
                        Enter(Phase.Screeching);
                    }
                    break;
                case Phase.Screeching:
                    float line = Ease(_elapsed / 3f);
                    Vector3 staging = _crest + _forward * 34f + Vector3.up * 12f;
                    Vector3 p = Bezier(_birdStart, _birdStart + _right * 18f,
                        staging + _forward * 12f, staging, line);
                    PoseBird(p, Vector3.Slerp(_right, -_forward, line), Mathf.Lerp(-14f, 0f, line));
                    if (_elapsed >= 3f) { _birdStart = p; Enter(Phase.Swooping); }
                    break;
                case Phase.Swooping:
                    float swoop = Ease(_elapsed / 3.8f);
                    Quaternion facing = Quaternion.LookRotation(-_forward, Vector3.up);
                    Vector3 catchPosition = BirdPositionForCart(_crest, facing);
                    _actor.transform.SetPositionAndRotation(Vector3.Lerp(_birdStart, catchPosition, swoop), facing);
                    ReachPanels(Ease((swoop - 0.65f) / 0.35f), 0f);
                    if (_elapsed >= 3.8f) Enter(Phase.Grabbing);
                    break;
                case Phase.Grabbing:
                    SetCart(_crest, _crestRotation);
                    PoseCarrier(_crest, Quaternion.LookRotation(-_forward), 1f, Ease(_elapsed / 1.35f));
                    if (_elapsed >= 1.35f)
                    {
                        ContactSound(0.35f);
                        Enter(Phase.Lifting);
                    }
                    break;
                case Phase.Lifting:
                    float lift = Ease(_elapsed / 3.8f);
                    Vector3 lifted = Bezier(_crest, _crest + Vector3.up * 6f,
                        _liftEnd + _forward * 3f, _liftEnd, lift);
                    Quaternion liftBird = Quaternion.LookRotation(-_forward);
                    SetCart(lifted, Quaternion.Slerp(_crestRotation, liftBird * Quaternion.Euler(0f, 180f, 0f), lift));
                    PoseCarrier(lifted, liftBird, 1f, 1f);
                    if (_elapsed >= 3.8f) Enter(Phase.Carrying);
                    break;
                case Phase.Carrying:
                    float valley = Ease(_elapsed / 25f);
                    Vector3 flight = _valleyPath.Position(valley);
                    Quaternion heading = FlightRotation(_valleyPath, valley);
                    if (valley < 0.08f) heading = Quaternion.Slerp(Quaternion.LookRotation(-_forward), heading, Ease(valley / 0.08f));
                    SetCart(flight, heading * Quaternion.Euler(0f, 180f, 0f));
                    PoseCarrier(flight, heading, 1f, 1f);
                    if (_elapsed >= 25f) Enter(Phase.Threatening);
                    break;
                case Phase.Threatening:
                    float threat = Mathf.Clamp01(_elapsed / 5.8f);
                    float turn = Ease((threat - 0.2f) / 0.55f);
                    float slip = Mathf.Sin(turn * Mathf.PI);
                    Vector3 anchor = Vector3.Lerp(_valleyPath.Position(1f), _threatEnd, Ease(threat));
                    SetCart(anchor - Vector3.up * (slip * 0.8f),
                        _phaseRotation * Quaternion.Euler(-slip * 9f, Mathf.Lerp(180f, 0f, turn), slip * 6f));
                    PoseCarrier(anchor, _phaseRotation, 1f - slip * 0.72f, 1f - slip);
                    if (_elapsed >= 5.8f) { ContactSound(0.24f); Enter(Phase.Returning); }
                    break;
                case Phase.Returning:
                    float returning = Ease(_elapsed / 13f);
                    Vector3 home = _returnPath.Position(returning);
                    Quaternion homeHeading = FlightRotation(_returnPath, returning);
                    if (returning < 0.12f) homeHeading = Quaternion.Slerp(_phaseRotation, homeHeading, Ease(returning / 0.12f));
                    if (returning > 0.75f) homeHeading = Quaternion.Slerp(homeHeading, _crestRotation, Ease((returning - 0.75f) / 0.25f));
                    SetCart(home, homeHeading);
                    PoseCarrier(home, homeHeading, 1f, 1f);
                    if (_elapsed >= 13f) Enter(Phase.Docking);
                    break;
                case Phase.Docking:
                    Vector3 dock = Vector3.Lerp(_crest + Vector3.up * 4f, _crest, Ease(_elapsed / 3f));
                    SetCart(dock, _crestRotation);
                    PoseCarrier(dock, _crestRotation, 1f, 1f);
                    if (_elapsed >= 3f)
                    {
                        ReturnPositionGap = Vector3.Distance(_controller.transform.position, _crest);
                        ReturnRotationGap = Quaternion.Angle(_controller.transform.rotation, _crestRotation);
                        ContactSound(0.15f);
                        Enter(Phase.Releasing);
                    }
                    break;
                case Phase.Releasing:
                    float release = Ease(_elapsed / 1.2f);
                    SetCart(_crest, _crestRotation);
                    PoseCarrier(_crest + Vector3.up * release, _crestRotation, 1f - release, 1f - release);
                    if (_elapsed >= 1.2f)
                    {
                        _departure = _actor.transform.position;
                        Released = true;
                        _controller.ReleaseCaveDrop();
                        Enter(Phase.Departing);
                        Debug.Log($"[Cave Drop] RELEASE | laps={CompletedLaps}, grip={GripGap:F3}m, "
                            + $"returnGap={ReturnPositionGap:F4}m, rotation={ReturnRotationGap:F3}deg");
                    }
                    break;
                case Phase.Departing:
                    float depart = _elapsed / 5f;
                    PoseBird(_departure + (_right * 22f + _forward * 38f + Vector3.up * 30f) * depart * depart,
                        Vector3.Slerp(_forward, (_right + _forward + Vector3.up).normalized, Ease(depart)), -10f * Ease(depart));
                    if (_elapsed >= 5f) { _actor.SetActive(false); Enter(Phase.Complete); }
                    break;
            }
            AirSpeed = ControlsCartPose ? Vector3.Distance(previous, _controller.transform.position) / Mathf.Max(0.001f, deltaTime) : 0f;
        }

        private void SetCart(Vector3 position, Quaternion rotation) => _controller.transform.SetPositionAndRotation(position, rotation);
        private Vector3 BirdPositionForCart(Vector3 cart, Quaternion rotation)
            => cart + Vector3.up * 2.25f - rotation * _rig.PelvisOffset;

        private void PoseCarrier(Vector3 cartAnchor, Quaternion rotation, float reach, float curl)
        {
            _actor.transform.SetPositionAndRotation(BirdPositionForCart(cartAnchor, rotation), rotation);
            _birdRotation = rotation;
            ReachPanels(reach, curl);
        }

        private void ReachPanels(float reach, float curl)
        {
            _rig.Reach(_controller.transform.TransformPoint(_leftPanel),
                _controller.transform.TransformPoint(_rightPanel), reach, curl);
        }

        private void PoseBird(Vector3 position, Vector3 direction, float bank)
        {
            _birdRotation = Quaternion.LookRotation(direction.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, bank);
            _actor.transform.SetPositionAndRotation(position, _birdRotation);
        }

        private Vector3 OrbitPosition(float angle) => _center
            + (_forward * Mathf.Cos(angle) + _right * Mathf.Sin(angle)) * OrbitRadius
            + Vector3.up * Mathf.Sin(angle * 2f) * 0.5f;

        private void Enter(Phase phase)
        {
            if (phase == Phase.Threatening || phase == Phase.Returning) _phaseRotation = _birdRotation;
            CurrentPhase = phase;
            _elapsed = 0f;
        }

        private void BuildFlightPaths()
        {
            Vector3 lake = LakeCenter;
            _liftEnd = _crest - _forward * 12f + Vector3.up * 9f;
            _valleyPath = new FlightPath(new[] { _liftEnd,
                lake + new Vector3(50f, 67f, -29f), lake + new Vector3(39f, 33f, -34f),
                lake + new Vector3(13f, 10f, -28f), lake + new Vector3(-18f, 7f, -13f),
                lake + new Vector3(-10f, 7f, 5f), lake + new Vector3(13f, 11f, 1f),
                lake + new Vector3(35f, 39f, -18f), lake + new Vector3(42f, 61f, -30f) });
            _threatEnd = _valleyPath.Position(1f) + Vector3.up * 7f;
            _returnPath = new FlightPath(new[] { _threatEnd,
                lake + new Vector3(64f, 81f, -20f), lake + new Vector3(70f, 88f, 7f),
                _crest - _forward * 26f + Vector3.up * 14f,
                _crest - _forward * 12f + Vector3.up * 7f, _crest + Vector3.up * 4f });
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
            _leftPanel = MeasurePanel(visual, -1f, out bool left);
            _rightPanel = MeasurePanel(visual, 1f, out bool right);
            _panelsMeasured &= left && right;
        }

        private Vector3 MeasurePanel(Transform visual, float side, out bool measured)
        {
            measured = false;
            Vector3 local = new(side * 0.9f, 0.55f, 0f);
            if (visual == null) return local;
            Ray ray = new(_controller.transform.TransformPoint(new Vector3(side * 8f, 0.55f, 0f)),
                _controller.transform.right * -side);
            float nearest = float.PositiveInfinity;
            foreach (MeshFilter filter in visual.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                GameObject probe = new("Temporary Claw Contact Probe");
                probe.transform.SetParent(filter.transform, false);
                MeshCollider collider = probe.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                Physics.SyncTransforms();
                if (collider.Raycast(ray, out RaycastHit hit, 16f) && hit.distance < nearest)
                {
                    nearest = hit.distance;
                    local = _controller.transform.InverseTransformPoint(hit.point);
                    measured = true;
                }
                Destroy(probe);
            }
            return local;
        }

        private void ContactSound(float strength)
        {
            _impact.transform.position = _controller.transform.position;
            _impact.Play();
            _controller.TriggerEncounterHaptic(strength, 0.18f);
        }

        internal void ResetForProgress(float progress)
        {
            CompletedLaps = 0;
            Screeched = false;
            Released = !Ready || progress > RideMotionProfile.CrestStart + 0.00001f;
            AirSpeed = 0f;
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
            Vector3 direction = (path.Position(Mathf.Min(1f, t + 0.005f)) - path.Position(Mathf.Max(0f, t - 0.005f))).normalized;
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
            return Quaternion.LookRotation(flat, Vector3.up)
                * Quaternion.Euler(Mathf.Clamp(-Mathf.Asin(direction.y) * Mathf.Rad2Deg * 0.3f, -14f, 14f), 0f, 0f);
        }

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
