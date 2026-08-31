import os

import bpy


SOURCE_GLB = "/Users/victorazevedo/Downloads/pteranodon_animated_1k.glb"
PROJECT_ROOT = "/Users/victorazevedo/projetos/vale-mesozoico-vr"
OUTPUT_DIR = os.path.join(
    PROJECT_ROOT,
    "Assets/_Game/Resources/Models/Dinosaurs/Pteranodon",
)
OUTPUT_FBX = os.path.join(OUTPUT_DIR, "Pteranodon.fbx")


def save_image(image_name: str, filename: str) -> None:
    image = bpy.data.images.get(image_name)
    if image is None:
        raise RuntimeError(f"Imagem ausente no GLB: {image_name}")

    image.filepath_raw = os.path.join(OUTPUT_DIR, filename)
    image.file_format = "PNG"
    image.save()


def save_specular_smoothness() -> None:
    specular = bpy.data.images.get("Image_1")
    roughness = bpy.data.images.get("Image_1.001")
    if specular is None or roughness is None:
        raise RuntimeError("Mapas specular/roughness ausentes no GLB")

    width, height = specular.size
    output = bpy.data.images.new(
        "Pteranodon_SpecGloss",
        width=width,
        height=height,
        alpha=True,
    )
    specular_pixels = list(specular.pixels[:])
    roughness_pixels = list(roughness.pixels[:])
    output_pixels = [0.0] * len(specular_pixels)

    for index in range(0, len(specular_pixels), 4):
        output_pixels[index] = specular_pixels[index]
        output_pixels[index + 1] = specular_pixels[index + 1]
        output_pixels[index + 2] = specular_pixels[index + 2]
        output_pixels[index + 3] = 1.0 - roughness_pixels[index + 3]

    output.pixels.foreach_set(output_pixels)
    output.filepath_raw = os.path.join(OUTPUT_DIR, "Pteranodon_SpecGloss.png")
    output.file_format = "PNG"
    output.save()


def main() -> None:
    os.makedirs(OUTPUT_DIR, exist_ok=True)

    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.gltf(filepath=SOURCE_GLB)

    for obj in list(bpy.context.scene.objects):
        if obj.name == "Icosphere" and obj.type == "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)

    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(armatures) != 1 or len(meshes) != 1:
        raise RuntimeError(
            f"Estrutura inesperada: {len(armatures)} armatures, {len(meshes)} meshes"
        )

    armatures[0].name = "Pteranodon_Rig"
    meshes[0].name = "Pteranodon_Body"
    meshes[0].data.name = "Pteranodon_Body_Mesh"

    save_image("Image_0", "Pteranodon_BaseColor.png")
    save_image("Image_2", "Pteranodon_Normal.png")
    save_image("Image_3", "Pteranodon_Occlusion.png")
    save_specular_smoothness()

    bpy.context.scene.render.fps = 24
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=OUTPUT_FBX,
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.25,
        path_mode="RELATIVE",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )

    print(
        "PTERANODON_EXPORT_OK "
        f"meshes={len(meshes)} actions={','.join(action.name for action in bpy.data.actions)} "
        f"fbx={OUTPUT_FBX}"
    )


main()
