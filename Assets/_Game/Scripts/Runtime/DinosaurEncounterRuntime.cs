using System.Collections;
using System.Linq;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class DinosaurRoamMotion : MonoBehaviour
    {
        private Vector3 _center;
        private float _radius;
        private float _speed;
        private float _phase;
        private float _angle;
        private bool _initialized;
        private bool _animationPrepared;
        private TriceratopsGrounding _grounding;

        internal void Initialize(Vector3 center, float radius, float speed, float phase)
        {
            _center = center;
            _radius = Mathf.Max(0.5f, radius);
            _speed = Mathf.Max(0.1f, speed);
            _phase = phase;
            _angle = phase;
            _grounding = GetComponent<TriceratopsGrounding>();
            if (_grounding != null)
            {
                float angle = _angle + _phase;
                Vector3 start = _center + new Vector3(Mathf.Cos(angle) * _radius, 0f, Mathf.Sin(angle) * _radius * 0.68f);
                start.y = OptimizedModelWorld.DinosaurGroundHeightAt(start.x, start.z);
                transform.SetPositionAndRotation(start,
                    Quaternion.LookRotation(new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle) * 0.68f)));
            }
            _initialized = true;
        }

        private void Update()
        {
            if (!_initialized)
            {
                return;
            }

            if (!_animationPrepared)
            {
                _animationPrepared = true;
                if (_grounding == null)
                    DinosaurAnimationRuntime.PlayPreferred(gameObject, "walk", 0.76f + _speed * 0.16f);
            }

            float angularSpeed = _speed / Mathf.Max(0.5f, _radius);
            _angle += angularSpeed * Time.deltaTime;
            float x = Mathf.Cos(_angle + _phase) * _radius;
            float z = Mathf.Sin(_angle + _phase) * _radius * 0.68f;
            Vector3 target = _center + new Vector3(x, 0f, z);
            Vector3 tangent = new(
                -Mathf.Sin(_angle + _phase),
                0f,
                Mathf.Cos(_angle + _phase) * 0.68f);
            if (tangent.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 1.8f);
            }
            Vector3 position = Vector3.Lerp(transform.position, target, Time.deltaTime * 1.65f);
            position.y = OptimizedModelWorld.DinosaurGroundHeightAt(position.x, position.z);
            if (_grounding != null)
            {
                Vector3 travel = position - transform.position;
                travel.y = 0f;
                _grounding.SetMovementSpeed(travel.magnitude / Mathf.Max(0.0001f, Time.deltaTime));
            }
            transform.position = position;
        }
    }

    internal sealed class TyrannosaurusChaseSequence : MonoBehaviour
    {
        internal static float TriggerProgress { get; private set; } = 0.625f;
        internal static float RoarProgress { get; private set; } = 0.65f;
        internal static float BreakProgress => TrackMeshFactory.BreakEndProgress + 0.014f;
        internal static float EndProgress { get; private set; } = 0.84f;

        private RideSpline _spline;
        private GameObject _tyrannosaurus;
        private RideController _controller;
        private TrackBreakSetpiece _breakawayTrack;
        private DinosaurAudioEmitter _roar;
        private AudioSource _stomps;
        private Vector3 _chaseStartPosition;
        private float _nextStompTime;
        private float _lastProgress;
        private int _lastDeveloperSeekVersion;
        private bool _attackAnimation;

        internal bool ChaseActive { get; private set; }
        internal bool Roared { get; private set; }
        internal bool TrackBroken { get; private set; }
        internal bool Completed { get; private set; }
        internal float ChaseDistanceTravelled { get; private set; }
        internal GameObject Tyrannosaurus => _tyrannosaurus;

        internal void Initialize(RideSpline spline, GameObject tyrannosaurus)
        {
            _spline = spline;
            TriggerProgress = RideMotionProfile.DropRecoveryEnd + 2f / spline.Length;
            RoarProgress = TriggerProgress + 7f / spline.Length;
            EndProgress = Mathf.Max(spline.MapBaseProgress(0.84f), BreakProgress + 24f / spline.Length);
            _tyrannosaurus = tyrannosaurus;
            _roar = tyrannosaurus != null ? tyrannosaurus.GetComponent<DinosaurAudioEmitter>() : null;
            if (_tyrannosaurus != null)
            {
                _stomps = _tyrannosaurus.AddComponent<AudioSource>();
                _stomps.clip = CreateStompClip();
                _stomps.playOnAwake = false;
                _stomps.loop = false;
                _stomps.spatialBlend = 1f;
                _stomps.rolloffMode = AudioRolloffMode.Logarithmic;
                _stomps.minDistance = 3f;
                _stomps.maxDistance = 52f;
                _stomps.dopplerLevel = 0.08f;
                _stomps.volume = 0.62f;
            }
        }

        private void Update()
        {
            if (_spline == null || _tyrannosaurus == null)
            {
                return;
            }

            _controller ??= FindFirstObjectByType<RideController>();
            _breakawayTrack ??= FindFirstObjectByType<TrackBreakSetpiece>();
            if (_controller == null)
            {
                return;
            }

            float progress = _controller.RideProgress;
            if (_controller.DeveloperScrubbing)
            {
                _lastProgress = progress;
                return;
            }

            if (_lastDeveloperSeekVersion != _controller.DeveloperSeekVersion)
            {
                _lastDeveloperSeekVersion = _controller.DeveloperSeekVersion;
                if (_controller.DeveloperLastSeekWasBackward)
                {
                    ResetEncounter();
                    _lastProgress = progress;
                    return;
                }
            }

            if (progress < 0.04f && _lastProgress > 0.9f)
            {
                ResetEncounter();
            }

            if (!ChaseActive && !Completed && progress >= TriggerProgress && progress < EndProgress)
            {
                BeginChase();
            }

            if (ChaseActive)
            {
                UpdateChase(progress);
            }

            _lastProgress = progress;
        }

        private void BeginChase()
        {
            ChaseActive = true;
            _tyrannosaurus.SetActive(true);
            DinosaurAnimationRuntime.PlayPreferred(_tyrannosaurus, "run", 1.16f);
            PlaceTyrannosaurus(TriggerProgress - 0.035f, 4.4f, true);
            _chaseStartPosition = _tyrannosaurus.transform.position;
            _nextStompTime = Time.time + 0.18f;
            Debug.Log("[T-Rex Encounter] CHASE START");
        }

        private void UpdateChase(float cartProgress)
        {
            float chaseProgress = Mathf.Min(
                cartProgress - Mathf.Lerp(0.036f, 0.022f, Mathf.InverseLerp(TriggerProgress, BreakProgress, cartProgress)),
                (TrackMeshFactory.BreakStartProgress + TrackMeshFactory.BreakEndProgress) * 0.5f);
            float sideOffset = Mathf.Lerp(4.4f, 2.1f, Mathf.InverseLerp(TriggerProgress, BreakProgress, cartProgress));
            PlaceTyrannosaurus(chaseProgress, sideOffset, false);
            ChaseDistanceTravelled = Mathf.Max(
                ChaseDistanceTravelled,
                Vector3.Distance(_chaseStartPosition, _tyrannosaurus.transform.position));

            if (!Roared && cartProgress >= RoarProgress)
            {
                Roared = _roar != null && _roar.PlayNow(1.32f, 0.9f);
                _controller.TriggerEncounterHaptic(0.18f, 0.16f);
                Debug.Log($"[T-Rex Encounter] ROAR | played={Roared}");
            }

            if (Time.time >= _nextStompTime && cartProgress < BreakProgress + 0.02f)
            {
                _nextStompTime = Time.time + 0.43f;
                if (_stomps != null)
                {
                    _stomps.pitch = Random.Range(0.88f, 1.04f);
                    _stomps.PlayOneShot(_stomps.clip, 0.68f);
                }
                _controller.TriggerEncounterHaptic(0.055f, 0.035f);
            }

            if (!TrackBroken && cartProgress >= BreakProgress)
            {
                TrackBroken = _breakawayTrack != null;
                if (TrackBroken)
                {
                    _attackAnimation = DinosaurAnimationRuntime.PlayPreferred(_tyrannosaurus, "attack", 1.05f);
                    _breakawayTrack.Break();
                    _controller.TriggerEncounterHaptic(0.28f, 0.22f);
                    _roar?.PlayNow(1.18f, 0.82f);
                }
                Debug.Log($"[T-Rex Encounter] STRIKE | trackBroken={TrackBroken}");
            }

            if (TrackBroken && _attackAnimation && cartProgress >= BreakProgress + 0.018f)
            {
                _attackAnimation = false;
                DinosaurAnimationRuntime.PlayPreferred(_tyrannosaurus, "idle", 0.86f);
            }

            if (cartProgress >= EndProgress)
            {
                ChaseActive = false;
                Completed = Roared && TrackBroken;
                Debug.Log(
                    $"[T-Rex Encounter] COMPLETE | roar={Roared}, trackBroken={TrackBroken}, "
                    + $"travel={ChaseDistanceTravelled:F1}m");
            }
        }

        private void PlaceTyrannosaurus(float progress, float sideOffset, bool immediate)
        {
            RidePose pose = _spline.PoseAtDistance(_spline.Length * Mathf.Clamp01(progress));
            Vector3 right = pose.Rotation * Vector3.right;
            Vector3 targetPosition = pose.Position + right * sideOffset;
            targetPosition.y = OptimizedModelWorld.DinosaurGroundHeightAt(targetPosition.x, targetPosition.z);
            Quaternion targetRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(pose.Tangent, Vector3.up).normalized,
                Vector3.up);
            if (immediate)
            {
                _tyrannosaurus.transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            float step = Mathf.Min(Time.deltaTime, 0.05f);
            Vector3 position = Vector3.MoveTowards(
                _tyrannosaurus.transform.position,
                targetPosition,
                14.5f * step);
            position.y = OptimizedModelWorld.DinosaurGroundHeightAt(position.x, position.z);
            _tyrannosaurus.transform.position = position;
            _tyrannosaurus.transform.rotation = Quaternion.Slerp(
                _tyrannosaurus.transform.rotation,
                targetRotation,
                step * 5.5f);
        }

        private void ResetEncounter()
        {
            ChaseActive = false;
            Roared = false;
            TrackBroken = false;
            Completed = false;
            ChaseDistanceTravelled = 0f;
            _attackAnimation = false;
            _tyrannosaurus.SetActive(false);
            _breakawayTrack?.ResetSection();
            Debug.Log("[T-Rex Encounter] RESET");
        }

        private static AudioClip CreateStompClip()
        {
            const int sampleRate = 22050;
            const float duration = 0.48f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            System.Random random = new(9417);
            float low = 0f;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float time = sample / (float)sampleRate;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.018f;
                float body = Mathf.Sin(time * Mathf.PI * 2f * 42f) * Mathf.Exp(-time * 11f);
                samples[sample] = Mathf.Clamp((body * 0.68f + low * 1.4f) * Mathf.Exp(-time * 7.2f), -0.82f, 0.82f);
            }

            AudioClip clip = AudioClip.Create("T-Rex Heavy Footstep", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

#if UNITY_EDITOR
    internal sealed class DinosaurRuntimeQa : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Dinosaur Runtime QA").AddComponent<DinosaurRuntimeQa>();
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.8f);

            Transform group = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name == "Animated 3D Dinosaurs");
            Transform[] actors = group != null
                ? group.Cast<Transform>().ToArray()
                : System.Array.Empty<Transform>();
            DinosaurRoamMotion[] roamers = group != null
                ? group.GetComponentsInChildren<DinosaurRoamMotion>(true)
                : System.Array.Empty<DinosaurRoamMotion>();
            DinosaurAudioEmitter[] emitters = group != null
                ? group.GetComponentsInChildren<DinosaurAudioEmitter>(true)
                : System.Array.Empty<DinosaurAudioEmitter>();
            SkinnedMeshRenderer[] skinned = group != null
                ? group.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                : System.Array.Empty<SkinnedMeshRenderer>();
            Animation[] animations = group != null
                ? group.GetComponentsInChildren<Animation>(true)
                : System.Array.Empty<Animation>();
            BrachiosaurusHeadClearance[] headGuards = group != null
                ? group.GetComponentsInChildren<BrachiosaurusHeadClearance>(true)
                : System.Array.Empty<BrachiosaurusHeadClearance>();
            TyrannosaurusChaseSequence chase = group != null
                ? group.GetComponent<TyrannosaurusChaseSequence>()
                : null;
            TrackBreakSetpiece breakaway = FindFirstObjectByType<TrackBreakSetpiece>();
            int groundDinosaurs = actors.Count(actor =>
                actor.name.StartsWith("Brachiosaurus")
                || actor.name.StartsWith("Triceratops")
                || actor.name.StartsWith("Tyrannosaurus"));
            int totalTriangles = skinned
                .Where(renderer => renderer.sharedMesh != null)
                .Sum(renderer => renderer.sharedMesh.triangles.Length / 3);
            int geometryValid = 0;
            int grounded = 0;
            Mesh posedMesh = new();
            foreach (Transform actor in actors.Where(item => !item.name.StartsWith("Pteranodon")))
            {
                bool hasVertices = false;
                Bounds posedBounds = default;
                foreach (SkinnedMeshRenderer renderer in actor.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.BakeMesh(posedMesh, true);
                    foreach (Vector3 vertex in posedMesh.vertices)
                    {
                        Vector3 world = renderer.transform.TransformPoint(vertex);
                        if (!hasVertices) posedBounds = new Bounds(world, Vector3.zero);
                        else posedBounds.Encapsulate(world);
                        hasVertices = true;
                    }
                }
                if (hasVertices && posedBounds.size.y >= 1f && posedBounds.size.y <= 15f) geometryValid++;
                Vector3 position = actor.position;
                if (Mathf.Abs(position.y - OptimizedModelWorld.DinosaurGroundHeightAt(position.x, position.z)) < 0.05f)
                    grounded++;
            }
            Destroy(posedMesh);
            bool staticPass = actors.Length >= 9
                && groundDinosaurs >= 9
                && roamers.Length == 4
                && geometryValid == groundDinosaurs
                && grounded == groundDinosaurs
                && headGuards.Length == 2
                && headGuards.All(guard => guard.CurrentClearance >= guard.RequiredClearance - 0.03f)
                && emitters.Length >= 7
                && skinned.Length >= 6
                && animations.Length >= 6
                && chase != null
                && chase.Tyrannosaurus != null
                && breakaway != null
                && breakaway.FragmentCount == 12
                && totalTriangles > 1000
                && totalTriangles <= 400000;
            Debug.Log(
                $"[Dinosaur QA] {(staticPass ? "PASS" : "FAIL")} | actors={actors.Length}, "
                + $"ground3D={groundDinosaurs}, roamers={roamers.Length}, audioEmitters={emitters.Length}, "
                + $"geometryValid={geometryValid}, grounded={grounded}, "
                + $"protectedLongNecks={headGuards.Length}, "
                + $"skinnedRenderers={skinned.Length}, animations={animations.Length}, "
                + $"triangles={totalTriangles}, breakFragments={(breakaway != null ? breakaway.FragmentCount : 0)}");

            RideController controller = FindFirstObjectByType<RideController>();
            bool chaseObserved = false;
            float timeoutAt = Time.realtimeSinceStartup + 120f;
            while (controller != null && chase != null && Time.realtimeSinceStartup < timeoutAt)
            {
                if (!chaseObserved
                    && controller.RideProgress >= TyrannosaurusChaseSequence.RoarProgress
                    && chase.ChaseActive
                    && chase.ChaseDistanceTravelled >= 2f)
                {
                    chaseObserved = true;
                    Debug.Log(
                        $"[T-Rex QA] CHASE | progress={controller.RideProgress:F3}, "
                        + $"travel={chase.ChaseDistanceTravelled:F1}m, roar={chase.Roared}");
                }

                if (controller.RideProgress >= TyrannosaurusChaseSequence.EndProgress + 0.005f)
                {
                    bool dynamicPass = staticPass
                        && chaseObserved
                        && chase.Roared
                        && chase.TrackBroken
                        && chase.Completed
                        && breakaway != null
                        && breakaway.IsBroken;
                    Debug.Log(
                        $"[T-Rex QA] {(dynamicPass ? "PASS" : "FAIL")} | chase={chaseObserved}, "
                        + $"roar={chase.Roared}, trackBroken={chase.TrackBroken}, "
                        + $"completed={chase.Completed}, travel={chase.ChaseDistanceTravelled:F1}m");
                    Destroy(gameObject);
                    yield break;
                }

                yield return null;
            }

            Debug.LogError(
                $"[T-Rex QA] FAIL | timeout=True, progress={(controller != null ? controller.RideProgress : -1f):F3}");
            Destroy(gameObject);
        }
    }
#endif
}
