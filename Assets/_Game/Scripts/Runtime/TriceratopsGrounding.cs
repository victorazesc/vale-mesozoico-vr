using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    // Four inexpensive two-bone solves after animation. No mesh baking in the frame loop.
    [DefaultExecutionOrder(100)]
    internal sealed class TriceratopsGrounding : MonoBehaviour
    {
        internal sealed class Leg
        {
            internal Transform Upper, Lower, Foot;
            internal Vector3 Sole, SoleUp, Planted, Target, Home, SwingStart, SwingEnd;
            internal Vector3 KneePole;
            internal float PhaseOffset;
            internal float MinimumY, MaximumY;
            internal bool Swing, Initialized;
            internal int[] SoleVertexIndices;
            internal Vector3 Contact => Foot.TransformPoint(Sole);
        }

        // Keep these phases and duty factor in sync with import_downloaded_triceratops.py.
        private const float StanceFraction = 0.75f;
        private static readonly float[] PhaseOffsets = { 0f, 0.5f, 0.25f, 0.75f };
        private static Material _skinMaterial;
        private readonly Leg[] _legs = new Leg[4];
        private Transform _model;
        private Animation _animation;
        private AnimationState _walk;
        private TriceratopsFootstepDust _dust;
        private float _strideLength;
        private float _movementSpeed;
        private float _bodyOffset;
        private Vector3 _basePosition;
        private Quaternion _slope = Quaternion.identity;
        private float _settleUntil;
        private Vector3 _previousPosition, _velocity;
        private float _previousYaw, _turnSpeed;
        private bool _motionInitialized;

        internal Leg[] Legs => _legs;
        internal float LengthMeters { get; private set; }
        internal int Footfalls { get; private set; }
        internal int DustBursts { get; private set; }
        internal int ActiveParticles => _dust != null ? _dust.ActiveParticles : 0;

        internal static void Configure(GameObject actor, float length)
        {
            Transform model = actor.transform.Find("Model Scale");
            SkinnedMeshRenderer skin = actor.GetComponentInChildren<SkinnedMeshRenderer>();
            if (model == null || skin == null) return;
            Mesh baked = new();
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            Bounds bounds = new(actor.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[0])), Vector3.zero);
            foreach (Vector3 vertex in vertices)
                bounds.Encapsulate(actor.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex)));
            float sourceLength = Mathf.Max(bounds.size.x, bounds.size.z);
            if (sourceLength < 0.001f)
            {
                Destroy(baked);
                return;
            }
            model.localScale *= length / sourceLength;
            skin.BakeMesh(baked, true);
            vertices = baked.vertices;
            bounds = new Bounds(actor.transform.InverseTransformPoint(skin.transform.TransformPoint(vertices[0])), Vector3.zero);
            foreach (Vector3 vertex in vertices)
                bounds.Encapsulate(actor.transform.InverseTransformPoint(skin.transform.TransformPoint(vertex)));
            model.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            float measuredLength = Mathf.Max(bounds.size.x, bounds.size.z);
            // An animation envelope only controls culling; it must never determine physical size.
            OptimizedModelWorld.TryGetDinosaurGeometryBounds(actor, out _);
            skin.sharedMaterial = SkinMaterial();
            skin.quality = SkinQuality.Bone4;
            skin.shadowCastingMode = ShadowCastingMode.On;
            skin.receiveShadows = true;
            actor.GetComponent<LODGroup>()?.RecalculateBounds();
            Destroy(baked);

            TriceratopsGrounding grounding = actor.AddComponent<TriceratopsGrounding>();
            grounding.LengthMeters = measuredLength;
            grounding.Initialize(skin, model);
        }

        private void Initialize(SkinnedMeshRenderer skin, Transform model)
        {
            _model = model;
            _basePosition = model.localPosition;
            _settleUntil = Time.time + 1f;
            string[] names = { "Front", "Back" };
            string[] sides = { ".L", ".R" };
            Transform[] bones = skin.bones;
            Mesh baked = new();
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            int legIndex = 0;
            foreach (string name in names)
            foreach (string side in sides)
            {
                string sourcePrefix = "Bip001 " + (side == ".L" ? "L " : "R ");
                Leg leg = new()
                {
                    Upper = bones.FirstOrDefault(bone => bone.name == name + "UpLeg" + side
                        || bone.name == sourcePrefix + (name == "Front" ? "UpperArm" : "Thigh")),
                    Lower = bones.FirstOrDefault(bone => bone.name == name + "LowLeg" + side
                        || bone.name == sourcePrefix + (name == "Front" ? "Forearm" : "Calf")),
                    Foot = bones.FirstOrDefault(bone => bone.name == name + "Foot" + side
                        || bone.name == sourcePrefix + (name == "Front" ? "Hand" : "Foot")),
                    MinimumY = float.MaxValue, MaximumY = float.MinValue
                };
                if (leg.Upper == null || leg.Lower == null || leg.Foot == null)
                {
                    Debug.LogError($"[Triceratops] Cadeia da pata ausente: {name}{side}");
                    Destroy(baked);
                    enabled = false;
                    return;
                }
                if (name == "Back")
                {
                    // Bind the bend direction before sampling Walk. An animated knee can
                    // cross the hip-to-ankle axis and invert an otherwise grounded leg.
                    Vector3 axis = (leg.Foot.position - leg.Upper.position).normalized;
                    Vector3 pole = Vector3.ProjectOnPlane(leg.Lower.position - leg.Upper.position, axis);
                    leg.KneePole = model.InverseTransformDirection(pole.normalized);
                }
                // The downloaded rig also skins the toes to child bones of each foot.
                bool[] footBones = bones.Select(bone => bone == leg.Foot || bone.IsChildOf(leg.Foot)).ToArray();
                float minimum = float.MaxValue;
                Vector3 soleCenter = Vector3.zero;
                int count = 0;
                for (int index = 0; index < vertices.Length; index++)
                {
                    if (WeightFor(weights[index], footBones) < 0.45f) continue;
                    Vector3 world = skin.transform.TransformPoint(vertices[index]);
                    minimum = Mathf.Min(minimum, world.y);
                    soleCenter += world;
                    count++;
                }
                if (count == 0)
                {
                    Debug.LogError($"[Triceratops] Sola sem pesos: {leg.Foot.name}");
                    Destroy(baked);
                    enabled = false;
                    return;
                }
                soleCenter /= count;
                soleCenter.y = minimum + 0.012f;
                leg.Sole = leg.Foot.InverseTransformPoint(soleCenter);
                leg.SoleUp = leg.Foot.InverseTransformDirection(Vector3.up);
                leg.SoleVertexIndices = Enumerable.Range(0, vertices.Length).Where(index =>
                    WeightFor(weights[index], footBones) >= 0.45f
                    && skin.transform.TransformPoint(vertices[index]).y <= minimum + 0.035f).ToArray();
                leg.PhaseOffset = PhaseOffsets[legIndex];
                _legs[legIndex++] = leg;
            }
            Destroy(baked);
            _animation = GetComponentInChildren<Animation>();
            AnimationClip clip = Resources.LoadAll<AnimationClip>("Models/Dinosaurs/Triceratops")
                .FirstOrDefault(candidate => candidate.legacy && candidate.name.Contains("Walk"));
            if (_animation == null || clip == null)
            {
                Debug.LogError("[Triceratops] Caminhada ausente; apoio desativado.");
                enabled = false;
                return;
            }
            float minimumZ = float.MaxValue, maximumZ = float.MinValue;
            // Calibrate the soles once against the actual imported walk, in model space.
            for (int sample = 0; sample < 48; sample++)
            {
                clip.SampleAnimation(_animation.gameObject, clip.length * sample / 48f);
                foreach (Leg leg in _legs)
                {
                    Vector3 point = model.InverseTransformPoint(leg.Contact);
                    leg.MinimumY = Mathf.Min(leg.MinimumY, point.y);
                    leg.MaximumY = Mathf.Max(leg.MaximumY, point.y);
                }
                float z = model.InverseTransformPoint(_legs[0].Contact).z;
                minimumZ = Mathf.Min(minimumZ, z);
                maximumZ = Mathf.Max(maximumZ, z);
            }
            _strideLength = Mathf.Max(0.5f, (maximumZ - minimumZ) * model.lossyScale.z / StanceFraction);
            // Mid-support is the anatomical footprint, before world-space foot locking.
            foreach (Leg leg in _legs)
            {
                float phase = Mathf.Repeat(StanceFraction * 0.5f - leg.PhaseOffset, 1f);
                clip.SampleAnimation(_animation.gameObject, clip.length * phase);
                leg.Home = transform.InverseTransformPoint(leg.Contact);
            }
            DinosaurAnimationRuntime.PlayPreferred(gameObject, "walk", 1f);
            _walk = _animation[clip.name];
            _walk.normalizedTime = Mathf.Abs(name.GetHashCode() % 997) / 997f;
            _animation.Sample();
            // The solver needs fresh bone poses while roaming outside the camera frustum.
            // Mesh skinning/rendering can still be culled independently.
            _animation.cullingType = AnimationCullingType.AlwaysAnimate;
            _dust = CreateDust();
        }

        internal void SetMovementSpeed(float speed)
        {
            _movementSpeed = Mathf.Lerp(_movementSpeed, speed, 1f - Mathf.Exp(-Time.deltaTime * 6f));
            if (_walk == null) return;
            // Outer feet cover more ground in a turn. Shorten the support interval
            // accordingly instead of crouching to reach a foot left far behind.
            float gaitSpeed = _movementSpeed;
            foreach (Leg leg in _legs)
            {
                Vector3 footVelocity = _velocity + Vector3.Cross(Vector3.up * (_turnSpeed * Mathf.Deg2Rad),
                    transform.TransformVector(leg.Home));
                gaitSpeed = Mathf.Max(gaitSpeed, footVelocity.magnitude);
            }
            _walk.speed = Mathf.Clamp(gaitSpeed * _walk.length / _strideLength, 0f, 1.3f);
        }

        private void LateUpdate()
        {
            if (_model == null || _walk == null) return;
            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            if (_motionInitialized)
            {
                Vector3 velocity = (transform.position - _previousPosition) / dt;
                velocity.y = 0f;
                _velocity = Vector3.Lerp(_velocity, velocity, 1f - Mathf.Exp(-dt * 6f));
                _turnSpeed = Mathf.Lerp(_turnSpeed,
                    Mathf.DeltaAngle(_previousYaw, transform.eulerAngles.y) / dt, 1f - Mathf.Exp(-dt * 6f));
            }
            _previousPosition = transform.position;
            _previousYaw = transform.eulerAngles.y;
            _motionInitialized = true;

            // Sample terrain beneath stable anatomical footprints, not the moving IK feet.
            Vector3 front = transform.TransformPoint((_legs[0].Home + _legs[1].Home) * 0.5f);
            Vector3 back = transform.TransformPoint((_legs[2].Home + _legs[3].Home) * 0.5f);
            Vector3 left = transform.TransformPoint((_legs[0].Home + _legs[2].Home) * 0.5f);
            Vector3 right = transform.TransformPoint((_legs[1].Home + _legs[3].Home) * 0.5f);
            front.y = Ground(front); back.y = Ground(back);
            left.y = Ground(left); right.y = Ground(right);
            Vector3 normal = Vector3.Cross(front - back, right - left).normalized;
            if (normal.y < 0f) normal = -normal;
            Quaternion slope = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(normal));
            slope = Quaternion.RotateTowards(Quaternion.identity, slope, 18f);
            _slope = Quaternion.Slerp(_slope, slope, 1f - Mathf.Exp(-dt * 3f));
            _model.localRotation = _slope;

            _model.localPosition = _basePosition;
            float correction = 0f;
            foreach (Leg leg in _legs)
            {
                Vector3 sole = leg.Contact;
                float phase = Mathf.Repeat(_walk.normalizedTime + leg.PhaseOffset, 1f);
                float swing = Mathf.Clamp01((phase - StanceFraction) / (1f - StanceFraction));
                float lift = LiftHeight(leg) * Mathf.Pow(Mathf.Sin(Mathf.PI * swing), 2f);
                correction += Ground(sole) - (sole.y - lift);
            }
            float desiredOffset = Mathf.Clamp(correction * 0.25f - LengthMeters * 0.018f, -1.4f, 1.4f);
            _model.localPosition = _basePosition + Vector3.up * _bodyOffset;

            foreach (Leg leg in _legs)
            {
                float phase = Mathf.Repeat(_walk.normalizedTime + leg.PhaseOffset, 1f);
                bool swing = phase >= StanceFraction;
                float progress = Mathf.Clamp01((phase - StanceFraction) / (1f - StanceFraction));
                if (!leg.Initialized)
                {
                    leg.Planted = leg.Contact;
                    leg.Planted.y = Ground(leg.Planted) + 0.012f;
                    leg.SwingStart = leg.Planted;
                    leg.SwingEnd = PredictLanding(leg, 1f - phase);
                }
                else if (swing && !leg.Swing)
                {
                    leg.SwingStart = leg.Planted;
                    leg.SwingEnd = PredictLanding(leg, 1f - phase);
                }
                else if (!swing && leg.Swing)
                {
                    leg.Planted = leg.SwingEnd;
                    if (_movementSpeed > 0.08f && Time.time > _settleUntil)
                    {
                        Footfalls++;
                        EmitFootfall(leg.Planted);
                    }
                }
                // World-space quintic swing: zero foot speed at lift-off and touchdown.
                float blend = progress * progress * progress * (progress * (progress * 6f - 15f) + 10f);
                Vector3 target = swing ? Vector3.Lerp(leg.SwingStart, leg.SwingEnd, blend) : leg.Planted;
                if (swing) target.y += LiftHeight(leg) * Mathf.Pow(Mathf.Sin(Mathf.PI * progress), 2f);
                leg.Target = target;
                leg.Initialized = true;
                leg.Swing = swing;
            }

            // Keep every ankle reachable, including at lift-off. Dropping the swing
            // constraint abruptly lets the body rise and locks the rear knee straight.
            float minimumShift = float.NegativeInfinity;
            float maximumShift = float.PositiveInfinity;
            foreach (Leg leg in _legs)
            {
                if (!leg.Swing) leg.Foot.rotation = GroundedRotation(leg, leg.Target);
                float reach = Vector3.Distance(leg.Upper.position, leg.Lower.position)
                    + Vector3.Distance(leg.Lower.position, leg.Foot.position) - 0.035f;
                Vector3 ankle = leg.Target - leg.Foot.TransformVector(leg.Sole);
                Vector3 delta = leg.Upper.position - ankle;
                float horizontalSquared = delta.x * delta.x + delta.z * delta.z;
                float verticalReach = Mathf.Sqrt(Mathf.Max(0f, reach * reach - horizontalSquared));
                minimumShift = Mathf.Max(minimumShift, 0.04f - delta.y);
                maximumShift = Mathf.Min(maximumShift, verticalReach - delta.y);
            }
            float reachableOffset = minimumShift <= maximumShift
                ? Mathf.Clamp(desiredOffset, _bodyOffset + minimumShift, _bodyOffset + maximumShift)
                : _bodyOffset + minimumShift;
            _bodyOffset = Mathf.MoveTowards(_bodyOffset, reachableOffset, dt * 0.65f);
            _model.localPosition = _basePosition + Vector3.up * _bodyOffset;
            foreach (Leg leg in _legs) SolveLeg(leg, leg.Target, !leg.Swing);
        }

        private float LiftHeight(Leg leg) => Mathf.Clamp(
            (leg.MaximumY - leg.MinimumY) * _model.lossyScale.y, 0.06f, LengthMeters * 0.024f);

        private Vector3 PredictLanding(Leg leg, float remainingPhase)
        {
            float cycleSeconds = _walk.length / Mathf.Max(_walk.speed, 0.1f);
            float seconds = Mathf.Min(remainingPhase * cycleSeconds, 1.2f);
            float radians = _turnSpeed * Mathf.Deg2Rad;
            float angle = radians * seconds;
            Vector3 displacement = Mathf.Abs(radians) < 0.001f ? _velocity * seconds
                : _velocity * (Mathf.Sin(angle) / radians)
                    + Vector3.Cross(Vector3.up, _velocity) * ((1f - Mathf.Cos(angle)) / radians);
            Quaternion turn = Quaternion.AngleAxis(_turnSpeed * seconds, Vector3.up);
            Vector3 home = turn * transform.TransformVector(leg.Home);
            Vector3 footVelocity = turn * _velocity + Vector3.Cross(Vector3.up * radians, home);
            Vector3 anticipation = footVelocity * (cycleSeconds * StanceFraction * 0.45f);
            anticipation.y = 0f;
            anticipation = Vector3.ClampMagnitude(anticipation, LengthMeters * 0.075f);
            Vector3 landing = transform.position + displacement + home + anticipation;
            landing.y = Ground(landing) + 0.012f;
            return landing;
        }

        private void SolveLeg(Leg leg, Vector3 soleTarget, bool planted)
        {
            Quaternion footRotation = planted ? GroundedRotation(leg, soleTarget) : leg.Foot.rotation;
            leg.Foot.rotation = footRotation;
            Vector3 target = soleTarget - leg.Foot.TransformVector(leg.Sole);
            Vector3 a = leg.Upper.position, b = leg.Lower.position, c = leg.Foot.position;
            float upperLength = Vector3.Distance(a, b), lowerLength = Vector3.Distance(b, c);
            Vector3 direction = (target - a).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(a, target),
                Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 hint = leg.KneePole.sqrMagnitude > 0f ? _model.TransformDirection(leg.KneePole) : b - a;
            Vector3 bend = Vector3.ProjectOnPlane(hint, direction).normalized;
            if (bend.sqrMagnitude < 0.01f) bend = Vector3.ProjectOnPlane(-transform.forward, direction).normalized;
            Vector3 knee = a + direction * along + bend * Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            leg.Upper.rotation = Quaternion.FromToRotation(b - a, knee - a) * leg.Upper.rotation;
            leg.Lower.rotation = Quaternion.FromToRotation(leg.Foot.position - leg.Lower.position,
                target - leg.Lower.position) * leg.Lower.rotation;
            // This CC0 rig exports the four foot controls as children of root,
            // independently of the shin chains. Move those controls with the solved ankles.
            if (!leg.Foot.IsChildOf(leg.Lower)) leg.Foot.position = a + direction * distance;
            leg.Foot.rotation = footRotation;
        }

        private static Quaternion GroundedRotation(Leg leg, Vector3 soleTarget)
        {
            const float sample = 0.18f;
            float dx = Ground(soleTarget + Vector3.right * sample) - Ground(soleTarget - Vector3.right * sample);
            float dz = Ground(soleTarget + Vector3.forward * sample) - Ground(soleTarget - Vector3.forward * sample);
            Vector3 normal = new Vector3(-dx, 2f * sample, -dz).normalized;
            return Quaternion.FromToRotation(leg.Foot.TransformDirection(leg.SoleUp), normal) * leg.Foot.rotation;
        }

        private void EmitFootfall(Vector3 contact)
        {
            Camera camera = Camera.main;
            if (_dust == null || camera == null || (camera.transform.position - contact).sqrMagnitude > 45f * 45f) return;
            _dust.Emit(contact, LengthMeters / 8.5f, Footfalls);
            DustBursts++;
        }

        private TriceratopsFootstepDust CreateDust()
        {
            GameObject effect = new("Footstep Soil Dust");
            effect.transform.SetParent(transform, false);
            return effect.AddComponent<TriceratopsFootstepDust>();
        }

        private static Material SkinMaterial()
        {
            if (_skinMaterial != null) return _skinMaterial;
            _skinMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"))
                { name = "Triceratops Skin PBR", enableInstancing = true };
            _skinMaterial.SetTexture("_BaseMap", Resources.Load<Texture2D>("Models/Dinosaurs/TriceratopsSkin/Triceratops_BaseColor"));
            _skinMaterial.SetColor("_BaseColor", Color.white);
            _skinMaterial.SetTexture("_BumpMap", Resources.Load<Texture2D>("Models/Dinosaurs/TriceratopsSkin/Triceratops_Normal"));
            _skinMaterial.EnableKeyword("_NORMALMAP");
            _skinMaterial.SetFloat("_BumpScale", 1f);
            _skinMaterial.SetTexture("_MetallicGlossMap", Resources.Load<Texture2D>(
                "Models/Dinosaurs/TriceratopsSkin/Triceratops_MetallicSmoothness"));
            _skinMaterial.EnableKeyword("_METALLICSPECGLOSSMAP");
            _skinMaterial.SetFloat("_AlphaClip", 1f);
            _skinMaterial.SetFloat("_Cutoff", 0.5f);
            _skinMaterial.SetFloat("_Cull", (float)CullMode.Off);
            _skinMaterial.EnableKeyword("_ALPHATEST_ON");
            _skinMaterial.SetOverrideTag("RenderType", "TransparentCutout");
            _skinMaterial.renderQueue = (int)RenderQueue.AlphaTest;
            _skinMaterial.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f));
            _skinMaterial.SetFloat("_Metallic", 0f);
            _skinMaterial.SetFloat("_Smoothness", 0.45f);
            return _skinMaterial;
        }

        internal static float Ground(Vector3 point) => OptimizedModelWorld.DinosaurGroundHeightAt(point.x, point.z);
        private static float WeightFor(BoneWeight weight, bool[] bones) =>
            (bones[weight.boneIndex0] ? weight.weight0 : 0f) + (bones[weight.boneIndex1] ? weight.weight1 : 0f)
            + (bones[weight.boneIndex2] ? weight.weight2 : 0f) + (bones[weight.boneIndex3] ? weight.weight3 : 0f);
    }
}
