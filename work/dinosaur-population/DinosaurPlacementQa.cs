#if UNITY_EDITOR
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class DinosaurPlacementQa : MonoBehaviour
    {
        private static string Output => Path.Combine(Application.dataPath, "../work/dinosaur-population");
        [InitializeOnLoadMethod]
        private static void Setup()
        {
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            string request = Path.Combine(Output, "request");
            if (!EditorApplication.isPlaying && !EditorApplication.isCompiling && File.Exists(request)
                && File.ReadAllText(request).Trim() == "prefab-audit")
            {
                File.Delete(request);
                StringBuilder text = new();
                foreach (string name in new[] { "Apatosaurus", "Triceratops", "Trex" })
                {
                    string path = "Models/Dinosaurs/" + name;
                    GameObject instance = Instantiate(Resources.Load<GameObject>(path));
                    instance.hideFlags = HideFlags.HideAndDontSave;
                    SkinnedMeshRenderer skin = instance.GetComponentInChildren<SkinnedMeshRenderer>();
                    Mesh baked = new();
                    skin.BakeMesh(baked, true);
                    text.AppendLine($"{name} bind={baked.bounds} declared={skin.bounds} scale={skin.transform.lossyScale} bone0={skin.bones[0].localToWorldMatrix} bind0={skin.sharedMesh.bindposes[0]}");
                    foreach (Transform t in instance.GetComponentsInChildren<Transform>().Take(8))
                        text.AppendLine($"  {t.name} pos={t.localPosition} scale={t.localScale}");
                    AnimationClip clip = Resources.LoadAll<AnimationClip>(path).First(c => c.name.Contains("Idle"));
                    clip.SampleAnimation(instance, 0.5f);
                    skin.BakeMesh(baked, true);
                    text.AppendLine($"  sampled={baked.bounds} bone0={skin.bones[0].localToWorldMatrix}");
                    foreach (Transform t in instance.GetComponentsInChildren<Transform>().Take(8))
                        text.AppendLine($"  {t.name} pos={t.localPosition} scale={t.localScale}");
                    DestroyImmediate(baked);
                    DestroyImmediate(instance);
                }
                File.WriteAllText(Path.Combine(Output, "prefabs.txt"), text.ToString());
                return;
            }
            if (!EditorApplication.isPlaying || Time.time < 2f || !File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            new GameObject("Dinosaur Placement QA Capture").AddComponent<DinosaurPlacementQa>()
                .StartCoroutine(Run(command));
        }

        private static IEnumerator Run(string command)
        {
            RideController ride = FindFirstObjectByType<RideController>();
            if (ride == null) yield break;
            ride.SetDeveloperPaused(true);
            string[] parts = command.Split('|');
            if (parts[0] == "audit")
            {
                StringBuilder report = new();
                ForestFloorSurface ground = ForestFloorSurface.Load();
                Transform group = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(t => t.name == "Animated 3D Dinosaurs");
                foreach (Transform actor in group)
                {
                    Vector3 p = actor.position;
                    bool sampled = ground != null && ground.Sample(p.x, p.z, out _, out _);
                    float floor = ground != null && ground.Sample(p.x, p.z, out Vector3 foot, out _) ? foot.y : float.NaN;
                    Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
                    Bounds bounds = renderers[0].bounds;
                    foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                    Animation anim = actor.GetComponentInChildren<Animation>(true);
                    string playing = anim == null ? "none" : string.Join(",", anim.Cast<AnimationState>().Where(s => anim.IsPlaying(s.name)).Select(s => s.name));
                    report.AppendLine($"{actor.name}: active={actor.gameObject.activeInHierarchy}, pos={p:F2}, ground={floor:F2}, gap={p.y-floor:F2}, bounds={bounds}, animation={playing}");
                    foreach (Renderer r in renderers)
                    {
                        report.AppendLine($"  renderer={r.name}, enabled={r.enabled}, visible={r.isVisible}, mats={string.Join(",", r.sharedMaterials.Select(m => m == null ? "MISSING" : m.name + ":" + m.shader.name))}");
                        if (r is SkinnedMeshRenderer skin)
                        {
                            Mesh baked = new();
                            skin.BakeMesh(baked, true);
                            Bounds actual = new(skin.transform.TransformPoint(baked.vertices[0]), Vector3.zero);
                            foreach (Vector3 v in baked.vertices) actual.Encapsulate(skin.transform.TransformPoint(v));
                            report.AppendLine($"  baked={actual}, mesh={skin.sharedMesh.bounds}, root={skin.rootBone?.position}, rootScale={skin.rootBone?.lossyScale}, scale={skin.transform.lossyScale}, forceOff={skin.forceRenderingOff}");
                            if (!actor.name.StartsWith("Pteranodon") && !actor.name.Contains("Chase"))
                            {
                                Vector3 center = actor.position;
                                float roamRadius = 0f;
                                DinosaurRoamMotion roam = actor.GetComponent<DinosaurRoamMotion>();
                                if (roam != null)
                                {
                                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                                    center = (Vector3)typeof(DinosaurRoamMotion).GetField("_center", flags).GetValue(roam);
                                    roamRadius = (float)typeof(DinosaurRoamMotion).GetField("_radius", flags).GetValue(roam);
                                }
                                float radius = 0f;
                                foreach (Vector3 vertex in baked.vertices)
                                {
                                    Vector3 delta = skin.transform.TransformPoint(vertex) - actor.position;
                                    radius = Mathf.Max(radius, new Vector2(delta.x, delta.z).magnitude);
                                }
                                float clearance = float.MaxValue;
                                for (int sample = 0; sample < 2048; sample++)
                                {
                                    Vector3 rail = ride.Spline.PoseAtDistance(ride.Spline.Length * sample / 2048f).Position;
                                    if (rail.y > actual.max.y + 3f) continue;
                                    Vector3 delta = rail - center;
                                    clearance = Mathf.Min(clearance, new Vector2(delta.x, delta.z).magnitude - radius - roamRadius);
                                }
                                report.AppendLine($"  sweptTrackClearance={clearance:F2}m bodyRadius={radius:F2}m roamRadius={roamRadius:F2}m");
                            }
                            Destroy(baked);
                        }
                        foreach (Material m in r.sharedMaterials)
                            if (m != null) report.AppendLine($"  mat={m.name} color={m.color} renderQueue={m.renderQueue} keywords={string.Join(",",m.shaderKeywords)}");
                    }
                }
                for (int i = 0; i < 40; i++)
                {
                    float b = i / 40f;
                    float p = ride.Spline.MapBaseProgress(b);
                    RidePose pose = ride.Spline.PoseAtDistance(ride.Spline.Length * p);
                    report.AppendLine($"route base={b:F3} progress={p:F4} position={pose.Position:F2} forward={pose.Tangent:F2}");
                }
                File.WriteAllText(Path.Combine(Output, parts.Length > 1 ? parts[1] + ".txt" : "audit.txt"), report.ToString());
                Debug.Log("[Dinosaur Placement QA] Audit written");
                yield break;
            }

            Camera camera = Camera.main;
            Transform parent = camera.transform.parent;
            Vector3 originalPosition = camera.transform.localPosition;
            Quaternion originalRotation = camera.transform.localRotation;
            float originalFov = camera.fieldOfView;
            if (parts[0] == "rider")
            {
                float progress = float.Parse(parts[1], CultureInfo.InvariantCulture);
                ride.DeveloperSeekToProgress(ride.Spline.MapBaseProgress(progress));
                ride.SetDeveloperPaused(true);
            }
            else if (parts[0] == "closeup")
            {
                Transform actor = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .First(t => t.name == parts[1]);
                Renderer[] renderers = actor.GetComponentsInChildren<Renderer>(true);
                Bounds bounds = renderers[0].bounds;
                foreach (Renderer r in renderers) bounds.Encapsulate(r.bounds);
                camera.transform.SetParent(null, true);
                camera.fieldOfView = 50f;
                camera.transform.position = bounds.center + (actor.forward + actor.right * 0.8f).normalized * bounds.size.magnitude * 1.0f + Vector3.up * 1.5f;
                camera.transform.LookAt(bounds.center);
            }
            for (int i = 0; i < 8; i++) yield return null;
            yield return new WaitForEndOfFrame();
            string path = Path.Combine(Output, parts[2] + ".png");
            Texture2D screenshot = new(Screen.width, Screen.height, TextureFormat.RGB24, false);
            screenshot.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            screenshot.Apply(false);
            File.WriteAllBytes(path, screenshot.EncodeToPNG());
            Destroy(screenshot);
            yield return new WaitForEndOfFrame();
            if (parts[0] == "closeup")
            {
                camera.transform.SetParent(parent, false);
                camera.transform.SetLocalPositionAndRotation(originalPosition, originalRotation);
                camera.fieldOfView = originalFov;
            }
            Debug.Log("[Dinosaur Placement QA] Screenshot: " + path);
        }
    }
}
#endif
