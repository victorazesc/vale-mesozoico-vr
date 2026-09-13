#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValeMesozoico
{
    internal static class FlightClearanceRuntimeQa
    {
        internal sealed class Sample
        {
            internal Vector3 Position, Head;
            internal Quaternion Rotation;
            internal Vector3[] Bird;
            internal string Phase;
        }

        internal static Sample Record(RideController cart, PteranodonDropSequence sequence)
        {
            string[] names = { "pelvis.", "chest.", "head.", "LupperArm.", "Lwrist.", "Lwing03.", "RupperArm.", "Rwrist.", "Rwing03." };
            Transform[] bones = sequence.Actor.GetComponentsInChildren<Transform>();
            return new Sample { Position = cart.transform.position, Rotation = cart.transform.rotation,
                Head = Camera.main.transform.position, Phase = sequence.CurrentPhase.ToString(),
                Bird = names.Select(prefix => bones.First(bone => bone.name.StartsWith(prefix)).position).ToArray() };
        }

        internal static bool Validate(RideController cart, List<Sample> samples, out string detail)
        {
            const int layer = 31, mask = 1 << layer;
            List<GameObject> probes = new();
            List<MeshCollider> obstacles = new();
            Vector3 originalPosition = cart.transform.position;
            Quaternion originalRotation = cart.transform.rotation;
            bool backfaces = Physics.queriesHitBackfaces;
            try
            {
                Bounds corridor = new(samples[0].Position, Vector3.zero);
                foreach (Sample sample in samples) corridor.Encapsulate(sample.Position);
                corridor.Expand(18f);
                GameObject environment = GameObject.Find("Blender Environment");
                var filters = environment.GetComponentsInChildren<MeshFilter>().Concat(
                    Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(f => f.name == "Tubular Ground Supports"));
                MeshCollider water = null;
                foreach (MeshFilter filter in filters)
                {
                    Renderer renderer = filter.GetComponent<Renderer>();
                    if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !corridor.Intersects(renderer.bounds)) continue;
                    bool isWater = renderer.sharedMaterial != null && renderer.sharedMaterial.shader.name == "Vale Mesozoico/Lagoon Water";
                    GameObject probe = new("Flight Clearance Probe") { layer = layer };
                    probe.transform.SetParent(filter.transform, false);
                    probes.Add(probe);
                    MeshCollider collider = probe.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    if (isWater) water = collider;
                    else obstacles.Add(collider);
                }
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                Renderer[] renderers = cart.GetComponentsInChildren<Renderer>().Where(r => !r.transform.IsChildOf(Camera.main.transform)).ToArray();
                int lowWater = 0;
                foreach (Sample sample in samples)
                {
                    cart.transform.SetPositionAndRotation(sample.Position, sample.Rotation);
                    foreach (Renderer renderer in renderers)
                    {
                        Bounds local = renderer.localBounds;
                        Vector3 scale = renderer.transform.lossyScale;
                        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        Vector3 center = renderer.transform.TransformPoint(local.center);
                        Collider hit = Physics.OverlapBox(center, Vector3.Scale(local.extents, scale) + Vector3.one * .06f,
                            renderer.transform.rotation, mask).FirstOrDefault(c => obstacles.Contains(c as MeshCollider));
                        MeshCollider enclosed = obstacles.FirstOrDefault(c => c.bounds.Contains(center)
                            && EnvironmentRuntimeQa.IsInsideClosedObstacle(c, center));
                        if (hit != null || enclosed != null)
                        {
                            Collider obstacle = hit != null ? hit : enclosed;
                            detail = $"cart vs {obstacle.transform.parent.name}, phase={sample.Phase}, position={sample.Position}";
                            CaveDropRuntimeQa.Capture("flight-clearance-failure", sample.Position + new Vector3(8f, 5f, 8f), sample.Position);
                            return false;
                        }
                    }
                    Collider headHit = Physics.OverlapSphere(sample.Head, .28f, mask)
                        .FirstOrDefault(c => obstacles.Contains(c as MeshCollider));
                    if (headHit != null) { detail = $"rider head vs {headHit.transform.parent.name} at {sample.Position}"; return false; }
                    int[] edges = { 0, 1, 1, 2, 3, 4, 4, 5, 6, 7, 7, 8 };
                    for (int edge = 0; edge < edges.Length; edge += 2)
                    {
                        Collider hit = Physics.OverlapCapsule(sample.Bird[edges[edge]], sample.Bird[edges[edge + 1]], .25f, mask)
                            .FirstOrDefault(c => obstacles.Contains(c as MeshCollider));
                        if (hit == null) continue;
                        detail = $"bird vs {hit.transform.parent.name}, phase={sample.Phase}, position={sample.Position}";
                        return false;
                    }
                    if (water != null && sample.Phase == "Carrying" && water.Raycast(new Ray(sample.Position, Vector3.down), out RaycastHit lake, 15f)
                        && lake.distance > 3f) lowWater++;
                }
                detail = $"samples={samples.Count}, obstacles={obstacles.Count}, cart/head/wingClearance=PASS, lowWaterSamples={lowWater}";
                return lowWater > 0;
            }
            finally
            {
                cart.transform.SetPositionAndRotation(originalPosition, originalRotation);
                Physics.queriesHitBackfaces = backfaces;
                foreach (GameObject probe in probes) Object.DestroyImmediate(probe);
            }
        }
    }
}
#endif
