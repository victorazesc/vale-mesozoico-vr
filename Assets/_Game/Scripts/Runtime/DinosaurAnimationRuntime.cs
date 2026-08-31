using System;
using UnityEngine;

namespace ValeMesozoico
{
    internal static class DinosaurAnimationRuntime
    {
        private const string DinosaurGroupName = "Animated 3D Dinosaurs";

        public static void RestoreAndPlay(Transform worldRoot)
        {
            Transform group = worldRoot.Find(DinosaurGroupName);
            if (group == null)
            {
                return;
            }

            foreach (Transform actor in group)
            {
                string resourcePath = ResolveResourcePath(actor.name);
                if (!string.IsNullOrEmpty(resourcePath))
                {
                    RestoreAndPlayActor(actor.gameObject, resourcePath);
                }
            }
        }

        private static void RestoreAndPlayActor(GameObject actor, string resourcePath)
        {
            bool isFlying = actor.name.StartsWith("Pteranodon", StringComparison.OrdinalIgnoreCase);
            bool isChaser = actor.name.IndexOf("Chase", StringComparison.OrdinalIgnoreCase) >= 0;
            PlayPreferred(
                actor,
                resourcePath,
                isFlying ? "fly" : isChaser ? "run" : "idle",
                isFlying ? 0.9f : isChaser ? 1.08f : 0.9f);
        }

        internal static bool PlayPreferred(GameObject actor, string preferredToken, float speed = 1f)
        {
            string resourcePath = actor != null ? ResolveResourcePath(actor.name) : null;
            return !string.IsNullOrEmpty(resourcePath)
                && PlayPreferred(actor, resourcePath, preferredToken, speed);
        }

        private static bool PlayPreferred(
            GameObject actor,
            string resourcePath,
            string preferredToken,
            float speed)
        {
            SkinnedMeshRenderer[] skinnedRenderers = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (skinnedRenderers.Length == 0)
            {
                Debug.LogWarning($"Dinossauro sem malha animada: {actor.name}");
                return false;
            }

            Animation animation = actor.GetComponentInChildren<Animation>(true);
            if (animation == null)
            {
                Transform visual = actor.transform.Find("Model Scale/Visual");
                animation = (visual != null ? visual.gameObject : actor).AddComponent<Animation>();
            }

            AnimationClip selected = SelectClip(
                Resources.LoadAll<AnimationClip>(resourcePath),
                preferredToken);
            if (selected == null || !selected.legacy)
            {
                Debug.LogWarning($"Modelo sem animação Legacy utilizável: {actor.name}");
                return false;
            }

            if (animation.GetClip(selected.name) == null)
            {
                animation.AddClip(selected, selected.name);
            }

            AnimationState state = animation[selected.name];
            if (state == null)
            {
                Debug.LogWarning($"Falha ao preparar animação idle: {actor.name}");
                return false;
            }

            state.wrapMode = WrapMode.Loop;
            float variation = Mathf.Abs(actor.name.GetHashCode() % 15) * 0.01f;
            state.speed = Mathf.Max(0.2f, speed + variation);
            animation.cullingType = AnimationCullingType.BasedOnRenderers;
            if (!animation.Play(selected.name))
            {
                Debug.LogWarning($"Falha ao iniciar animação idle: {actor.name}");
                return false;
            }

            foreach (SkinnedMeshRenderer skinned in skinnedRenderers)
            {
                skinned.enabled = true;
                skinned.updateWhenOffscreen = false;
                skinned.quality = SkinQuality.Bone2;
            }

            RemoveStaticFallbacks(actor);
            return true;
        }

        private static AnimationClip SelectClip(AnimationClip[] clips, string preferredToken)
        {
            AnimationClip fallback = null;
            foreach (AnimationClip clip in clips)
            {
                if (clip == null || clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                fallback ??= clip;
                if (clip.name.IndexOf(preferredToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return clip;
                }
            }

            return fallback;
        }

        private static void RemoveStaticFallbacks(GameObject actor)
        {
            MeshRenderer[] renderers = actor.GetComponentsInChildren<MeshRenderer>(true);
            foreach (MeshRenderer renderer in renderers)
            {
                if (!renderer.name.EndsWith(" Static Visual", StringComparison.Ordinal))
                {
                    continue;
                }

                renderer.enabled = false;
                MeshFilter filter = renderer.GetComponent<MeshFilter>();
                if (filter != null && filter.sharedMesh != null)
                {
                    UnityEngine.Object.Destroy(filter.sharedMesh);
                }
                UnityEngine.Object.Destroy(renderer.gameObject);
            }
        }

        private static string ResolveResourcePath(string actorName)
        {
            if (actorName.StartsWith("Pteranodon", StringComparison.OrdinalIgnoreCase))
            {
                return "Models/Dinosaurs/Pteranodon/Pteranodon";
            }

            if (actorName.StartsWith("Apatosaurus", StringComparison.OrdinalIgnoreCase))
            {
                return "Models/Dinosaurs/Apatosaurus";
            }

            if (actorName.StartsWith("Triceratops", StringComparison.OrdinalIgnoreCase))
            {
                return "Models/Dinosaurs/Triceratops";
            }

            return actorName.StartsWith("Tyrannosaurus Rex", StringComparison.OrdinalIgnoreCase)
                ? "Models/Dinosaurs/Trex"
                : null;
        }
    }
}
