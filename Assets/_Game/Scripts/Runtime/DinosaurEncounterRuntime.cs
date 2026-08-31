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

        internal void Initialize(Vector3 center, float radius, float speed, float phase)
        {
            _center = center;
            _radius = Mathf.Max(0.5f, radius);
            _speed = Mathf.Max(0.1f, speed);
            _phase = phase;
            _angle = phase;
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
                DinosaurAnimationRuntime.PlayPreferred(gameObject, "walk", 0.76f + _speed * 0.16f);
            }

            float angularSpeed = _speed / Mathf.Max(0.5f, _radius);
            _angle += angularSpeed * Time.deltaTime;
            float x = Mathf.Cos(_angle + _phase) * _radius;
            float z = Mathf.Sin(_angle + _phase) * _radius * 0.68f;
            Vector3 target = _center + new Vector3(x, 0f, z);
            target.y = ProceduralWorld.HeightAt(target.x, target.z);
            Vector3 tangent = new(
                -Mathf.Sin(_angle + _phase),
                0f,
                Mathf.Cos(_angle + _phase) * 0.68f);
            if (tangent.sqrMagnitude > 0.01f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(tangent.normalized, Vector3.up);
                transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, Time.deltaTime * 1.8f);
            }
            transform.position = Vector3.Lerp(transform.position, target, Time.deltaTime * 1.65f);
        }
    }

    internal sealed class TyrannosaurusChaseSequence : MonoBehaviour
    {
        internal const float TriggerProgress = 0.625f;
        internal const float RoarProgress = 0.65f;
        internal const float BreakProgress = TrackMeshFactory.BreakEndProgress + 0.014f;
        internal const float EndProgress = 0.84f;

        private RideSpline _spline;
        private GameObject _tyrannosaurus;
        private RideController _controller;
        private TrackBreakSetpiece _breakawayTrack;
        private DinosaurAudioEmitter _roar;
        private AudioSource _stomps;
        private Vector3 _chaseStartPosition;
        private float _nextStompTime;
        private float _lastProgress;
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
            targetPosition.y = ProceduralWorld.HeightAt(targetPosition.x, targetPosition.z);
            Quaternion targetRotation = Quaternion.LookRotation(
                Vector3.ProjectOnPlane(pose.Tangent, Vector3.up).normalized,
                Vector3.up);
            if (immediate)
            {
                _tyrannosaurus.transform.SetPositionAndRotation(targetPosition, targetRotation);
                return;
            }

            float step = Mathf.Min(Time.deltaTime, 0.05f);
            _tyrannosaurus.transform.position = Vector3.MoveTowards(
                _tyrannosaurus.transform.position,
                targetPosition,
                14.5f * step);
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
            TyrannosaurusChaseSequence chase = group != null
                ? group.GetComponent<TyrannosaurusChaseSequence>()
                : null;
            TrackBreakSetpiece breakaway = FindFirstObjectByType<TrackBreakSetpiece>();
            int groundDinosaurs = actors.Count(actor =>
                actor.name.StartsWith("Apatosaurus")
                || actor.name.StartsWith("Triceratops")
                || actor.name.StartsWith("Tyrannosaurus"));
            int totalTriangles = skinned
                .Where(renderer => renderer.sharedMesh != null)
                .Sum(renderer => renderer.sharedMesh.triangles.Length / 3);
            bool staticPass = actors.Length >= 9
                && groundDinosaurs >= 6
                && roamers.Length == 5
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
