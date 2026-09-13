using System;
using UnityEngine;

namespace ValeMesozoico
{
    [DefaultExecutionOrder(250)]
    internal sealed class BrachiosaurusHeadClearance : MonoBehaviour
    {
        private const float MaximumCorrectionPerPass = 18f;
        private const int CorrectionPasses = 3;

        private Transform _neckRoot;
        private Transform _head;
        private float _requiredClearance;
        private bool _warnedMissingRig;

        internal float CurrentClearance { get; private set; } = float.PositiveInfinity;
        internal float MinimumObservedClearance { get; private set; } = float.PositiveInfinity;
        internal float RequiredClearance => _requiredClearance;
        internal int CorrectionCount { get; private set; }

        internal static void Configure(GameObject actor, float targetHeight)
        {
            if (actor == null)
            {
                return;
            }

            BrachiosaurusHeadClearance guard = actor.GetComponent<BrachiosaurusHeadClearance>()
                ?? actor.AddComponent<BrachiosaurusHeadClearance>();
            guard._requiredClearance = Mathf.Clamp(targetHeight * 0.075f, 0.5f, 0.85f);
            guard.ResolveRig();
        }

        private void LateUpdate()
        {
            if ((_neckRoot == null || _head == null) && !ResolveRig())
            {
                return;
            }

            for (int pass = 0; pass < CorrectionPasses; pass++)
            {
                float groundHeight = OptimizedModelWorld.DinosaurGroundHeightAt(
                    _head.position.x,
                    _head.position.z);
                float missingClearance = groundHeight + _requiredClearance - _head.position.y;
                if (missingClearance <= 0f)
                {
                    break;
                }

                Vector3 neckToHead = _head.position - _neckRoot.position;
                float reach = neckToHead.magnitude;
                if (reach < 0.01f)
                {
                    break;
                }

                float currentElevation = Mathf.Asin(Mathf.Clamp(neckToHead.y / reach, -1f, 1f));
                float targetElevation = Mathf.Asin(Mathf.Clamp(
                    (groundHeight + _requiredClearance - _neckRoot.position.y) / reach,
                    -1f,
                    1f));
                float correction = Mathf.Clamp(
                    (targetElevation - currentElevation) * Mathf.Rad2Deg,
                    0f,
                    MaximumCorrectionPerPass);
                Vector3 axis = Vector3.Cross(neckToHead, Vector3.up);
                if (correction <= 0.01f || axis.sqrMagnitude < 0.0001f)
                {
                    break;
                }

                _neckRoot.Rotate(axis.normalized, correction, Space.World);
                CorrectionCount++;
            }

            float finalGroundHeight = OptimizedModelWorld.DinosaurGroundHeightAt(
                _head.position.x,
                _head.position.z);
            CurrentClearance = _head.position.y - finalGroundHeight;
            MinimumObservedClearance = Mathf.Min(MinimumObservedClearance, CurrentClearance);
        }

        private bool ResolveRig()
        {
            foreach (Transform bone in GetComponentsInChildren<Transform>(true))
            {
                if (_head == null && bone.name.IndexOf("Head_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _head = bone;
                }

                if (_neckRoot == null && bone.name.IndexOf("Neck_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _neckRoot = bone;
                }
            }

            if (_neckRoot != null && _head != null)
            {
                return true;
            }

            if (!_warnedMissingRig)
            {
                _warnedMissingRig = true;
                Debug.LogWarning($"Rig do pescoço não encontrado em {name}; proteção de solo desativada.");
            }
            return false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private void OnDisable()
        {
            if (!float.IsPositiveInfinity(MinimumObservedClearance))
            {
                Debug.Log(
                    $"[Brachiosaurus Head Clearance] {(MinimumObservedClearance >= _requiredClearance - 0.03f ? "PASS" : "FAIL")} "
                    + $"| actor={name}, minimum={MinimumObservedClearance:F2}m, required={_requiredClearance:F2}m, "
                    + $"corrections={CorrectionCount}");
            }
        }
#endif
    }
}
