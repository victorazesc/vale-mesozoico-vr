from pathlib import Path

import bpy
import numpy as np


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE_GLB = (
    PROJECT_ROOT
    / "SourceAssets/Sketchfab/AbandonedCoasterCart/AbandonedCoasterCart_1K.glb"
)
OUTPUT_DIR = (
    PROJECT_ROOT
    / "Assets/_Game/Resources/Models/Ride/AbandonedCart"
)
OUTPUT_FBX = OUTPUT_DIR / "AbandonedCoasterCart.fbx"

MATERIALS = {
    "lambert1": ("CartFrame", "Image_7", "Image_8", "Image_9"),
    "RollerCoasterCart_blinn1SG1": ("CartTrim", "Image_4", "Image_5", "Image_6"),
    "RollerCoasterCart_blinn2SG1": ("CartBody", "Image_0", "Image_1", "Image_3"),
}


def save_image(image_name: str, filename: str) -> None:
    image = bpy.data.images.get(image_name)
    if image is None:
        raise RuntimeError(f"Imagem ausente no GLB: {image_name}")

    image.filepath_raw = str(OUTPUT_DIR / filename)
    image.file_format = "PNG"
    image.save()


def save_packed_maps(image_name: str, prefix: str) -> None:
    image = bpy.data.images.get(image_name)
    if image is None:
        raise RuntimeError(f"Mapa ORM ausente no GLB: {image_name}")

    width, height = image.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    image.pixels.foreach_get(pixels)
    rgba = pixels.reshape((-1, 4))

    occlusion_pixels = np.empty_like(rgba)
    occlusion_pixels[:, :3] = rgba[:, 0:1]
    occlusion_pixels[:, 3] = 1.0

    metallic_pixels = np.empty_like(rgba)
    metallic_pixels[:, :3] = rgba[:, 2:3]
    metallic_pixels[:, 3] = 1.0 - rgba[:, 1]

    occlusion = bpy.data.images.new(
        f"{prefix}_Occlusion",
        width=width,
        height=height,
        alpha=True,
    )
    occlusion.pixels.foreach_set(occlusion_pixels.ravel())
    occlusion.filepath_raw = str(OUTPUT_DIR / f"{prefix}_Occlusion.png")
    occlusion.file_format = "PNG"
    occlusion.save()

    metallic = bpy.data.images.new(
        f"{prefix}_MetallicSmoothness",
        width=width,
        height=height,
        alpha=True,
    )
    metallic.pixels.foreach_set(metallic_pixels.ravel())
    metallic.filepath_raw = str(OUTPUT_DIR / f"{prefix}_MetallicSmoothness.png")
    metallic.file_format = "PNG"
    metallic.save()


def main() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE_GLB))

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    for obj in list(meshes):
        if len(obj.data.polygons) <= 12:
            bpy.data.objects.remove(obj, do_unlink=True)

    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(meshes) != 3:
        raise RuntimeError(f"Estrutura inesperada: {len(meshes)} meshes")

    exported_materials = set()
    for obj in meshes:
        if not obj.material_slots or obj.material_slots[0].material is None:
            raise RuntimeError(f"Material ausente no mesh: {obj.name}")

        source_material = obj.material_slots[0].material
        material_data = MATERIALS.get(source_material.name)
        if material_data is None:
            raise RuntimeError(f"Material inesperado: {source_material.name}")

        prefix, albedo_image, orm_image, normal_image = material_data
        source_material.name = prefix
        obj.name = prefix
        obj.data.name = f"{prefix}_Mesh"
        exported_materials.add(prefix)

        save_image(albedo_image, f"{prefix}_BaseColor.png")
        save_image(normal_image, f"{prefix}_Normal.png")
        save_packed_maps(orm_image, prefix)

    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)

    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT_FBX),
        use_selection=True,
        object_types={"MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        bake_anim=False,
        path_mode="RELATIVE",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )

    triangle_count = sum(len(obj.data.loop_triangles) for obj in meshes)
    print(
        "COASTER_CART_EXPORT_OK "
        f"meshes={len(meshes)} materials={','.join(sorted(exported_materials))} "
        f"triangles={triangle_count} fbx={OUTPUT_FBX}"
    )


main()
