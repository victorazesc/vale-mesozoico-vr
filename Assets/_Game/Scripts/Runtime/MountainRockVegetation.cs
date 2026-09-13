using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    internal static class MountainRockVegetation
    {
        private const int PlantLimit = 65;
        private const int GroundcoverLimit = 180;
        private const int TriangleLimit = 192000;
        private const int RouteSamples = 256;
        private const float TrackClearance = 4.8f;
        private const string SourceResource = "Models/Environment/MountainFernSources";
        private const string SourceBakeId = "mountain-fern-sources-v1";
        internal static Vector3[] ForestFloorRoots { get; private set; } = Array.Empty<Vector3>();

        [Serializable]
        private sealed class SourceBake
        {
            public string bakeId;
            public BakedMesh[] sources;
        }

        [Serializable]
        private sealed class BakedMesh
        {
            public string templateId;
            public string meshName;
            public int vertexCount;
            public int triangleCount;
            public Vector3[] vertices;
        }

        private sealed class PlantSource
        {
            public Mesh Mesh;
            public Material[] Materials;
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public Vector3 InstanceScale;
            public Vector3[] Vertices;
            public Vector3 Foot;
            public float Height;
            public int Triangles;
        }

        public static void Build(Transform environment, Transform mountainRoot, Mesh mountainMesh, RideSpline spline)
        {
            if (environment == null || mountainRoot == null || mountainMesh == null || spline == null)
            {
                return;
            }

            TextAsset sourceAsset = Resources.Load<TextAsset>(SourceResource);
            SourceBake bake = sourceAsset != null ? JsonUtility.FromJson<SourceBake>(sourceAsset.text) : null;
            if (bake == null || bake.bakeId != SourceBakeId || bake.sources == null)
            {
                Debug.LogWarning("[Mountain Vegetation] Execute Bake Mountain Fern Sources no Editor para atualizar as fontes.");
                return;
            }

            List<PlantSource> sources = new();
            AddSource(environment, "PC_Foreground_Undergrowth_006", "VM_ENV_021", bake, sources);
            AddSource(environment, "PC_Foreground_Undergrowth_018", "VM_ENV_029", bake, sources);
            if (sources.Count == 0)
            {
                AddSource(environment, "PC_REFINE_ShorePlant_047", "VM_ENV_021", bake, sources);
            }
            if (sources.Count == 0)
            {
                Debug.LogWarning("[Mountain Vegetation] Samambaias de origem indisponíveis.");
                return;
            }

            // Textured, spreading fronds form a low canopy. The untextured
            // meadow blade mesh read as rigid bristles when repeated on the crown.
            PlantSource groundcoverSource = sources[0];
            Vector3[] route = new Vector3[RouteSamples + 1];
            for (int index = 0; index <= RouteSamples; index++)
            {
                route[index] = spline.PoseAtDistance(spline.Length * index / RouteSamples).Position;
            }

            GameObject probeObject = new("Mountain Vegetation Surface Probe");
            probeObject.hideFlags = HideFlags.HideAndDontSave;
            probeObject.transform.SetParent(mountainRoot, false);
            MeshCollider surface = probeObject.AddComponent<MeshCollider>();
            surface.sharedMesh = mountainMesh;
            GameObject plants = new("Mountain Ledge Ferns");
            plants.transform.SetParent(mountainRoot, false);
            GameObject groundcover = new("Mountain Groundcover Patches");
            groundcover.transform.SetParent(mountainRoot, false);
            List<Matrix4x4[]> groundcoverPatches = new();
            Dictionary<Vector2Int, List<Matrix4x4>> groundcoverCells = new();
            int placed = 0;
            int groundcoverPlaced = 0;
            int foregroundGroundcoverPlaced = 0;
            int fernTrackRejected = 0;
            int fernSupportRejected = 0;
            int triangles = 0;
            try
            {
                Physics.SyncTransforms();
                Bounds mountainBounds = surface.bounds;
                System.Random random = new(52719);
                List<Vector3> roots = new();
                List<Vector3> groundcoverRoots = new();
                List<Vector3> patchRoots = new();
                List<Vector3> anchors = PlantingAnchors(mountainMesh, mountainRoot, mountainBounds,
                    out List<Vector3> foregroundAnchors, out HashSet<int> exteriorTriangles);
                // Spend the first patches on the actual exterior crown and the
                // lake-facing shelves, not the empty bounding box or cave floor.
                for (int cluster = 0; cluster < 1400 && anchors.Count > 0; cluster++)
                {
                    if (groundcoverPlaced >= GroundcoverLimit && placed >= PlantLimit)
                    {
                        break;
                    }
                    List<Vector3> plantingArea = cluster % 3 == 0 && foregroundAnchors.Count > 0
                        ? foregroundAnchors : anchors;
                    Vector3 center = plantingArea[random.Next(plantingArea.Count)];
                    center.y = mountainBounds.max.y + 3f;
                    if (!SurfaceHit(surface, center, mountainBounds, exteriorTriangles, out RaycastHit centerHit))
                    {
                        continue;
                    }

                    // Establish overlapping growth across the mountain before
                    // spending the remaining budget filling gaps in planted areas.
                    if (cluster < 520 && TooCloseToPlant(centerHit.point, patchRoots, 1.65f))
                    {
                        continue;
                    }
                    patchRoots.Add(centerHit.point);

                    // Broad, low fronds overlap into irregular mats. Larger ferns
                    // break the silhouette, with every planted leaf outside the
                    // vehicle corridor and roots supported by the rock surface.
                    float patchRadius = Mathf.Lerp(1.35f, 2.8f, Next(random));
                    int groundcoverCount = random.Next(4, 9);
                    float patchHeight = Mathf.Lerp(0.32f, 0.52f, Next(random));
                    for (int member = 0; groundcoverSource != null && member < groundcoverCount
                        && groundcoverPlaced < GroundcoverLimit && triangles + groundcoverSource.Triangles <= TriangleLimit; member++)
                    {
                        float angle = Next(random) * Mathf.PI * 2f;
                        float radius = Mathf.Sqrt(Next(random)) * patchRadius;
                        Vector3 candidate = centerHit.point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                        candidate.y = mountainBounds.max.y + 3f;
                        if (!SurfaceHit(surface, candidate, mountainBounds, exteriorTriangles, out RaycastHit hit)
                            || TooCloseToPlant(hit.point, groundcoverRoots, 0.55f))
                        {
                            continue;
                        }
                        float heightVariation = Mathf.Lerp(0.78f, 1.18f,
                            Mathf.PerlinNoise(hit.point.x * 0.71f, hit.point.z * 0.71f));
                        float height = Mathf.Clamp(patchHeight * heightVariation
                            * Mathf.Lerp(0.78f, 1.2f, Next(random)), 0.26f, 0.62f);
                        Vector3 scale = groundcoverSource.InstanceScale * (height / groundcoverSource.Height);
                        scale.x *= Mathf.Lerp(1.3f, 1.9f, Next(random));
                        scale.z *= Mathf.Lerp(1.3f, 1.9f, Next(random));
                        Quaternion rotation = Quaternion.Slerp(Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, hit.normal), 0.18f)
                            * Quaternion.Euler(0f, Next(random) * 360f, 0f);
                        Vector3 position = hit.point - Vector3.up * 0.07f;
                        if (!ClearsTrack(BoundsAt(groundcoverSource, position, rotation, scale), route, 2.5f)
                            || !FeetSupported(groundcoverSource, position, rotation, scale, surface, mountainBounds))
                        {
                            continue;
                        }
                        Vector2Int cell = new(Mathf.FloorToInt(hit.point.x / 6f), Mathf.FloorToInt(hit.point.z / 6f));
                        if (!groundcoverCells.TryGetValue(cell, out List<Matrix4x4> cellMatrices))
                        {
                            cellMatrices = new List<Matrix4x4>();
                            groundcoverCells.Add(cell, cellMatrices);
                        }
                        cellMatrices.Add(Matrix4x4.TRS(position, rotation, scale)
                            * Matrix4x4.TRS(groundcoverSource.Position - groundcoverSource.Foot, groundcoverSource.Rotation, groundcoverSource.Scale));
                        groundcoverRoots.Add(hit.point);
                        groundcoverPlaced++;
                        if (hit.point.x > mountainBounds.center.x
                            && hit.point.z < Mathf.Lerp(mountainBounds.min.z, mountainBounds.max.z, 0.62f))
                        {
                            foregroundGroundcoverPlaced++;
                        }
                        triangles += groundcoverSource.Triangles;
                    }
                    int clusterSize = random.Next(1, 3);
                    for (int member = 0; member < clusterSize && placed < PlantLimit; member++)
                    {
                        float angle = Next(random) * Mathf.PI * 2f;
                        float radius = member == 0 ? 0f : Mathf.Lerp(0.45f, 2.4f, Next(random));
                        Vector3 candidate = centerHit.point + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                        candidate.y = mountainBounds.max.y + 3f;
                        if (!SurfaceHit(surface, candidate, mountainBounds, exteriorTriangles, out RaycastHit hit)
                            || TooCloseToPlant(hit.point, roots))
                        {
                            continue;
                        }

                        PlantSource source = sources[random.Next(sources.Count)];
                        if (triangles + source.Triangles > TriangleLimit)
                        {
                            continue;
                        }

                        float height = Mathf.Lerp(0.8f, 1.45f, Next(random));
                        Vector3 scale = source.InstanceScale * (height / source.Height);
                        Quaternion rotation = Quaternion.Slerp(Quaternion.identity,
                            Quaternion.FromToRotation(Vector3.up, hit.normal), 0.15f)
                            * Quaternion.Euler(0f, Next(random) * 360f, 0f);
                        Vector3 position = hit.point - Vector3.up * 0.14f;
                        Bounds plantBounds = BoundsAt(source, position, rotation, scale);
                        if (!ClearsTrack(plantBounds, route))
                        {
                            fernTrackRejected++;
                            continue;
                        }
                        if (!FeetSupported(source, position, rotation, scale, surface, mountainBounds))
                        {
                            fernSupportRejected++;
                            continue;
                        }

                        GameObject plant = new("Mountain Ledge Fern " + placed.ToString("D2"));
                        plant.transform.SetParent(plants.transform, false);
                        plant.transform.SetPositionAndRotation(position, rotation);
                        plant.transform.localScale = scale;
                        GameObject geometry = new("Geometry");
                        geometry.transform.SetParent(plant.transform, false);
                        geometry.transform.SetLocalPositionAndRotation(source.Position - source.Foot, source.Rotation);
                        geometry.transform.localScale = source.Scale;
                        geometry.AddComponent<MeshFilter>().sharedMesh = source.Mesh;
                        geometry.AddComponent<MeshRenderer>().sharedMaterials = source.Materials;
                        // The parent origin is the measured foot, so sway cannot lift the root.
                        plant.AddComponent<TropicalWindSway>().Initialize(Next(random) * 12f, 0.48f, 2.4f);
                        roots.Add(hit.point);
                        triangles += source.Triangles;
                        placed++;
                    }
                }
            }
            finally
            {
                probeObject.SetActive(false);
                UnityEngine.Object.Destroy(probeObject);
            }

            // Spatially merge the growth patches so denser cover does not mean
            // hundreds of extra draw submissions; each cell still culls separately.
            foreach (List<Matrix4x4> cell in groundcoverCells.Values)
            {
                for (int offset = 0; offset < cell.Count; offset += 512)
                {
                    groundcoverPatches.Add(cell.GetRange(offset, Mathf.Min(512, cell.Count - offset)).ToArray());
                }
            }
            if (groundcoverSource != null && groundcoverPatches.Count > 0)
            {
                groundcover.AddComponent<MountainGroundcoverPatchRenderer>().Initialize(
                    groundcoverSource.Mesh, groundcoverSource.Materials, groundcoverPatches.ToArray());
            }
            Debug.Log($"[Mountain Vegetation] Samambaias apoiadas={placed}, groundcoverTufts={groundcoverPlaced}, "
                + $"foregroundGroundcover={foregroundGroundcoverPlaced}, groundcoverPatches={groundcoverPatches.Count}, "
                + $"triangles={triangles}/{TriangleLimit}, trackClearance={TrackClearance:F1}m, "
                + $"fernRejectedTrack={fernTrackRejected}, fernRejectedRoots={fernSupportRejected}");
        }

        public static void BuildForestFloor(Transform environment, RideSpline spline)
        {
            ForestFloorRoots = Array.Empty<Vector3>();
            ForestFloorSurface surface = ForestFloorSurface.Load();
            TextAsset asset = Resources.Load<TextAsset>(SourceResource);
            SourceBake bake = asset != null ? JsonUtility.FromJson<SourceBake>(asset.text) : null;
            if (surface == null || bake == null || bake.bakeId != SourceBakeId || bake.sources == null)
            {
                Debug.LogWarning("[Forest Floor] Bake do terreno ou das plantas indisponível.");
                return;
            }
            List<PlantSource> sources = new();
            AddSource(environment, "PC_Foreground_Undergrowth_006", "VM_ENV_021", bake, sources);
            AddSource(environment, "PC_Foreground_Undergrowth_018", "VM_ENV_029", bake, sources);
            if (sources.Count == 0) return;
            int fernSources = sources.Count;
            int broadleafIndex = sources.Count;
            AddSource(environment, "PC_Foreground_Undergrowth_027", "VM_ENV_031", bake, sources);
            if (sources.Count == broadleafIndex) broadleafIndex = -1;
            // Keep the approved mountain materials independent from the darker
            // understory; broad, stretched pale ferns looked pasted onto the soil.
            for (int index = 0; index < fernSources; index++)
            {
                Material[] originals = sources[index].Materials;
                Material[] materials = new Material[originals.Length];
                for (int slot = 0; slot < originals.Length; slot++)
                {
                    materials[slot] = new Material(originals[slot]) { name = "Forest Understory Fern", enableInstancing = true };
                    Color color = originals[slot].GetColor("_BaseColor");
                    materials[slot].SetColor("_BaseColor", color * new Color(0.72f, 0.85f, 0.73f, 1f));
                }
                sources[index].Materials = materials;
            }
            int creeperIndex = -1;
            if (ForestFloorCreeperMesh.Create(out Mesh creeperMesh, out Material creeperMaterial,
                out Vector3[] creeperVertices, out Vector3 creeperFoot, out float creeperHeight))
            {
                creeperIndex = sources.Count;
                sources.Add(new PlantSource
                {
                    Mesh = creeperMesh, Materials = new[] { creeperMaterial }, Vertices = creeperVertices,
                    Foot = creeperFoot, Height = creeperHeight, Position = Vector3.zero,
                    Rotation = Quaternion.identity, Scale = Vector3.one, InstanceScale = Vector3.one,
                    Triangles = (int)creeperMesh.GetIndexCount(0) / 3
                });
            }
            int grassIndex = -1;
            if (ForestFloorCreeperMesh.CreateGrass(out Mesh grassMesh, out Material grassMaterial,
                out Vector3[] grassVertices, out Vector3 grassFoot, out float grassHeight))
            {
                grassIndex = sources.Count;
                sources.Add(new PlantSource
                {
                    Mesh = grassMesh, Materials = new[] { grassMaterial }, Vertices = grassVertices,
                    Foot = grassFoot, Height = grassHeight, Position = Vector3.zero,
                    Rotation = Quaternion.identity, Scale = Vector3.one, InstanceScale = Vector3.one,
                    Triangles = (int)grassMesh.GetIndexCount(0) / 3
                });
            }

            // These are shared mesh instances across the whole route. Only nearby
            // cells render; a global limit suitable for a single view left most
            // of the forest without its ground layer.
            const int triangleBudget = 5800000;
            const int plantLimit = 22000;
            Vector3[] route = new Vector3[RouteSamples + 1];
            for (int index = 0; index <= RouteSamples; index++)
                route[index] = spline.PoseAtDistance(spline.Length * index / RouteSamples).Position;

            List<Vector3> centers = new();
            List<Vector3> backgroundCenters = new();
            List<Renderer> oldGrass = new();
            List<Bounds> rocks = new();
            List<Bounds> water = new();
            foreach (MeshRenderer renderer in environment.GetComponentsInChildren<MeshRenderer>())
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material == null) continue;
                    string name = material.name;
                    if (name == "PC_Jurassic_Meadow_Grass_PBR")
                    {
                        backgroundCenters.Add(renderer.bounds.center);
                        oldGrass.Add(renderer);
                        break;
                    }
                    if (name == "PC_Boulder_LOD0" || name == "PC_Mountainside_LOD0"
                        || name == "PC_PolyHaven_RockMossSet01_CC0" || name == "Mountain Cave Rock PBR")
                    {
                        rocks.Add(renderer.bounds);
                        break;
                    }
                    if (name.StartsWith("M_Lagoon_Water", StringComparison.Ordinal)
                        || name.StartsWith("PC_Lagoon_Shallows_PBR", StringComparison.Ordinal))
                    {
                        water.Add(renderer.bounds);
                        break;
                    }
                }
            }

            // Add irregular patches near every ground-level section of the ride,
            // so the closest surfaces have real leaves with parallax and shadows.
            System.Random random = new(90127);
            for (float distance = 0f; distance < spline.Length; distance += 2.8f)
            {
                RidePose pose = spline.PoseAtDistance(distance);
                Vector3 right = Vector3.Cross(Vector3.up, pose.Tangent).normalized;
                for (int side = -1; side <= 1; side += 2)
                {
                    for (int band = 0; band < 3; band++)
                        centers.Add(pose.Position + right * side * (3.2f + band * 3.1f
                            + Mathf.Lerp(-0.8f, 0.8f, Next(random))));
                }
            }
            for (int index = centers.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (centers[index], centers[other]) = (centers[other], centers[index]);
            }
            centers.AddRange(backgroundCenters);

            Dictionary<(int source, int x, int z), List<Matrix4x4>> cells = new();
            List<Vector3> roots = new();
            Dictionary<Vector2Int, List<Vector3>> rootCells = new();
            int triangles = 0;
            int rejectedGround = 0;
            int rejectedClearance = 0;
            int[] plantedBySource = new int[sources.Count];
            foreach (Vector3 center in centers)
            {
                if (roots.Count >= plantLimit || triangles >= triangleBudget) break;
                int members = random.Next(28, 45);
                float patchRadius = Mathf.Lerp(1.3f, 2.2f, Next(random));
                for (int member = 0; member < members && roots.Count < plantLimit; member++)
                {
                    float angle = Next(random) * Mathf.PI * 2f;
                    float radius = member == 0 ? 0f : Mathf.Sqrt(Next(random)) * patchRadius;
                    Vector3 candidate = center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                    if (!surface.Sample(candidate.x, candidate.z, out Vector3 point, out Vector3 normal)
                        || normal.y < 0.64f || HasNearbyRoot(point))
                        continue;
                    float routeDistance = float.PositiveInfinity;
                    for (int index = 1; index < route.Length; index++)
                    {
                        Vector3 segment = route[index] - route[index - 1];
                        float t = Mathf.Clamp01(Vector3.Dot(point - route[index - 1], segment)
                            / Mathf.Max(0.0001f, segment.sqrMagnitude));
                        routeDistance = Mathf.Min(routeDistance,
                            (point - route[index - 1] - segment * t).sqrMagnitude);
                    }
                    if (routeDistance > 14f * 14f || IsUnderwater(point)) continue;
                    float species = Next(random);
                    int sourceIndex = grassIndex >= 0 && species < 0.90f ? grassIndex
                        : creeperIndex >= 0 && species >= 0.90f && species < 0.98f ? creeperIndex
                        : broadleafIndex >= 0 && species >= 0.98f && species < 0.99f
                            ? broadleafIndex : random.Next(fernSources);
                    if (grassIndex >= 0 && sourceIndex != grassIndex
                        && plantedBySource[sourceIndex] >= (sourceIndex == creeperIndex ? 1800
                            : sourceIndex == broadleafIndex ? 170 : 60))
                        sourceIndex = grassIndex;
                    PlantSource source = sources[sourceIndex];
                    if (triangles + source.Triangles > triangleBudget) continue;
                    bool grass = sourceIndex == grassIndex;
                    bool lowCover = sourceIndex == creeperIndex || grass;
                    bool broadleaf = sourceIndex == broadleafIndex;
                    float height = grass ? Mathf.Lerp(0.22f, 0.38f, Next(random))
                        : lowCover ? Mathf.Lerp(0.17f, 0.27f, Next(random))
                        : broadleaf ? Mathf.Lerp(0.42f, 0.85f, Next(random))
                        : Mathf.Lerp(0.42f, 0.76f, Next(random));
                    Vector3 scale = source.InstanceScale * (height / source.Height);
                    scale.x *= grass ? Mathf.Lerp(1.1f, 1.5f, Next(random)) : Mathf.Lerp(0.85f, 1.12f, Next(random));
                    scale.z *= grass ? Mathf.Lerp(1.1f, 1.5f, Next(random)) : Mathf.Lerp(0.85f, 1.12f, Next(random));
                    Quaternion rotation = Quaternion.Slerp(Quaternion.identity,
                        Quaternion.FromToRotation(Vector3.up, normal), lowCover ? 0.85f : 0.3f)
                        * Quaternion.Euler(0f, Next(random) * 360f, 0f);
                    Vector3 position = point - Vector3.up * 0.06f;
                    Bounds bounds = BoundsAt(source, position, rotation, scale);
                    if (!ClearsTrack(bounds, route, lowCover ? 0.45f : 1f) || IntersectsRock(bounds))
                    {
                        rejectedClearance++;
                        continue;
                    }
                    if (!RootSupported(source, position, rotation, scale))
                    {
                        rejectedGround++;
                        continue;
                    }
                    var key = (sourceIndex, Mathf.FloorToInt(point.x / 8f), Mathf.FloorToInt(point.z / 8f));
                    if (!cells.TryGetValue(key, out List<Matrix4x4> instances))
                    {
                        instances = new List<Matrix4x4>();
                        cells.Add(key, instances);
                    }
                    instances.Add(Matrix4x4.TRS(position, rotation, scale)
                        * Matrix4x4.TRS(source.Position - source.Foot, source.Rotation, source.Scale));
                    roots.Add(point);
                    Vector2Int rootCell = new(Mathf.FloorToInt(point.x / 0.3f), Mathf.FloorToInt(point.z / 0.3f));
                    if (!rootCells.TryGetValue(rootCell, out List<Vector3> neighbors))
                    {
                        neighbors = new List<Vector3>();
                        rootCells.Add(rootCell, neighbors);
                    }
                    neighbors.Add(point);
                    triangles += source.Triangles;
                    plantedBySource[sourceIndex]++;
                }
            }
            if (roots.Count == 0) return;
            ForestFloorRoots = roots.ToArray();
            foreach (Renderer renderer in oldGrass) renderer.enabled = false;
            GameObject root = new("Forest Floor Undergrowth");
            root.transform.SetParent(environment, false);
            for (int index = 0; index < sources.Count; index++)
            {
                List<Matrix4x4[]> patches = new();
                foreach (var pair in cells)
                {
                    if (pair.Key.source != index) continue;
                    for (int offset = 0; offset < pair.Value.Count; offset += 512)
                        patches.Add(pair.Value.GetRange(offset, Mathf.Min(512, pair.Value.Count - offset)).ToArray());
                }
                GameObject patchRoot = new("Forest Floor Plant Patches " + index);
                patchRoot.transform.SetParent(root.transform, false);
                patchRoot.AddComponent<MountainGroundcoverPatchRenderer>().Initialize(
                    sources[index].Mesh, sources[index].Materials, patches.ToArray(),
                    index == grassIndex ? 28f : index == creeperIndex ? 34f : 52f, index != grassIndex);
            }
            Debug.Log($"[Forest Floor] PASS plants={roots.Count}, triangles={triangles}/{triangleBudget}, "
                + $"cells={cells.Count}, replacedBladeTufts={oldGrass.Count}, rejectedGround={rejectedGround}, "
                + $"rejectedClearance={rejectedClearance}, routeMargin=0.45m/1m, solidColliders=0, "
                + $"speciesCounts={string.Join(",", plantedBySource)}");

            bool HasNearbyRoot(Vector3 point)
            {
                Vector2Int cell = new(Mathf.FloorToInt(point.x / 0.3f), Mathf.FloorToInt(point.z / 0.3f));
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                        if (rootCells.TryGetValue(cell + new Vector2Int(x, z), out List<Vector3> neighbors))
                            foreach (Vector3 neighbor in neighbors)
                                if ((point - neighbor).sqrMagnitude < 0.25f * 0.25f) return true;
                return false;
            }

            bool IsUnderwater(Vector3 point)
            {
                foreach (Bounds bounds in water)
                    if (point.x >= bounds.min.x && point.x <= bounds.max.x
                        && point.z >= bounds.min.z && point.z <= bounds.max.z && point.y < bounds.max.y + 0.12f)
                        return true;
                return false;
            }
            bool IntersectsRock(Bounds bounds)
            {
                foreach (Bounds rock in rocks) if (bounds.Intersects(rock)) return true;
                return false;
            }
            bool RootSupported(PlantSource source, Vector3 position, Quaternion rotation, Vector3 scale)
            {
                float height = source.Height * scale.y / source.InstanceScale.y;
                float radius = Mathf.Clamp(height * 0.18f, 0.06f, 0.18f);
                int checkedFeet = 0;
                foreach (Vector3 vertex in source.Vertices)
                {
                    Vector3 offset = Vector3.Scale(vertex - source.Foot, scale);
                    if (offset.y > height * 0.07f || offset.x * offset.x + offset.z * offset.z > radius * radius)
                        continue;
                    Vector3 foot = position + rotation * offset;
                    if (!surface.Sample(foot.x, foot.z, out Vector3 ground, out _)
                        || foot.y - ground.y > 0.06f || ground.y - foot.y > 0.25f) return false;
                    checkedFeet++;
                }
                return checkedFeet > 0;
            }
        }

        private static void AddSource(Transform environment, string name, string templateId,
            SourceBake bake, List<PlantSource> sources)
        {
            Transform instance = environment.Find(name);
            Transform geometry = instance != null ? instance.Find("Geometry") : null;
            if (geometry == null || !geometry.TryGetComponent(out MeshFilter filter)
                || filter.sharedMesh == null
                || !geometry.TryGetComponent(out MeshRenderer renderer))
            {
                return;
            }

            Mesh mesh = filter.sharedMesh;
            BakedMesh baked = Array.Find(bake.sources, source => source != null && source.templateId == templateId);
            int triangleCount = 0;
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                triangleCount += (int)mesh.GetIndexCount(submesh) / 3;
            }
            if (baked == null || baked.meshName != mesh.name || baked.vertexCount != mesh.vertexCount
                || baked.vertices == null || baked.vertices.Length != mesh.vertexCount || mesh.vertexCount == 0
                || baked.triangleCount != triangleCount)
            {
                Debug.LogWarning("[Mountain Vegetation] Bake desatualizado para " + templateId + "; regenere Mountain Fern Sources.");
                return;
            }
            // Only two small vertex sets are retained on the CPU. The imported
            // environment stays non-readable on Quest and keeps shared GPU meshes.
            Vector3[] vertices = (Vector3[])baked.vertices.Clone();
            Matrix4x4 geometryMatrix = Matrix4x4.TRS(geometry.localPosition, geometry.localRotation, geometry.localScale);
            float minY = float.PositiveInfinity;
            float maxY = float.NegativeInfinity;
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index] = geometryMatrix.MultiplyPoint3x4(vertices[index]);
                float y = vertices[index].y * instance.localScale.y;
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
            }
            float height = maxY - minY;
            if (height < 0.001f || instance.localScale.y <= 0f)
            {
                return;
            }

            Vector3 foot = Vector3.zero;
            int footCount = 0;
            foreach (Vector3 vertex in vertices)
            {
                if (vertex.y * instance.localScale.y <= minY + height * 0.025f)
                {
                    foot += vertex;
                    footCount++;
                }
            }
            foot /= footCount;
            foot.y = minY / instance.localScale.y;
            sources.Add(new PlantSource
            {
                Mesh = mesh,
                Materials = renderer.sharedMaterials,
                Position = geometry.localPosition,
                Rotation = geometry.localRotation,
                Scale = geometry.localScale,
                InstanceScale = instance.localScale,
                Vertices = vertices,
                Foot = foot,
                Height = height,
                Triangles = triangleCount
            });
        }

        private static List<Vector3> PlantingAnchors(Mesh mountainMesh, Transform mountainRoot,
            Bounds mountainBounds, out List<Vector3> foreground, out HashSet<int> exteriorTriangles)
        {
            Vector3[] vertices = mountainMesh.vertices;
            int[] triangles = mountainMesh.triangles;
            Color[] colors = mountainMesh.colors;
            List<Vector3> anchors = new();
            foreground = new List<Vector3>();
            exteriorTriangles = new HashSet<int>();
            bool hasGrowthMask = colors.Length == vertices.Length;
            float crownHeight = Mathf.Lerp(mountainBounds.min.y, mountainBounds.max.y, 0.63f);
            for (int triangle = 0; triangle < triangles.Length; triangle += 3)
            {
                int ia = triangles[triangle];
                int ib = triangles[triangle + 1];
                int ic = triangles[triangle + 2];
                // The existing mountain alpha stores exterior vegetation coverage;
                // tunnel faces have zero, including the deceptively level floor.
                if (hasGrowthMask && (colors[ia].a + colors[ib].a + colors[ic].a) / 3f < 0.12f)
                {
                    continue;
                }
                exteriorTriangles.Add(triangle / 3);
                Vector3 a = mountainRoot.TransformPoint(vertices[ia]);
                Vector3 b = mountainRoot.TransformPoint(vertices[ib]);
                Vector3 c = mountainRoot.TransformPoint(vertices[ic]);
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Vector3 point = (a + b + c) / 3f;
                if (normal.y < 0.38f || point.y < 4f)
                {
                    continue;
                }
                anchors.Add(point);
                bool nearFace = point.x > mountainBounds.center.x
                    && point.z < Mathf.Lerp(mountainBounds.min.z, mountainBounds.max.z, 0.62f);
                if (nearFace && (point.y > crownHeight || normal.y > 0.58f))
                {
                    foreground.Add(point);
                }
            }
            Debug.Log($"[Mountain Planting Anchors] exterior={anchors.Count}, "
                + $"crownAndNearShelves={foreground.Count}, allowedTriangles={exteriorTriangles.Count}");
            return anchors;
        }

        private static bool SurfaceHit(MeshCollider surface, Vector3 origin, Bounds mountainBounds,
            HashSet<int> exteriorTriangles, out RaycastHit hit)
        {
            float minimumHeight = Mathf.Max(1.5f, Mathf.Lerp(mountainBounds.min.y, mountainBounds.max.y, 0.13f));
            return surface.Raycast(new Ray(origin, Vector3.down), out hit, mountainBounds.size.y + 6f)
                && exteriorTriangles.Contains(hit.triangleIndex)
                && hit.point.y > minimumHeight && hit.normal.y > 0.38f;
        }

        private static bool TooCloseToPlant(Vector3 point, List<Vector3> roots, float minimumDistance = 0.72f)
        {
            foreach (Vector3 root in roots)
            {
                if ((point - root).sqrMagnitude < minimumDistance * minimumDistance)
                {
                    return true;
                }
            }
            return false;
        }

        private static Bounds BoundsAt(PlantSource source, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Bounds bounds = new(position + rotation * Vector3.Scale(source.Vertices[0] - source.Foot, scale), Vector3.zero);
            foreach (Vector3 vertex in source.Vertices)
            {
                bounds.Encapsulate(position + rotation * Vector3.Scale(vertex - source.Foot, scale));
            }
            return bounds;
        }

        private static bool ClearsTrack(Bounds bounds, Vector3[] route, float clearance = TrackClearance)
        {
            // A box expanded on every axis conservatively contains the route envelope.
            // Extra room covers gentle sway and the spline curvature between samples.
            bounds.Expand((clearance + 0.55f) * 2f);
            for (int index = 1; index < route.Length; index++)
            {
                Vector3 segment = route[index] - route[index - 1];
                float length = segment.magnitude;
                if (bounds.Contains(route[index - 1]) || bounds.Contains(route[index])
                    || (length > 0.001f && bounds.IntersectRay(new Ray(route[index - 1], segment / length), out float distance)
                        && distance <= length))
                {
                    return false;
                }
            }
            return true;
        }

        private static bool FeetSupported(PlantSource source, Vector3 position, Quaternion rotation, Vector3 scale,
            MeshCollider surface, Bounds mountainBounds)
        {
            float plantHeight = source.Height * scale.y / source.InstanceScale.y;
            float rootRadius = Mathf.Clamp(plantHeight * 0.16f, 0.08f, 0.19f);
            int checkedFeet = 0;
            // Pick the actual low stem vertices before tilting the plant. Low
            // frond tips after rotation are not roots and may overhang a ledge.
            foreach (Vector3 vertex in source.Vertices)
            {
                Vector3 localOffset = Vector3.Scale(vertex - source.Foot, scale);
                if (localOffset.y > plantHeight * 0.07f
                    || localOffset.x * localOffset.x + localOffset.z * localOffset.z > rootRadius * rootRadius)
                {
                    continue;
                }
                Vector3 point = position + rotation * localOffset;
                Vector3 origin = new(point.x, mountainBounds.max.y + 3f, point.z);
                if (!surface.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, mountainBounds.size.y + 6f)
                    || point.y - hit.point.y > 0.08f || hit.point.y - point.y > 0.4f)
                {
                    return false;
                }
                checkedFeet++;
            }
            return checkedFeet > 0;
        }

        private static float Next(System.Random random)
        {
            return (float)random.NextDouble();
        }
    }

    internal sealed class MountainGroundcoverPatchRenderer : MonoBehaviour
    {
        private Mesh mesh;
        private Material[] materials;
        private Matrix4x4[][] patches;
        private Bounds[] patchBounds;
        private bool[] visiblePatches;
        private float viewDistance;
        private float viewDistanceSquared;
        private bool supportsInstancing;
        private ShadowCastingMode shadows;
        private Camera viewCamera;

        public void Initialize(Mesh sourceMesh, Material[] sourceMaterials, Matrix4x4[][] sourcePatches,
            float maximumViewDistance = 0f, bool castShadows = false)
        {
            mesh = sourceMesh;
            materials = sourceMaterials;
            patches = sourcePatches;
            viewDistance = maximumViewDistance;
            viewDistanceSquared = maximumViewDistance * maximumViewDistance;
            supportsInstancing = SystemInfo.supportsInstancing;
            shadows = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            patchBounds = new Bounds[patches.Length];
            visiblePatches = maximumViewDistance > 0f ? new bool[patches.Length] : Array.Empty<bool>();
            Bounds sourceBounds = mesh.bounds;
            for (int patch = 0; patch < patches.Length; patch++)
            {
                bool first = true;
                foreach (Matrix4x4 matrix in patches[patch])
                {
                    Vector3 center = matrix.MultiplyPoint3x4(sourceBounds.center);
                    Vector3 e = sourceBounds.extents;
                    Vector3 extent = new(
                        Mathf.Abs(matrix.m00) * e.x + Mathf.Abs(matrix.m01) * e.y + Mathf.Abs(matrix.m02) * e.z,
                        Mathf.Abs(matrix.m10) * e.x + Mathf.Abs(matrix.m11) * e.y + Mathf.Abs(matrix.m12) * e.z,
                        Mathf.Abs(matrix.m20) * e.x + Mathf.Abs(matrix.m21) * e.y + Mathf.Abs(matrix.m22) * e.z);
                    Bounds bounds = new(center, extent * 2f);
                    if (first) patchBounds[patch] = bounds;
                    else patchBounds[patch].Encapsulate(bounds);
                    first = false;
                }
            }
            foreach (Material material in materials)
            {
                if (material != null)
                {
                    material.enableInstancing = true;
                }
            }
        }

        private void LateUpdate()
        {
            if (mesh == null || materials == null || patches == null || patches.Length == 0)
            {
                return;
            }
            if (viewDistance > 0f && viewCamera == null) viewCamera = Camera.main;
            bool distanceCulling = viewDistance > 0f && viewCamera != null;
            if (distanceCulling)
            {
                Vector3 cameraPosition = viewCamera.transform.position;
                for (int index = 0; index < patches.Length; index++)
                    visiblePatches[index] = !(patchBounds[index].SqrDistance(cameraPosition) > viewDistanceSquared);
            }
            int layer = gameObject.layer;
            int submeshCount = Mathf.Min(mesh.subMeshCount, materials.Length);
            // Preserve submission order and let Unity handle per-eye frustum/shadow culling.
            for (int submesh = 0; submesh < submeshCount; submesh++)
            {
                Material material = materials[submesh];
                if (material == null)
                {
                    continue;
                }
                for (int index = 0; index < patches.Length; index++)
                {
                    if (distanceCulling && !visiblePatches[index]) continue;
                    Matrix4x4[] patch = patches[index];
                    // Small spatial batches preserve culling, share the source GPU
                    // buffers and add no per-tuft transforms, colliders or Updates.
                    if (supportsInstancing)
                    {
                        Graphics.DrawMeshInstanced(mesh, submesh, material, patch, patch.Length,
                            null, shadows, true, layer, null, LightProbeUsage.Off);
                    }
                    else
                    {
                        foreach (Matrix4x4 matrix in patch)
                        {
                            Graphics.DrawMesh(mesh, matrix, material, layer, null,
                                submesh, null, shadows, true, null, LightProbeUsage.Off);
                        }
                    }
                }
            }
        }
    }
}
