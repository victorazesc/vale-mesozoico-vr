using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    // Climbing stems follow the rock, while the hanging vines flex with the cart.
    // Both share geometry and materials; no solid collider can stop the ride.
    internal sealed class CaveEntranceFoliage : MonoBehaviour
    {
        private const int JointsPerFrond = 4;
        private readonly List<Frond> _fronds = new();
        private RideController _ride;
        private RideSpline _spline;
        private Mesh _mesh;
        private Material _material;
        private Material _stemMaterial;
        private int _seekVersion = -1;
        private float _previousDistance;
        private float _frontReach = 2.2f;
        private float _rearReach = 2.8f;

        private sealed class Frond
        {
            public Transform[] Joints;
            public Quaternion[] Rest;
            public float Distance;
            public float Side;
            public float Phase;
            public float Bend;
            public float Velocity;
        }

        internal int FrondCount => _fronds.Count;
        internal int InteractiveFrondCount { get; private set; }
        internal float FirstDistance { get; private set; } = float.PositiveInfinity;
        internal float LastDistance { get; private set; }
        internal float RouteLength => _spline.Length;
        internal float EntranceProgress => (FirstDistance + LastDistance) * 0.5f / RouteLength;
        internal float MaximumBend
        {
            get
            {
                float maximum = 0f;
                foreach (Frond frond in _fronds) maximum = Mathf.Max(maximum, Mathf.Abs(frond.Bend));
                return maximum;
            }
        }

        public static void Build(Transform mountain, Mesh surfaceMesh, RideSpline spline, int[] surfaceKinds = null)
        {
            Texture2D texture = Resources.Load<Texture2D>("Textures/Realistic/JungleVineLeaves_Atlas");
            if (texture == null)
            {
                Debug.LogWarning("[Cave Foliage] Textura das folhas ausente.");
                return;
            }

            GameObject root = new("Cave Entrance Foliage");
            root.transform.SetParent(mountain, false);
            CaveEntranceFoliage foliage = root.AddComponent<CaveEntranceFoliage>();
            foliage._spline = spline;
            foliage._material = ProceduralWorld.CreateFoliageMaterial("Jungle Vine Leaves", texture);
            foliage._material.SetColor("_BaseColor", new Color(0.60f, 0.71f, 0.56f));
            foliage._material.SetFloat("_Smoothness", 0.1f);
            // Separate front/back vertices give both leaf faces correct lighting;
            // rendering backfaces with front normals made the curtain almost black.
            foliage._material.SetFloat("_Cull", (float)CullMode.Back);
            foliage._stemMaterial = new Material(foliage._material) { name = "Jungle Vine Stems" };
            foliage._stemMaterial.SetTexture("_BaseMap", Texture2D.whiteTexture);
            foliage._stemMaterial.SetColor("_BaseColor", new Color(0.18f, 0.21f, 0.075f));
            foliage._stemMaterial.SetFloat("_AlphaClip", 0f);
            foliage._stemMaterial.DisableKeyword("_ALPHATEST_ON");
            foliage._stemMaterial.renderQueue = (int)RenderQueue.Geometry;

            GameObject probe = new("Cave Foliage Surface Probe");
            probe.hideFlags = HideFlags.HideAndDontSave;
            probe.transform.SetParent(mountain, false);
            MeshCollider collider = probe.AddComponent<MeshCollider>();
            collider.sharedMesh = surfaceMesh;
            try
            {
                Physics.SyncTransforms();
                foliage.CreateFronds(collider, surfaceKinds);
            }
            finally
            {
                probe.SetActive(false);
                Destroy(probe);
            }

            if (foliage.InteractiveFrondCount == 0)
            {
                Debug.LogWarning("[Cave Foliage] Nenhuma borda rochosa encontrada para apoiar as folhas.");
                Destroy(root);
                return;
            }
            Debug.Log($"[Cave Foliage] fronds={foliage.FrondCount}, triangles={foliage._mesh.triangles.Length / 3}, "
                + $"entrance={foliage.EntranceProgress:F4}, first={foliage.FirstDistance / spline.Length:F4}, "
                + $"last={foliage.LastDistance / spline.Length:F4}, renderers=1, materials=2, solidColliders=0");
        }

        private sealed class VineGeometry
        {
            public readonly List<Vector3> Vertices = new();
            public readonly List<Vector2> Uvs = new();
            public readonly List<int> Leaves = new();
            public readonly List<int> Stems = new();
            public readonly List<BoneWeight> Weights = new();
            public readonly List<Transform> Bones = new();
            public int StaticBone = -1;
        }

        private void CreateFronds(MeshCollider surface, int[] surfaceKinds)
        {
            VineGeometry geometry = new();
            System.Random random = new(16392);
            for (int layer = 0; layer < 3; layer++)
            {
                for (int column = 0; column < 20; column++)
                {
                    float variation = (float)random.NextDouble();
                    float angle = Mathf.Lerp(-1.25f, 1.25f, (column + variation) / 20f);
                    for (float progress = _spline.MapBaseProgress(0.360f); progress < MountainTrackCave.StartProgress; progress += 0.0005f)
                    {
                        float distance = progress * _spline.Length;
                        RidePose pose = _spline.PoseAtDistance(distance);
                        Vector3 direction = pose.Rotation * new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                        Vector3 origin = pose.Position + pose.Rotation * Vector3.up * 1.2f;
                        if (!surface.Raycast(new Ray(origin, direction), out _, 7f)) continue;
                        // Root at the irregular lip itself; deeper rows add a little
                        // layering without exposing a bare, straight band of rock.
                        distance += 0.07f + layer * 0.65f;
                        pose = _spline.PoseAtDistance(distance);
                        direction = pose.Rotation * new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0f);
                        origin = pose.Position + pose.Rotation * Vector3.up * 1.2f;
                        if (!surface.Raycast(new Ray(origin, direction), out RaycastHit hit, 7f)) continue;
                        float height = Vector3.Dot(hit.point - pose.Position, pose.Rotation * Vector3.up);
                        // Overlapping long strands close the opening near track
                        // height; staggered tips keep the curtain irregular.
                        float lengthFactor = layer == 0 ? Mathf.Lerp(0.78f, 1.06f, variation)
                            : Mathf.Lerp(0.93f, 1.12f, variation);
                        float length = Mathf.Clamp((height - 0.35f) * lengthFactor, 1.4f, 7.8f);
                        float phase = (float)random.NextDouble() * 24f;
                        Quaternion rotation = pose.Rotation * Quaternion.Euler(variation * 10f - 5f,
                            phase * 1.8f - 22f, -angle * 24f + Mathf.Sin(phase) * 7f);
                        Vector3[] path = new Vector3[25];
                        for (int index = 0; index < path.Length; index++)
                        {
                            float t = index / (float)(path.Length - 1);
                            path[index] = new Vector3((Mathf.Sin(t * 6f + phase) - Mathf.Sin(phase)) * 0.14f * t,
                                -length * t, Mathf.Sin(t * 3.1f) * 0.22f + Mathf.Sin(t * 7f + phase) * 0.06f * t);
                        }
                        AddVine(hit.point + direction * 0.015f, rotation, path, 0.46f + variation * 0.22f,
                            distance, Mathf.Sin(angle), phase, geometry);
                        break;
                    }
                }
            }

            // Project creepers onto the exposed front face. Broken paths stop at
            // the opening: the stems never bridge through empty space or stone.
            float entranceProgress = float.IsPositiveInfinity(FirstDistance)
                ? MountainTrackCave.StartProgress - 0.01f : EntranceProgress;
            RidePose entrance = _spline.PoseAtDistance(_spline.Length * entranceProgress);
            Vector3 forward = entrance.Tangent;
            Vector3 right = entrance.Rotation * Vector3.right;
            Vector3 up = entrance.Rotation * Vector3.up;
            int rooted = 0;
            for (int vine = 0; vine < 32; vine++)
            {
                float phase = (float)random.NextDouble() * 20f;
                bool crown = vine < 18;
                float x = crown ? Mathf.Lerp(-6.5f, 6.5f, (vine + 0.5f) / 18f)
                    : (vine % 2 == 0 ? -1f : 1f) * (4.7f + (float)random.NextDouble() * 1.8f);
                float y = crown ? 8.8f + Mathf.Sin(phase) * 1.3f : 4.2f + (float)random.NextDouble() * 3.5f;
                float length = crown ? 3.8f + (float)random.NextDouble() * 3f : 2.2f + (float)random.NextDouble() * 3f;
                List<Vector3> points = new();
                for (int index = 0; index <= 30; index++)
                {
                    float t = index / 30f;
                    Vector3 origin = entrance.Position + right * (x + Mathf.Sin(t * 7f + phase) * 0.45f)
                        + up * (y - length * t) - forward * 23f;
                    if (!surface.Raycast(new Ray(origin, forward), out RaycastHit hit, 31f)
                        || Vector3.Dot(hit.normal, -forward) < 0.08f
                        || (points.Count > 0 && Vector3.Distance(points[^1], hit.point) > 0.7f))
                    {
                        if (points.Count >= 5) break;
                        points.Clear();
                        continue;
                    }
                    points.Add(hit.point + hit.normal * 0.045f);
                }
                if (points.Count < 5) continue;
                Vector3 anchor = points[0];
                Quaternion rotation = Quaternion.LookRotation(-forward, up);
                Quaternion inverse = Quaternion.Inverse(rotation);
                for (int index = 0; index < points.Count; index++) points[index] = inverse * (points[index] - anchor);
                AddVine(anchor, rotation, points.ToArray(), 0.34f + (float)random.NextDouble() * 0.18f,
                    -1f, 0f, phase, geometry);
                rooted++;
            }

            // Broad, overlapping creepers cover the sheer faces where grasses
            // cannot root. Every point is projected onto the exterior rock skin.
            Bounds mountain = surface.bounds;
            int[] surfaceTriangles = surface.sharedMesh.triangles;
            int wallCreepers = 0;
            for (int sector = 0; sector < 36; sector++)
            {
                for (int band = 0; band < 5; band++)
                {
                    float phase = (float)random.NextDouble() * 20f;
                    float angle = (sector + (float)random.NextDouble() * 0.55f) * Mathf.PI * 2f / 36f;
                    Vector3 outward = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                    Vector3 across = new(-outward.z, 0f, outward.x);
                    float radius = mountain.extents.magnitude + 4f;
                    float height = Mathf.Lerp(mountain.min.y, mountain.max.y, 0.95f - band * 0.17f);
                    float length = 4.5f + (float)random.NextDouble() * 3f;
                    List<Vector3> points = new();
                    for (int index = 0; index <= 32; index++)
                    {
                        float t = index / 32f;
                        Vector3 origin = mountain.center + outward * radius
                            + across * (Mathf.Sin(phase + t * 6f) * 0.7f + Mathf.Sin(phase) * t * 1.2f);
                        origin.y = height - length * t;
                        if (!surface.Raycast(new Ray(origin, -outward), out RaycastHit hit, radius * 2f)
                            || hit.normal.y < -0.05f || !IsExterior(hit)
                            || (points.Count > 0 && Vector3.Distance(points[^1], hit.point) > 0.8f))
                        {
                            if (points.Count >= 6) break;
                            points.Clear();
                            continue;
                        }
                        points.Add(hit.point + hit.normal * 0.065f);
                    }
                    if (points.Count < 6) continue;
                    Vector3 anchor = points[0];
                    Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);
                    Quaternion inverse = Quaternion.Inverse(rotation);
                    for (int index = 0; index < points.Count; index++) points[index] = inverse * (points[index] - anchor);
                    AddVine(anchor, rotation, points.ToArray(), 0.72f + (float)random.NextDouble() * 0.30f,
                        -1f, 0f, phase, geometry);
                    wallCreepers++;
                }
            }

            _mesh = new Mesh
            {
                name = "Rooted Jungle Vines",
                indexFormat = geometry.Vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
                subMeshCount = 2
            };
            _mesh.SetVertices(geometry.Vertices);
            _mesh.SetUVs(0, geometry.Uvs);
            _mesh.SetTriangles(geometry.Leaves, 0);
            _mesh.SetTriangles(geometry.Stems, 1);
            _mesh.boneWeights = geometry.Weights.ToArray();
            Matrix4x4[] bindposes = new Matrix4x4[geometry.Bones.Count];
            for (int index = 0; index < bindposes.Length; index++)
                bindposes[index] = geometry.Bones[index].worldToLocalMatrix * transform.localToWorldMatrix;
            _mesh.bindposes = bindposes;
            _mesh.RecalculateNormals();
            _mesh.RecalculateTangents();
            _mesh.RecalculateBounds();
            SkinnedMeshRenderer renderer = gameObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = _mesh;
            renderer.sharedMaterials = new[] { _material, _stemMaterial };
            renderer.bones = geometry.Bones.ToArray();
            renderer.rootBone = transform;
            renderer.quality = SkinQuality.Bone2;
            renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
            Bounds bounds = _mesh.bounds;
            bounds.Expand(12f);
            renderer.localBounds = bounds;
            Debug.Log($"[Cave Vines] hanging={InteractiveFrondCount}, rootedCreepers={rooted}, "
                + $"wallCreepers={wallCreepers}, leafTriangles={geometry.Leaves.Count / 3}, stemTriangles={geometry.Stems.Count / 3}");

            bool IsExterior(RaycastHit hit)
            {
                if (surfaceKinds == null || surfaceKinds.Length != surface.sharedMesh.vertexCount) return false;
                int triangle = hit.triangleIndex * 3;
                return triangle >= 0 && triangle + 2 < surfaceTriangles.Length
                    && surfaceKinds[surfaceTriangles[triangle]] == 0
                    && surfaceKinds[surfaceTriangles[triangle + 1]] == 0
                    && surfaceKinds[surfaceTriangles[triangle + 2]] == 0;
            }
        }

        private void AddVine(Vector3 anchor, Quaternion rotation, Vector3[] path, float leafSize,
            float distance, float side, float phase, VineGeometry geometry)
        {
            int firstBone = geometry.Bones.Count;
            int boneCount = distance < 0f ? 1 : JointsPerFrond;
            Frond frond = new()
            {
                Joints = new Transform[boneCount], Rest = new Quaternion[boneCount],
                Distance = distance, Side = side, Phase = phase
            };
            Vector3 previous = Vector3.zero;
            if (distance < 0f)
            {
                // All rock-bound creepers share one fixed transform. Their
                // vertices are already expressed in the foliage root's space.
                if (geometry.StaticBone < 0)
                {
                    Transform fixedRoot = new GameObject("Rooted Creepers Anchor").transform;
                    fixedRoot.SetParent(transform, false);
                    geometry.StaticBone = geometry.Bones.Count;
                    geometry.Bones.Add(fixedRoot);
                }
                firstBone = geometry.StaticBone;
                frond.Joints[0] = geometry.Bones[firstBone];
                frond.Rest[0] = Quaternion.identity;
            }
            for (int joint = 0; distance >= 0f && joint < boneCount; joint++)
            {
                Transform bone = new GameObject($"Vine {_fronds.Count:00} Joint {joint}").transform;
                bone.SetParent(joint == 0 ? transform : frond.Joints[joint - 1], false);
                Vector3 point = At(joint / (float)Mathf.Max(1, boneCount - 1));
                if (joint == 0) bone.SetPositionAndRotation(anchor, rotation);
                else bone.localPosition = point - previous;
                previous = point;
                frond.Joints[joint] = bone;
                frond.Rest[joint] = bone.localRotation;
                geometry.Bones.Add(bone);
            }

            float pathLength = 0f;
            for (int index = 1; index < path.Length; index++)
            {
                pathLength += Vector3.Distance(path[index - 1], path[index]);
                Stem(path[index - 1], path[index], (index - 1f) / (path.Length - 1), index / (float)(path.Length - 1), 0.012f);
            }
            int leafCount = Mathf.Max(6, Mathf.CeilToInt(pathLength * (distance < 0f ? 5f : 6.5f)));
            for (int leaf = 0; leaf < leafCount; leaf++)
            {
                float t = (leaf + 0.3f) / leafCount;
                float noise = Mathf.Sin(leaf * 2.37f + phase);
                float sign = leaf % 2 == 0 ? -1f : 1f;
                Vector3 stemPoint = At(t);
                Vector3 petiole = stemPoint + new Vector3(sign * (0.05f + 0.035f * noise), -0.025f, 0.04f);
                Stem(stemPoint, petiole, t, t, 0.004f);
                Vector3 leafUp = new Vector3(sign * (0.55f + noise * 0.3f), -0.65f, 0.1f).normalized;
                float size = leafSize * Mathf.Lerp(1.1f, 0.60f, t) * (0.90f + noise * 0.22f);
                Leaf(petiole, leafUp, size, t, leaf);

                if (leaf % 5 != 2 || t > 0.83f) continue;
                // Occasional short side shoots break the regular alternating rhythm.
                Vector3 end = stemPoint + new Vector3(sign * (0.25f + leafSize * 0.6f), -0.20f, 0.045f);
                Vector3 middle = Vector3.Lerp(stemPoint, end, 0.55f) + Vector3.up * 0.06f;
                Stem(stemPoint, middle, t, t, 0.006f);
                Stem(middle, end, t, t + 0.03f, 0.004f);
                Leaf(middle, new Vector3(sign * 0.7f, -0.35f, 0.2f), size * 0.8f, t, leaf + 1);
                Leaf(end, new Vector3(sign * 0.55f, -0.8f, 0.1f), size * 0.65f, t + 0.03f, leaf + 2);
            }

            _fronds.Add(frond);
            if (distance >= 0f)
            {
                InteractiveFrondCount++;
                FirstDistance = Mathf.Min(FirstDistance, distance);
                LastDistance = Mathf.Max(LastDistance, distance);
            }

            Vector3 At(float t)
            {
                float index = Mathf.Clamp01(t) * (path.Length - 1);
                int lower = Mathf.Min(Mathf.FloorToInt(index), path.Length - 2);
                return Vector3.Lerp(path[lower], path[lower + 1], index - lower);
            }
            void Vertex(Vector3 point, Vector2 uv, float t)
            {
                geometry.Vertices.Add(transform.InverseTransformPoint(anchor + rotation * point));
                geometry.Uvs.Add(uv);
                float bonePosition = Mathf.Clamp01(t) * (boneCount - 1);
                int lower = Mathf.Min(Mathf.FloorToInt(bonePosition), Mathf.Max(0, boneCount - 2));
                float blend = boneCount == 1 ? 0f : bonePosition - lower;
                geometry.Weights.Add(new BoneWeight
                {
                    boneIndex0 = firstBone + lower, weight0 = 1f - blend,
                    boneIndex1 = firstBone + Mathf.Min(lower + 1, boneCount - 1), weight1 = blend
                });
            }
            void Stem(Vector3 a, Vector3 b, float ta, float tb, float radius)
            {
                const int sides = 3;
                Vector3 tangent = (b - a).normalized;
                Vector3 across = Vector3.Cross(tangent, Vector3.forward).normalized;
                if (across.sqrMagnitude < 0.5f) across = Vector3.right;
                Vector3 normal = Vector3.Cross(across, tangent).normalized;
                int first = geometry.Vertices.Count;
                for (int ring = 0; ring < 2; ring++)
                {
                    for (int edge = 0; edge < sides; edge++)
                    {
                        float angle = edge * Mathf.PI * 2f / sides;
                        Vector3 offset = (across * Mathf.Cos(angle) + normal * Mathf.Sin(angle)) * radius;
                        Vertex((ring == 0 ? a : b) + offset, new Vector2(edge / (float)sides, ring), ring == 0 ? ta : tb);
                    }
                }
                for (int edge = 0; edge < sides; edge++)
                {
                    int next = (edge + 1) % sides;
                    geometry.Stems.Add(first + edge); geometry.Stems.Add(first + edge + sides); geometry.Stems.Add(first + next);
                    geometry.Stems.Add(first + next); geometry.Stems.Add(first + edge + sides); geometry.Stems.Add(first + next + sides);
                }
            }
            void Leaf(Vector3 origin, Vector3 leafUp, float size, float t, int variant)
            {
                leafUp.Normalize();
                Vector3 across = Vector3.Cross(leafUp, Vector3.forward).normalized;
                Vector3 normal = Vector3.Cross(across, leafUp).normalized;
                Quaternion curl = Quaternion.AngleAxis(Mathf.Sin(phase + variant * 1.7f) * 27f, leafUp);
                across = curl * across;
                normal = curl * normal;
                int seed = variant + Mathf.FloorToInt(phase);
                int atlas = seed % 6 == 0 ? 1 : seed % 3 == 0 ? 3 : seed % 2 == 0 ? 0 : 2;
                Vector2 uvOrigin = new((atlas % 2) * 0.5f, (atlas / 2) * 0.5f);
                int first = geometry.Vertices.Count;
                int rows = distance < 0f ? 2 : 3;
                for (int row = 0; row < rows; row++)
                {
                    float v = row / (float)(rows - 1);
                    for (int column = 0; column < 3; column++)
                    {
                        float u = column / 2f;
                        float fold = (0.2f + Mathf.Sin(v * Mathf.PI)) * (1f - Mathf.Abs(u * 2f - 1f)) * size * 0.08f;
                        Vector3 point = origin + leafUp * ((v - 0.055f) * size)
                            + across * ((u - 0.5f) * size) + normal * fold;
                        Vertex(point, uvOrigin + new Vector2(u, v) * 0.5f, t);
                    }
                }
                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        int index = first + row * 3 + column;
                        geometry.Leaves.Add(index); geometry.Leaves.Add(index + 1); geometry.Leaves.Add(index + 3);
                        geometry.Leaves.Add(index + 1); geometry.Leaves.Add(index + 4); geometry.Leaves.Add(index + 3);
                    }
                }
                int back = geometry.Vertices.Count;
                for (int vertex = first; vertex < first + rows * 3; vertex++)
                {
                    geometry.Vertices.Add(geometry.Vertices[vertex]);
                    geometry.Uvs.Add(geometry.Uvs[vertex]);
                    geometry.Weights.Add(geometry.Weights[vertex]);
                }
                for (int row = 0; row < rows - 1; row++)
                {
                    for (int column = 0; column < 2; column++)
                    {
                        int index = back + row * 3 + column;
                        geometry.Leaves.Add(index); geometry.Leaves.Add(index + 3); geometry.Leaves.Add(index + 1);
                        geometry.Leaves.Add(index + 1); geometry.Leaves.Add(index + 3); geometry.Leaves.Add(index + 4);
                    }
                }
            }
        }

        private void Start() => BindRide();

        private void BindRide()
        {
            _ride = FindFirstObjectByType<RideController>();
            if (_ride == null) return;
            // Measure the installed cart, including its approved scale. Exclude
            // the camera rig so its comfort overlay cannot inflate contact range.
            Camera camera = Camera.main;
            foreach (Renderer renderer in _ride.GetComponentsInChildren<Renderer>())
            {
                if (camera != null && renderer.transform.IsChildOf(camera.transform)) continue;
                Bounds bounds = renderer.localBounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 point = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f,
                            (corner & 4) == 0 ? -1f : 1f));
                    float forward = Vector3.Dot(renderer.transform.TransformPoint(point) - _ride.transform.position,
                        _ride.transform.forward);
                    _frontReach = Mathf.Max(_frontReach, forward);
                    _rearReach = Mathf.Max(_rearReach, -forward);
                }
            }
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        internal void Tick(float deltaTime)
        {
            if (_ride == null) BindRide();
            if (_ride == null || _spline == null) return;
            float distance = _ride.RideProgress * _spline.Length;
            bool reset = _seekVersion != _ride.DeveloperSeekVersion || distance < _previousDistance - 10f;
            _seekVersion = _ride.DeveloperSeekVersion;
            _previousDistance = distance;
            foreach (Frond frond in _fronds)
            {
                if (frond.Distance < 0f) continue;
                float relative = distance - frond.Distance;
                float contact = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-_frontReach - 1.4f, -_frontReach + 0.3f, relative))
                    * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_rearReach + 0.6f, _rearReach + 2f, relative)));
                if (frond.Distance < 0f) contact = 0f;
                float target = contact * Mathf.Lerp(76f, 58f, Mathf.Abs(frond.Side));
                if (reset)
                {
                    frond.Bend = target;
                    frond.Velocity = 0f;
                }
                float remaining = Mathf.Clamp(deltaTime, 0f, 0.1f);
                while (remaining > 0f)
                {
                    float step = Mathf.Min(remaining, 1f / 120f);
                    float stiffness = contact > 0.01f ? 180f : 18f;
                    float damping = contact > 0.01f ? 24f : 6f;
                    frond.Velocity += ((target - frond.Bend) * stiffness - frond.Velocity * damping) * step;
                    frond.Bend = Mathf.Clamp(frond.Bend + frond.Velocity * step, -8f, 85f);
                    remaining -= step;
                }
                for (int joint = 0; joint < frond.Joints.Length; joint++)
                {
                    float wind = frond.Distance < 0f ? 0f : Mathf.Sin(Time.time * (1.1f + joint * 0.18f) + frond.Phase + joint * 0.7f)
                        * MesozoicWind.Gust(Time.time, frond.Phase) * (1.3f + joint * 0.45f);
                    float share = joint == 0 ? 0.65f : 0.2f;
                    frond.Joints[joint].localRotation = frond.Rest[joint] * Quaternion.Euler(
                        -frond.Bend * share + wind, wind * 0.3f,
                        frond.Side * frond.Bend * share * 0.6f + wind * 0.35f);
                }
            }
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_material != null) Destroy(_material);
            if (_stemMaterial != null) Destroy(_stemMaterial);
        }
    }
}
