from __future__ import annotations

import math
from pathlib import Path

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
ASSET_ROOT = PROJECT_ROOT / "Blender" / "Assets" / "Sketchfab" / "TyrannosaurusRexRunning"
COLLECTION_NAME = "17_PC_TREX_HERO"
SOURCE_URL = (
    "https://sketchfab.com/3d-models/"
    "animated-tyrannosaurus-rex-dinosaur-running-loop-38007d947ae74dea83988cb0b08ee053"
)
TARGET_LENGTH_METERS = 14.0
TARGET_WORLD_XY = (75.0, 70.0)
TARGET_GROUND_Z = 7.0
TARGET_YAW_RADIANS = math.radians(15.0)


def remove_collection(name: str) -> None:
    collection = bpy.data.collections.get(name)
    if collection is None:
        return
    for obj in list(collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(collection)


def remove_stale_import_data() -> None:
    for material in list(bpy.data.materials):
        if material.users == 0 and material.name.startswith("Material_0"):
            bpy.data.materials.remove(material)
    for image in list(bpy.data.images):
        if image.users == 0 and image.name.startswith(("Image_0", "Image_1", "Image_2", "Image_3")):
            bpy.data.images.remove(image)
    action_names = {"attack_tail", "bite", "idle", "roar", "run"}
    for action in list(bpy.data.actions):
        if action.users == 0 and action.name.split(".")[0].lower() in action_names:
            bpy.data.actions.remove(action)


def find_model() -> Path:
    candidates = [
        path
        for suffix in ("*.glb", "*.gltf", "*.fbx")
        for path in ASSET_ROOT.rglob(suffix)
        if path.is_file()
    ]
    if not candidates:
        raise FileNotFoundError(f"No GLB, glTF or FBX found under {ASSET_ROOT}")
    return max(candidates, key=lambda path: path.stat().st_size)


def hierarchy_bounds(objects: list[bpy.types.Object]) -> tuple[Vector, Vector]:
    points = [
        obj.matrix_world @ Vector(corner)
        for obj in objects
        if obj.type == "MESH"
        for corner in obj.bound_box
    ]
    if not points:
        raise RuntimeError("Imported T-Rex has no mesh bounds")
    return (
        Vector(tuple(min(point[index] for point in points) for index in range(3))),
        Vector(tuple(max(point[index] for point in points) for index in range(3))),
    )


def import_model(path: Path) -> tuple[list[bpy.types.Object], list[bpy.types.Action]]:
    objects_before = set(bpy.data.objects)
    actions_before = set(bpy.data.actions)
    if path.suffix.lower() in {".glb", ".gltf"}:
        bpy.ops.import_scene.gltf(filepath=str(path), import_pack_images=True)
    else:
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    imported = [obj for obj in bpy.data.objects if obj not in objects_before]
    actions = [action for action in bpy.data.actions if action not in actions_before]
    if not imported:
        raise RuntimeError(f"Blender imported no objects from {path}")
    return imported, actions


def pose_animation(objects: list[bpy.types.Object], actions: list[bpy.types.Action]) -> str:
    armatures = [obj for obj in objects if obj.type == "ARMATURE"]
    if not armatures or not actions:
        return "static"
    preferred = next(
        (action for action in actions if "roar" in action.name.lower()),
        next(
            (action for action in actions if "run" in action.name.lower()),
            actions[0],
        ),
    )
    armature = armatures[0]
    animation_data = armature.animation_data_create()
    animation_data.action = preferred
    start, end = preferred.frame_range
    bpy.context.scene.frame_set(round(start + (end - start) * 0.46))
    return preferred.name


def polish_materials(objects: list[bpy.types.Object]) -> None:
    materials = {
        material
        for obj in objects
        if obj.type == "MESH"
        for material in obj.data.materials
        if material is not None
    }
    for material in materials:
        material.use_nodes = True
        shader = next(
            (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if shader is None:
            continue
        base_color = shader.inputs.get("Base Color")
        if base_color is not None and base_color.is_linked:
            source = base_color.links[0].from_socket
            material.node_tree.links.remove(base_color.links[0])
            color_grade = material.node_tree.nodes.new("ShaderNodeHueSaturation")
            color_grade.name = "PC_Trex_Natural_Color"
            color_grade.inputs["Saturation"].default_value = 0.55
            color_grade.inputs["Value"].default_value = 0.78
            material.node_tree.links.new(source, color_grade.inputs["Color"])
            material.node_tree.links.new(color_grade.outputs["Color"], base_color)
        roughness = shader.inputs.get("Roughness")
        if roughness is not None:
            if roughness.is_linked:
                source = roughness.links[0].from_socket
                material.node_tree.links.remove(roughness.links[0])
                roughness_range = material.node_tree.nodes.new("ShaderNodeValToRGB")
                roughness_range.name = "PC_Trex_Roughness_Range"
                roughness_range.color_ramp.elements[0].color = (0.34, 0.34, 0.34, 1.0)
                roughness_range.color_ramp.elements[1].color = (0.76, 0.76, 0.76, 1.0)
                material.node_tree.links.new(source, roughness_range.inputs["Fac"])
                material.node_tree.links.new(roughness_range.outputs["Color"], roughness)
            else:
                roughness.default_value = 0.48
        specular = shader.inputs.get("Specular IOR Level")
        if specular is not None and not specular.is_linked:
            specular.default_value = 0.34
        for obj in objects:
            if obj.type == "MESH" and any(slot == material for slot in obj.data.materials):
                for polygon in obj.data.polygons:
                    polygon.use_smooth = True


def old_hero_anchor() -> tuple[Vector, float, float]:
    old = bpy.data.objects.get("PC_Tyrannosaurus_Hero")
    if old is None:
        return Vector((-57.0, 14.0, 0.0)), 0.0, math.radians(38.0)
    hierarchy = [old] + list(old.children_recursive)
    anchor = old.matrix_world.translation.copy()
    try:
        bottom = hierarchy_bounds(hierarchy)[0].z
    except RuntimeError:
        bottom = anchor.z
    yaw = old.rotation_euler.z
    for obj in hierarchy:
        obj.hide_render = True
        obj.hide_viewport = True
    return anchor, bottom, yaw


def clear_hero_silhouette() -> None:
    for name in ("PC_Triceratops_Hero_04", "PC_Triceratops_Hero_05"):
        placeholder = bpy.data.objects.get(name)
        if placeholder is None:
            continue
        for obj in [placeholder] + list(placeholder.children_recursive):
            obj.hide_render = True
            obj.hide_viewport = True


def main() -> None:
    model_path = find_model()
    old_hero_anchor()
    remove_collection(COLLECTION_NAME)
    remove_stale_import_data()
    collection = bpy.data.collections.new(COLLECTION_NAME)
    bpy.context.scene.collection.children.link(collection)

    imported, actions = import_model(model_path)
    for obj in list(imported):
        if obj.type == "MESH" and "icos" in obj.name.lower():
            imported.remove(obj)
            bpy.data.objects.remove(obj, do_unlink=True)
    for obj in imported:
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)

    root = bpy.data.objects.new("PC_Tyrannosaurus_Realistic_Hero", None)
    collection.objects.link(root)
    top_level = [obj for obj in imported if obj.parent not in imported]
    for obj in top_level:
        matrix_world = obj.matrix_world.copy()
        obj.parent = root
        obj.matrix_world = matrix_world

    bpy.context.view_layer.update()
    bounds_min, bounds_max = hierarchy_bounds(imported)
    longest_axis = max((bounds_max - bounds_min)[:])
    root.scale = (TARGET_LENGTH_METERS / longest_axis,) * 3
    root.rotation_euler.z = TARGET_YAW_RADIANS
    bpy.context.view_layer.update()
    imported_bottom = hierarchy_bounds(imported)[0].z
    root.location = Vector(
        (TARGET_WORLD_XY[0], TARGET_WORLD_XY[1], TARGET_GROUND_Z - imported_bottom)
    )
    root["source_url"] = SOURCE_URL
    root["license"] = "CC BY 4.0"
    root["author"] = "LasquetiSpice"
    root["pc_cinematic_asset"] = True

    action_name = pose_animation(imported, actions)
    polish_materials(imported)
    clear_hero_silhouette()
    remove_stale_import_data()
    bpy.context.view_layer.update()
    bpy.ops.object.select_all(action="DESELECT")
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_mainfile()
    print(
        "PC_REALISTIC_TREX_OK "
        f"model={model_path.name} objects={len(imported)} action={action_name} "
        f"target_length={TARGET_LENGTH_METERS}"
    )


if __name__ == "__main__":
    main()
