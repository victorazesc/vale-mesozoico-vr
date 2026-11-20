using System;
using UnityEngine;

namespace ValeMesozoico
{
    // The source supplies flight. Contact, toe curl and release are authored here
    // on its weighted leg bones, using the same clock as the ride.
    internal sealed class QuetzalcoatlusGrabRig
    {
        private readonly Transform _actor, _visual, _pelvis;
        private readonly AnimationClip _flight;
        private readonly Leg[] _legs;
        internal bool Ready => _flight != null && _pelvis != null && _legs != null;
        internal float ContactGap { get; private set; }
        internal Vector3 PelvisOffset => _actor.InverseTransformPoint(_pelvis.position);
        internal Vector3[] ContactPoints => new[] { _legs[0].Contact, _legs[1].Contact };

        internal QuetzalcoatlusGrabRig(GameObject actor)
        {
            _actor = actor.transform;
            _visual = _actor.Find("Model Scale/Visual");
            foreach (AnimationClip clip in Resources.LoadAll<AnimationClip>(
                "Models/Dinosaurs/Quetzalcoatlus/Quetzalcoatlus"))
                if (!clip.name.StartsWith("__", StringComparison.Ordinal) && clip.length > 1f)
                { _flight = clip; break; }
            foreach (Animation animation in actor.GetComponentsInChildren<Animation>(true))
            { animation.Stop(); animation.enabled = false; }
            _pelvis = Find("Bone");
            if (_flight == null || _pelvis == null || _visual == null) return;
            SampleFlight(0f);
            _legs = new[] { CreateLeg("Bone_L"), CreateLeg("Bone_R") };
            if (_legs[0] == null || _legs[1] == null) _legs = null;
        }

        internal void SampleFlight(float time)
        {
            if (_flight != null) _flight.SampleAnimation(_visual.gameObject, Mathf.Repeat(time, _flight.length));
        }

        internal void Reach(Vector3 a, Vector3 b, float reach, float curl)
        {
            if (!Ready) return;
            ContactGap = 0f;
            foreach (Leg leg in _legs)
            {
                Vector3 target = Vector3.Dot(a - b, _actor.right) * leg.Side > 0f ? a : b;
                Vector3 relaxed = leg.Contact;
                target = Vector3.Lerp(relaxed, target, Mathf.Clamp01(reach));
                // Flex the final toe joint, then keep that visible skin point on
                // the panel as the ankle/knee/hip settle under the cart's weight.
                leg.Toe.localRotation *= Quaternion.AngleAxis(-42f * curl, Vector3.right);
                for (int iteration = 0; iteration < 18; iteration++)
                {
                    if ((leg.Contact - target).sqrMagnitude < 0.000004f) break;
                    for (int j = leg.Joints.Length - 1; j >= 0; j--)
                    {
                        Transform joint = leg.Joints[j];
                        Vector3 from = leg.Contact - joint.position;
                        Vector3 to = target - joint.position;
                        if (from.sqrMagnitude < 0.000001f || to.sqrMagnitude < 0.000001f) continue;
                        Quaternion turn = Quaternion.FromToRotation(from, to);
                        joint.rotation = Quaternion.RotateTowards(Quaternion.identity, turn, 35f) * joint.rotation;
                    }
                }
                ContactGap = Mathf.Max(ContactGap, Vector3.Distance(leg.Contact, target));
            }
        }

        private Leg CreateLeg(string prefix)
        {
            Transform toe = Find(prefix + ".004");
            Transform[] joints = { Find(prefix + ".001"), Find(prefix + ".002"), Find(prefix + ".003") };
            if (toe == null || Array.Exists(joints, t => t == null)) return null;
            Vector3 grip = toe.position;
            float best = float.PositiveInfinity;
            Mesh posed = new();
            foreach (SkinnedMeshRenderer skin in _actor.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                int index = Array.IndexOf(skin.bones, toe);
                if (index < 0) continue;
                skin.BakeMesh(posed);
                Vector3[] vertices = posed.vertices;
                BoneWeight[] weights = skin.sharedMesh.boneWeights;
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight w = weights[i];
                    float influence = (w.boneIndex0 == index ? w.weight0 : 0f)
                        + (w.boneIndex1 == index ? w.weight1 : 0f)
                        + (w.boneIndex2 == index ? w.weight2 : 0f)
                        + (w.boneIndex3 == index ? w.weight3 : 0f);
                    if (influence < 0.85f) continue;
                    Vector3 world = skin.transform.TransformPoint(vertices[i]);
                    float distance = (world - toe.position).sqrMagnitude;
                    if (distance >= best) continue;
                    best = distance;
                    grip = world;
                }
            }
            UnityEngine.Object.Destroy(posed);
            if (float.IsPositiveInfinity(best)) return null;
            return new Leg
            {
                Toe = toe, Joints = joints, GripLocal = toe.InverseTransformPoint(grip),
                Side = Mathf.Sign(Vector3.Dot(toe.position - _pelvis.position, _actor.right))
            };
        }

        private Transform Find(string name)
        {
            foreach (Transform child in _actor.GetComponentsInChildren<Transform>(true))
                if (child.name == name) return child;
            return null;
        }

        private sealed class Leg
        {
            internal Transform Toe;
            internal Transform[] Joints;
            internal Vector3 GripLocal;
            internal float Side;
            internal Vector3 Contact => Toe.TransformPoint(GripLocal);
        }
    }
}
