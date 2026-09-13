using System;
using UnityEngine;

namespace ValeMesozoico
{
    // Flight is supplied by the free source. Feet and fingers are posed against
    // the cart rim after sampling it, on the ride's pauseable simulation clock.
    internal sealed class PteranodonGrabRig
    {
        private readonly Transform _actor, _visual, _pelvis;
        private readonly AnimationClip _flight;
        private readonly Leg[] _legs;
        private readonly Transform[] _shoulders, _elbows;
        internal bool Ready => _flight != null && _pelvis != null && _legs != null;
        internal float ContactGap { get; private set; }
        internal Vector3 PelvisOffset => _actor.InverseTransformPoint(_pelvis.position);
        internal Vector3[] ContactPoints => new[] { _legs[0].Contact, _legs[1].Contact };
        internal float CarryHeight => 2.2f;

        internal PteranodonGrabRig(GameObject actor)
        {
            _actor = actor.transform;
            _visual = _actor.Find("Model Scale/Visual");
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>("Models/Dinosaurs/PteranodonCrest/PteranodonCrest"))
                if (clip.name.Contains("flying", StringComparison.OrdinalIgnoreCase) && !clip.name.StartsWith("__"))
                { _flight = clip; break; }
            foreach (Animation animation in actor.GetComponentsInChildren<Animation>(true))
            { animation.Stop(); animation.enabled = false; }
            _pelvis = Find("pelvis.");
            _shoulders = new[] { Find("LupperArm."), Find("RupperArm.") };
            _elbows = new[] { Find("LforeArm."), Find("RforeArm.") };
            if (_flight == null || _pelvis == null || _visual == null) return;
            SampleFlight(0f);
            _legs = new[] { CreateLeg("L"), CreateLeg("R") };
            if (_legs[0] == null || _legs[1] == null) _legs = null;
            if (Ready) Debug.Log($"[Pteranodon Rig] legs={_legs[0].Length:F2}/{_legs[1].Length:F2}m, pelvisHeight={CarryHeight:F2}m");
        }

        internal void SampleFlight(float time)
        {
            if (_flight != null && _visual != null)
                _flight.SampleAnimation(_visual.gameObject, Mathf.Repeat(time, _flight.length));
        }

        internal void Screech(float amount)
        {
            Transform jaw = Find("jaw.");
            if (jaw != null) jaw.rotation = Quaternion.AngleAxis(22f * Mathf.Clamp01(amount), _actor.right) * jaw.rotation;
        }

        internal void RaiseWings(float amount)
        {
            // The passenger moves below a wing while the cart turns. Shift
            // the stroke upward during the one-foot hold, keeping its rhythm.
            for (int i = 0; i < _shoulders.Length; i++)
            {
                if (_shoulders[i] == null || _elbows[i] == null) continue;
                Vector3 direction = (_elbows[i].position - _shoulders[i].position).normalized;
                Vector3 horizontal = Vector3.ProjectOnPlane(direction, _actor.up).normalized;
                const float elevation = 25f * Mathf.Deg2Rad;
                if (Vector3.Dot(direction, _actor.up) >= Mathf.Sin(elevation)) continue;
                Vector3 raised = horizontal * Mathf.Cos(elevation) + _actor.up * Mathf.Sin(elevation);
                _shoulders[i].rotation = Quaternion.Slerp(Quaternion.identity,
                    Quaternion.FromToRotation(direction, raised), amount) * _shoulders[i].rotation;
            }
        }

        internal void Reach(Vector3 left, Vector3 right, float reach, float curl, float loosen = 0f)
        {
            if (!Ready) return;
            ContactGap = 0f;
            for (int i = 0; i < _legs.Length; i++)
            {
                Leg leg = _legs[i];
                float weight = reach * (i == 1 ? 1f - loosen : 1f);
                Vector3 target = leg.Side > 0 ? left : right;
                target = Vector3.Lerp(leg.Contact, target, reach);
                if (i == 1 && loosen > 0f)
                {
                    Vector3 tucked = _pelvis.position + _actor.right * (leg.Side * 1.3f)
                        - _actor.up * .35f + _actor.forward * .2f;
                    target = Vector3.Lerp(target, tucked, loosen * reach);
                }
                foreach (Transform finger in leg.Fingers)
                    finger.localRotation *= Quaternion.AngleAxis(-24f * curl * (i == 1 ? 1f - loosen : 1f), Vector3.right);
                Quaternion[] sampled = leg.Sampled;
                for (int j = 0; j < leg.Joints.Length; j++) sampled[j] = leg.Joints[j].localRotation;
                for (int iteration = 0; iteration < 28; iteration++)
                {
                    if ((leg.Contact - target).sqrMagnitude < 0.000004f) break;
                    for (int j = leg.Joints.Length - 1; j >= 0; j--)
                    {
                        Transform joint = leg.Joints[j];
                        Vector3 from = leg.Contact - joint.position, to = target - joint.position;
                        if (from.sqrMagnitude < .000001f || to.sqrMagnitude < .000001f) continue;
                        Quaternion turn = Quaternion.FromToRotation(from, to);
                        joint.rotation = Quaternion.RotateTowards(Quaternion.identity, turn, 28f) * joint.rotation;
                        joint.localRotation = Quaternion.RotateTowards(sampled[j], joint.localRotation, j == 2 ? 110f : 150f);
                    }
                }
                if (weight > .99f) ContactGap = Mathf.Max(ContactGap, Vector3.Distance(leg.Contact, target));
            }
        }

        private Leg CreateLeg(string side)
        {
            Transform toe = Find(side + "middle02.");
            Transform[] joints = { Find(side + "thigh."), Find(side + "knee."), Find(side + "ankle.") };
            if (toe == null || Array.Exists(joints, t => t == null)) return null;
            Vector3 grip = toe.position;
            float best = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer skin in _actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int index = Array.IndexOf(skin.bones, toe);
                if (index < 0) continue;
                Vector3[] vertices = skin.sharedMesh.vertices;
                Matrix4x4 bind = skin.sharedMesh.bindposes[index];
                BoneWeight[] weights = skin.sharedMesh.boneWeights;
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float influence = (w.boneIndex0 == index ? w.weight0 : 0f)
                        + (w.boneIndex1 == index ? w.weight1 : 0f)
                        + (w.boneIndex2 == index ? w.weight2 : 0f)
                        + (w.boneIndex3 == index ? w.weight3 : 0f);
                    if (influence < .7f) continue;
                    // Bind poses map the actual weighted skin vertex into the
                    // toe's local space even before the first renderer update.
                    Vector3 world = toe.TransformPoint(bind.MultiplyPoint3x4(vertices[i]));
                    float distance = (world - toe.position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    grip = world;
                }
            }
            if (float.IsPositiveInfinity(best)) return null;
            Transform[] fingers = Array.FindAll(_actor.GetComponentsInChildren<Transform>(true),
                t => t.name.StartsWith(side, StringComparison.Ordinal)
                    && (t.name.Contains("index0") || t.name.Contains("middle0") || t.name.Contains("ring0")
                        || t.name.Contains("pinky0") || t.name.Contains("thumb0")));
            return new Leg { Toe = toe, Joints = joints, Fingers = fingers, Sampled = new Quaternion[3],
                GripLocal = toe.InverseTransformPoint(grip),
                Side = Mathf.Sign(Vector3.Dot(toe.position - _pelvis.position, _actor.right)),
                Length = Vector3.Distance(joints[0].position, joints[1].position)
                    + Vector3.Distance(joints[1].position, joints[2].position) + Vector3.Distance(joints[2].position, grip) };
        }

        private Transform Find(string prefix)
        {
            foreach (Transform child in _actor.GetComponentsInChildren<Transform>(true))
                if (child.name.StartsWith(prefix, StringComparison.Ordinal)) return child;
            return null;
        }

        private sealed class Leg
        {
            internal Transform Toe;
            internal Transform[] Joints, Fingers;
            internal Quaternion[] Sampled;
            internal Vector3 GripLocal;
            internal float Side, Length;
            internal Vector3 Contact => Toe.TransformPoint(GripLocal);
        }
    }
}
