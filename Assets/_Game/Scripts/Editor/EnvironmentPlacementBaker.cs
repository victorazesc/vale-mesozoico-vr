#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ValeMesozoico.Editor
{
    // Offline placement repair. The player only reads the resulting small manifest.
    [InitializeOnLoad]
    public static class EnvironmentPlacementBaker
    {
        private const string RunningKey = "Vale.PlacementBake.Running";
        private const string OutputAsset = "Assets/_Game/Resources/Models/Environment/EnvironmentPlacementCorrections.bytes";
        private const int Layer = 30;
        private const int Mask = 1 << Layer;
        [Serializable] private sealed class Manifest { public Placement[] instances; }
        [Serializable] private sealed class Placement { public string name; public string template; }
        [Serializable] private sealed class Correction { public string name; public Vector3 position; public bool disabled; }
        [Serializable] private sealed class Corrections { public Correction[] entries; }
        [Serializable] private sealed class Report
        {
            public int candidates, grounded, relocated, disabled, routeSamples;
            public string[] blockersBefore, blockersAfter;
            public Correction[] entries;
        }
        private sealed class Prop
        {
            public Transform root;
            public MeshCollider collider;
            public bool tree, solid;
            public Vector3 original;
            public Vector3[] feet;
        }
        private readonly struct Probe
        {
            public readonly Vector3 center, extents;
            public readonly Quaternion rotation;
            public readonly Bounds bounds;
            public Probe(Vector3 center, Vector3 extents, Quaternion rotation)
            {
                this.center = center; this.extents = extents; this.rotation = rotation;
                Vector3 x = Abs(rotation * Vector3.right) * extents.x;
                Vector3 y = Abs(rotation * Vector3.up) * extents.y;
                Vector3 z = Abs(rotation * Vector3.forward) * extents.z;
                bounds = new Bounds(center, (x + y + z) * 2f);
            }
        }
        static EnvironmentPlacementBaker() { EditorApplication.update += Tick; }

        public static void RunFromCli()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Inicie o baker fora de Play Mode.");
            SessionState.SetBool(RunningKey, true);
            EditorSceneManager.OpenScene("Assets/_Game/Scenes/JurassicRide.unity", OpenSceneMode.Single);
            EditorApplication.EnterPlaymode();
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(RunningKey, false) || !EditorApplication.isPlaying
                || Time.frameCount < 4 || GameObject.Find("Blender Environment") == null) return;
            SessionState.SetBool(RunningKey, false);
            int exitCode = 0;
            try
            {
                if (HasArgument("-validateEnvironmentPlacements"))
                {
                    MonoBehaviour controller = Controller();
                    Type qa = controller.GetType().Assembly.GetType("ValeMesozoico.EnvironmentRuntimeQa");
                    MethodInfo validate = qa.GetMethod("ValidateFullRouteForPreview", BindingFlags.Static | BindingFlags.NonPublic);
                    object[] arguments = { null };
                    bool pass = validate != null && (bool)validate.Invoke(null, arguments);
                    WriteReport(arguments[0]?.ToString() ?? "Validação indisponível");
                    if (!pass) exitCode = 1;
                }
                else if (HasArgument("-bakeEnvironmentPlacements")) Bake();
                else throw new InvalidOperationException("Informe -bakeEnvironmentPlacements ou -validateEnvironmentPlacements.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception.GetBaseException());
                WriteReport(exception.GetBaseException().ToString());
                exitCode = 1;
            }
            EditorApplication.ExitPlaymode();
            EditorApplication.delayCall += () => EditorApplication.Exit(exitCode);
        }

        private static void Bake()
        {
            Transform environment = GameObject.Find("Blender Environment").transform;
            TextAsset source = Resources.Load<TextAsset>("Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export");
            Dictionary<string, string> templates = JsonUtility.FromJson<Manifest>(source.text).instances
                .ToDictionary(item => item.name, item => item.template, StringComparer.Ordinal);
            List<GameObject> temporary = new();
            Dictionary<Mesh, Vector3[]> vertices = new();
            List<Prop> props = new();
            MeshCollider terrain = null;
            bool oldBackfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            try
            {
                foreach (MonoBehaviour behaviour in environment.GetComponentsInChildren<MonoBehaviour>())
                    if (behaviour.GetType().Name.Contains("Wind")) behaviour.enabled = false;
                foreach (Transform instance in environment)
                {
                    if (!instance.gameObject.activeInHierarchy) continue;
                    if (!templates.TryGetValue(instance.name, out string template)) continue;
                    int id = int.Parse(template.Substring("VM_ENV_".Length));
                    if (id == 1 || id == 36 || id == 42) continue;
                    MeshFilter filter = instance.GetComponentInChildren<MeshFilter>();
                    if (filter == null || filter.sharedMesh == null) continue;
                    GameObject probe = new("Temporary Placement Collider") { layer = Layer };
                    temporary.Add(probe);
                    probe.transform.SetParent(filter.transform, false);
                    MeshCollider collider = probe.AddComponent<MeshCollider>();
                    collider.sharedMesh = filter.sharedMesh;
                    if (id == 43) { terrain = collider; continue; }
                    bool tree = id == 2 || id == 3 || id >= 38 && id <= 40;
                    bool rock = id == 0 || id >= 4 && id <= 10;
                    bool plant = id >= 16 && id <= 34 || id == 37;
                    if (!tree && !rock && !plant) continue;
                    if (!vertices.TryGetValue(filter.sharedMesh, out Vector3[] points))
                        vertices[filter.sharedMesh] = points = filter.sharedMesh.vertices;
                    Vector3[] world = points.Select(filter.transform.TransformPoint).ToArray();
                    Vector3[] feet = FootSamples(world, tree || plant);
                    props.Add(new Prop { root = instance, collider = collider, tree = tree || plant,
                        solid = rock, original = instance.localPosition,
                        feet = feet.Select(point => point - instance.position).ToArray() });
                }
                if (terrain == null) throw new InvalidOperationException("Malha do terreno importado ausente.");
                Physics.SyncTransforms();
                Probe[] route = BuildRouteProbes();
                List<string> before = props.Where(prop => IntersectsRoute(prop, route)).Select(prop => prop.root.name).ToList();
                int grounded = 0, relocated = 0, disabled = 0;
                foreach (Prop prop in props)
                    if (Ground(prop, terrain, false)) grounded++;
                Physics.SyncTransforms();
                foreach (Prop prop in props)
                {
                    if (!IntersectsRoute(prop, route)) continue;
                    Vector3 origin = prop.root.position;
                    bool placed = false;
                    foreach (float radius in new[] { 2f, 4f, 6f, 9f, 13f, 18f, 25f })
                    {
                        for (int direction = 0; direction < 16 && !placed; direction++)
                        {
                            float angle = direction * Mathf.PI / 8f;
                            prop.root.position = origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                            if (!Ground(prop, terrain, true)) continue;
                            Physics.SyncTransforms();
                            placed = !IntersectsRoute(prop, route);
                        }
                        if (placed) break;
                    }
                    if (placed) relocated++;
                    else
                    {
                        // Retain its authored instance for reversible future adjustments.
                        prop.root.position = origin;
                        prop.root.gameObject.SetActive(false);
                        disabled++;
                    }
                }
                Physics.SyncTransforms();
                string[] remaining = props.Where(prop => prop.root.gameObject.activeSelf && IntersectsRoute(prop, route))
                    .Select(prop => prop.root.name).ToArray();
                if (remaining.Length > 0) throw new InvalidOperationException("Obstáculos restantes: " + string.Join(",", remaining));
                Correction[] entries = props.Where(prop => !prop.root.gameObject.activeSelf
                        || Vector3.Distance(prop.original, prop.root.localPosition) > 0.001f)
                    .Select(prop => new Correction { name = prop.root.name, position = prop.root.localPosition,
                        disabled = !prop.root.gameObject.activeSelf }).OrderBy(entry => entry.name, StringComparer.Ordinal).ToArray();
                string output = Path.GetFullPath(OutputAsset);
                File.WriteAllText(output, JsonUtility.ToJson(new Corrections { entries = entries }, true));
                AssetDatabase.ImportAsset(OutputAsset);
                Report report = new() { candidates = props.Count, grounded = grounded, relocated = relocated,
                    disabled = disabled, routeSamples = 2048, blockersBefore = before.ToArray(),
                    blockersAfter = remaining, entries = entries };
                WriteReport(JsonUtility.ToJson(report, true));
                Debug.Log($"[Placement Bake] PASS | candidates={props.Count}, grounded={grounded}, relocated={relocated}, "
                    + $"disabled={disabled}, before={before.Count}, after={remaining.Length}, patches={entries.Length}");
            }
            finally
            {
                Physics.queriesHitBackfaces = oldBackfaces;
                foreach (GameObject item in temporary) if (item != null) Object.DestroyImmediate(item);
            }
        }

        private static Vector3[] FootSamples(Vector3[] vertices, bool rooted)
        {
            float minimum = vertices.Min(point => point.y);
            if (rooted)
            {
                Vector3[] bottom = vertices.Where(point => point.y <= minimum + 0.12f).ToArray();
                return new[] { new Vector3(bottom.Average(point => point.x), minimum, bottom.Average(point => point.z)) };
            }
            Bounds bounds = new(vertices[0], Vector3.zero);
            foreach (Vector3 point in vertices) bounds.Encapsulate(point);
            float cell = Mathf.Max(0.2f, Mathf.Max(bounds.size.x, bounds.size.z) / 5f);
            return vertices.GroupBy(point => (Mathf.FloorToInt((point.x - bounds.min.x) / cell),
                    Mathf.FloorToInt((point.z - bounds.min.z) / cell)))
                .Select(group => group.OrderBy(point => point.y).First()).ToArray();
        }

        private static bool Ground(Prop prop, MeshCollider terrain, bool relocating)
        {
            List<float> gaps = new();
            foreach (Vector3 foot in prop.feet)
            {
                Vector3 point = prop.root.position + foot;
                if (terrain.Raycast(new Ray(new Vector3(point.x, 250f, point.z), Vector3.down), out RaycastHit hit, 500f))
                    gaps.Add(point.y - hit.point.y);
            }
            if (gaps.Count == 0) return false;
            gaps.Sort();
            // Embed most of the underside on slopes, rather than balancing a rock
            // on one low corner while the rest of its silhouette hangs in the air.
            float gap = gaps[prop.tree ? 0 : Mathf.Min(gaps.Count - 1, Mathf.FloorToInt(gaps.Count * 0.60f))];
            float delta = -gap - (prop.tree ? 0.08f : 0.18f);
            if (!relocating && (Mathf.Abs(delta) < 0.16f || !prop.tree && delta > 0f)) return false;
            prop.root.position += Vector3.up * delta;
            return true;
        }

        private static Probe[] BuildRouteProbes()
        {
            MonoBehaviour controller = Controller();
            controller.enabled = false;
            MethodInfo seek = controller.GetType().GetMethod("DeveloperSeekToProgress", BindingFlags.Instance | BindingFlags.NonPublic);
            Camera camera = Camera.main;
            Renderer[] renderers = controller.GetComponentsInChildren<Renderer>()
                .Where(renderer => renderer.enabled && !renderer.transform.IsChildOf(camera.transform)).ToArray();
            List<Probe> probes = new();
            for (int sample = 0; sample < 2048; sample++)
            {
                seek.Invoke(controller, new object[] { sample / 2048f });
                foreach (Renderer renderer in renderers)
                    probes.Add(new Probe(renderer.transform.TransformPoint(renderer.localBounds.center),
                        Vector3.Scale(renderer.localBounds.extents, Abs(renderer.transform.lossyScale)) + Vector3.one * 0.75f,
                        renderer.transform.rotation));
                probes.Add(new Probe(camera.transform.position, Vector3.one * 0.85f, controller.transform.rotation));
                probes.Add(new Probe(controller.transform.position - controller.transform.up * 0.6f,
                    new Vector3(1.15f, 0.55f, 0.3f), controller.transform.rotation));
            }
            return probes.ToArray();
        }

        private static bool IntersectsRoute(Prop prop, Probe[] route)
        {
            Bounds bounds = prop.collider.bounds;
            foreach (Probe probe in route)
            {
                if (!bounds.Intersects(probe.bounds)) continue;
                if (Physics.OverlapBox(probe.center, probe.extents, probe.rotation, Mask, QueryTriggerInteraction.Ignore)
                    .Contains(prop.collider)) return true;
                if (prop.solid && bounds.Contains(probe.center) && Inside(prop.collider, probe.center)) return true;
            }
            return false;
        }
        private static bool Inside(MeshCollider collider, Vector3 point)
        {
            foreach (Vector3 direction in new[] { new Vector3(.863f,.371f,.343f).normalized, new Vector3(-.417f,.839f,-.349f).normalized })
            {
                Vector3 start = point;
                int count = 0;
                while (count < 64 && collider.Raycast(new Ray(start, direction), out RaycastHit hit, collider.bounds.size.magnitude + 1f))
                { start += direction * (hit.distance + .002f); count++; }
                if (count % 2 == 0) return false;
            }
            return true;
        }
        private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        private static MonoBehaviour Controller() => Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None)
            .First(behaviour => behaviour.GetType().Name == "RideController");
        private static bool HasArgument(string value) => Array.IndexOf(Environment.GetCommandLineArgs(), value) >= 0;
        private static void WriteReport(string report)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-placementReport");
            if (index >= 0 && index + 1 < args.Length)
            {
                string path = Path.GetFullPath(args[index + 1]);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, report);
            }
            Debug.Log("[Placement QA] " + (report.Length < 2500 ? report : "Relatório de posições salvo."));
        }
    }
}
#endif
