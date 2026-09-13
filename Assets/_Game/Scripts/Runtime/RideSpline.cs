using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal readonly struct RidePose
    {
        public RidePose(Vector3 position, Quaternion rotation, Vector3 tangent, float normalizedDistance, float bankDegrees)
        {
            Position = position;
            Rotation = rotation;
            Tangent = tangent;
            NormalizedDistance = normalizedDistance;
            BankDegrees = bankDegrees;
        }

        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public Vector3 Tangent { get; }
        public float NormalizedDistance { get; }
        public float BankDegrees { get; }
    }

    internal sealed class RideSpline
    {
        private const int LookupSamples = 4096;
        private const int BaseSamples = 1024;
        private readonly Vector3[] _points;
        private readonly float[] _distances = new float[LookupSamples + 1];
        private readonly float[] _baseDistances = new float[BaseSamples + 1];
        private readonly RideInversions _inversions;
        private readonly float _inversionStart, _inversionEnd;

        public RideSpline(Vector3[] points)
        {
            if (points == null || points.Length < 4)
            {
                throw new ArgumentException("A spline fechada precisa de pelo menos quatro pontos.", nameof(points));
            }

            _points = points;
            Vector3 previousBase = EvaluateBase(0f);
            for (int i = 1; i <= BaseSamples; i++)
            {
                Vector3 current = EvaluateBase(i / (float)BaseSamples);
                _baseDistances[i] = _baseDistances[i - 1] + Vector3.Distance(previousBase, current);
                previousBase = current;
            }
            if (points.Length >= 15)
            {
                _inversionStart = 11f / points.Length;
                _inversionEnd = 12f / points.Length;
                Vector3 entry = (EvaluateBase(_inversionStart + 0.0001f) - EvaluateBase(_inversionStart - 0.0001f)).normalized;
                Vector3 exit = (EvaluateBase(_inversionEnd + 0.0001f) - EvaluateBase(_inversionEnd - 0.0001f)).normalized;
                _inversions = new RideInversions(points[11], points[12], entry, exit);
            }
            Vector3 previous = Evaluate(0f);
            for (int i = 1; i <= LookupSamples; i++)
            {
                float t = i / (float)LookupSamples;
                Vector3 current = Evaluate(t);
                _distances[i] = _distances[i - 1] + Vector3.Distance(previous, current);
                previous = current;
            }

            Length = _distances[LookupSamples];
        }

        public float Length { get; }
        internal float InversionsStartProgress => ProgressAtParameter(_inversionStart);
        internal float InversionsEndProgress => ProgressAtParameter(_inversionEnd);
        internal Vector3 InversionRight => _inversions.Right;

        internal IEnumerable<float> InversionSupportDistances()
        {
            if (_inversions == null) yield break;
            foreach (float time in RideInversions.SupportTimes)
                yield return ProgressAtParameter(Mathf.Lerp(_inversionStart, _inversionEnd, time)) * Length;
        }

        internal float MapBaseProgress(float progress)
        {
            float distance = Mathf.Clamp01(progress) * _baseDistances[BaseSamples];
            return ProgressAtParameter(ParameterAtDistance(_baseDistances, distance));
        }

        private float ProgressAtParameter(float normalized)
        {
            float index = Mathf.Clamp01(normalized) * LookupSamples;
            int lower = Mathf.Min(LookupSamples - 1, Mathf.FloorToInt(index));
            return Mathf.Lerp(_distances[lower], _distances[lower + 1], index - lower) / Length;
        }

        internal bool IsInversionAtDistance(float distance) => IsInversion(ParameterAtDistance(_distances, Mathf.Repeat(distance, Length)));
        internal bool IsLoopAtDistance(float distance) => _inversions != null
            && _inversions.IsLoop(InversionTime(ParameterAtDistance(_distances, Mathf.Repeat(distance, Length))));
        internal bool IsCorkscrewAtDistance(float distance) => _inversions != null
            && _inversions.IsCorkscrew(InversionTime(ParameterAtDistance(_distances, Mathf.Repeat(distance, Length))));

        private bool IsInversion(float normalized) => _inversions != null
            && normalized >= _inversionStart && normalized <= _inversionEnd;
        private float InversionTime(float normalized) => (normalized - _inversionStart) / (_inversionEnd - _inversionStart);

        public Vector3 Evaluate(float normalized)
        {
            float wrapped = Mathf.Repeat(normalized, 1f);
            return IsInversion(wrapped) ? _inversions.Evaluate(InversionTime(wrapped)) : EvaluateBase(wrapped);
        }

        private Vector3 EvaluateBase(float normalized)
        {
            float wrapped = Mathf.Repeat(normalized, 1f);
            float scaled = wrapped * _points.Length;
            int index = Mathf.FloorToInt(scaled);
            float t = scaled - index;

            Vector3 p0 = _points[Wrap(index - 1)];
            Vector3 p1 = _points[Wrap(index)];
            Vector3 p2 = _points[Wrap(index + 1)];
            Vector3 p3 = _points[Wrap(index + 2)];

            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1)
                + (-p0 + p2) * t
                + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
                + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        public RidePose PoseAtDistance(float distance)
        {
            float wrappedDistance = Mathf.Repeat(distance, Length);
            float normalized = ParameterAtDistance(_distances, wrappedDistance);
            Vector3 position = Evaluate(normalized);
            Vector3 tangent = TangentAt(normalized);
            bool inversion = IsInversion(normalized);
            float sectionTime = inversion ? InversionTime(normalized) : 0f;
            float bank = BankAt(normalized) * (inversion ? _inversions.BankWeight(sectionTime) : 1f);
            Vector3 up = inversion ? _inversions.UpHint(sectionTime) : Vector3.up;
            Quaternion rotation = Quaternion.LookRotation(tangent, up) * Quaternion.AngleAxis(bank, Vector3.forward);
            return new RidePose(position, rotation, tangent, normalized, bank);
        }

        private static float ParameterAtDistance(float[] distances, float distance)
        {
            int samples = distances.Length - 1;
            int upper = Array.BinarySearch(distances, distance);
            if (upper < 0)
            {
                upper = ~upper;
            }

            upper = Mathf.Clamp(upper, 1, samples);
            int lower = upper - 1;
            float segmentLength = distances[upper] - distances[lower];
            float interpolation = segmentLength > 0.0001f
                ? (distance - distances[lower]) / segmentLength
                : 0f;
            return Mathf.Lerp(lower / (float)samples, upper / (float)samples, interpolation);
        }

        private Vector3 TangentAt(float normalized)
        {
            const float sample = 1f / (LookupSamples * 4f);
            return (Evaluate(normalized + sample) - Evaluate(normalized - sample)).normalized;
        }

        private float BankAt(float normalized)
        {
            const float sampleOffset = 0.01f;
            const float referenceSpeed = 8f;
            Vector3 before = Vector3.ProjectOnPlane(TangentAt(normalized - sampleOffset), Vector3.up).normalized;
            Vector3 after = Vector3.ProjectOnPlane(TangentAt(normalized + sampleOffset), Vector3.up).normalized;
            if (before.sqrMagnitude < 0.5f || after.sqrMagnitude < 0.5f)
            {
                return 0f;
            }

            float signedTurnRadians = Vector3.SignedAngle(before, after, Vector3.up) * Mathf.Deg2Rad;
            float sampledArcLength = Mathf.Max(0.01f, Length * sampleOffset * 2f);
            float signedCurvature = signedTurnRadians / sampledArcLength;
            float idealBank = -Mathf.Atan(referenceSpeed * referenceSpeed * signedCurvature / 9.81f) * Mathf.Rad2Deg;
            return Mathf.Clamp(idealBank, -12f, 12f);
        }

        private int Wrap(int index)
        {
            int result = index % _points.Length;
            return result < 0 ? result + _points.Length : result;
        }
    }

    internal static class TrackMeshFactory
    {
        internal const int PathSegments = 768;
        internal static float BreakStartProgress { get; private set; } = 218f / 288f;
        internal static float BreakEndProgress { get; private set; } = 224f / 288f;

        internal static void ConfigureBreak(RideSpline spline)
        {
            float originalStart = spline.MapBaseProgress(218f / 288f);
            float originalEnd = spline.MapBaseProgress(224f / 288f);
            BreakStartProgress = Mathf.Max(originalStart, RideMotionProfile.DropRecoveryEnd + 12f / spline.Length);
            BreakEndProgress = BreakStartProgress + originalEnd - originalStart;
        }

        public static GameObject CreateTrack(Transform parent, RideSpline spline, Material rail, Material sleeper, Material support)
        {
            GameObject root = new GameObject("Procedural Tubular Coaster Track");
            root.transform.SetParent(parent, false);

            CreateMeshObject("Polished Running Rails", root.transform, BuildRunningRails(spline), rail);
            Material wheelContactMaterial = CreateWheelContactMaterial(rail);
            GameObject wheelContact = CreateMeshObject(
                "Wheel Contact Grease Bands",
                root.transform,
                BuildWheelContactGrease(spline),
                wheelContactMaterial);
            MeshRenderer wheelContactRenderer = wheelContact.GetComponent<MeshRenderer>();
            wheelContactRenderer.shadowCastingMode = ShadowCastingMode.Off;
            wheelContactRenderer.receiveShadows = true;
            CreateMeshObject("Painted Central Spine", root.transform, BuildCentralSpine(spline), sleeper);
            CreateMeshObject("Painted Y Brackets", root.transform, BuildBrackets(spline), sleeper);
            CreateMeshObject("Bolted Spine Couplers", root.transform, BuildCouplers(spline), support);
            ForestFloorSurface groundSurface = parent.Find("Blender Environment") != null
                ? ForestFloorSurface.Load() : null;
            CreateMeshObject("Tubular Ground Supports", root.transform, BuildSupports(spline, groundSurface), support);
            Material liftChainMaterial = CreateLiftChainMaterial(support);
            GameObject liftChain = CreateMeshObject(
                "Procedural Lift Chain 3D",
                root.transform,
                BuildLiftChain(spline),
                liftChainMaterial);
            liftChain.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            liftChain.AddComponent<LiftChainTextureAnimator>().Initialize(liftChainMaterial);
            CreateBreakawayTrack(parent, spline, rail, sleeper);
            return root;
        }

        private static Mesh BuildRunningRails(RideSpline spline)
        {
            const int pathSegments = PathSegments;
            const int ringSegments = 10;
            const float gauge = 0.66f;

            List<Vector3> vertices = new((pathSegments + 1) * ringSegments * 2);
            List<Vector3> normals = new(vertices.Capacity);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(pathSegments * ringSegments * 12);
            AddSplineTube(spline, pathSegments, ringSegments, -gauge, 0.08f, 0.072f, vertices, uvs, triangles, true, normals);
            AddSplineTube(spline, pathSegments, ringSegments, gauge, 0.08f, 0.072f, vertices, uvs, triangles, true, normals);
            return CreateTexturedMesh("Procedural Polished Running Rails", vertices, uvs, triangles, normals);
        }

        private static Mesh BuildWheelContactGrease(RideSpline spline)
        {
            const int pathSegments = PathSegments;
            const int bandSegments = 4;
            const float gauge = 0.66f;
            const float railVerticalOffset = 0.08f;
            const float railRadius = 0.072f;
            const int railRingSegments = 10;

            int verticesPerBand = (pathSegments + 1) * (bandSegments + 1);
            List<Vector3> vertices = new(verticesPerBand * 2);
            List<Vector3> normals = new(vertices.Capacity);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(pathSegments * bandSegments * 12);

            AddWheelContactBand(-gauge);
            AddWheelContactBand(gauge);
            return CreateTexturedMesh("Procedural Wheel Contact Grease Bands", vertices, uvs, triangles, normals);

            void AddWheelContactBand(float lateralOffset)
            {
                int vertexOffset = vertices.Count;
                int rowWidth = bandSegments + 1;
                for (int path = 0; path <= pathSegments; path++)
                {
                    float distance = spline.Length * path / pathSegments;
                    RidePose pose = spline.PoseAtDistance(distance);
                    Vector3 right = pose.Rotation * Vector3.right;
                    Vector3 up = pose.Rotation * Vector3.up;
                    Vector3 center = pose.Position + right * lateralOffset + up * railVerticalOffset;
                    float seed = lateralOffset < 0f ? 13.7f : 37.2f;
                    float centerVariation = (Mathf.PerlinNoise(distance * 0.27f, seed) - 0.5f) * 0.09f;
                    float halfAngle = Mathf.Lerp(0.14f, 0.30f, Mathf.PerlinNoise(distance * 0.63f, seed + 8f));
                    halfAngle = Mathf.Min(halfAngle, 0.30f - Mathf.Abs(centerVariation));

                    for (int band = 0; band <= bandSegments; band++)
                    {
                        float cross = band / (float)bandSegments;
                        float angle = Mathf.PI * 0.5f
                            + centerVariation
                            + Mathf.Lerp(-halfAngle, halfAngle, cross);
                        // Follow the actual flat tube face; a circular overlay floats above its ten-sided mesh.
                        float ring = angle / (Mathf.PI * 2f) * railRingSegments;
                        float firstAngle = Mathf.Floor(ring) * Mathf.PI * 2f / railRingSegments;
                        float nextAngle = firstAngle + Mathf.PI * 2f / railRingSegments;
                        Vector3 first = right * Mathf.Cos(firstAngle) + up * Mathf.Sin(firstAngle);
                        Vector3 next = right * Mathf.Cos(nextAngle) + up * Mathf.Sin(nextAngle);
                        Vector3 radial = Vector3.Lerp(first, next, ring - Mathf.Floor(ring));
                        vertices.Add(center + radial * railRadius + up * 0.0004f);
                        normals.Add(radial.normalized);
                        uvs.Add(new Vector2(distance * 0.23f + seed, Mathf.Lerp(0.17f, 0.83f, cross)));
                    }
                }

                for (int path = 0; path < pathSegments; path++)
                {
                    float midpointProgress = (path + 0.5f) / pathSegments;
                    if (midpointProgress >= BreakStartProgress && midpointProgress <= BreakEndProgress)
                    {
                        continue;
                    }

                    for (int band = 0; band < bandSegments; band++)
                    {
                        int a = vertexOffset + path * rowWidth + band;
                        int b = a + 1;
                        int c = vertexOffset + (path + 1) * rowWidth + band;
                        int d = c + 1;
                        triangles.Add(a); triangles.Add(b); triangles.Add(c);
                        triangles.Add(b); triangles.Add(d); triangles.Add(c);
                    }
                }
            }
        }

        private static Mesh BuildCentralSpine(RideSpline spline)
        {
            const int pathSegments = PathSegments;
            List<Vector3> vertices = new((pathSegments + 1) * 12);
            List<Vector3> normals = new(vertices.Capacity);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new(pathSegments * 72);
            AddSplineTube(spline, pathSegments, 12, 0f, -0.47f, 0.235f, vertices, uvs, triangles, true, normals);
            return CreateTexturedMesh("Procedural Painted Central Spine", vertices, uvs, triangles, normals);
        }

        private static Mesh BuildBrackets(RideSpline spline)
        {
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            for (float distance = 0f; distance < spline.Length; distance += 1.55f)
            {
                float progress = distance / spline.Length;
                if (progress >= BreakStartProgress && progress <= BreakEndProgress)
                {
                    continue;
                }

                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 up = pose.Rotation * Vector3.up;
                Vector3 spineJunction = pose.Position - up * 0.28f;
                Vector3 leftRail = pose.Position - right * 0.66f + up * 0.08f;
                Vector3 rightRail = pose.Position + right * 0.66f + up * 0.08f;
                Vector3 leftMount = leftRail - up * 0.11f;
                Vector3 rightMount = rightRail - up * 0.11f;

                AddTubeSegment(vertices, uvs, triangles, spineJunction, leftMount, 0.045f, 6);
                AddTubeSegment(vertices, uvs, triangles, spineJunction, rightMount, 0.045f, 6);
                AddTubeSegment(vertices, uvs, triangles, leftMount, rightMount, 0.032f, 6);
                // Weld the brackets underneath; full collars would obstruct the wheel-contact surface.
            }

            return CreateTexturedMesh("Procedural Painted Y Brackets", vertices, uvs, triangles);
        }

        private static Mesh BuildCouplers(RideSpline spline)
        {
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            for (float distance = 8f; distance < spline.Length; distance += 15.5f)
            {
                float progress = distance / spline.Length;
                if (progress >= BreakStartProgress && progress <= BreakEndProgress)
                {
                    continue;
                }

                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 up = pose.Rotation * Vector3.up;
                Vector3 tangent = pose.Tangent;
                Vector3 center = pose.Position - up * 0.47f;
                AddTubeSegment(vertices, uvs, triangles, center - tangent * 0.095f, center + tangent * 0.095f, 0.265f, 12);

                for (int bolt = 0; bolt < 8; bolt++)
                {
                    float angle = bolt * Mathf.PI * 2f / 8f;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    AddTubeSegment(
                        vertices,
                        uvs,
                        triangles,
                        center + radial * 0.245f,
                        center + radial * 0.305f,
                        0.023f,
                        6);
                }
            }

            return CreateTexturedMesh("Procedural Bolted Spine Couplers", vertices, uvs, triangles);
        }

        private static Mesh BuildSupports(RideSpline spline, ForestFloorSurface groundSurface)
        {
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            for (float distance = 4f; distance < spline.Length; distance += 10.5f)
            {
                if (!spline.IsInversionAtDistance(distance)) AddSupport(distance, Vector3.zero, false);
            }
            foreach (float distance in spline.InversionSupportDistances())
            {
                Vector3 trackUp = spline.PoseAtDistance(distance).Rotation * Vector3.up;
                bool shoulder = spline.IsLoopAtDistance(distance) && Mathf.Abs(trackUp.y) < 0.3f;
                Vector3 outward = shoulder ? Vector3.ProjectOnPlane(-trackUp, Vector3.up).normalized * 3.2f : Vector3.zero;
                float spread = shoulder ? 2.6f : 1.45f;
                AddSupport(distance, outward - spline.InversionRight * spread, true);
                AddSupport(distance, outward + spline.InversionRight * spread, true);
            }

            return CreateTexturedMesh("Procedural Tubular Ground Supports", vertices, uvs, triangles);

            void AddSupport(float distance, Vector3 footprintOffset, bool inclined)
            {
                // Attach to the rendered spine segment, which is linear between spline samples.
                float samplePosition = distance / spline.Length * PathSegments;
                int segment = Mathf.FloorToInt(samplePosition);
                RidePose first = spline.PoseAtDistance(spline.Length * segment / PathSegments);
                RidePose next = spline.PoseAtDistance(spline.Length * (segment + 1) / PathSegments);
                float blend = samplePosition - segment;
                Quaternion rotation = Quaternion.Slerp(first.Rotation, next.Rotation, blend);
                Vector3 right = rotation * Vector3.right;
                Vector3 trackUp = rotation * Vector3.up;
                Vector3 spineCenter = Vector3.Lerp(first.Position - first.Rotation * Vector3.up * 0.47f,
                    next.Position - next.Rotation * Vector3.up * 0.47f, blend);
                Vector3 columnCenter = spineCenter + footprintOffset;
                float footRadius = inclined ? 0.42f : 0.23f;
                float ground = GroundHeight(columnCenter.x, columnCenter.z);
                float lowestGround = ground;
                float highestGround = ground;
                for (int sample = 0; sample < 16; sample++)
                {
                    float angle = sample * Mathf.PI * 2f / 16f;
                    float footGround = GroundHeight(columnCenter.x + Mathf.Cos(angle) * footRadius,
                        columnCenter.z + Mathf.Sin(angle) * footRadius);
                    lowestGround = Mathf.Min(lowestGround, footGround);
                    highestGround = Mathf.Max(highestGround, footGround);
                }
                Vector3 baseCenter = new(columnCenter.x, lowestGround - 0.12f, columnCenter.z);
                Vector3 footTop = new(columnCenter.x, highestGround + 0.10f, columnCenter.z);
                Vector3 supportTop = inclined ? spineCenter - trackUp * 0.04f : columnCenter - Vector3.up * 0.12f;
                if (supportTop.y - footTop.y < 0.55f)
                {
                    return;
                }

                AddTubeSegment(vertices, uvs, triangles, baseCenter, supportTop, inclined ? 0.17f : 0.13f, 8);
                AddTubeSegment(vertices, uvs, triangles, baseCenter, footTop, footRadius, 10, true);
                Vector3 braceAnchor = Vector3.Lerp(baseCenter, supportTop, inclined ? 0.86f : 0.64f);
                float mountOffset = inclined ? 0.12f : 0.155f;
                const float braceRadius = 0.055f;
                AddTubeSegment(vertices, uvs, triangles, braceAnchor, spineCenter - right * mountOffset - trackUp * 0.055f, braceRadius, 6);
                AddTubeSegment(vertices, uvs, triangles, braceAnchor, spineCenter + right * mountOffset - trackUp * 0.055f, braceRadius, 6);
            }

            float GroundHeight(float x, float z) => groundSurface != null
                && groundSurface.Sample(x, z, out Vector3 point, out _)
                ? point.y : ProceduralWorld.HeightAt(x, z);
        }

        private static Mesh BuildLiftChain(RideSpline spline)
        {
            const float linkSpacing = 0.36f;
            const float halfWidth = 0.17f;
            const float verticalOffset = 0.025f;
            float startDistance = spline.Length * RideMotionProfile.LiftStart;
            float endDistance = spline.Length * RideMotionProfile.CrestStart;
            List<Vector3> vertices = new();
            List<Vector2> uvs = new();
            List<int> triangles = new();
            int linkIndex = 0;

            for (float distance = startDistance; distance < endDistance; distance += linkSpacing)
            {
                float nextDistance = Mathf.Min(distance + linkSpacing, endDistance);
                RidePose pose = spline.PoseAtDistance(distance);
                RidePose nextPose = spline.PoseAtDistance(nextDistance);
                float progress = distance / spline.Length;
                float averageSlope = (pose.Tangent.y + nextPose.Tangent.y) * 0.5f;
                if (!RideMotionProfile.IsLiftChainActive(progress, averageSlope))
                {
                    continue;
                }

                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 nextRight = nextPose.Rotation * Vector3.right;
                Vector3 up = pose.Rotation * Vector3.up;
                Vector3 nextUp = nextPose.Rotation * Vector3.up;
                Vector3 center = pose.Position + up * verticalOffset;
                Vector3 nextCenter = nextPose.Position + nextUp * verticalOffset;

                AddTubeSegment(
                    vertices,
                    uvs,
                    triangles,
                    center - right * halfWidth,
                    nextCenter - nextRight * halfWidth,
                    0.028f,
                    6);
                AddTubeSegment(
                    vertices,
                    uvs,
                    triangles,
                    center + right * halfWidth,
                    nextCenter + nextRight * halfWidth,
                    0.028f,
                    6);
                AddTubeSegment(
                    vertices,
                    uvs,
                    triangles,
                    center - right * 0.235f,
                    center + right * 0.235f,
                    0.042f,
                    8);

                if (linkIndex % 2 == 0)
                {
                    AddTexturedBox(
                        vertices,
                        uvs,
                        triangles,
                        center + up * 0.065f,
                        pose.Rotation,
                        new Vector3(0.13f, 0.12f, 0.17f));
                }
                linkIndex++;
            }

            return CreateTexturedMesh("Procedural Lift Chain Roller Links", vertices, uvs, triangles);
        }

        private static void CreateBreakawayTrack(
            Transform parent,
            RideSpline spline,
            Material railMaterial,
            Material spineMaterial)
        {
            const int chunkCount = 6;
            GameObject root = new("T-Rex Breakaway Track Section");
            root.transform.SetParent(parent, false);
            List<Transform> fragments = new(chunkCount * 2);
            float startDistance = spline.Length * BreakStartProgress;
            float endDistance = spline.Length * BreakEndProgress;

            for (int chunk = 0; chunk < chunkCount; chunk++)
            {
                float chunkStart = Mathf.Lerp(startDistance, endDistance, chunk / (float)chunkCount);
                float chunkEnd = Mathf.Lerp(startDistance, endDistance, (chunk + 1f) / chunkCount);
                RidePose start = spline.PoseAtDistance(chunkStart);
                RidePose end = spline.PoseAtDistance(chunkEnd);
                RidePose middle = spline.PoseAtDistance((chunkStart + chunkEnd) * 0.5f);
                Vector3 pivot = middle.Position;
                Vector3 startRight = start.Rotation * Vector3.right;
                Vector3 endRight = end.Rotation * Vector3.right;
                Vector3 startUp = start.Rotation * Vector3.up;
                Vector3 endUp = end.Rotation * Vector3.up;

                List<Vector3> railVertices = new();
                List<Vector2> railUvs = new();
                List<int> railTriangles = new();
                AddTubeSegment(
                    railVertices,
                    railUvs,
                    railTriangles,
                    start.Position - startRight * 0.66f + startUp * 0.08f - pivot,
                    end.Position - endRight * 0.66f + endUp * 0.08f - pivot,
                    0.076f,
                    10);
                AddTubeSegment(
                    railVertices,
                    railUvs,
                    railTriangles,
                    start.Position + startRight * 0.66f + startUp * 0.08f - pivot,
                    end.Position + endRight * 0.66f + endUp * 0.08f - pivot,
                    0.076f,
                    10);
                GameObject railFragment = CreateMeshObject(
                    $"Breakaway Rails {chunk + 1:00}",
                    root.transform,
                    CreateTexturedMesh($"Breakaway Rails Mesh {chunk + 1:00}", railVertices, railUvs, railTriangles),
                    railMaterial);
                railFragment.transform.position = pivot;
                fragments.Add(railFragment.transform);

                List<Vector3> spineVertices = new();
                List<Vector2> spineUvs = new();
                List<int> spineTriangles = new();
                Vector3 spineStart = start.Position - startUp * 0.47f;
                Vector3 spineEnd = end.Position - endUp * 0.47f;
                AddTubeSegment(
                    spineVertices,
                    spineUvs,
                    spineTriangles,
                    spineStart - pivot,
                    spineEnd - pivot,
                    0.235f,
                    12);

                Vector3 middleRight = middle.Rotation * Vector3.right;
                Vector3 middleUp = middle.Rotation * Vector3.up;
                Vector3 junction = middle.Position - middleUp * 0.28f;
                Vector3 leftMount = middle.Position - middleRight * 0.66f - middleUp * 0.03f;
                Vector3 rightMount = middle.Position + middleRight * 0.66f - middleUp * 0.03f;
                AddTubeSegment(spineVertices, spineUvs, spineTriangles, junction - pivot, leftMount - pivot, 0.045f, 6);
                AddTubeSegment(spineVertices, spineUvs, spineTriangles, junction - pivot, rightMount - pivot, 0.045f, 6);
                AddTubeSegment(spineVertices, spineUvs, spineTriangles, leftMount - pivot, rightMount - pivot, 0.032f, 6);
                GameObject spineFragment = CreateMeshObject(
                    $"Breakaway Spine {chunk + 1:00}",
                    root.transform,
                    CreateTexturedMesh($"Breakaway Spine Mesh {chunk + 1:00}", spineVertices, spineUvs, spineTriangles),
                    spineMaterial);
                spineFragment.transform.position = pivot;
                fragments.Add(spineFragment.transform);
            }

            RidePose fracturePose = spline.PoseAtDistance((startDistance + endDistance) * 0.5f);
            root.AddComponent<TrackBreakSetpiece>().Initialize(fragments.ToArray(), fracturePose);
        }

        private static Material CreateLiftChainMaterial(Material source)
        {
            Material material = new(source)
            {
                name = "Lift Chain Dark Steel PBR",
                enableInstancing = true
            };
            Color steel = new(0.16f, 0.18f, 0.17f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", steel);
            if (material.HasProperty("_Color")) material.SetColor("_Color", steel);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.72f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.38f);
            return material;
        }

        private static Material CreateWheelContactMaterial(Material source)
        {
            Material material = new(source)
            {
                name = "Wheel Contact Grease PBR",
                enableInstancing = true,
                doubleSidedGI = true
            };
            Texture2D wear = Resources.Load<Texture2D>("Textures/Realistic/Track/TrackWheelWear_Albedo");
            Color oilyWear = new(0.58f, 0.56f, 0.51f, wear != null ? 0.76f : 0f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", oilyWear);
            if (material.HasProperty("_Color")) material.SetColor("_Color", oilyWear);
            if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", wear);
            if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", wear);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0.52f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.32f);
            if (material.HasProperty("_BumpScale")) material.SetFloat("_BumpScale", 0.12f);
            if (material.HasProperty("_Cull")) material.SetFloat("_Cull", 2f);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            material.SetShaderPassEnabled("ShadowCaster", false);
            material.SetShaderPassEnabled("DepthOnly", false);
            Vector2 streakScale = Vector2.one;
            if (material.HasProperty("_BaseMap")) material.SetTextureScale("_BaseMap", streakScale);
            if (material.HasProperty("_MainTex")) material.SetTextureScale("_MainTex", streakScale);
            if (material.HasProperty("_BumpMap")) material.SetTextureScale("_BumpMap", streakScale);
            return material;
        }

        private static void AddSplineTube(
            RideSpline spline,
            int pathSegments,
            int ringSegments,
            float lateralOffset,
            float verticalOffset,
            float radius,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            bool leaveBreakawayGap = false,
            List<Vector3> normals = null)
        {
            int vertexOffset = vertices.Count;
            int rowWidth = ringSegments + 1;
            for (int path = 0; path <= pathSegments; path++)
            {
                float distance = spline.Length * path / pathSegments;
                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = pose.Rotation * Vector3.right;
                Vector3 up = pose.Rotation * Vector3.up;
                Vector3 center = pose.Position + right * lateralOffset + up * verticalOffset;
                for (int ring = 0; ring <= ringSegments; ring++)
                {
                    // Duplicate the UV seam, with matching radial normals on both sides.
                    float angle = (ring % ringSegments) * Mathf.PI * 2f / ringSegments;
                    Vector3 radial = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                    vertices.Add(center + radial * radius);
                    normals?.Add(radial);
                    uvs.Add(new Vector2(ring / (float)ringSegments, distance * 0.34f));
                }
            }

            for (int path = 0; path < pathSegments; path++)
            {
                float midpointProgress = (path + 0.5f) / pathSegments;
                if (leaveBreakawayGap
                    && midpointProgress >= BreakStartProgress
                    && midpointProgress <= BreakEndProgress)
                {
                    continue;
                }

                for (int ring = 0; ring < ringSegments; ring++)
                {
                    int a = vertexOffset + path * rowWidth + ring;
                    int b = a + 1;
                    int c = a + rowWidth;
                    int d = c + 1;
                    triangles.Add(a); triangles.Add(b); triangles.Add(c);
                    triangles.Add(b); triangles.Add(d); triangles.Add(c);
                }
            }
        }

        private static void AddTubeSegment(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 start,
            Vector3 end,
            float radius,
            int ringSegments,
            bool capEnds = false)
        {
            Vector3 axis = end - start;
            float length = axis.magnitude;
            if (length < 0.0001f)
            {
                return;
            }

            axis /= length;
            Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f
                ? Vector3.right
                : Vector3.up;
            Vector3 right = Vector3.Cross(axis, reference).normalized;
            Vector3 up = Vector3.Cross(right, axis).normalized;
            int vertexOffset = vertices.Count;
            for (int endIndex = 0; endIndex < 2; endIndex++)
            {
                Vector3 center = endIndex == 0 ? start : end;
                for (int ring = 0; ring < ringSegments; ring++)
                {
                    float angle = ring * Mathf.PI * 2f / ringSegments;
                    vertices.Add(center + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
                    uvs.Add(new Vector2(ring / (float)ringSegments, endIndex * length * 0.34f));
                }
            }

            for (int ring = 0; ring < ringSegments; ring++)
            {
                int nextRing = (ring + 1) % ringSegments;
                int a = vertexOffset + ring;
                int b = vertexOffset + nextRing;
                int c = vertexOffset + ringSegments + ring;
                int d = vertexOffset + ringSegments + nextRing;
                triangles.Add(a); triangles.Add(c); triangles.Add(b);
                triangles.Add(b); triangles.Add(c); triangles.Add(d);
            }

            if (!capEnds) return;
            for (int endIndex = 0; endIndex < 2; endIndex++)
            {
                int capCenter = vertices.Count;
                vertices.Add(endIndex == 0 ? start : end);
                uvs.Add(new Vector2(0.5f, 0.5f));
                // Separate cap vertices keep the foot's top flat instead of rounding its rim normals.
                for (int ring = 0; ring < ringSegments; ring++)
                {
                    vertices.Add(vertices[vertexOffset + endIndex * ringSegments + ring]);
                    float angle = ring * Mathf.PI * 2f / ringSegments;
                    uvs.Add(new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f + Vector2.one * 0.5f);
                }
                for (int ring = 0; ring < ringSegments; ring++)
                {
                    int a = capCenter + 1 + ring;
                    int b = capCenter + 1 + (ring + 1) % ringSegments;
                    triangles.Add(capCenter);
                    triangles.Add(endIndex == 0 ? a : b);
                    triangles.Add(endIndex == 0 ? b : a);
                }
            }
        }

        private static Mesh CreateTexturedMesh(
            string name,
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            List<Vector3> normals = null)
        {
            Mesh mesh = NewMesh(name, vertices.Count);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            if (normals != null) mesh.SetNormals(normals);
            else mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        internal static void AddBox(List<Vector3> vertices, List<int> triangles, Vector3 center, Quaternion rotation, Vector3 size)
        {
            int start = vertices.Count;
            Vector3 half = size * 0.5f;
            Vector3[] corners =
            {
                new(-half.x, -half.y, -half.z), new(half.x, -half.y, -half.z),
                new(half.x, half.y, -half.z), new(-half.x, half.y, -half.z),
                new(-half.x, -half.y, half.z), new(half.x, -half.y, half.z),
                new(half.x, half.y, half.z), new(-half.x, half.y, half.z)
            };

            foreach (Vector3 corner in corners)
            {
                vertices.Add(center + rotation * corner);
            }

            int[] indices =
            {
                0, 2, 1, 0, 3, 2,
                4, 5, 6, 4, 6, 7,
                0, 1, 5, 0, 5, 4,
                2, 3, 7, 2, 7, 6,
                1, 2, 6, 1, 6, 5,
                3, 0, 4, 3, 4, 7
            };
            foreach (int index in indices)
            {
                triangles.Add(start + index);
            }
        }

        private static void AddTexturedBox(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            int firstVertex = vertices.Count;
            AddBox(vertices, triangles, center, rotation, size);
            for (int index = 0; index < 8; index++)
            {
                uvs.Add(new Vector2(index % 2, index / 4));
            }
            Debug.Assert(vertices.Count - firstVertex == 8);
        }

        internal static Mesh NewMesh(string name, int vertexCount)
        {
            Mesh mesh = new() { name = name };
            if (vertexCount > ushort.MaxValue)
            {
                mesh.indexFormat = IndexFormat.UInt32;
            }

            return mesh;
        }

        internal static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            GameObject gameObject = new(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            gameObject.AddComponent<MeshRenderer>().sharedMaterial = material;
            return gameObject;
        }
    }

    internal sealed class TrackBreakSetpiece : MonoBehaviour
    {
        private Transform[] _fragments = Array.Empty<Transform>();
        private Vector3[] _initialPositions = Array.Empty<Vector3>();
        private Quaternion[] _initialRotations = Array.Empty<Quaternion>();
        private Vector3[] _velocities = Array.Empty<Vector3>();
        private Vector3[] _angularVelocities = Array.Empty<Vector3>();
        private Vector3 _origin;
        private Vector3 _right;
        private Vector3 _up;
        private Vector3 _forward;
        private AudioSource _fractureAudio;
        private float _brokenAt;

        internal bool IsBroken { get; private set; }
        internal int FragmentCount => _fragments.Length;

        internal void Initialize(Transform[] fragments, RidePose fracturePose)
        {
            _fragments = fragments ?? Array.Empty<Transform>();
            _initialPositions = new Vector3[_fragments.Length];
            _initialRotations = new Quaternion[_fragments.Length];
            _velocities = new Vector3[_fragments.Length];
            _angularVelocities = new Vector3[_fragments.Length];
            _origin = fracturePose.Position;
            _right = fracturePose.Rotation * Vector3.right;
            _up = fracturePose.Rotation * Vector3.up;
            _forward = fracturePose.Tangent;
            for (int index = 0; index < _fragments.Length; index++)
            {
                if (_fragments[index] == null)
                {
                    continue;
                }

                _initialPositions[index] = _fragments[index].position;
                _initialRotations[index] = _fragments[index].rotation;
            }

            _fractureAudio = gameObject.AddComponent<AudioSource>();
            _fractureAudio.clip = CreateFractureClip();
            _fractureAudio.playOnAwake = false;
            _fractureAudio.loop = false;
            _fractureAudio.spatialBlend = 1f;
            _fractureAudio.rolloffMode = AudioRolloffMode.Logarithmic;
            _fractureAudio.minDistance = 4f;
            _fractureAudio.maxDistance = 70f;
            _fractureAudio.dopplerLevel = 0f;
            _fractureAudio.volume = 0.82f;
        }

        internal void Break()
        {
            if (IsBroken)
            {
                return;
            }

            IsBroken = true;
            _brokenAt = Time.time;
            if (_fractureAudio != null)
            {
                _fractureAudio.Play();
            }

            for (int index = 0; index < _fragments.Length; index++)
            {
                float side = index % 2 == 0 ? -1f : 1f;
                float variation = 0.7f + (index % 5) * 0.16f;
                _velocities[index] = _right * side * (2.4f + variation)
                    + _up * (1.1f + variation)
                    - _forward * (0.4f + (index % 3) * 0.35f);
                _angularVelocities[index] = new Vector3(
                    54f + index * 7f,
                    side * (82f + index * 4f),
                    37f + index * 9f);
            }

            EmitDust();
            Debug.Log($"[T-Rex Encounter] TRACK BREAK | fragments={FragmentCount}");
        }

        internal void ResetSection()
        {
            IsBroken = false;
            _brokenAt = 0f;
            if (_fractureAudio != null)
            {
                _fractureAudio.Stop();
            }

            for (int index = 0; index < _fragments.Length; index++)
            {
                Transform fragment = _fragments[index];
                if (fragment == null)
                {
                    continue;
                }

                fragment.SetPositionAndRotation(_initialPositions[index], _initialRotations[index]);
                fragment.gameObject.SetActive(true);
                _velocities[index] = Vector3.zero;
                _angularVelocities[index] = Vector3.zero;
            }
        }

        private void Update()
        {
            if (!IsBroken)
            {
                return;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            for (int index = 0; index < _fragments.Length; index++)
            {
                Transform fragment = _fragments[index];
                if (fragment == null || !fragment.gameObject.activeSelf)
                {
                    continue;
                }

                _velocities[index] += Physics.gravity * (0.82f * deltaTime);
                fragment.position += _velocities[index] * deltaTime;
                fragment.Rotate(_angularVelocities[index] * deltaTime, Space.World);
                if (Time.time - _brokenAt > 4.8f)
                {
                    fragment.gameObject.SetActive(false);
                }
            }
        }

        private void EmitDust()
        {
            GameObject dustObject = new("Track Break Dust");
            dustObject.transform.SetParent(transform, true);
            dustObject.transform.position = _origin - _up * 0.15f;
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color");
            if (shader == null)
            {
                Destroy(dustObject);
                return;
            }

            Material material = new(shader)
            {
                name = "Track Break Dust Material",
                renderQueue = (int)RenderQueue.Transparent
            };
            Color dustColor = new(0.46f, 0.35f, 0.21f, 0.58f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", dustColor);
            if (material.HasProperty("_Color")) material.SetColor("_Color", dustColor);
            if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            System.Random random = new(6629);
            for (int index = 0; index < 14; index++)
            {
                GameObject puff = GameObject.CreatePrimitive(PrimitiveType.Quad);
                puff.name = $"Dust Puff {index + 1:00}";
                puff.transform.SetParent(dustObject.transform, false);
                puff.transform.localPosition = new Vector3(
                    Mathf.Lerp(-1.15f, 1.15f, (float)random.NextDouble()),
                    Mathf.Lerp(-0.25f, 0.9f, (float)random.NextDouble()),
                    Mathf.Lerp(-1.15f, 1.15f, (float)random.NextDouble()));
                float size = Mathf.Lerp(0.55f, 1.35f, (float)random.NextDouble());
                puff.transform.localScale = Vector3.one * size;
                Destroy(puff.GetComponent<Collider>());
                puff.GetComponent<MeshRenderer>().sharedMaterial = material;
                Vector3 velocity = _right * Mathf.Lerp(-2.8f, 2.8f, (float)random.NextDouble())
                    + _up * Mathf.Lerp(0.8f, 2.7f, (float)random.NextDouble())
                    + _forward * Mathf.Lerp(-1.6f, 1f, (float)random.NextDouble());
                puff.AddComponent<DustPuffMotion>().Initialize(velocity, Mathf.Lerp(1.1f, 2.1f, (float)random.NextDouble()));
            }

            Destroy(material, 3f);
            Destroy(dustObject, 3f);
        }

        private static AudioClip CreateFractureClip()
        {
            const int sampleRate = 22050;
            const float duration = 1.15f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] samples = new float[sampleCount];
            System.Random random = new(7813);
            float low = 0f;
            for (int sample = 0; sample < sampleCount; sample++)
            {
                float time = sample / (float)sampleRate;
                float impact = Mathf.Exp(-time * 5.4f);
                float secondary = time > 0.25f ? Mathf.Exp(-(time - 0.25f) * 8.5f) * 0.55f : 0f;
                float noise = (float)(random.NextDouble() * 2.0 - 1.0);
                low += (noise - low) * 0.036f;
                float metal = Mathf.Sin(time * Mathf.PI * 2f * 84f) * Mathf.Exp(-time * 3.8f);
                samples[sample] = Mathf.Clamp((low * 0.92f + noise * 0.16f) * (impact + secondary) + metal * 0.25f, -0.9f, 0.9f);
            }

            AudioClip clip = AudioClip.Create("T-Rex Track Fracture", sampleCount, 1, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
    }

    internal sealed class DustPuffMotion : MonoBehaviour
    {
        private Vector3 _velocity;
        private float _lifetime;
        private float _age;
        private Vector3 _initialScale;

        internal void Initialize(Vector3 velocity, float lifetime)
        {
            _velocity = velocity;
            _lifetime = Mathf.Max(0.2f, lifetime);
            _initialScale = transform.localScale;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float normalized = Mathf.Clamp01(_age / _lifetime);
            transform.position += _velocity * Time.deltaTime;
            _velocity += Physics.gravity * (0.12f * Time.deltaTime);
            transform.localScale = _initialScale * Mathf.Lerp(0.55f, 2.4f, normalized);
            Camera camera = Camera.main;
            if (camera != null)
            {
                transform.rotation = Quaternion.LookRotation(transform.position - camera.transform.position, Vector3.up);
            }

            if (normalized >= 1f)
            {
                gameObject.SetActive(false);
            }
        }
    }

    internal sealed class LiftChainTextureAnimator : MonoBehaviour
    {
        private Material _material;

        public void Initialize(Material material)
        {
            _material = material;
        }

        private void Update()
        {
            if (_material == null)
            {
                return;
            }

            Vector2 offset = new(0f, -Mathf.Repeat(Time.time * 0.72f, 1f));
            if (_material.HasProperty("_BaseMap")) _material.SetTextureOffset("_BaseMap", offset);
            if (_material.HasProperty("_MainTex")) _material.SetTextureOffset("_MainTex", offset);
            if (_material.HasProperty("_BumpMap")) _material.SetTextureOffset("_BumpMap", offset);
        }
    }
}
