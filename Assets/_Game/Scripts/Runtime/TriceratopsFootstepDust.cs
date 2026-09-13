using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    // Fixed pool: one dynamic mesh per animal, with no Particle System package dependency.
    internal sealed class TriceratopsFootstepDust : MonoBehaviour
    {
        private const int Capacity = 48;
        private struct Puff
        {
            internal Vector3 Position, Velocity;
            internal float Age, Lifetime, Size;
        }
        private static Material _material;
        private readonly Puff[] _puffs = new Puff[Capacity];
        private readonly Vector3[] _vertices = new Vector3[Capacity * 4];
        private readonly Color32[] _colors = new Color32[Capacity * 4];
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private int _cursor;
        internal int ActiveParticles { get; private set; }

        private void Awake()
        {
            _mesh = new Mesh { name = "Pooled Triceratops Footfall Dust" };
            _mesh.MarkDynamic();
            Vector2[] uv = new Vector2[Capacity * 4];
            int[] triangles = new int[Capacity * 6];
            for (int index = 0; index < Capacity; index++)
            {
                int v = index * 4, t = index * 6;
                uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(1, 0);
                uv[v + 2] = new Vector2(1, 1); uv[v + 3] = new Vector2(0, 1);
                triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                triangles[t + 3] = v; triangles[t + 4] = v + 3; triangles[t + 5] = v + 2;
            }
            _mesh.vertices = _vertices; _mesh.uv = uv; _mesh.colors32 = _colors;
            _mesh.triangles = triangles;
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            if (_material == null)
            {
                Shader shader = Resources.Load<Shader>("Shaders/TriceratopsSoilDust");
                if (shader == null)
                {
                    Debug.LogError("[Triceratops] Shader da poeira ausente.");
                    enabled = false;
                    return;
                }
                _material = new Material(shader) { name = "Soft Forest Soil Dust" };
            }
            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.enabled = false;
        }

        internal void Emit(Vector3 contact, float scale, int step)
        {
            for (int index = 0; index < 9; index++)
            {
                float angle = index * 2.399963f + step;
                Vector3 radial = new(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                _puffs[_cursor] = new Puff
                {
                    Position = contact + radial * 0.10f * scale + Vector3.up * 0.08f,
                    Velocity = radial * Random.Range(0.20f, 0.48f) * scale
                        + Vector3.up * Random.Range(0.14f, 0.28f),
                    Lifetime = Random.Range(0.55f, 0.95f),
                    Size = Random.Range(0.16f, 0.28f) * scale
                };
                _cursor = (_cursor + 1) % Capacity;
            }
        }

        private void LateUpdate()
        {
            Camera camera = Camera.main;
            if (camera == null) return;
            Matrix4x4 inverse = transform.worldToLocalMatrix;
            Vector3 right = inverse.MultiplyVector(camera.transform.right);
            Vector3 up = inverse.MultiplyVector(camera.transform.up);
            ActiveParticles = 0;
            for (int index = 0; index < Capacity; index++)
            {
                ref Puff puff = ref _puffs[index];
                int vertex = index * 4;
                if (puff.Lifetime <= 0f || puff.Age >= puff.Lifetime)
                {
                    for (int corner = 0; corner < 4; corner++) _vertices[vertex + corner] = Vector3.zero;
                    continue;
                }
                ActiveParticles++;
                puff.Age += Time.deltaTime;
                puff.Position += puff.Velocity * Time.deltaTime;
                float age = Mathf.Clamp01(puff.Age / puff.Lifetime);
                float size = puff.Size * Mathf.Lerp(0.6f, 2f, age);
                float alpha = Mathf.Min(age * 10f, 1f) * (1f - age) * 0.38f;
                Vector3 position = inverse.MultiplyPoint3x4(puff.Position);
                _vertices[vertex] = position - right * size - up * size;
                _vertices[vertex + 1] = position + right * size - up * size;
                _vertices[vertex + 2] = position + right * size + up * size;
                _vertices[vertex + 3] = position - right * size + up * size;
                Color32 color = new Color(0.48f, 0.40f, 0.29f, alpha);
                for (int corner = 0; corner < 4; corner++) _colors[vertex + corner] = color;
            }
            _renderer.enabled = ActiveParticles > 0;
            if (ActiveParticles == 0) return;
            _mesh.vertices = _vertices;
            _mesh.colors32 = _colors;
            _mesh.RecalculateBounds();
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
