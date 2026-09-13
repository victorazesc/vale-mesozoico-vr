using UnityEngine;

namespace ValeMesozoico
{
    // Replace only the section between the original route's waypoints 11 and 12.
    // The cave and the rest of the route retain their authored world positions.
    internal sealed class RideInversions
    {
        internal const float LoopHeight = 18f;
        private const float LeadEnd = 0.04f;
        private const float LoopEnd = 0.67f;
        private const float CorkscrewEnd = 0.97f;
        // Support the loop's feet and vertical shoulders, leaving its crown open.
        // The corkscrew rests on its upright approach and exit.
        internal static readonly float[] SupportTimes =
        {
            Mathf.Lerp(LeadEnd, LoopEnd, 0.04f),
            Mathf.Lerp(LeadEnd, LoopEnd, 0.25f),
            Mathf.Lerp(LeadEnd, LoopEnd, 0.75f),
            Mathf.Lerp(LeadEnd, LoopEnd, 0.96f),
            Mathf.Lerp(LoopEnd, CorkscrewEnd, 0.22f),
            Mathf.Lerp(LoopEnd, CorkscrewEnd, 0.78f)
        };
        private readonly Vector3 _start, _end, _entryTangent, _exitTangent;
        internal Vector3 Forward { get; }
        internal Vector3 Right { get; }

        internal RideInversions(Vector3 start, Vector3 end, Vector3 entryTangent, Vector3 exitTangent)
        {
            _start = start;
            _end = end;
            _entryTangent = entryTangent;
            _exitTangent = exitTangent;
            Forward = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
            Right = Vector3.Cross(Vector3.up, Forward);
        }

        internal Vector3 Evaluate(float t)
        {
            if (t < LeadEnd)
                return Hermite(_start, Loop(0f), _entryTangent * 4f, Forward * 4f, t / LeadEnd);
            if (t < LoopEnd) return Loop((t - LeadEnd) / (LoopEnd - LeadEnd));
            if (t < CorkscrewEnd) return Corkscrew((t - LoopEnd) / (CorkscrewEnd - LoopEnd));
            return Hermite(Corkscrew(1f), _end, Forward * 3f, _exitTangent * 3f,
                (t - CorkscrewEnd) / (1f - CorkscrewEnd));
        }

        internal Vector3 UpHint(float t)
        {
            if (t >= LeadEnd && t <= LoopEnd)
            {
                float angle = Mathf.InverseLerp(LeadEnd, LoopEnd, t) * Mathf.PI * 2f;
                return Vector3.up * Mathf.Cos(angle) - Forward * Mathf.Sin(angle);
            }
            if (t > LoopEnd && t < CorkscrewEnd)
            {
                float angle = Ease(Mathf.InverseLerp(LoopEnd, CorkscrewEnd, t)) * 360f;
                return Quaternion.AngleAxis(angle, Forward) * Vector3.up;
            }
            return Vector3.up;
        }

        internal float BankWeight(float t) => t < LeadEnd ? 1f - Ease(t / LeadEnd)
            : t > CorkscrewEnd ? Ease((t - CorkscrewEnd) / (1f - CorkscrewEnd)) : 0f;

        internal bool IsLoop(float t) => t >= LeadEnd && t <= LoopEnd;
        internal bool IsCorkscrew(float t) => t > LoopEnd && t < CorkscrewEnd;

        private Vector3 Loop(float t)
        {
            float angle = t * Mathf.PI * 2f;
            // Separate the incoming and outgoing legs sideways so they cannot
            // intersect near the base of the loop.
            return _start + Forward * (4f + 8f * t + 12f * Mathf.Sin(angle))
                + Right * (4.4f * Ease(t)) + Vector3.up * (0.5f + LoopHeight * 0.5f * (1f - Mathf.Cos(angle)));
        }

        private Vector3 Corkscrew(float t)
        {
            float ease = Ease(t);
            float angle = ease * Mathf.PI * 2f;
            float height = Mathf.Lerp(_start.y + 0.5f, _end.y - 0.2f, ease) - _start.y;
            return _start + Forward * (12f + 24f * t)
                + Right * (4.4f * (1f - ease) + 2.8f * Mathf.Sin(angle))
                + Vector3.up * (height + 2.8f * (1f - Mathf.Cos(angle)));
        }

        private static float Ease(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * t * (t * (t * 6f - 15f) + 10f);
        }

        private static Vector3 Hermite(Vector3 a, Vector3 b, Vector3 tangentA, Vector3 tangentB, float t)
        {
            return (2f * t * t * t - 3f * t * t + 1f) * a
                + (t * t * t - 2f * t * t + t) * tangentA
                + (-2f * t * t * t + 3f * t * t) * b
                + (t * t * t - t * t) * tangentB;
        }
    }
}
