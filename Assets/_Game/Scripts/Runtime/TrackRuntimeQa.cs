#if UNITY_EDITOR
using System.Collections;
using System.Linq;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class TrackRuntimeQa : MonoBehaviour
    {
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
                && wheelContactTriangles <= 6000
                && wheelContact.GetComponent<MeshRenderer>()?.sharedMaterial?.name == "Wheel Contact Grease PBR";
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
                && materials.Count(material => material.GetTexture("_SpecGlossMap") != null) == 3
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
            string status = root != null
                && completeMeshes
                && wheelContactPass
                && liftChainMeshPass
                && materialsPass
                && audioPass
                && transitionPass
                && budgetPass
                ? "PASS"
                : "FAIL";
            Debug.Log(
                $"[Track QA] {status} | meshes={filters.Length}, materials={materials.Length}, "
                + $"totalTriangles={totalTriangles}, completeMeshes={completeMeshes}, "
                + $"wheelContactTriangles={wheelContactTriangles}, wheelContactPass={wheelContactPass}, "
                + $"liftChainTriangles={liftChainTriangles}, liftChainMeshPass={liftChainMeshPass}, "
                + $"materialsPass={materialsPass}, audioSources={audioSources.Length}, audioPass={audioPass}, "
                + $"transitionPass={transitionPass}, budgetPass={budgetPass}");

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
    }
}
#endif
