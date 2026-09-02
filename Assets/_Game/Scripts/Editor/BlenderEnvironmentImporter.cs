#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ValeMesozoico.Editor
{
    internal static class BlenderEnvironmentImporter
    {
        private const string ModelPath =
            "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.fbx";
        private const string ManifestPath =
            "Assets/_Game/Resources/Models/Environment/ValeMesozoicoPC/ValeMesozoicoEnvironment.export.json";

        [Serializable]
        private sealed class EnvironmentManifest
        {
            public int objectCount;
            public Vector3 unityBoundsSize;
            public EnvironmentTemplate[] templates;
            public EnvironmentInstance[] instances;
        }

        [Serializable]
        private sealed class EnvironmentTemplate
        {
            public string id;
        }

        [Serializable]
        private sealed class EnvironmentInstance
        {
            public string template;
            public string name;
        }

        private static readonly string[] RequiredObjects =
        {
            "Terrain_Editable_129x129",
            "Lagoon_Water_Editable",
                "PC_FINAL_Cave_Tunnel_Interior"
        };

        private static readonly string[] ForbiddenTokens =
        {
            "rail_left",
            "rail_right",
            "central_spine",
            "track_crossties",
            "track_supports",
            "ride_cart",
            "camera_",
            "tyrannosaurus",
            "triceratops",
            "apatosaurus",
            "pteranodon"
        };

        [MenuItem("Vale Mesozoico/Assets/Validar cenário Blender PC")]
        public static void ValidateFromMenu()
        {
            Validate(false);
        }

        public static void ValidateFromCli()
        {
            Validate(true);
        }

        private static void Validate(bool exitEditor)
        {
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                AssetDatabase.ImportAsset(
                    ModelPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

                GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
                TextAsset manifestAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestPath);
                if (model == null || manifestAsset == null)
                {
                    throw new InvalidOperationException("Biblioteca FBX ou manifesto não importado.");
                }

                EnvironmentManifest manifest = JsonUtility.FromJson<EnvironmentManifest>(manifestAsset.text);
                if (manifest?.instances == null || manifest.templates == null
                    || manifest.instances.Length != manifest.objectCount
                    || manifest.objectCount < 2000)
                {
                    throw new InvalidOperationException("Manifesto de instâncias incompleto.");
                }

                Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length < 35 || renderers.Length > 100)
                {
                    throw new InvalidOperationException(
                        $"Biblioteca de malhas inesperada: {renderers.Length} renderers.");
                }

                foreach (string required in RequiredObjects)
                {
                    if (!manifest.instances.Any(instance =>
                        instance.name.StartsWith(required, StringComparison.Ordinal)))
                    {
                        throw new InvalidOperationException($"Objeto obrigatório ausente: {required}");
                    }
                }

                string forbidden = manifest.instances
                    .Select(instance => instance.name)
                    .FirstOrDefault(name => ForbiddenTokens.Any(token =>
                        name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0));
                if (!string.IsNullOrEmpty(forbidden))
                {
                    throw new InvalidOperationException(
                        $"Conteúdo de runtime foi duplicado no manifesto: {forbidden}");
                }

                HashSet<string> templateIds = manifest.templates
                    .Where(template => template != null && !string.IsNullOrEmpty(template.id))
                    .Select(template => template.id)
                    .ToHashSet(StringComparer.Ordinal);
                string missingTemplate = manifest.instances
                    .Select(instance => instance.template)
                    .FirstOrDefault(template => !templateIds.Contains(template));
                if (!string.IsNullOrEmpty(missingTemplate))
                {
                    throw new InvalidOperationException($"Template ausente: {missingTemplate}");
                }

                HashSet<Mesh> meshes = new();
                HashSet<Material> materials = new();
                foreach (Renderer renderer in renderers)
                {
                    foreach (Material material in renderer.sharedMaterials)
                    {
                        if (material != null)
                        {
                            materials.Add(material);
                        }
                    }

                    Mesh mesh = renderer is SkinnedMeshRenderer skinned
                        ? skinned.sharedMesh
                        : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
                    if (mesh != null)
                    {
                        meshes.Add(mesh);
                    }
                }

                Vector3 size = manifest.unityBoundsSize;
                if (size.x < 280f || size.x > 360f
                    || size.y < 45f || size.y > 85f
                    || size.z < 280f || size.z > 360f)
                {
                    throw new InvalidOperationException(
                        $"Escala/orientação inválida. Bounds declarados: {size}");
                }

                long vertices = meshes.Sum(mesh => (long)mesh.vertexCount);
                long triangles = meshes.Sum(mesh => (long)mesh.triangles.Length / 3L);
                Transform lagoonTemplate = model.transform.Find("VM_ENV_001");
                if (lagoonTemplate != null)
                {
                    Debug.Log(
                        "[Vale Blender Import] Template axis "
                        + $"| position={lagoonTemplate.localPosition} "
                        + $"| rotation={lagoonTemplate.localEulerAngles} "
                        + $"| scale={lagoonTemplate.localScale}");
                }
                Debug.Log(
                    "[Vale Blender Import] OK "
                    + $"| instances={manifest.objectCount:N0} "
                    + $"| libraryRenderers={renderers.Length:N0} "
                    + $"| meshes={meshes.Count:N0} "
                    + $"| vertices={vertices:N0} "
                    + $"| triangles={triangles:N0} "
                    + $"| materials={materials.Count:N0} "
                    + $"| bounds=({size.x:F2}, {size.y:F2}, {size.z:F2})");

                AssetDatabase.SaveAssets();
                if (exitEditor && Application.isBatchMode)
                {
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                if (exitEditor && Application.isBatchMode)
                {
                    EditorApplication.Exit(2);
                    return;
                }

                throw;
            }
        }
    }
}
#endif
