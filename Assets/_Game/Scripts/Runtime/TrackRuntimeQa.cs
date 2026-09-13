#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class TrackRuntimeQa : MonoBehaviour
    {
        internal bool InitialValidationComplete { get; private set; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            new GameObject("Track Runtime QA").AddComponent<TrackRuntimeQa>();
        }

        private IEnumerator Start()
        {
            yield return null;
            yield return new WaitForSecondsRealtime(0.5f);

            Transform root = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name == "Procedural Tubular Coaster Track");
            MeshFilter[] filters = root != null
                ? root.GetComponentsInChildren<MeshFilter>(true)
                : System.Array.Empty<MeshFilter>();
            MeshRenderer[] renderers = root != null
                ? root.GetComponentsInChildren<MeshRenderer>(true)
                : System.Array.Empty<MeshRenderer>();
            int totalTriangles = filters
                .Where(filter => filter.sharedMesh != null)
                .Sum(filter => filter.sharedMesh.triangles.Length / 3);
            MeshFilter liftChain = filters.FirstOrDefault(filter => filter.name == "Procedural Lift Chain 3D");
            int liftChainTriangles = liftChain != null && liftChain.sharedMesh != null
                ? liftChain.sharedMesh.triangles.Length / 3
                : 0;
            MeshFilter wheelContact = filters.FirstOrDefault(filter => filter.name == "Wheel Contact Grease Bands");
            int wheelContactTriangles = wheelContact != null && wheelContact.sharedMesh != null
                ? wheelContact.sharedMesh.triangles.Length / 3
                : 0;
            bool wheelContactPass = wheelContact != null
                && wheelContact.sharedMesh != null
                && wheelContact.sharedMesh.vertexCount > 2000
                && wheelContactTriangles > 3000
                && wheelContactTriangles <= TrackMeshFactory.PathSegments * 16
                && wheelContact.GetComponent<MeshRenderer>()?.sharedMaterial?.name == "Wheel Contact Grease PBR";
            Material wearMaterial = wheelContact != null ? wheelContact.GetComponent<MeshRenderer>()?.sharedMaterial : null;
            wheelContactPass &= wearMaterial != null
                && wearMaterial.GetTexture("_BaseMap")?.name == "TrackWheelWear_Albedo"
                && wearMaterial.GetFloat("_Surface") == 1f
                && wearMaterial.GetFloat("_ZWrite") == 0f;
            bool liftChainMeshPass = liftChain != null
                && liftChain.sharedMesh != null
                && liftChain.sharedMesh.vertexCount > 5000
                && liftChainTriangles > 5000
                && liftChainTriangles <= 40000
                && liftChain.GetComponent<LiftChainTextureAnimator>() != null;
            bool completeMeshes = filters.Length == 7
                && filters.All(filter => filter.sharedMesh != null
                    && filter.sharedMesh.vertexCount > 0
                    && filter.sharedMesh.uv.Length == filter.sharedMesh.vertexCount);
            Material[] materials = renderers
                .Select(renderer => renderer.sharedMaterial)
                .Where(material => material != null)
                .Distinct()
                .ToArray();
            bool materialsPass = materials.Length == 5
                && materials.All(material => material.shader != null
                    && material.shader.name == "Universal Render Pipeline/Lit"
                    && material.GetTexture("_BaseMap") != null
                    && material.GetTexture("_BumpMap") != null)
                && materials.Count(material => material.GetTexture("_SpecGlossMap") != null) == 2
                && materials.Count(material =>
                    material.GetTexture("_BaseMap").name == "TrackRustedSteel_Albedo"
                    && material.GetTexture("_BumpMap").name == "TrackRustedSteel_Normal"
                    && material.GetFloat("_Metallic") <= 0.1f
                    && material.GetFloat("_Smoothness") <= 0.3f) == 2
                && materials.Count(material => material.name == "Wheel Contact Grease PBR") == 1
                && materials.Count(material => material.name == "Lift Chain Dark Steel PBR") == 1;
            RideController controller = FindFirstObjectByType<RideController>();
            AudioSource[] audioSources = controller != null
                ? controller.GetComponents<AudioSource>()
                : System.Array.Empty<AudioSource>();
            bool audioPass = audioSources.Length == 4
                && audioSources.Any(source => source.clip != null
                    && source.clip.name == "Enhanced Wheel Rail Roll")
                && audioSources.Any(source => source.clip != null
                    && source.clip.name == "LiftChainStart"
                    && !source.loop
                    && source.volume <= 0.001f)
                && audioSources.Any(source => source.clip != null
                    && source.clip.name == "LiftChainLoop"
                    && source.loop
                    && !source.isPlaying)
                && audioSources.Any(source => source.clip != null
                    && source.clip.name == "LiftChainEnd"
                    && !source.loop
                    && !source.isPlaying)
                && controller != null
                && !controller.LiftChainActive;
            bool transitionPass = RideMotionProfile.IsLiftChainActive(0.12f, 0.18f)
                && !RideMotionProfile.IsLiftChainActive(0.12f, -0.08f)
                && !RideMotionProfile.IsLiftChainActive(0.52f, 0.18f);
            bool budgetPass = totalTriangles > 20000 && totalTriangles <= 180000;
            bool railSeamsPass = ValidateRailSeams(filters.FirstOrDefault(filter => filter.name == "Polished Running Rails"));
            bool supportContactsPass = ValidateSupportContacts(
                filters.FirstOrDefault(filter => filter.name == "Tubular Ground Supports"),
                filters.FirstOrDefault(filter => filter.name == "Painted Central Spine"),
                out int groundedSupports, out float maxFootGap, out float maxTopRadius);
            string status = root != null
                && completeMeshes
                && wheelContactPass
                && liftChainMeshPass
                && materialsPass
                && audioPass
                && transitionPass
                && budgetPass
                && railSeamsPass
                && supportContactsPass
                ? "PASS"
                : "FAIL";
            Debug.Log(
                $"[Track QA] {status} | meshes={filters.Length}, materials={materials.Length}, "
                + $"totalTriangles={totalTriangles}, completeMeshes={completeMeshes}, "
                + $"wheelContactTriangles={wheelContactTriangles}, wheelContactPass={wheelContactPass}, "
                + $"liftChainTriangles={liftChainTriangles}, liftChainMeshPass={liftChainMeshPass}, "
                + $"materialsPass={materialsPass}, audioSources={audioSources.Length}, audioPass={audioPass}, "
                + $"transitionPass={transitionPass}, budgetPass={budgetPass}, railSeamsPass={railSeamsPass}, "
                + $"supportContactsPass={supportContactsPass}, groundedSupports={groundedSupports}, "
                + $"maxFootGap={maxFootGap:F4}m, maxTopRadius={maxTopRadius:F4}m");
            InitialValidationComplete = true;

            bool liftObserved = false;
            float timeoutAt = Time.realtimeSinceStartup + 85f;
            while (controller != null && Time.realtimeSinceStartup < timeoutAt)
            {
                if (!liftObserved
                    && controller.LiftChainActive
                    && controller.LiftChainVolume >= 0.2f)
                {
                    liftObserved = true;
                    Debug.Log(
                        $"[Lift Audio QA] ACTIVE | progress={controller.RideProgress:F3}, "
                        + $"chainVolume={controller.LiftChainVolume:F3}, "
                        + $"wheelVolume={controller.WheelRailVolume:F3}");
                }

                if (liftObserved
                    && !controller.LiftChainActive
                    && controller.RideProgress >= RideMotionProfile.CrestStart
                    && controller.LiftChainVolume <= 0.01f
                    && controller.LiftChainEndPlaying
                    && controller.WheelRailVolume > 0.02f)
                {
                    Debug.Log(
                        $"[Lift Audio QA] PASS | liftStoppedAtCrest=True, "
                        + $"progress={controller.RideProgress:F3}, chainVolume={controller.LiftChainVolume:F3}, "
                        + $"releasePlayed=True, wheelContinues=True, wheelVolume={controller.WheelRailVolume:F3}");
                    Destroy(gameObject);
                    yield break;
                }

                yield return null;
            }

            Debug.LogError(
                $"[Lift Audio QA] FAIL | liftObserved={liftObserved}, "
                + $"progress={(controller != null ? controller.RideProgress : -1f):F3}");
            Destroy(gameObject);
        }

        private static bool ValidateRailSeams(MeshFilter rails)
        {
            Mesh mesh = rails != null ? rails.sharedMesh : null;
            if (mesh == null) return false;
            Vector3[] vertices = mesh.vertices;
            Vector3[] normals = mesh.normals;
            Vector2[] uv = mesh.uv;
            int[] triangles = mesh.triangles;
            if (vertices.Length % 11 != 0 || normals.Length != vertices.Length) return false;
            for (int ring = 0; ring < vertices.Length; ring += 11)
            {
                if ((vertices[ring] - vertices[ring + 10]).sqrMagnitude > 0.000001f
                    || Vector3.Dot(normals[ring], normals[ring + 10]) < 0.999f) return false;
            }
            for (int index = 0; index < triangles.Length; index += 3)
            {
                float a = uv[triangles[index]].x;
                float b = uv[triangles[index + 1]].x;
                float c = uv[triangles[index + 2]].x;
                if (Mathf.Max(a, Mathf.Max(b, c)) - Mathf.Min(a, Mathf.Min(b, c)) > 0.101f) return false;
                Vector3 face = Vector3.Cross(vertices[triangles[index + 1]] - vertices[triangles[index]],
                    vertices[triangles[index + 2]] - vertices[triangles[index]]);
                if (Vector3.Dot(face, normals[triangles[index]]) <= 0f) return false;
            }
            return true;
        }

        private static bool ValidateSupportContacts(MeshFilter supports, MeshFilter spine,
            out int groundedSupports, out float maxFootGap, out float maxTopRadius)
        {
            groundedSupports = 0;
            maxFootGap = 0f;
            maxTopRadius = 0f;
            if (supports == null || spine == null || supports.sharedMesh == null || spine.sharedMesh == null)
                return false;
            bool imported = GameObject.Find("Blender Environment") != null;
            ForestFloorSurface surface = imported ? ForestFloorSurface.Load() : null;
            if (imported && surface == null) return false;
            Vector3[] vertices = supports.sharedMesh.vertices;
            Vector3[] spineVertices = spine.sharedMesh.vertices;
            // Column: 16 vertices; capped foot: 42; two braces: 12 each.
            const int stride = 82;
            if (vertices.Length == 0 || vertices.Length % stride != 0 || spineVertices.Length % 13 != 0)
                return false;
            Vector3[] centers = new Vector3[spineVertices.Length / 13];
            for (int ring = 0; ring < centers.Length; ring++)
            {
                for (int vertex = 0; vertex < 12; vertex++) centers[ring] += spineVertices[ring * 13 + vertex];
                centers[ring] = spine.transform.TransformPoint(centers[ring] / 12f);
            }
            bool pass = true;
            for (int support = 0; support < vertices.Length; support += stride)
            {
                bool footPass = true;
                for (int vertex = 16; vertex < 26; vertex++)
                {
                    Vector3 bottom = supports.transform.TransformPoint(vertices[support + vertex]);
                    Vector3 top = supports.transform.TransformPoint(vertices[support + vertex + 10]);
                    float ground;
                    if (surface != null)
                    {
                        if (!surface.Sample(bottom.x, bottom.z, out Vector3 point, out _))
                        {
                            footPass = false;
                            continue;
                        }
                        ground = point.y;
                    }
                    else ground = ProceduralWorld.HeightAt(bottom.x, bottom.z);
                    maxFootGap = Mathf.Max(maxFootGap, bottom.y - ground);
                    footPass &= bottom.y <= ground + 0.01f && top.y >= ground - 0.01f;
                }
                if (footPass) groundedSupports++;
                pass &= footPass;
                Vector3 columnTop = Vector3.zero;
                Vector3 braceStart = Vector3.zero;
                for (int i = 8; i < 16; i++) columnTop += vertices[support + i] / 8f;
                for (int i = 58; i < 64; i++) braceStart += vertices[support + i] / 6f;
                bool lateralSupport = Vector3.Distance(columnTop, braceStart) < 0.001f;
                if (lateralSupport)
                {
                    for (int i = 58; i < 76; i++)
                    {
                        if (i >= 64 && i < 70) continue;
                        pass &= Vector3.Distance(vertices[support + i], columnTop) <= 0.205f;
                    }
                }
                for (int vertex = 8; vertex < stride; vertex++)
                {
                    if (lateralSupport && vertex < 16) continue;
                    if (vertex >= 16 && vertex < 64 || vertex >= 70 && vertex < 76) continue;
                    Vector3 tip = supports.transform.TransformPoint(vertices[support + vertex]);
                    float nearest = float.PositiveInfinity;
                    for (int segment = 0; segment < centers.Length - 1; segment++)
                    {
                        Vector3 axis = centers[segment + 1] - centers[segment];
                        float t = Mathf.Clamp01(Vector3.Dot(tip - centers[segment], axis)
                            / Mathf.Max(axis.sqrMagnitude, 0.000001f));
                        nearest = Mathf.Min(nearest, Vector3.Distance(tip, centers[segment] + axis * t));
                    }
                    maxTopRadius = Mathf.Max(maxTopRadius, nearest);
                    // Every end-ring vertex must be inside the 12-sided spine, not poking through its wall.
                    pass &= nearest <= 0.228f;
                }
            }
            return pass;
        }
    }
}
#endif
