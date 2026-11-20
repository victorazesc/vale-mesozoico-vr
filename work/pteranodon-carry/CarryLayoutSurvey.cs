#if UNITY_EDITOR
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEditor;
namespace ValeMesozoico
{
    internal sealed class CarryLayoutSurvey : MonoBehaviour
    {
        [MenuItem("Vale Mesozoico/Preview/Carry Layout Survey")]
        private static void Run() { SessionState.SetBool("CarrySurvey", true); EditorApplication.isPlaying = true; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Begin()
        {
            if (!SessionState.GetBool("CarrySurvey", false)) return;
            SessionState.SetBool("CarrySurvey", false);
            new GameObject("Carry Survey").AddComponent<CarryLayoutSurvey>();
        }
        private IEnumerator Start()
        {
            yield return new WaitForSeconds(1.5f);
            RideController c = FindFirstObjectByType<RideController>();
            RideDebugTimeline timeline = c.GetComponent<RideDebugTimeline>();
            if (timeline != null) timeline.enabled = false;
            c.DeveloperSeekToProgress(RideMotionProfile.CrestStart);
            var p = c.Spline.PoseAtDistance(c.Spline.Length * RideMotionProfile.CrestStart);
            string report = $"CREST {p.Position:F3} tangent={p.Tangent:F3} cart={c.transform.position:F3}\n";
            foreach (var r in FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                if (r.sharedMaterial != null && r.sharedMaterial.shader.name == "Vale Mesozoico/Lagoon Water") report += $"WATER {r.name} {r.bounds}\n";
            var cart=c.transform.Find("Abandoned Coaster Cart 3D");
            foreach (var r in cart.GetComponentsInChildren<Renderer>())
                report += $"CART {r.name} world={r.bounds} localCenter={c.transform.InverseTransformPoint(r.bounds.center)}\n";
            var actor=FindFirstObjectByType<PteranodonDropSequence>().Actor;
            actor.SetActive(true);
            actor.transform.position=c.transform.position+Vector3.up*6;
            var visual=actor.transform.Find("Model Scale/Visual");
            foreach(var t in actor.GetComponentsInChildren<Transform>())
                if(t.name.Contains("Rig") || t.name == "Visual" || t.name == "Model Scale") report += $"TRANSFORM {t.name} scale={t.localScale} position={t.localPosition} rot={t.localEulerAngles}\n";
            var clips=Resources.LoadAll<AnimationClip>("Models/Dinosaurs/Quetzalcoatlus/Quetzalcoatlus");
            foreach(var clip in clips)
            {
                report+=$"CLIP {clip.name} {clip.length}\n";
                if(!clip.name.StartsWith("__")) clip.SampleAnimation(visual.gameObject, 0.4f);
            }
            foreach(var t in actor.GetComponentsInChildren<Transform>())
                if(t.name.StartsWith("Bone")) report += $"BONE {t.name} local={actor.transform.InverseTransformPoint(t.position):F3}\n";
            File.WriteAllText("work/pteranodon-carry/layout.txt", report);
            CaveDropRuntimeQa.Capture("layout", c.transform.position+new Vector3(22,14,22),c.transform.position+Vector3.up*4,"work/pteranodon-carry");
            EditorApplication.isPlaying=false;
        }
    }
}
#endif
