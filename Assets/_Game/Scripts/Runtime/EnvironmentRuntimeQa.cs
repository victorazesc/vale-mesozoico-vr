#if UNITY_EDITOR
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
                    || material.shader.name == "Vale Mesozoico/World Projected Ground"));
            int texturedMaterials = materials.Count(material => material.HasProperty("_BaseMap")
                && material.GetTexture("_BaseMap") != null);
            int cutoutMaterials = materials.Count(material => material.HasProperty("_AlphaClip")
                && material.GetFloat("_AlphaClip") > 0.5f
                && material.GetTexture("_BaseMap") != null);

            Transform lagoon = FindImportedDescendant(importedEnvironment, "Lagoon_Water_Editable");
            Transform cave = FindImportedDescendant(importedEnvironment, "PC_FINAL_Cave_Tunnel_Interior");
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
                && pbrMaterials == materials.Length
                && texturedMaterials >= 12
                && cutoutMaterials >= 5
                && lagoonX >= 30f
                && lagoonX <= 50f
                && caveX >= 35f
                && caveX <= 55f
                && terrainUvCount > 0
                && terrainTexture != "none"
                && trackVerticalRange >= 27f
                && sways.Length >= 150
                && swayDelta > 0.002f;

            string message =
                $"[Imported Environment QA] {(pass ? "PASS" : "FAIL")} | "
                + $"instances={importedEnvironment.childCount}, renderers={renderers.Length}, "
                + $"meshes={meshes.Count}, materials={materials.Length}, pbrMaterials={pbrMaterials}, "
                + $"texturedMaterials={texturedMaterials}, cutoutMaterials={cutoutMaterials}, "
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
