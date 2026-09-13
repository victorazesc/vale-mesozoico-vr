using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ValeMesozoico
{
    // The ride is procedural. Match its fixed geometry to the scene's baked renderers.
    public sealed class BakedSceneLighting : MonoBehaviour
    {
        [Serializable]
        public sealed class Entry
        {
            public string path;
            public MeshRenderer renderer;
            public Mesh mesh;
            public int lightmapIndex = -1;
            public Vector4 lightmapScaleOffset;
            public int sourceVertexCount;
            public string sourceMeshName;
        }

        public Entry[] entries = Array.Empty<Entry>();
        public Light sun;

        public static string PathOf(Transform target, Transform root)
        {
            List<string> parts = new();
            while (target != root && target != null)
            {
                int occurrence = 0;
                if (target.parent != null)
                {
                    for (int i = 0; i < target.GetSiblingIndex(); i++)
                        if (target.parent.GetChild(i).name == target.name) occurrence++;
                }
                parts.Add($"{target.name}[{occurrence}]");
                target = target.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void Apply(Transform world)
        {
            BakedSceneLighting baked = FindFirstObjectByType<BakedSceneLighting>();
            if (baked == null) return;
#if UNITY_EDITOR
            if (UnityEditor.SessionState.GetBool("Vale.Lightmap.Capturing", false))
            {
                baked.gameObject.SetActive(false);
                return;
            }
#endif
            Dictionary<string, MeshRenderer> targets = new(StringComparer.Ordinal);
            foreach (MeshRenderer renderer in world.GetComponentsInChildren<MeshRenderer>(true))
                targets[PathOf(renderer.transform, world)] = renderer;

            int applied = 0;
            int skipped = 0;
            foreach (Entry entry in baked.entries)
            {
                MeshRenderer source = entry.renderer;
                if (source == null || !targets.TryGetValue(entry.path, out MeshRenderer target)
                    || !target.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null
                    || filter.sharedMesh.vertexCount != entry.sourceVertexCount
                    || filter.sharedMesh.name != entry.sourceMeshName
                    || !SameTransform(source.transform.localToWorldMatrix, target.transform.localToWorldMatrix)
                    || entry.mesh == null || entry.lightmapIndex < 0
                    || entry.lightmapIndex >= LightmapSettings.lightmaps.Length)
                {
                    skipped++;
                    continue;
                }
                // Keep the original mesh/UV transform even when the build batches the bake scene.
                filter.sharedMesh = entry.mesh;
                target.receiveGI = ReceiveGI.Lightmaps;
                target.lightmapIndex = entry.lightmapIndex;
                target.lightmapScaleOffset = entry.lightmapScaleOffset;
                target.gameObject.isStatic = true;
                applied++;
            }

            if (baked.sun != null && RenderSettings.sun != null && RenderSettings.sun != baked.sun)
                RenderSettings.sun.bakingOutput = baked.sun.bakingOutput;
            baked.gameObject.SetActive(false);
            Debug.Log($"[Vale Lightmap] Runtime: {applied}/{baked.entries.Length} renderers com lightmap; "
                + $"{LightmapSettings.lightmaps.Length} mapas carregados.");
            if (skipped > 0)
                Debug.LogWarning($"[Vale Lightmap] {skipped} objetos alterados ou sem bake. Gere a iluminação novamente.");
        }

        private static bool SameTransform(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 16; i++)
                if (Mathf.Abs(a[i] - b[i]) > 0.002f) return false;
            return true;
        }
    }
}
