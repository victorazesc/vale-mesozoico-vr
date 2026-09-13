#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class EnvironmentRuntimeQa : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Environment Runtime QA").AddComponent<EnvironmentRuntimeQa>();
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);

            Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Transform importedEnvironment = transforms.FirstOrDefault(item => item.name == "Blender Environment");
            if (importedEnvironment != null)
            {
                yield return ValidateImportedEnvironment(importedEnvironment, transforms);
                Destroy(gameObject);
                yield break;
            }

            Transform[] palms = transforms.Where(item => item.name.StartsWith("Hero Coconut Palm ")).ToArray();
            Transform[] closedCliffs = transforms
                .Where(item => item.name.StartsWith("Closed 3D Cliff ")
                    || item.name.StartsWith("Photogrammetry 3D Cliff "))
                .ToArray();
            Transform[] closedBoulders = transforms
                .Where(item => item.name.StartsWith("Closed 3D Boulder ")
                    || item.name.StartsWith("Photogrammetry 3D Boulder "))
                .ToArray();
            int cliffs = closedCliffs.Length;
            int boulders = closedBoulders.Length;
            Transform[] closedRocks = closedCliffs.Concat(closedBoulders).ToArray();
            int closedRockLods = closedRocks.Count(item => item.GetComponent<LODGroup>() != null);
            int completeRockMeshSets = closedRocks.Count(item =>
            {
                LODGroup group = item.GetComponent<LODGroup>();
                if (group == null)
                {
                    return false;
                }

                LOD[] lods = group.GetLODs();
                return lods.Length == 3
                    && lods.All(lod => lod.renderers.Length > 0
                        && lod.renderers.All(renderer => renderer != null));
            });
            int legacyScanObjects = transforms.Count(item => item.name.StartsWith("Photogrammetry Cliff ")
                || item.name.StartsWith("Photogrammetry Boulder "));
            int plants = transforms.Count(item => item.name.StartsWith("Detailed Plant "));
            Transform[] canopyVarieties = transforms
                .Where(item => item.name.StartsWith("Canopy Fan Palm ")
                    || item.name.StartsWith("Canopy Tall Jungle Palm ")
                    || item.name.StartsWith("Canopy Bent River Palm "))
                .ToArray();
            int canopyTypes = canopyVarieties
                .Select(item => item.name.Split(' ')[1])
                .Distinct()
                .Count();
            int canopyLods = canopyVarieties.Count(item => item.GetComponent<LODGroup>() != null);
            int canopySways = canopyVarieties.Count(item => item.GetComponent<TropicalWindSway>() != null);
            Transform cave = transforms.FirstOrDefault(item => item.name == "Rock Cave Tunnel Interior 3D");
            MeshFilter caveFilter = cave != null ? cave.GetComponent<MeshFilter>() : null;
            Renderer caveRenderer = cave != null ? cave.GetComponent<Renderer>() : null;
            int caveTriangles = caveFilter != null && caveFilter.sharedMesh != null
                ? caveFilter.sharedMesh.triangles.Length / 3
                : 0;
            int caveMouthRockLods = transforms.Count(item => (item.name.StartsWith("Waterfall Cave Entrance ")
                    || item.name.StartsWith("Waterfall Cave Exit "))
                && item.GetComponent<LODGroup>() != null);
            bool cavePbr = caveRenderer != null
                && caveRenderer.sharedMaterial != null
                && caveRenderer.sharedMaterial.shader != null
                && caveRenderer.sharedMaterial.shader.name == "Universal Render Pipeline/Lit"
                && caveRenderer.sharedMaterial.GetTexture("_BaseMap") != null
                && caveRenderer.sharedMaterial.GetTexture("_BumpMap") != null;
            Transform caveAtmosphere = transforms.FirstOrDefault(item => item.name == "Cave Atmosphere");
            bool caveAtmospherePass = caveAtmosphere != null
                && caveAtmosphere.GetComponent<Light>() != null
                && caveAtmosphere.GetComponent<AudioReverbZone>() != null;
            Transform runningRails = transforms.FirstOrDefault(item => item.name == "Polished Running Rails");
            MeshFilter railFilter = runningRails != null ? runningRails.GetComponent<MeshFilter>() : null;
            float trackVerticalRange = railFilter != null && railFilter.sharedMesh != null
                ? railFilter.sharedMesh.bounds.size.y
                : 0f;
            Transform terrain = transforms.FirstOrDefault(item => item.name == "Terrain");
            MeshFilter terrainFilter = terrain != null ? terrain.GetComponent<MeshFilter>() : null;
            float terrainVerticalRange = terrainFilter != null && terrainFilter.sharedMesh != null
                ? terrainFilter.sharedMesh.bounds.size.y
                : 0f;
            Transform[] shorelineMeshes = transforms
                .Where(item => item.name == "Wet Shoreline Transition 3D")
                .ToArray();
            Transform[] shallowWaterBands = transforms
                .Where(item => item.name.StartsWith("Shallow Water Transition 3D "))
                .ToArray();
            Transform[] shorelineBlendBands = transforms
                .Where(item => item.name.StartsWith("Wet Shoreline Blend 3D "))
                .ToArray();
            int shorelinePebbles = transforms.Count(item => (item.name.StartsWith("Shoreline Pebble 3D ")
                    || item.name.StartsWith("Photogrammetry Shore Boulder "))
                && item.GetComponent<LODGroup>() != null);
            Transform[] waterfallFlows = transforms
                .Where(item => item.name.StartsWith("Waterfall Flow 3D "))
                .ToArray();
            Transform[] waterfallSources = transforms
                .Where(item => item.name == "Waterfall Source Stream 3D")
                .ToArray();
            int waterfallRockLods = transforms.Count(item => (item.name.StartsWith("Waterfall Rock ")
                    || item.name.StartsWith("Photogrammetry Waterfall "))
                && item.GetComponent<LODGroup>() != null);
            int waterfallFoamVolumes = transforms.Count(item => item.name == "Waterfall Foam Volume 3D"
                || item.name == "Waterfall Crest Volume 3D"
                || item.name == "Waterfall Splash Jets 3D"
                || item.name == "Waterfall Source Lip Foam 3D");
            int volumetricFlows = waterfallFlows.Count(item =>
            {
                MeshFilter filter = item.GetComponent<MeshFilter>();
                return filter != null
                    && filter.sharedMesh != null
                    && filter.sharedMesh.bounds.size.z > 0.25f;
            });
            int waterfallTriangles = transforms
                .Where(item => item.name.StartsWith("Waterfall Flow 3D ")
                    || item.name == "Waterfall Foam Volume 3D"
                    || item.name == "Waterfall Crest Volume 3D"
                    || item.name == "Waterfall Splash Jets 3D"
                    || item.name == "Waterfall Source Stream 3D"
                    || item.name == "Waterfall Source Lip Foam 3D")
                .Select(item => item.GetComponent<MeshFilter>())
                .Where(filter => filter != null && filter.sharedMesh != null)
                .Sum(filter => filter.sharedMesh.triangles.Length / 3);
            int waterfallPbrMaterials = waterfallFlows
                .Select(item => item.GetComponent<Renderer>())
                .Where(renderer => renderer != null && renderer.sharedMaterial != null)
                .Select(renderer => renderer.sharedMaterial)
                .Count(material => material.shader != null
                    && material.shader.name == "Universal Render Pipeline/Lit"
                    && material.GetTexture("_BaseMap") != null
                    && material.GetTexture("_BumpMap") != null);
            int shorelineTriangles = shorelineMeshes
                .Concat(shallowWaterBands)
                .Concat(shorelineBlendBands)
                .Select(item => item.GetComponent<MeshFilter>())
                .Where(filter => filter != null && filter.sharedMesh != null)
                .Sum(filter => filter.sharedMesh.triangles.Length / 3);
            int shorelinePbrMaterials = shorelineMeshes
                .Select(item => item.GetComponent<Renderer>())
                .Where(renderer => renderer != null && renderer.sharedMaterial != null)
                .Select(renderer => renderer.sharedMaterial)
                .Count(material => material.shader != null
                    && material.shader.name == "Universal Render Pipeline/Lit"
                    && material.GetTexture("_BaseMap") != null
                    && material.GetTexture("_BumpMap") != null);
            int validWaterfallSources = waterfallSources.Count(item =>
            {
                MeshFilter filter = item.GetComponent<MeshFilter>();
                Renderer renderer = item.GetComponent<Renderer>();
                return filter != null
                    && filter.sharedMesh != null
                    && filter.sharedMesh.bounds.size.z > 6f
                    && filter.sharedMesh.bounds.size.y > 0.35f
                    && renderer != null
                    && renderer.sharedMaterial != null
                    && renderer.sharedMaterial.GetTexture("_BaseMap") != null
                    && renderer.sharedMaterial.GetTexture("_BumpMap") != null;
            });
            int palmLods = palms.Count(item => item.GetComponent<LODGroup>() != null);
            int palmSways = palms.Count(item => item.GetComponent<TropicalWindSway>() != null);
            int barkRenderers = palms.Sum(item => item.GetComponentsInChildren<Renderer>(true)
                .Count(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.name == "Hero Coconut Palm Bark"));
            int leafRenderers = palms.Sum(item => item.GetComponentsInChildren<Renderer>(true)
                .Count(renderer => renderer.sharedMaterial != null && renderer.sharedMaterial.name == "Hero Coconut Palm Leaves"));
            Material[] rockMaterials = transforms
                .Where(item => item.name.StartsWith("Closed 3D Cliff ")
                    || item.name.StartsWith("Closed 3D Boulder ")
                    || item.name.StartsWith("Photogrammetry 3D Cliff ")
                    || item.name.StartsWith("Photogrammetry 3D Boulder "))
                .SelectMany(item => item.GetComponentsInChildren<Renderer>(true))
                .Select(renderer => renderer.sharedMaterial)
                .Where(material => material != null)
                .Distinct()
                .ToArray();
            int pbrRockMaterials = rockMaterials.Count(material => material.shader != null
                && material.shader.name == "Universal Render Pipeline/Lit"
                && material.GetTexture("_BaseMap") != null
                && material.GetTexture("_BumpMap") != null);

            bool countsPass = palms.Length >= 46
                && cliffs == 9
                && boulders == 14
                && closedRockLods == cliffs + boulders
                && completeRockMeshSets == cliffs + boulders
                && legacyScanObjects == 0
                && plants == 240
                && canopyVarieties.Length >= 34
                && canopyTypes == 3
                && canopyLods == canopyVarieties.Length
                && canopySways == canopyVarieties.Length
                && palmLods == palms.Length
                && palmSways == palms.Length
                && barkRenderers >= palms.Length * 3
                && leafRenderers >= palms.Length * 3
                && pbrRockMaterials >= 2
                && shorelineMeshes.Length == 1
                && shallowWaterBands.Length == 3
                && shorelineBlendBands.Length == 2
                && shorelinePebbles == 18
                && shorelinePbrMaterials == 1
                && shorelineTriangles > 1000
                && shorelineTriangles <= 6000
                && waterfallFlows.Length == 3
                && volumetricFlows == waterfallFlows.Length
                && waterfallFoamVolumes == 4
                && waterfallRockLods == 14
                && waterfallPbrMaterials == waterfallFlows.Length
                && waterfallSources.Length == 1
                && validWaterfallSources == waterfallSources.Length
                && waterfallTriangles > 1000
                && waterfallTriangles <= 12000
                && caveTriangles >= 700
                && caveTriangles <= 2400
                && caveMouthRockLods == 6
                && cavePbr
                && caveAtmospherePass
                && trackVerticalRange >= 27f
                && terrainVerticalRange >= 16f;

            Quaternion before = palms.Length > 0 ? palms[0].localRotation : Quaternion.identity;
            yield return new WaitForSecondsRealtime(0.8f);
            float swayDelta = palms.Length > 0 ? Quaternion.Angle(before, palms[0].localRotation) : 0f;
            bool windPass = swayDelta > 0.01f;

            string status = countsPass && windPass ? "PASS" : "FAIL";
            Debug.Log(
                $"[Environment QA] {status} | palms={palms.Length}, palmLods={palmLods}, palmSways={palmSways}, "
                + $"leafRenderers={leafRenderers}, barkRenderers={barkRenderers}, cliffs={cliffs}, "
                + $"boulders={boulders}, closedRockLods={closedRockLods}, "
                + $"completeRockMeshSets={completeRockMeshSets}, legacyScanObjects={legacyScanObjects}, "
                + $"plants={plants}, canopyVarieties={canopyVarieties.Length}, canopyTypes={canopyTypes}, "
                + $"canopyLods={canopyLods}, canopySways={canopySways}, "
                + $"pbrRockMaterials={pbrRockMaterials}, terrainVerticalRange={terrainVerticalRange:F1}m, "
                + $"shorelineMeshes={shorelineMeshes.Length}, shallowWaterBands={shallowWaterBands.Length}, "
                + $"shorelineBlendBands={shorelineBlendBands.Length}, "
                + $"shorelinePebbles={shorelinePebbles}, shorelinePbrMaterials={shorelinePbrMaterials}, "
                + $"shorelineTriangles={shorelineTriangles}, "
                + $"waterfallFlows={waterfallFlows.Length}, volumetricFlows={volumetricFlows}, "
                + $"waterfallFoamVolumes={waterfallFoamVolumes}, waterfallRockLods={waterfallRockLods}, "
                + $"waterfallPbrMaterials={waterfallPbrMaterials}, waterfallSources={waterfallSources.Length}, "
                + $"validWaterfallSources={validWaterfallSources}, waterfallTriangles={waterfallTriangles}, "
                + $"caveTriangles={caveTriangles}, caveMouthRockLods={caveMouthRockLods}, "
                + $"cavePbr={cavePbr}, caveAtmosphere={caveAtmospherePass}, "
                + $"trackVerticalRange={trackVerticalRange:F1}m, "
                + $"swayDelta={swayDelta:F3}deg");

            RideController controller = FindFirstObjectByType<RideController>();
            Transform waterfallRoot = transforms.FirstOrDefault(item => item != null
                && item.name == "Volumetric 3D Waterfall");
            bool caveEntered = false;
            float caveQaTimeout = Time.realtimeSinceStartup + 90f;
            while (controller != null && Time.realtimeSinceStartup < caveQaTimeout)
            {
                float progress = controller.RideProgress;
                if (!caveEntered && progress >= 0.405f && progress <= 0.465f)
                {
                    Vector3 cartPosition = controller.transform.position;
                    bool insideCaveBounds = caveRenderer != null && caveRenderer.bounds.Contains(cartPosition);
                    float waterfallDistance = waterfallRoot != null
                        ? Vector2.Distance(
                            new Vector2(cartPosition.x, cartPosition.z),
                            new Vector2(waterfallRoot.position.x, waterfallRoot.position.z))
                        : float.PositiveInfinity;
                    caveEntered = insideCaveBounds && waterfallDistance <= 9f;
                    Debug.Log(
                        $"[Cave Ride QA] ENTER | progress={progress:F3}, "
                        + $"insideCaveBounds={insideCaveBounds}, waterfallDistance={waterfallDistance:F1}m");
                }

                if (caveEntered && progress >= 0.478f)
                {
                    Debug.Log(
                        $"[Cave Ride QA] PASS | descentThroughCave=True, "
                        + $"waterfallAligned=True, exitProgress={progress:F3}");
                    Destroy(gameObject);
                    yield break;
                }

                yield return null;
            }

            Debug.LogError($"[Cave Ride QA] FAIL | caveEntered={caveEntered}");

            Destroy(gameObject);
        }

        private static IEnumerator ValidateImportedEnvironment(
            Transform importedEnvironment,
            Transform[] sceneTransforms)
        {
            Renderer[] renderers = importedEnvironment.GetComponentsInChildren<Renderer>(true);
            MeshFilter[] filters = importedEnvironment.GetComponentsInChildren<MeshFilter>(true);
            HashSet<Mesh> meshes = filters
                .Where(filter => filter.sharedMesh != null)
                .Select(filter => filter.sharedMesh)
                .ToHashSet();
            Material[] materials = renderers
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Distinct()
                .ToArray();
            int pbrMaterials = materials.Count(material => material.shader != null
                && (material.shader.name == "Universal Render Pipeline/Lit"
                    || material.shader.name == "Vale Mesozoico/World Projected Ground"
                    || material.shader.name == "ValeMesozoico/MountainRock"));
            int waterMaterials = materials.Count(material => material.shader != null
                && material.shader.name == "Vale Mesozoico/Lagoon Water" && material.shader.isSupported
                && material.GetTexture("_BumpMap") != null
                && material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent
                && material.GetFloat("_AlphaMax") > 0f && material.GetFloat("_AlphaMax") < 1f
                && material.GetFloat("_EdgeFade") > 0f && material.GetFloat("_Absorption") > 0f);
            int texturedMaterials = materials.Count(material => material.HasProperty("_BaseMap")
                && material.GetTexture("_BaseMap") != null);
            int cutoutMaterials = materials.Count(material => material.HasProperty("_AlphaClip")
                && material.GetFloat("_AlphaClip") > 0.5f
                && material.GetTexture("_BaseMap") != null);

            Transform lagoon = FindImportedDescendant(importedEnvironment, "Lagoon_Water_Editable");
            Transform cave = FindImportedDescendant(importedEnvironment, "Mountain Cave Interior")
                ?? FindImportedDescendant(importedEnvironment, "PC_FINAL_Cave_Tunnel_Interior");
            Renderer lagoonRenderer = lagoon != null ? lagoon.GetComponentInChildren<Renderer>(true) : null;
            Renderer caveRenderer = cave != null ? cave.GetComponentInChildren<Renderer>(true) : null;
            float lagoonX = lagoonRenderer != null ? lagoonRenderer.bounds.center.x : float.NegativeInfinity;
            float caveX = caveRenderer != null ? caveRenderer.bounds.center.x : float.NegativeInfinity;

            Transform terrain = FindImportedDescendant(importedEnvironment, "Terrain_Editable_129x129");
            MeshFilter terrainFilter = terrain != null ? terrain.GetComponentInChildren<MeshFilter>(true) : null;
            Renderer terrainRenderer = terrain != null ? terrain.GetComponentInChildren<Renderer>(true) : null;
            Mesh terrainMesh = terrainFilter != null ? terrainFilter.sharedMesh : null;
            Material terrainMaterial = terrainRenderer != null ? terrainRenderer.sharedMaterial : null;
            int terrainUvCount = terrainMesh != null
                && terrainMesh.HasVertexAttribute(UnityEngine.Rendering.VertexAttribute.TexCoord0)
                    ? terrainMesh.vertexCount
                    : 0;
            string terrainTexture = terrainMaterial != null
                && terrainMaterial.HasProperty("_BaseMap")
                && terrainMaterial.GetTexture("_BaseMap") != null
                    ? terrainMaterial.GetTexture("_BaseMap").name
                    : "none";
            Color terrainColor = terrainMaterial != null && terrainMaterial.HasProperty("_BaseColor")
                ? terrainMaterial.GetColor("_BaseColor")
                : Color.clear;
            Vector3 riderProbe = new(0f, 6.15f, -56.7f);
            string riderGroundCandidates = string.Join(", ", renderers
                .Where(renderer => renderer.bounds.min.x <= riderProbe.x
                    && renderer.bounds.max.x >= riderProbe.x
                    && renderer.bounds.min.z <= riderProbe.z
                    && renderer.bounds.max.z >= riderProbe.z
                    && renderer.bounds.min.y <= riderProbe.y)
                .OrderByDescending(renderer => renderer.bounds.max.y <= riderProbe.y
                    ? renderer.bounds.max.y
                    : float.NegativeInfinity)
                .Take(6)
                .Select(renderer =>
                    $"{renderer.transform.parent?.name ?? renderer.name}:"
                    + $"{renderer.sharedMaterial?.name ?? "none"}@{renderer.bounds.max.y:F1}"));

            Transform runningRails = sceneTransforms.FirstOrDefault(item => item.name == "Polished Running Rails");
            MeshFilter railFilter = runningRails != null ? runningRails.GetComponent<MeshFilter>() : null;
            float trackVerticalRange = railFilter != null && railFilter.sharedMesh != null
                ? railFilter.sharedMesh.bounds.size.y
                : 0f;

            TropicalWindSway[] sways = importedEnvironment.GetComponentsInChildren<TropicalWindSway>(true);
            yield return new WaitForSecondsRealtime(0.8f);
            float swayDelta = sways.Length > 0
                ? sways.Max(sway => sway.AppliedAngle)
                : 0f;

            bool pass = importedEnvironment.childCount >= 2700
                && renderers.Length >= 2700
                && meshes.Count >= 35
                && meshes.Count <= 100
                && materials.Length >= 16
                && pbrMaterials + waterMaterials == materials.Length
                && texturedMaterials >= 12
                && cutoutMaterials >= 5
                && lagoonX >= 30f
                && lagoonX <= 50f
                && caveRenderer != null
                && cave.gameObject.activeInHierarchy
                && (cave.name == "Mountain Cave Interior" || (caveX >= 35f && caveX <= 55f))
                && terrainUvCount > 0
                && terrainTexture != "none"
                && trackVerticalRange >= 27f
                && sways.Length >= 150
                && swayDelta > 0.002f;

            string message =
                $"[Imported Environment QA] {(pass ? "PASS" : "FAIL")} | "
                + $"instances={importedEnvironment.childCount}, renderers={renderers.Length}, "
                + $"meshes={meshes.Count}, materials={materials.Length}, pbrMaterials={pbrMaterials}, "
                + $"waterMaterials={waterMaterials}, texturedMaterials={texturedMaterials}, cutoutMaterials={cutoutMaterials}, "
                + $"lagoonX={lagoonX:F1}, caveX={caveX:F1}, "
                + $"terrainUvs={terrainUvCount}, terrainTexture={terrainTexture}, "
                + $"terrainColor={terrainColor}, "
                + $"riderGround=[{riderGroundCandidates}], "
                + $"trackVerticalRange={trackVerticalRange:F1}m, sways={sways.Length}, "
                + $"swayDelta={swayDelta:F3}deg";
            if (pass)
            {
                Debug.Log(message);
            }
            else
            {
                Debug.LogError(message);
            }
        }

        // Invoked by the Editor preview through reflection: Runtime types remain internal.
        // Probe actual mesh surfaces and the real cart pose; AABBs only narrow the candidate set.
        internal static bool ValidateForestFloorForPreview(out string detail)
        {
            const float heightTolerance = 0.14f;
            Vector3[] roots = MountainRockVegetation.ForestFloorRoots;
            GameObject environment = GameObject.Find("Blender Environment");
            Transform geometry = environment != null
                ? environment.transform.Find("Terrain_Editable_129x129/Geometry") : null;
            MeshFilter filter = geometry != null ? geometry.GetComponent<MeshFilter>() : null;
            if (roots == null || roots.Length < 200 || filter == null || filter.sharedMesh == null)
            {
                detail = $"plantas insuficientes ({roots?.Length ?? 0}/200) ou geometria real do terreno ausente";
                return false;
            }
            for (int index = 0; index < roots.Length; index++)
            {
                Vector3 root = roots[index];
                if (float.IsNaN(root.x) || float.IsInfinity(root.x)
                    || float.IsNaN(root.y) || float.IsInfinity(root.y)
                    || float.IsNaN(root.z) || float.IsInfinity(root.z))
                {
                    detail = $"raiz {index} contém coordenada não finita";
                    return false;
                }
            }

            GameObject probe = null;
            bool originalBackfaces = Physics.queriesHitBackfaces;
            try
            {
                // Use the rendered terrain mesh and its full live transform, not
                // the heightfield that was used to place these roots.
                probe = new GameObject("Temporary Forest Floor QA Collider") { layer = 31 };
                probe.transform.SetParent(filter.transform, false);
                MeshCollider collider = probe.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                Bounds bounds = collider.bounds;
                float maxHeightError = 0f;
                for (int index = 0; index < roots.Length; index++)
                {
                    Vector3 root = roots[index];
                    Vector3 origin = new(root.x, bounds.max.y + 1f, root.z);
                    if (!collider.Raycast(new Ray(origin, Vector3.down), out RaycastHit hit, bounds.size.y + 2f))
                    {
                        detail = $"raiz {index} em {root} sem apoio na malha real";
                        return false;
                    }
                    float heightError = Mathf.Abs(root.y - hit.point.y);
                    maxHeightError = Mathf.Max(maxHeightError, heightError);
                    if (heightError > heightTolerance)
                    {
                        detail = $"raiz {index} em {root}: erro de altura={heightError:F3}m, tolerância={heightTolerance:F2}m";
                        return false;
                    }
                }
                detail = $"plants={roots.Length}, finiteRoots=PASS, terrainMesh={filter.sharedMesh.name}, "
                    + $"maxHeightError={maxHeightError:F3}m, tolerance={heightTolerance:F2}m, support=realMeshRaycast";
                Debug.Log("[Forest Floor Geometry QA] PASS | " + detail);
                return true;
            }
            finally
            {
                Physics.queriesHitBackfaces = originalBackfaces;
                if (probe != null)
                {
                    probe.SetActive(false);
                    DestroyImmediate(probe);
                }
                Physics.SyncTransforms();
            }
        }

        internal static bool ValidateCaveFoliageForPreview(out string detail)
        {
            CaveEntranceFoliage foliage = FindFirstObjectByType<CaveEntranceFoliage>();
            RideController controller = FindFirstObjectByType<RideController>();
            if (foliage == null || controller == null || foliage.InteractiveFrondCount < 18
                || foliage.GetComponentsInChildren<Collider>().Length != 0)
            {
                detail = "folhas insuficientes, controlador ausente ou collider bloqueando a passagem";
                return false;
            }

            float originalProgress = controller.RideProgress;
            float peak = 0f;
            float returnError = 0f;
            Mesh deformed = new();
            try
            {
                SkinnedMeshRenderer renderer = foliage.GetComponent<SkinnedMeshRenderer>();
                foreach (int fps in new[] { 24, 72, 144 })
                {
                    foreach (float speed in new[] { 2f, 10f, RideMotionProfile.MaxSpeed })
                    {
                        float distance = foliage.FirstDistance - 16f;
                        controller.DeveloperSeekToProgress(distance / foliage.RouteLength);
                        foliage.Tick(0f);
                        if (foliage.MaximumBend > 0.1f)
                        {
                            detail = "folhas abertas antes da aproximação";
                            return false;
                        }
                        float passPeak = 0f;
                        while (distance < foliage.LastDistance + 16f)
                        {
                            distance += speed / fps;
                            controller.DeveloperPreviewSeekToProgress(distance / foliage.RouteLength);
                            foliage.Tick(1f / fps);
                            passPeak = Mathf.Max(passPeak, foliage.MaximumBend);
                        }
                        for (int frame = 0; frame < fps * 3; frame++) foliage.Tick(1f / fps);
                        returnError = Mathf.Max(returnError, foliage.MaximumBend);
                        peak = Mathf.Max(peak, passPeak);
                        if (passPeak < 50f || foliage.MaximumBend > 2f)
                        {
                            detail = $"flexão/retorno inválido: fps={fps}, speed={speed}, peak={passPeak:F1}, rest={foliage.MaximumBend:F2}";
                            return false;
                        }
                    }
                }
                // Scrubbing into contact must also open the curtain immediately.
                controller.DeveloperSeekToProgress(foliage.EntranceProgress);
                foliage.Tick(0f);
                if (foliage.MaximumBend < 50f)
                {
                    detail = "folhas não respondem ao reposicionar o carrinho na entrada";
                    return false;
                }
                renderer.BakeMesh(deformed);
                foreach (Vector3 vertex in deformed.vertices)
                {
                    if (float.IsNaN(vertex.x) || float.IsNaN(vertex.y) || float.IsNaN(vertex.z)
                        || !renderer.localBounds.Contains(vertex))
                    {
                        detail = "malha deformada inválida ou fora dos limites de renderização";
                        return false;
                    }
                }
                detail = $"fronds={foliage.FrondCount}, fps=24/72/144, speeds=2/10/{RideMotionProfile.MaxSpeed}m/s, peak={peak:F1}, "
                    + $"returnError={returnError:F3}, seek=PASS, skinnedVertices={deformed.vertexCount}, solidColliders=0";
                Debug.Log("[Cave Foliage QA] PASS | " + detail);
                return true;
            }
            finally
            {
                controller.DeveloperSeekToProgress(originalProgress);
                foliage.Tick(0f);
                DestroyImmediate(deformed);
            }
        }

        internal static bool ValidateMountainCaveForPreview(out string detail)
            => ValidateRideSectionForPreview(MountainTrackCave.StartProgress - 0.032f,
                MountainTrackCave.EndProgress + 0.028f, true, out detail);

        internal static bool ValidateRideSectionForPreview(float startProgress, float endProgress,
            bool requireTunnel, out string detail)
        {
            const int sampleCount = 241;
            const int probeLayer = 31;
            const int probeMask = 1 << probeLayer;
            RideController controller = FindFirstObjectByType<RideController>();
            Camera camera = Camera.main;
            GameObject environment = GameObject.Find("Blender Environment");
            GameObject cave = GameObject.Find("Mountain Cave Interior");
            if (controller == null || camera == null || environment == null || cave == null)
            {
                detail = "controlador, câmera, ambiente ou malha da caverna ausente";
                Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                return false;
            }

            float originalProgress = controller.RideProgress;
            bool originalBackfaces = Physics.queriesHitBackfaces;
            List<GameObject> temporaryObjects = new();
            HashSet<Collider> obstacles = new();
            List<MeshCollider> meshObstacles = new();
            List<MeshCollider> caveSurfaces = new();
            int enclosureSamples = 0;
            int entranceSamples = 0;
            int exitSamples = 0;
            try
            {
                Renderer[] cartRenderers = controller.GetComponentsInChildren<Renderer>()
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(camera.transform)).ToArray();
                if (cartRenderers.Length == 0)
                {
                    detail = "nenhum renderer do carrinho disponível para medir seu envelope";
                    return false;
                }

                Bounds corridor = new(cave.transform.position, Vector3.zero);
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    controller.DeveloperSeekToProgress(Mathf.Lerp(startProgress, endProgress, sample / (float)(sampleCount - 1)));
                    if (sample == 0)
                    {
                        corridor = new Bounds(controller.transform.position, Vector3.zero);
                    }
                    corridor.Encapsulate(controller.transform.position);
                }
                corridor.Expand(24f);

                MeshFilter[] supportMeshes = FindObjectsByType<MeshFilter>(FindObjectsSortMode.None)
                    .Where(filter => filter.name == "Tubular Ground Supports").ToArray();
                foreach (MeshFilter filter in environment.GetComponentsInChildren<MeshFilter>().Concat(supportMeshes))
                {
                    Renderer renderer = filter.GetComponent<Renderer>();
                    if (filter.sharedMesh == null || renderer == null || !renderer.enabled
                        || !corridor.Intersects(renderer.bounds))
                    {
                        continue;
                    }

                    GameObject probe = new("Temporary Cave QA Collider") { layer = probeLayer };
                    temporaryObjects.Add(probe);
                    probe.transform.SetParent(filter.transform, false);
                    MeshCollider collider = probe.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    obstacles.Add(collider);
                    meshObstacles.Add(collider);
                    if (filter.gameObject == cave || filter.transform.IsChildOf(cave.transform))
                    {
                        caveSurfaces.Add(collider);
                    }
                }

                if (requireTunnel && caveSurfaces.Count == 0)
                {
                    detail = "malha da caverna sem collider temporário válido";
                    return false;
                }

                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float progress = Mathf.Lerp(startProgress, endProgress, sample / (float)(sampleCount - 1));
                    controller.DeveloperSeekToProgress(progress);
                    Physics.SyncTransforms();
                    foreach (Renderer renderer in cartRenderers)
                    {
                        Bounds local = renderer.localBounds;
                        Vector3 scale = renderer.transform.lossyScale;
                        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        Vector3 center = renderer.transform.TransformPoint(local.center);
                        Vector3 extents = Vector3.Scale(local.extents, scale) + Vector3.one * 0.06f;
                        Collider hit = Physics.OverlapBox(center, extents, renderer.transform.rotation,
                            probeMask, QueryTriggerInteraction.Ignore).FirstOrDefault(obstacles.Contains);
                        if (hit != null)
                        {
                            detail = $"cart intersects {DescribeObstacle(hit)}, renderer={renderer.name}, progress={progress:F4}";
                            Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                            return false;
                        }

                        MeshCollider enclosedBy = meshObstacles.FirstOrDefault(obstacle => obstacle.bounds.Contains(center)
                            && IsInsideClosedObstacle(obstacle, center));
                        if (enclosedBy != null)
                        {
                            detail = $"cart center enclosed by {DescribeObstacle(enclosedBy)}, "
                                + $"renderer={renderer.name}, progress={progress:F4}";
                            Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                            return false;
                        }
                    }

                    Vector3 eye = camera.transform.position;
                    Collider headHit = Physics.OverlapSphere(eye, 0.22f, probeMask, QueryTriggerInteraction.Ignore)
                        .FirstOrDefault(obstacles.Contains);
                    MeshCollider enclosingRock = meshObstacles.FirstOrDefault(obstacle => obstacle.bounds.Contains(eye)
                        && IsInsideClosedObstacle(obstacle, eye));
                    if (headHit != null || enclosingRock != null)
                    {
                        Collider hit = headHit != null ? headHit : enclosingRock;
                        detail = $"head intersects/enclosed by {DescribeObstacle(hit)}, progress={progress:F4}";
                        Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                        return false;
                    }

                    if (requireTunnel && progress >= MountainTrackCave.StartProgress + 0.007f
                        && progress <= MountainTrackCave.EndProgress - 0.005f)
                    {
                        Vector3[] directions = {
                            controller.transform.right, -controller.transform.right,
                            controller.transform.up, -controller.transform.up
                        };
                        foreach (Vector3 direction in directions)
                        {
                            Ray ray = new(eye, direction);
                            if (!caveSurfaces.Any(surface => surface.Raycast(ray, out _, 16f)))
                            {
                                detail = $"cave wall/ceiling/floor open, progress={progress:F4}, direction={direction}";
                                Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                                return false;
                            }
                        }
                        enclosureSamples++;
                    }
                    if (progress <= MountainTrackCave.StartProgress) entranceSamples++;
                    if (progress >= MountainTrackCave.EndProgress) exitSamples++;
                }

                bool pass = !requireTunnel || (enclosureSamples >= 60 && entranceSamples >= 15 && exitSamples >= 20);
                detail = $"samples={sampleCount}, cartRenderers={cartRenderers.Length}, obstacles={obstacles.Count}, "
                    + $"enclosedSamples={enclosureSamples}, entranceSamples={entranceSamples}, exitSamples={exitSamples}, "
                    + "cartMargin=0.06m, headRadius=0.22m, solidContainment=rayParity";
                if (pass) Debug.Log($"[Mountain Cave Geometry QA] PASS | {detail}");
                else Debug.LogError($"[Mountain Cave Geometry QA] FAIL | {detail}");
                return pass;
            }
            finally
            {
                Physics.queriesHitBackfaces = originalBackfaces;
                foreach (GameObject temporary in temporaryObjects)
                {
                    if (temporary != null) DestroyImmediate(temporary);
                }
                controller.DeveloperSeekToProgress(originalProgress);
                Physics.SyncTransforms();
            }
        }

        [Serializable]
        private sealed class RouteQaManifest
        {
            public RouteQaInstance[] instances;
        }

        [Serializable]
        private sealed class RouteQaInstance
        {
            public string name;
            public string template;
        }

        private sealed class RouteQaProbe
        {
            public MeshCollider Collider;
            public MeshFilter Filter;
            public string Name;
            public int Template;
            public bool Solid;
        }

        // Opt-in Editor validation. Every pose is sampled even when a blocker is found.
        internal static bool ValidateFullRouteForPreview(out string detail)
        {
            const int sampleCount = 2049;
            const int probeLayer = 31;
            const int probeMask = 1 << probeLayer;
            RideController controller = FindFirstObjectByType<RideController>();
            Camera camera = Camera.main;
            GameObject environment = GameObject.Find("Blender Environment");
            GameObject mountain = GameObject.Find("Mountain Track Cave");
            if (controller == null || camera == null || environment == null || mountain == null)
            {
                detail = "controlador, câmera, ambiente ou nova caverna ausente";
                Debug.LogError($"[Full Route Geometry QA] FAIL | {detail}");
                return false;
            }

            float originalProgress = controller.RideProgress;
            bool originalBackfaces = Physics.queriesHitBackfaces;
            TropicalWindSway[] sways = environment.GetComponentsInChildren<TropicalWindSway>()
                .Where(sway => sway.enabled).ToArray();
            List<GameObject> temporaryObjects = new();
            List<RouteQaProbe> probes = new();
            Dictionary<Collider, string> firstBlockers = new();
            int blockedSamples = 0;
            int blockerOccurrences = 0;
            int unclassifiedMeshes = 0;
            try
            {
                foreach (TropicalWindSway sway in sways) sway.enabled = false;
                Renderer[] cartRenderers = controller.GetComponentsInChildren<Renderer>()
                    .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(camera.transform)).ToArray();
                if (cartRenderers.Length == 0)
                {
                    detail = "nenhum renderer do carrinho disponível para medir seu envelope";
                    Debug.LogError($"[Full Route Geometry QA] FAIL | {detail}");
                    return false;
                }

                TextAsset manifestAsset = Resources.Load<TextAsset>(
                    "Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export");
                RouteQaManifest manifest = manifestAsset != null
                    ? JsonUtility.FromJson<RouteQaManifest>(manifestAsset.text) : null;
                if (manifest?.instances == null)
                    throw new InvalidOperationException("manifesto ausente para classificar as malhas do ambiente");
                Dictionary<string, int> templates = new(StringComparer.Ordinal);
                foreach (RouteQaInstance instance in manifest.instances)
                {
                    if (instance != null && !string.IsNullOrEmpty(instance.name))
                        templates[instance.name] = RouteQaTemplateIndex(instance.template);
                }

                IEnumerable<MeshFilter> filters = environment.GetComponentsInChildren<MeshFilter>()
                    .Concat(mountain.GetComponentsInChildren<MeshFilter>()).Distinct();
                foreach (MeshFilter filter in filters)
                {
                    Renderer renderer = filter.GetComponent<Renderer>();
                    if (filter.sharedMesh == null || renderer == null || !renderer.enabled
                        || !filter.gameObject.activeInHierarchy) continue;
                    bool isMountain = filter.transform == mountain.transform
                        || filter.transform.IsChildOf(mountain.transform);
                    // Ferns on the mountain are open leaf meshes. They still
                    // participate in overlap checks, but cannot enclose a cart.
                    bool isMountainStone = isMountain && filter.name == "Mountain Cave Interior";
                    Transform instance = filter.transform;
                    while (instance.parent != null && instance.parent != environment.transform
                        && instance != mountain.transform) instance = instance.parent;
                    int template = templates.TryGetValue(instance.name, out int mapped)
                        ? mapped : RouteQaTemplateIndex(filter.sharedMesh.name);
                    // Water, shallow water and the open shoreline gradient are visual surfaces.
                    if (!isMountain && (template == 1 || template == 36 || template == 42)) continue;
                    if (!isMountain && (template < 0 || template > 43))
                    {
                        unclassifiedMeshes++;
                        continue;
                    }

                    GameObject probe = new("Temporary Full Route QA Collider") { layer = probeLayer };
                    temporaryObjects.Add(probe);
                    probe.transform.SetParent(filter.transform, false);
                    MeshCollider collider = probe.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    probes.Add(new RouteQaProbe
                    {
                        Collider = collider,
                        Filter = filter,
                        Name = $"{instance.name}/{filter.name}",
                        Template = template,
                        Solid = isMountainStone || template == 0 || (template >= 4 && template <= 15)
                            || template == 35 || template == 41
                    });
                }

                Dictionary<Collider, RouteQaProbe> obstacles = probes.ToDictionary(probe => (Collider)probe.Collider);
                RouteQaProbe[] solids = probes.Where(probe => probe.Solid).ToArray();
                HashSet<Collider> sampleHits = new();
                Physics.queriesHitBackfaces = true;
                Physics.SyncTransforms();
                for (int sample = 0; sample < sampleCount; sample++)
                {
                    float progress = sample / (float)sampleCount;
                    controller.DeveloperSeekToProgress(progress);
                    Physics.SyncTransforms();
                    sampleHits.Clear();
                    foreach (Renderer renderer in cartRenderers)
                    {
                        Bounds local = renderer.localBounds;
                        Vector3 scale = renderer.transform.lossyScale;
                        scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                        Vector3 center = renderer.transform.TransformPoint(local.center);
                        Vector3 extents = Vector3.Scale(local.extents, scale) + Vector3.one * 0.12f;
                        foreach (Collider hit in Physics.OverlapBox(center, extents, renderer.transform.rotation,
                            probeMask, QueryTriggerInteraction.Ignore))
                        {
                            if (obstacles.ContainsKey(hit)) sampleHits.Add(hit);
                        }
                        foreach (RouteQaProbe solid in solids)
                        {
                            if (solid.Collider.bounds.Contains(center)
                                && IsInsideClosedObstacle(solid.Collider, center)) sampleHits.Add(solid.Collider);
                        }
                    }

                    Vector3 eye = camera.transform.position;
                    foreach (Collider hit in Physics.OverlapSphere(eye, 0.25f, probeMask, QueryTriggerInteraction.Ignore))
                    {
                        if (obstacles.ContainsKey(hit)) sampleHits.Add(hit);
                    }
                    foreach (RouteQaProbe solid in solids)
                    {
                        if (solid.Collider.bounds.Contains(eye)
                            && IsInsideClosedObstacle(solid.Collider, eye)) sampleHits.Add(solid.Collider);
                    }
                    if (sampleHits.Count > 0) blockedSamples++;
                    blockerOccurrences += sampleHits.Count;
                    foreach (Collider hit in sampleHits)
                    {
                        if (!firstBlockers.ContainsKey(hit))
                            firstBlockers[hit] = $"{obstacles[hit].Name}@{progress:F5}";
                    }
                }

                bool grounded = ValidateRoutePropGrounding(probes, out string groundingDetail);
                bool pass = firstBlockers.Count == 0 && grounded && unclassifiedMeshes == 0;
                detail = $"samples={sampleCount}, cartRenderers={cartRenderers.Length}, obstacles={probes.Count}, "
                    + $"blockedSamples={blockedSamples}, blockers={firstBlockers.Count}, "
                    + $"blockerOccurrences={blockerOccurrences}, unclassifiedMeshes={unclassifiedMeshes}, "
                    + "cartMargin=0.12m, headRadius=0.25m, solidContainment=rayParity, "
                    + $"grounding=[{groundingDetail}], firstBlockers=[{string.Join("; ", firstBlockers.Values.Take(40))}]";
                if (pass) Debug.Log($"[Full Route Geometry QA] PASS | {detail}");
                else Debug.LogError($"[Full Route Geometry QA] FAIL | {detail}");
                return pass;
            }
            catch (Exception exception)
            {
                detail = $"validação interrompida: {exception.GetBaseException().Message}";
                Debug.LogError($"[Full Route Geometry QA] FAIL | {detail}");
                return false;
            }
            finally
            {
                Physics.queriesHitBackfaces = originalBackfaces;
                foreach (GameObject temporary in temporaryObjects)
                {
                    if (temporary != null) DestroyImmediate(temporary);
                }
                foreach (TropicalWindSway sway in sways)
                {
                    if (sway != null) sway.enabled = true;
                }
                controller.DeveloperSeekToProgress(originalProgress);
                Physics.SyncTransforms();
            }
        }

        private static int RouteQaTemplateIndex(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1;
            int index = name.IndexOf("VM_ENV_", StringComparison.Ordinal);
            return index >= 0 && name.Length >= index + 10
                && int.TryParse(name.Substring(index + 7, 3), out int template) ? template : -1;
        }

        private static bool ValidateRoutePropGrounding(List<RouteQaProbe> probes, out string detail)
        {
            MeshCollider[] terrain = probes.Where(probe => probe.Template == 43)
                .Select(probe => probe.Collider).ToArray();
            if (terrain.Length == 0)
            {
                detail = "nenhuma malha de solo VM_ENV_043 disponível";
                return false;
            }
            Dictionary<Mesh, Vector3[]> verticesByMesh = new();
            List<string> failures = new();
            int treeCount = 0;
            int rockCount = 0;
            int failureCount = 0;
            foreach (RouteQaProbe probe in probes)
            {
                bool tree = probe.Template == 2 || probe.Template == 3
                    || (probe.Template >= 38 && probe.Template <= 40);
                bool rock = probe.Template == 0 || (probe.Template >= 4 && probe.Template <= 10);
                if (!tree && !rock) continue;
                if (tree) treeCount++;
                else rockCount++;
                if (!verticesByMesh.TryGetValue(probe.Filter.sharedMesh, out Vector3[] vertices))
                {
                    vertices = probe.Filter.sharedMesh.vertices;
                    verticesByMesh[probe.Filter.sharedMesh] = vertices;
                }
                float minY = float.PositiveInfinity;
                Vector2 minXZ = Vector2.one * float.PositiveInfinity;
                Vector2 maxXZ = Vector2.one * float.NegativeInfinity;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = probe.Filter.transform.TransformPoint(vertex);
                    minY = Mathf.Min(minY, point.y);
                    minXZ = Vector2.Min(minXZ, new Vector2(point.x, point.z));
                    maxXZ = Vector2.Max(maxXZ, new Vector2(point.x, point.z));
                }
                float cellSize = Mathf.Max(0.2f, Mathf.Max(maxXZ.x - minXZ.x, maxXZ.y - minXZ.y) / 5f);
                List<Vector3> basePoints = new();
                Dictionary<Vector2Int, Vector3> rockBaseCells = new();
                Vector3 meanBase = Vector3.zero;
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = probe.Filter.transform.TransformPoint(vertex);
                    if (tree)
                    {
                        if (point.y > minY + 0.12f) continue;
                        basePoints.Add(point);
                        meanBase += point;
                    }
                    else
                    {
                        // Sample the underside across the entire footprint, including uphill cells.
                        Vector2Int cell = new(Mathf.FloorToInt((point.x - minXZ.x) / cellSize),
                            Mathf.FloorToInt((point.z - minXZ.y) / cellSize));
                        if (!rockBaseCells.TryGetValue(cell, out Vector3 lowest) || point.y < lowest.y)
                            rockBaseCells[cell] = point;
                    }
                }
                float gap = float.PositiveInfinity;
                if (tree && basePoints.Count > 0)
                {
                    meanBase /= basePoints.Count;
                    meanBase.y = minY;
                    if (TryRouteTerrainHeight(terrain, meanBase, out float ground)) gap = minY - ground;
                }
                else
                {
                    List<float> supportGaps = new();
                    foreach (Vector3 point in rockBaseCells.Values)
                    {
                        supportGaps.Add(TryRouteTerrainHeight(terrain, point, out float ground)
                            ? point.y - ground : float.PositiveInfinity);
                    }
                    supportGaps.Sort();
                    if (supportGaps.Count > 0)
                        gap = supportGaps[Mathf.CeilToInt(supportGaps.Count * 0.60f) - 1];
                }
                bool floating = gap > (tree ? 0.18f : 0.25f);
                bool buriedTrunk = tree && gap < -0.75f;
                if (!floating && !buriedTrunk) continue;
                failureCount++;
                if (failures.Count < 40) failures.Add($"{probe.Name}:gap={gap:F3}m");
            }
            detail = $"trees={treeCount}, rocks={rockCount}, failures={failureCount}, "
                + $"treeTolerance=0.18m, rockTolerance=0.25m, rockSupport=60%-baseCells, "
                + $"names=[{string.Join("; ", failures)}]";
            return failureCount == 0 && treeCount > 0 && rockCount > 0;
        }

        private static bool TryRouteTerrainHeight(MeshCollider[] terrain, Vector3 point, out float height)
        {
            height = float.NegativeInfinity;
            Ray ray = new(new Vector3(point.x, point.y + 2048f, point.z), Vector3.down);
            foreach (MeshCollider surface in terrain)
            {
                if (surface.Raycast(ray, out RaycastHit hit, 4096f)) height = Mathf.Max(height, hit.point.y);
            }
            return !float.IsNegativeInfinity(height);
        }

        private static string DescribeObstacle(Collider collider)
        {
            Transform instance = collider.transform.parent;
            while (instance.parent != null && instance.parent.name != "Blender Environment")
            {
                instance = instance.parent;
            }
            return $"{instance.name}/{collider.transform.parent.name}";
        }

        internal static bool IsInsideClosedObstacle(MeshCollider collider, Vector3 point)
        {
            // Two oblique directions reduce false positives from open scenery surfaces.
            // A point in the bored cave has two shell crossings and remains outside solid rock.
            return OddSurfaceCrossings(collider, point, new Vector3(0.863f, 0.371f, 0.343f).normalized)
                && OddSurfaceCrossings(collider, point, new Vector3(-0.417f, 0.839f, -0.349f).normalized);
        }

        private static bool OddSurfaceCrossings(MeshCollider collider, Vector3 point, Vector3 direction)
        {
            float remaining = collider.bounds.size.magnitude + 1f;
            int intersections = 0;
            while (remaining > 0f && intersections < 64
                && collider.Raycast(new Ray(point, direction), out RaycastHit hit, remaining))
            {
                float advance = hit.distance + 0.002f;
                point += direction * advance;
                remaining -= advance;
                intersections++;
            }
            return intersections % 2 == 1;
        }

        private static Transform FindImportedDescendant(Transform root, string expectedName)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(candidate => candidate != null
                    && (candidate.name == expectedName
                        || candidate.name.StartsWith(expectedName)));
        }
    }
}
#endif
