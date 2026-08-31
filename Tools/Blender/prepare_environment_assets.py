import os
import shutil
from pathlib import Path

import bmesh
import bpy


ROOT = Path(os.getcwd())
MODEL_ROOT = ROOT / "Assets/_Game/Resources/Models/EnvironmentHero"


ASSETS = (
    {
        "name": "CoconutPalm",
        "source": ROOT / "SourceAssets/Sketchfab/CoconutPalm/CoconutTree_4K.glb",
        "targets": (20260, 11200, 4600),
    },
    {
        "name": "Boulder01",
        "source": ROOT / "SourceAssets/PolyHaven/Boulder01/boulder_01_2k.gltf",
        "targets": (30000, 12000, 4200),
        "weld": True,
    },
    {
        "name": "Mountainside",
        "source": ROOT / "SourceAssets/PolyHaven/Mountainside/mountainside_2k.gltf",
        "targets": (55000, 18000, 6200),
    },
)


def triangles(obj):
    return sum(max(0, len(polygon.vertices) - 2) for polygon in obj.data.polygons)


def import_asset(source):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    for obj in list(bpy.context.scene.objects):
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    return meshes


def decimate_to_target(meshes, target_triangles):
    source_triangles = sum(triangles(obj) for obj in meshes)
    if target_triangles >= source_triangles:
        return source_triangles

    for pass_index in range(3):
        current_triangles = sum(triangles(obj) for obj in meshes)
        if current_triangles <= target_triangles * 1.04:
            break
        ratio = max(0.025, target_triangles / current_triangles)
        for obj in meshes:
            if triangles(obj) < 80:
                continue
            bpy.context.view_layer.objects.active = obj
            obj.select_set(True)
            modifier = obj.modifiers.new(name=f"Quest LOD {pass_index + 1}", type="DECIMATE")
            modifier.decimate_type = "COLLAPSE"
            modifier.ratio = ratio
            modifier.use_collapse_triangulate = False
            modifier.use_symmetry = False
            bpy.ops.object.modifier_apply(modifier=modifier.name)
            obj.select_set(False)
    return sum(triangles(obj) for obj in meshes)


def weld_duplicate_vertices(meshes):
    for obj in meshes:
        mesh = bmesh.new()
        mesh.from_mesh(obj.data)
        bmesh.ops.remove_doubles(mesh, verts=mesh.verts, dist=0.000001)
        mesh.to_mesh(obj.data)
        mesh.free()
        obj.data.update()


def export_fbx(meshes, destination):
    destination.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")
    for obj in meshes:
        obj.select_set(True)
        obj.data.materials.clear()
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.export_scene.fbx(
        filepath=str(destination),
        use_selection=True,
        object_types={"MESH"},
        apply_unit_scale=True,
        bake_space_transform=True,
        axis_forward="-Z",
        axis_up="Y",
        add_leaf_bones=False,
        path_mode="STRIP",
        embed_textures=False,
        use_mesh_modifiers=True,
        use_triangles=True,
    )


def export_lods(asset):
    for level, target in enumerate(asset["targets"]):
        meshes = import_asset(asset["source"])
        if asset.get("weld"):
            weld_duplicate_vertices(meshes)
        result = decimate_to_target(meshes, target)
        destination = MODEL_ROOT / asset["name"] / f"{asset['name']}_LOD{level}.fbx"
        export_fbx(meshes, destination)
        print(f"ENV_EXPORT {asset['name']} LOD{level} tris={result} path={destination}")


def save_palm_texture():
    meshes = import_asset(ASSETS[0]["source"])
    del meshes
    image = next((candidate for candidate in bpy.data.images if candidate.size[0] > 8), None)
    if image is None:
        raise RuntimeError("Coconut palm texture was not found in the GLB")
    image.scale(2048, 2048)
    destination = MODEL_ROOT / "CoconutPalm/CoconutPalm_Albedo.png"
    destination.parent.mkdir(parents=True, exist_ok=True)
    image.filepath_raw = str(destination)
    image.file_format = "PNG"
    image.save()
    print(f"ENV_TEXTURE CoconutPalm size=2048 path={destination}")


def copy_rock_textures(name, source_dir, source_prefix):
    destination = MODEL_ROOT / name
    destination.mkdir(parents=True, exist_ok=True)
    files = {
        f"{source_prefix}_diff_2k.jpg": f"{name}_Albedo.jpg",
        f"{source_prefix}_nor_gl_2k.jpg": f"{name}_Normal.jpg",
        f"{source_prefix}_arm_2k.jpg": f"{name}_ARM.jpg",
    }
    for source_name, destination_name in files.items():
        shutil.copy2(source_dir / source_name, destination / destination_name)
        print(f"ENV_TEXTURE {name} path={destination / destination_name}")


for asset in ASSETS:
    export_lods(asset)

save_palm_texture()
copy_rock_textures(
    "Boulder01",
    ROOT / "SourceAssets/PolyHaven/Boulder01/textures",
    "boulder_01",
)
copy_rock_textures(
    "Mountainside",
    ROOT / "SourceAssets/PolyHaven/Mountainside/textures",
    "mountainside",
)
