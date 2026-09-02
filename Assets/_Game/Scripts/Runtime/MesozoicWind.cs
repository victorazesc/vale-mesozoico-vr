using UnityEngine;

namespace ValeMesozoico
{
    internal static class MesozoicWind
    {
        private static readonly Vector3 LagoonCenter = new(38f, 0f, 28f);
        private static readonly Vector3 PrevailingWind = new(0.32f, 0f, 0.18f);

        public static Vector3 DirectionAt(Vector3 worldPosition)
        {
            Vector3 radial = worldPosition - LagoonCenter;
            radial.y = 0f;
            if (radial.sqrMagnitude < 9f)
            {
                radial = Vector3.right;
            }

            Vector3 circularFlow = new Vector3(-radial.z, 0f, radial.x).normalized;
            return (circularFlow + PrevailingWind).normalized;
        }

        public static float Gust(float time, float phase)
        {
            float broad = Mathf.Sin(time * 0.43f + phase) * 0.24f;
            float detail = Mathf.Sin(time * 1.37f + phase * 1.73f) * 0.08f;
            return Mathf.Clamp01(0.68f + broad + detail);
        }
    }

    internal sealed class TropicalWindSway : MonoBehaviour
    {
        private Quaternion _restRotation;
        private float _phase;
        private float _amplitude;
        private float _response;
        private bool _initialized;

        internal float AppliedAngle { get; private set; }

        public void Initialize(float phase, float amplitude, float response)
        {
            _phase = phase;
            _amplitude = amplitude;
            _response = response;
            _restRotation = transform.localRotation;
            _initialized = true;
        }

        private void Awake()
        {
            if (_initialized)
            {
                return;
            }

            _restRotation = transform.localRotation;
            _phase = transform.position.x * 0.071f + transform.position.z * 0.053f;
            _amplitude = 1.6f;
            _response = 3.2f;
            _initialized = true;
        }

        private void LateUpdate()
        {
            Vector3 windWorld = MesozoicWind.DirectionAt(transform.position);
            Vector3 windLocal = transform.parent != null
                ? transform.parent.InverseTransformDirection(windWorld)
                : windWorld;
            float gust = MesozoicWind.Gust(Time.unscaledTime, _phase);
            float bend = _amplitude * Mathf.Lerp(0.42f, 1f, gust);
            float flutter = Mathf.Sin(Time.unscaledTime * 2.15f + _phase * 2.3f) * _amplitude * 0.12f;
            Quaternion target = _restRotation
                * Quaternion.Euler(windLocal.z * bend, flutter, -windLocal.x * bend);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0.1f, _response) * Time.unscaledDeltaTime);
            transform.localRotation = Quaternion.Slerp(transform.localRotation, target, blend);
            AppliedAngle = Quaternion.Angle(_restRotation, transform.localRotation);
        }
    }
}
