using System;
using System.Collections.Generic;
using UnityEngine;

namespace ValeMesozoico
{
    internal sealed class EnvironmentPlacementCorrections
    {
        private const string ResourcePath = "Models/Environment/EnvironmentPlacementCorrections";

        [Serializable]
        private sealed class CorrectionManifest
        {
            public CorrectionEntry[] entries;
        }

        [Serializable]
        private sealed class CorrectionEntry
        {
            public string name;
            public CorrectionPosition position;
            public bool disabled;
        }

        [Serializable]
        private sealed class CorrectionPosition
        {
            public float x;
            public float y;
            public float z;

            public Vector3 Value => new(x, y, z);
            public bool IsFinite => IsFiniteValue(x) && IsFiniteValue(y) && IsFiniteValue(z);

            private static bool IsFiniteValue(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        private readonly Dictionary<string, CorrectionEntry> pending = new(StringComparer.Ordinal);
        private int appliedCount;
        private int disabledCount;

        public static EnvironmentPlacementCorrections LoadForAttachment()
        {
            EnvironmentPlacementCorrections corrections = new();
#if UNITY_EDITOR
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-bakeEnvironmentPlacements") >= 0)
            {
                return corrections;
            }
#endif
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                return corrections;
            }

            CorrectionManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<CorrectionManifest>(asset.text);
            }
            catch (ArgumentException exception)
            {
                Debug.LogWarning($"[Vale Placement] Correções inválidas: {exception.Message}");
                return corrections;
            }

            if (manifest?.entries == null)
            {
                Debug.LogWarning("[Vale Placement] Recurso de correções sem a lista entries.");
                return corrections;
            }

            int invalidCount = 0;
            foreach (CorrectionEntry entry in manifest.entries)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.name)
                    || entry.position == null || !entry.position.IsFinite
                    || corrections.pending.ContainsKey(entry.name))
                {
                    invalidCount++;
                    continue;
                }

                corrections.pending.Add(entry.name, entry);
            }

            if (invalidCount > 0)
            {
                Debug.LogWarning(
                    $"[Vale Placement] Ignoradas {invalidCount} correções com nome/posição inválidos ou nome duplicado.");
            }

            return corrections;
        }

        public void Apply(GameObject instance)
        {
            if (!pending.TryGetValue(instance.name, out CorrectionEntry correction))
            {
                return;
            }

            instance.transform.localPosition = correction.position.Value;
            if (correction.disabled)
            {
                instance.SetActive(false);
                disabledCount++;
            }

            pending.Remove(instance.name);
            appliedCount++;
        }

        public void Report()
        {
            if (appliedCount > 0)
            {
                Debug.Log($"[Vale Placement] Aplicadas {appliedCount} correções; ocultados {disabledCount} objetos.");
            }

            if (pending.Count > 0)
            {
                List<string> examples = new();
                foreach (string name in pending.Keys)
                {
                    examples.Add(name);
                    if (examples.Count == 5)
                    {
                        break;
                    }
                }

                Debug.LogWarning(
                    $"[Vale Placement] {pending.Count} correções sem instância correspondente: {string.Join(", ", examples)}.");
            }
        }
    }
}
