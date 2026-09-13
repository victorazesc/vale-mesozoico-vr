#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ValeMesozoico
{
    [DefaultExecutionOrder(200)]
    internal sealed class TriceratopsRuntimeQa : MonoBehaviour
    {
        private TriceratopsGrounding[] _animals;
        private float[] _worstContact;
        private int[] _samples;
        private bool _sampling;
        private float _nextSample;
        private float _nextMeshSample;
        private float[] _meshGap, _meshPenetration;
        private Mesh _baked;
        internal static bool Passed { get; private set; }
        internal static string Report { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void StartRequestedQa()
        {
            Passed = false;
            Report = "Aguardando amostragem";
            if (Environment.GetCommandLineArgs().Contains("-triceratopsPreview")
                || UnityEditor.SessionState.GetBool("ValeMesozoico.TriceratopsPreview", false))
                new GameObject("Triceratops Runtime QA").AddComponent<TriceratopsRuntimeQa>();
        }

        private IEnumerator Start()
        {
            yield return new WaitForSeconds(3f);
            TriceratopsGrounding[] animals = FindObjectsByType<TriceratopsGrounding>(FindObjectsSortMode.None);
            float[] worstContact = new float[animals.Length];
            int[] samples = new int[animals.Length];
            _animals = animals;
            _worstContact = worstContact;
            _samples = samples;
            _meshGap = new float[animals.Length];
            _meshPenetration = new float[animals.Length];
            _baked = new Mesh();
            Camera camera = Camera.main;
            TriceratopsGrounding hero = animals.FirstOrDefault(animal => animal.name == "Triceratops");
            if (hero == null) yield break;
            RideController ride = FindFirstObjectByType<RideController>();
            ride.enabled = false;
            MonoBehaviour tracker = camera.GetComponents<MonoBehaviour>().FirstOrDefault(b => b.GetType().Name == "XRHeadTracker");
            if (tracker != null) tracker.enabled = false;
            _sampling = true;
            for (int sample = 0; sample < 160; sample++)
            {
                camera.transform.position = hero.transform.position + new Vector3(11f, 5f, -11f);
                camera.transform.LookAt(hero.transform.position + Vector3.up * 1.5f);
                yield return new WaitForSeconds(0.1f);
            }
            _sampling = false;
            StringBuilder report = new();
            bool passed = animals.Length == 5;
            for (int index = 0; index < animals.Length; index++)
            {
                TriceratopsGrounding animal = animals[index];
                SkinnedMeshRenderer skin = animal.GetComponentInChildren<SkinnedMeshRenderer>();
                bool valid = animal.enabled && samples[index] >= 100 && worstContact[index] <= 0.12f
                    && _meshGap[index] <= 0.10f && _meshPenetration[index] <= 0.14f
                    && animal.Footfalls >= 4 && skin.sharedMaterial.GetTexture("_BumpMap") != null
                    && skin.sharedMesh.vertexCount > 6000 && skin.sharedMesh.vertexCount < 30000;
                passed &= valid;
                report.AppendLine($"{animal.name}: {(valid ? "PASS" : "FAIL")} length={animal.LengthMeters:F2}m "
                    + $"supportError={worstContact[index]:F3}m samples={samples[index]} steps={animal.Footfalls} "
                    + $"meshGap={_meshGap[index]:F3}m penetration={_meshPenetration[index]:F3}m "
                    + $"dustBursts={animal.DustBursts} vertices={skin.sharedMesh.vertexCount}");
                DinosaurRoamMotion roam = animal.GetComponent<DinosaurRoamMotion>();
                var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                Vector3 center = animal.transform.position;
                float radius = 0f;
                if (roam != null)
                {
                    center = (Vector3)typeof(DinosaurRoamMotion).GetField("_center", flags).GetValue(roam);
                    radius = (float)typeof(DinosaurRoamMotion).GetField("_radius", flags).GetValue(roam);
                }
                skin.BakeMesh(_baked, true);
                float bodyRadius = 0f;
                foreach (Vector3 vertex in _baked.vertices)
                {
                    Vector3 offset = skin.transform.TransformPoint(vertex) - animal.transform.position;
                    bodyRadius = Mathf.Max(bodyRadius, new Vector2(offset.x, offset.z).magnitude);
                }
                float clearance = float.MaxValue;
                for (int sample = 0; sample < 2048; sample++)
                {
                    Vector3 rail = ride.Spline.PoseAtDistance(ride.Spline.Length * sample / 2048f).Position;
                    if (rail.y > center.y + 6f) continue;
                    Vector3 offset = rail - center;
                    clearance = Mathf.Min(clearance, new Vector2(offset.x, offset.z).magnitude - bodyRadius - radius);
                }
                passed &= clearance >= 1.5f;
                report.AppendLine($"  Swept clearance to rails: {clearance:F2}m (whole roaming envelope)");
            }
            passed &= hero.DustBursts > 0;
            Passed = passed;
            Report = report.ToString();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../work/triceratops-realism"));
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, "runtime-qa.txt"), (passed ? "PASS\n" : "FAIL\n") + Report);
            Debug.Log($"[Triceratops QA] {(passed ? "PASS" : "FAIL")}\n{Report}");
        }

        private void LateUpdate()
        {
            if (!_sampling || Time.time < _nextSample) return;
            _nextSample = Time.time + 0.1f;
            bool sampleMesh = Time.time >= _nextMeshSample;
            if (sampleMesh) _nextMeshSample = Time.time + 0.5f;
            for (int index = 0; index < _animals.Length; index++)
            {
                SkinnedMeshRenderer skin = _animals[index].GetComponentInChildren<SkinnedMeshRenderer>();
                Vector3[] vertices = null;
                if (sampleMesh)
                {
                    skin.BakeMesh(_baked, true);
                    vertices = _baked.vertices;
                }
                foreach (TriceratopsGrounding.Leg leg in _animals[index].Legs)
                {
                    if (leg == null || !leg.Initialized || leg.Swing) continue;
                    Vector3 point = leg.Contact;
                    _worstContact[index] = Mathf.Max(_worstContact[index], Mathf.Abs(point.y - TriceratopsGrounding.Ground(point)));
                    _samples[index]++;
                    if (vertices == null) continue;
                    float lowest = float.MaxValue;
                    foreach (int vertex in leg.SoleVertexIndices)
                    {
                        Vector3 sole = skin.transform.TransformPoint(vertices[vertex]);
                        lowest = Mathf.Min(lowest, sole.y - TriceratopsGrounding.Ground(sole));
                    }
                    _meshGap[index] = Mathf.Max(_meshGap[index], lowest);
                    _meshPenetration[index] = Mathf.Max(_meshPenetration[index], -lowest);
                }
            }
        }

        private void OnDestroy()
        {
            if (_baked != null) Destroy(_baked);
        }
    }
}
#endif
