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
            internal Vector3 Sole, SoleUp, Planted;
            internal float MinimumY, MaximumY;
            internal bool Swing, Initialized;
            internal int[] SoleVertexIndices;
            internal Vector3 Contact => Foot.TransformPoint(Sole);
        }

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
                Leg leg = new()
                {
                    Upper = bones.FirstOrDefault(bone => bone.name == name + "UpLeg" + side),
                    Lower = bones.FirstOrDefault(bone => bone.name == name + "LowLeg" + side),
                    Foot = bones.FirstOrDefault(bone => bone.name == name + "Foot" + side),
                    MinimumY = float.MaxValue, MaximumY = float.MinValue
                };
                if (leg.Upper == null || leg.Lower == null || leg.Foot == null)
                {
                    Debug.LogError($"[Triceratops] Cadeia da pata ausente: {name}{side}");
                    Destroy(baked);
                    enabled = false;
                    return;
                }
                int boneIndex = Array.IndexOf(bones, leg.Foot);
                float minimum = float.MaxValue;
                Vector3 soleCenter = Vector3.zero;
                int count = 0;
                for (int index = 0; index < vertices.Length; index++)
                {
                    if (WeightFor(weights[index], boneIndex) < 0.45f) continue;
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
                    WeightFor(weights[index], boneIndex) >= 0.45f
                    && skin.transform.TransformPoint(vertices[index]).y <= minimum + 0.035f).ToArray();
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
            _strideLength = Mathf.Max(0.5f, (maximumZ - minimumZ) * model.lossyScale.z / 0.65f);
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
            _movementSpeed = speed;
            if (_walk != null) _walk.speed = Mathf.Clamp(speed * _walk.length / _strideLength, 0.08f, 1.3f);
        }

        private void LateUpdate()
        {
            if (_model == null || _walk == null) return;
            Vector3 front = (_legs[0].Contact + _legs[1].Contact) * 0.5f;
            Vector3 back = (_legs[2].Contact + _legs[3].Contact) * 0.5f;
            Vector3 left = (_legs[0].Contact + _legs[2].Contact) * 0.5f;
            Vector3 right = (_legs[1].Contact + _legs[3].Contact) * 0.5f;
            front.y = Ground(front); back.y = Ground(back);
            left.y = Ground(left); right.y = Ground(right);
            Vector3 normal = Vector3.Cross(front - back, right - left).normalized;
            if (normal.y < 0f) normal = -normal;
            Quaternion slope = Quaternion.FromToRotation(Vector3.up, transform.InverseTransformDirection(normal));
            slope = Quaternion.RotateTowards(Quaternion.identity, slope, 18f);
            _slope = Quaternion.Slerp(_slope, slope, 1f - Mathf.Exp(-Time.deltaTime * 5f));
            _model.localRotation = _slope;

            float correction = 0f;
            foreach (Leg leg in _legs)
            {
                Vector3 sole = leg.Contact;
                float lift = Mathf.Max(0f, (_model.InverseTransformPoint(sole).y - leg.MinimumY) * _model.lossyScale.y);
                correction += Ground(sole) - (sole.y - lift);
            }
            float kneeBend = LengthMeters * 0.022f;
            _bodyOffset = Mathf.Clamp(_bodyOffset + (correction * 0.25f - kneeBend)
                * (1f - Mathf.Exp(-Time.deltaTime * 9f)), -1.4f, 1.4f);
            _model.localPosition = _basePosition + Vector3.up * _bodyOffset;

            // Keep every support target reachable as the heavy body crosses a slope.
            float lowerBody = 0f;
            foreach (Leg leg in _legs)
            {
                if (!leg.Initialized || leg.Swing) continue;
                Vector3 ankle = leg.Planted - leg.Foot.TransformVector(leg.Sole);
                float reach = Vector3.Distance(leg.Upper.position, leg.Lower.position)
                    + Vector3.Distance(leg.Lower.position, leg.Foot.position) - 0.015f;
                Vector3 delta = leg.Upper.position - ankle;
                float excess = delta.magnitude - reach;
                if (excess > 0f)
                    lowerBody = Mathf.Max(lowerBody, excess / Mathf.Max(0.5f, delta.normalized.y));
            }
            _bodyOffset -= Mathf.Min(lowerBody, 0.5f);
            _model.localPosition = _basePosition + Vector3.up * _bodyOffset;

            foreach (Leg leg in _legs)
            {
                Vector3 sole = leg.Contact;
                float lift = Mathf.Max(0f, (_model.InverseTransformPoint(sole).y - leg.MinimumY) * _model.lossyScale.y);
                float range = (leg.MaximumY - leg.MinimumY) * _model.lossyScale.y;
                float threshold = Mathf.Max(0.025f, range * 0.22f);
                bool swing = lift > threshold;
                if (!leg.Initialized || swing || leg.Swing)
                {
                    leg.Planted = sole;
                    leg.Planted.y = Ground(sole) + 0.012f;
                }
                // A planted foot stays in world space during the support part of the stride.
                Vector3 target = leg.Planted;
                if (swing) target.y += Mathf.Max(0f, lift - threshold);
                if (leg.Initialized && leg.Swing && !swing && _movementSpeed > 0.08f && Time.time > _settleUntil)
                {
                    Footfalls++;
                    EmitFootfall(target);
                }
                SolveLeg(leg, target, !swing);
                leg.Initialized = true;
                leg.Swing = swing;
            }
        }

        private void SolveLeg(Leg leg, Vector3 soleTarget, bool planted)
        {
            Quaternion footRotation = leg.Foot.rotation;
            if (planted)
            {
                const float sample = 0.18f;
                float dx = Ground(soleTarget + Vector3.right * sample) - Ground(soleTarget - Vector3.right * sample);
                float dz = Ground(soleTarget + Vector3.forward * sample) - Ground(soleTarget - Vector3.forward * sample);
                Vector3 normal = new Vector3(-dx, 2f * sample, -dz).normalized;
                footRotation = Quaternion.FromToRotation(leg.Foot.TransformDirection(leg.SoleUp), normal) * footRotation;
                leg.Foot.rotation = footRotation;
            }
            Vector3 target = soleTarget - leg.Foot.TransformVector(leg.Sole);
            Vector3 a = leg.Upper.position, b = leg.Lower.position, c = leg.Foot.position;
            float upperLength = Vector3.Distance(a, b), lowerLength = Vector3.Distance(b, c);
            Vector3 direction = (target - a).normalized;
            float distance = Mathf.Clamp(Vector3.Distance(a, target),
                Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
            float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
            Vector3 bend = Vector3.ProjectOnPlane(b - a, direction).normalized;
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
            _skinMaterial.SetFloat("_BumpScale", 0.8f);
            _skinMaterial.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f));
            _skinMaterial.SetFloat("_Metallic", 0f);
            _skinMaterial.SetFloat("_Smoothness", 0.22f);
            return _skinMaterial;
        }

        internal static float Ground(Vector3 point) => OptimizedModelWorld.DinosaurGroundHeightAt(point.x, point.z);
        private static float WeightFor(BoneWeight weight, int bone) =>
            (weight.boneIndex0 == bone ? weight.weight0 : 0f) + (weight.boneIndex1 == bone ? weight.weight1 : 0f)
            + (weight.boneIndex2 == bone ? weight.weight2 : 0f) + (weight.boneIndex3 == bone ? weight.weight3 : 0f);
    }
}
