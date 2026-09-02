"""Export the approved Blender environment for the existing Unity ride scene.

Only static environment collections are exported. Track, cart, cameras, lights,
atmosphere volumes and dinosaurs stay owned by the Unity runtime.
"""

from __future__ import annotations

import json
import math
import re
import shutil
import subprocess
import tempfile
from pathlib import Path

import bpy
from mathutils import Matrix, Vector


PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = (
    PROJECT_ROOT
    / "Assets"
    / "_Game"
    / "Resources"
    / "Models"
    / "Environment"
    / "ValeMesozoicoPC"
)
OUTPUT_FBX = OUTPUT_DIR / "ValeMesozoicoEnvironment.fbx"
OUTPUT_MANIFEST = OUTPUT_DIR / "ValeMesozoicoEnvironment.export.json"
OUTPUT_TEXTURE_DIR = OUTPUT_DIR / "ValeMesozoicoEnvironment.fbm"

INCLUDED_COLLECTIONS = {
    "01_TERRAIN",
    "02_WATER_AND_SHORE",
    "04_CAVE_AND_WATERFALL",
    "05_ROCKS",
    "06_VEGETATION",
    "10_PC_CINEMATIC_VEGETATION",
    "11_PC_CINEMATIC_ROCKS",
    "16_PC_REFERENCE_POLISH",
    "PC_Lagoon_Shore_Details",
}

EXCLUDED_NAME_TOKENS = (
    "camera",
    "guide_",
    "haze",
    "track",
    "rail_",
    "wheel_grease",
    "ride_cart",
    "cartbody",
    "cartframe",
    "carttrim",
    "tyrannosaurus",
    "trex",
    "triceratops",
    "apatosaurus",
    "pteranodon",
    "pterodactyl",
)

UNITY_FROM_BLENDER = Matrix(
    (
        (1.0, 0.0, 0.0, 0.0),
        (0.0, 0.0, 1.0, 0.0),
        (0.0, -1.0, 0.0, 0.0),
        (0.0, 0.0, 0.0, 1.0),
    )
)


def is_exportable(obj: bpy.types.Object) -> bool:
    if obj.type != "MESH" or obj.hide_render or obj.name.startswith("_"):
        return False
    if not any(collection.name in INCLUDED_COLLECTIONS for collection in obj.users_collection):
        return False
    lowered = obj.name.lower()
    return not any(token in lowered for token in EXCLUDED_NAME_TOKENS)


def triangle_count(mesh: bpy.types.Mesh) -> int:
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


def rounded(value: float) -> float:
    result = round(float(value), 6)
    return 0.0 if abs(result) < 0.0000005 else result


def vector_json(value: Vector) -> dict[str, float]:
    return {"x": rounded(value.x), "y": rounded(value.y), "z": rounded(value.z)}


def object_material_signature(obj: bpy.types.Object) -> tuple[str, ...]:
    return tuple(
        slot.material.name if slot.material is not None else ""
        for slot in obj.material_slots
    )


def mesh_needs_generated_uv(obj: bpy.types.Object) -> bool:
    if len(obj.data.uv_layers) > 0:
        return False
    return any(
        material is not None
        and material.use_nodes
        and material.node_tree is not None
        and any(node.type == "TEX_IMAGE" and node.image is not None for node in material.node_tree.nodes)
        for material in obj.data.materials
    )


def add_planar_uv(mesh: bpy.types.Mesh, meters_per_tile: float = 14.0) -> None:
    spans = [
        max(vertex.co[axis] for vertex in mesh.vertices)
        - min(vertex.co[axis] for vertex in mesh.vertices)
        for axis in range(3)
    ]
    axes = sorted(range(3), key=lambda axis: spans[axis], reverse=True)[:2]
    uv_layer = mesh.uv_layers.new(name="UVMap")
    for loop in mesh.loops:
        coordinate = mesh.vertices[loop.vertex_index].co
        uv_layer.data[loop.index].uv = (
            coordinate[axes[0]] / meters_per_tile,
            coordinate[axes[1]] / meters_per_tile,
        )


def principled_node(material: bpy.types.Material):
    if not material.use_nodes or material.node_tree is None:
        return None
    return next(
        (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )


def socket_value(node, name: str, fallback):
    if node is None:
        return fallback
    socket = node.inputs.get(name)
    return socket.default_value if socket is not None else fallback


def copied_texture_name(image: bpy.types.Image | None) -> str:
    if image is None or not image.filepath:
        return ""
    source = Path(bpy.path.abspath(image.filepath)).resolve()
    if not source.is_file():
        print(f"VALE_UNITY_TEXTURE_MISSING {source}")
        return ""
    OUTPUT_TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT_TEXTURE_DIR / source.name
    if source != destination:
        shutil.copy2(source, destination)
    return source.stem


def safe_asset_stem(value: str) -> str:
    return re.sub(r"[^A-Za-z0-9_-]+", "_", value).strip("_") or "Material"


def imagemagick_executable() -> str:
    executable = shutil.which("magick")
    if executable:
        return executable
    homebrew = Path("/opt/homebrew/bin/magick")
    return str(homebrew) if homebrew.is_file() else ""


def ffmpeg_executable() -> str:
    executable = shutil.which("ffmpeg")
    if executable:
        return executable
    homebrew = Path("/opt/homebrew/bin/ffmpeg")
    return str(homebrew) if homebrew.is_file() else ""


def packed_base_alpha_texture_name(
    material_name: str,
    base_image: bpy.types.Image | None,
    alpha_image: bpy.types.Image | None,
) -> str:
    if base_image is None or alpha_image is None:
        return ""
    base_source = Path(bpy.path.abspath(base_image.filepath)).resolve()
    alpha_source = Path(bpy.path.abspath(alpha_image.filepath)).resolve()
    executable = imagemagick_executable()
    if not executable or not base_source.is_file() or not alpha_source.is_file():
        return ""
    OUTPUT_TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT_TEXTURE_DIR / f"{safe_asset_stem(material_name)}_Cutout.png"
    try:
        subprocess.run(
            [
                executable,
                str(base_source),
                str(alpha_source),
                "-alpha",
                "off",
                "-compose",
                "CopyOpacity",
                "-composite",
                str(destination),
            ],
            check=True,
            capture_output=True,
        )
    except subprocess.CalledProcessError as error:
        print(f"VALE_UNITY_ALPHA_PACK_FAILED {material_name}: {error.stderr!r}")
        return ""
    return destination.stem


def packed_metallic_smoothness_texture_name(
    material_name: str,
    roughness_image: bpy.types.Image | None,
) -> str:
    if roughness_image is None:
        return ""
    source = Path(bpy.path.abspath(roughness_image.filepath)).resolve()
    executable = imagemagick_executable()
    if not executable or not source.is_file():
        return ""
    width, height = (int(value) for value in roughness_image.size)
    OUTPUT_TEXTURE_DIR.mkdir(parents=True, exist_ok=True)
    destination = OUTPUT_TEXTURE_DIR / (
        f"{safe_asset_stem(material_name)}_MetallicSmoothness.png"
    )
    try:
        with tempfile.TemporaryDirectory(prefix="vale-roughness-") as temporary:
            processed_source = source
            if source.suffix.lower() == ".exr":
                ffmpeg = ffmpeg_executable()
                if not ffmpeg:
                    return ""
                processed_source = Path(temporary) / "roughness.png"
                subprocess.run(
                    [
                        ffmpeg,
                        "-hide_banner",
                        "-loglevel",
                        "error",
                        "-y",
                        "-i",
                        str(source),
                        "-frames:v",
                        "1",
                        str(processed_source),
                    ],
                    check=True,
                    capture_output=True,
                )
            subprocess.run(
                [
                    executable,
                    "-size",
                    f"{width}x{height}",
                    "xc:black",
                    "xc:black",
                    "xc:black",
                    "(",
                    str(processed_source),
                    "-negate",
                    "-colorspace",
                    "Gray",
                    ")",
                    "-combine",
                    str(destination),
                ],
                check=True,
                capture_output=True,
            )
    except subprocess.CalledProcessError as error:
        print(f"VALE_UNITY_ROUGHNESS_PACK_FAILED {material_name}: {error.stderr!r}")
        return ""
    return destination.stem


def material_json(material: bpy.types.Material) -> dict[str, object]:
    surface = principled_node(material)
    base_value = socket_value(surface, "Base Color", material.diffuse_color)
    alpha_value = float(socket_value(surface, "Alpha", material.diffuse_color[3]))
    metallic = float(socket_value(surface, "Metallic", material.metallic))
    roughness = float(socket_value(surface, "Roughness", material.roughness))

    base_candidates: list[bpy.types.Image] = []
    normal_candidates: list[bpy.types.Image] = []
    roughness_candidates: list[bpy.types.Image] = []
    occlusion_candidates: list[bpy.types.Image] = []
    mask_candidates: list[bpy.types.Image] = []
    alpha_candidates: list[bpy.types.Image] = []
    if material.use_nodes and material.node_tree is not None:
        for node in material.node_tree.nodes:
            if node.type != "TEX_IMAGE" or node.image is None:
                continue
            lowered = Path(node.image.filepath).name.lower()
            if "alpha" in lowered:
                alpha_candidates.append(node.image)
            elif "nor_gl" in lowered or "normal" in lowered:
                normal_candidates.append(node.image)
            elif "rough" in lowered:
                roughness_candidates.append(node.image)
            elif "occlusion" in lowered or lowered.endswith("_ao.png"):
                occlusion_candidates.append(node.image)
            elif "metallic" in lowered or "specgloss" in lowered or "_arm" in lowered:
                mask_candidates.append(node.image)
            elif any(token in lowered for token in ("diff", "albedo", "basecolor", "base_color")):
                base_candidates.append(node.image)

    base_image = base_candidates[0] if base_candidates else None
    normal_image = normal_candidates[0] if normal_candidates else None
    roughness_image = roughness_candidates[0] if roughness_candidates else None
    occlusion_image = occlusion_candidates[0] if occlusion_candidates else None
    mask_image = mask_candidates[0] if mask_candidates else None
    alpha_image = alpha_candidates[0] if alpha_candidates else None

    base_texture = copied_texture_name(base_image)
    normal_texture = copied_texture_name(normal_image)
    roughness_texture = copied_texture_name(roughness_image)
    occlusion_texture = copied_texture_name(occlusion_image)
    mask_texture = copied_texture_name(mask_image)
    alpha_texture = copied_texture_name(alpha_image)
    packed_base_texture = packed_base_alpha_texture_name(
        material.name, base_image, alpha_image
    )
    if packed_base_texture:
        base_texture = packed_base_texture
    if not mask_texture:
        mask_texture = packed_metallic_smoothness_texture_name(
            material.name, roughness_image
        )

    lowered_name = material.name.lower()
    foliage = any(
        token in lowered_name
        for token in ("leaf", "leaves", "fern", "anthurium", "calathea", "shrub", "grass", "palm")
    )
    transparent = "water" in lowered_name or "shallows" in lowered_name
    base_color = (1.0, 1.0, 1.0, alpha_value) if base_texture else base_value
    if material.name == "PC_Jurassic_Meadow_Grass_PBR":
        base_color = (0.32, 0.50, 0.16, 1.0)
    elif material.name == "M_Terrain_Jungle_PBR":
        base_color = (1.0, 1.08, 0.88, 1.0)
    elif material.name == "PC_Lagoon_Shallows_PBR":
        base_color = (0.035, 0.18, 0.16, 0.62)
    elif transparent:
        base_color = (
            base_color[0],
            base_color[1],
            base_color[2],
            min(base_color[3], material.diffuse_color[3]),
        )
    return {
        "name": material.name,
        "baseColor": {
            "r": rounded(base_color[0]),
            "g": rounded(base_color[1]),
            "b": rounded(base_color[2]),
            "a": rounded(base_color[3]),
        },
        "metallic": rounded(metallic),
        "smoothness": rounded(1.0 - roughness),
        "baseTexture": base_texture,
        "normalTexture": normal_texture,
        "roughnessTexture": roughness_texture,
        "occlusionTexture": occlusion_texture,
        "maskTexture": mask_texture,
        "alphaTexture": alpha_texture,
        "alphaClip": foliage or bool(alpha_texture),
        "transparent": transparent,
        "doubleSided": foliage,
        "cutoff": 0.38 if foliage else 0.5,
    }


def unity_transform_json(obj: bpy.types.Object) -> dict[str, object]:
    unity_matrix = UNITY_FROM_BLENDER @ obj.matrix_world @ UNITY_FROM_BLENDER.inverted()
    location, rotation, scale = unity_matrix.decompose()
    rotation.normalize()
    return {
        "position": vector_json(location),
        "rotation": {
            "x": rounded(rotation.x),
            "y": rounded(rotation.y),
            "z": rounded(rotation.z),
            "w": rounded(rotation.w),
        },
        "scale": vector_json(scale),
    }


def reference_camera_json() -> dict[str, object] | None:
    camera = bpy.context.scene.camera
    if camera is None or camera.type != "CAMERA":
        return None
    basis = UNITY_FROM_BLENDER.to_3x3()
    world_rotation = camera.matrix_world.to_quaternion()
    forward = (basis @ (world_rotation @ Vector((0.0, 0.0, -1.0)))).normalized()
    up = (basis @ (world_rotation @ Vector((0.0, 1.0, 0.0)))).normalized()
    return {
        "name": camera.name,
        "position": vector_json(UNITY_FROM_BLENDER @ camera.matrix_world.translation),
        "forward": vector_json(forward),
        "up": vector_json(up),
        "verticalFovDegrees": rounded(math.degrees(camera.data.angle_y)),
    }


def export_environment() -> None:
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.object.select_all(action="DESELECT")

    instances = sorted(
        (obj for obj in bpy.data.objects if is_exportable(obj)),
        key=lambda obj: obj.name,
    )
    if not instances:
        raise RuntimeError("No static environment objects matched the export profile")

    signature_sources: dict[tuple[int, tuple[str, ...]], bpy.types.Object] = {}
    for obj in instances:
        signature = (obj.data.as_pointer(), object_material_signature(obj))
        signature_sources.setdefault(signature, obj)

    temporary_collection = bpy.data.collections.get("__UNITY_EXPORT_TEMPLATES")
    if temporary_collection is not None:
        for obj in list(temporary_collection.objects):
            bpy.data.objects.remove(obj, do_unlink=True)
    else:
        temporary_collection = bpy.data.collections.new("__UNITY_EXPORT_TEMPLATES")
        bpy.context.scene.collection.children.link(temporary_collection)

    templates: list[dict[str, object]] = []
    template_by_signature: dict[tuple[int, tuple[str, ...]], str] = {}
    temporary_objects: list[bpy.types.Object] = []
    temporary_meshes: list[bpy.types.Mesh] = []
    for index, (signature, source) in enumerate(
        sorted(signature_sources.items(), key=lambda item: item[1].name)
    ):
        template_id = f"VM_ENV_{index:03d}"
        template = source.copy()
        if mesh_needs_generated_uv(source):
            template.data = source.data.copy()
            add_planar_uv(template.data)
            temporary_meshes.append(template.data)
        else:
            template.data = source.data
        template.name = template_id
        template.parent = None
        template.animation_data_clear()
        template.matrix_world = Matrix.Identity(4)
        temporary_collection.objects.link(template)
        template.select_set(True)
        temporary_objects.append(template)
        template_by_signature[signature] = template_id
        templates.append(
            {
                "id": template_id,
                "sourceName": source.name,
                "meshName": source.data.name,
                "materials": list(object_material_signature(source)),
                "vertexCount": len(source.data.vertices),
                "triangleCount": triangle_count(source.data),
            }
        )

    bpy.context.view_layer.objects.active = temporary_objects[0]

    unique_meshes = {obj.data for obj in instances}
    materials = {
        material
        for obj in instances
        for material in obj.data.materials
        if material is not None
    }

    try:
        result = bpy.ops.export_scene.fbx(
            filepath=str(OUTPUT_FBX),
            use_selection=True,
            use_visible=False,
            use_active_collection=False,
            global_scale=1.0,
            apply_unit_scale=True,
            apply_scale_options="FBX_SCALE_UNITS",
            use_space_transform=True,
            bake_space_transform=False,
            object_types={"MESH"},
            use_mesh_modifiers=True,
            use_mesh_modifiers_render=True,
            mesh_smooth_type="FACE",
            colors_type="SRGB",
            use_subsurf=False,
            use_mesh_edges=False,
            use_tspace=True,
            use_triangles=True,
            use_custom_props=False,
            add_leaf_bones=False,
            primary_bone_axis="Y",
            secondary_bone_axis="X",
            use_armature_deform_only=True,
            armature_nodetype="NULL",
            bake_anim=False,
            path_mode="COPY",
            embed_textures=False,
            batch_mode="OFF",
            axis_forward="-Z",
            axis_up="Y",
        )
        if "FINISHED" not in result:
            raise RuntimeError(f"FBX export failed: {result}")
    finally:
        for obj in temporary_objects:
            bpy.data.objects.remove(obj, do_unlink=True)
        for mesh in temporary_meshes:
            bpy.data.meshes.remove(mesh)
        bpy.data.collections.remove(temporary_collection)

    instance_entries = []
    unity_points = []
    for obj in instances:
        signature = (obj.data.as_pointer(), object_material_signature(obj))
        entry = {
            "template": template_by_signature[signature],
            "name": obj.name,
            **unity_transform_json(obj),
        }
        instance_entries.append(entry)
        for corner in obj.bound_box:
            blender_world = obj.matrix_world @ Vector(corner)
            unity_points.append(UNITY_FROM_BLENDER @ blender_world)

    bounds_min = Vector(
        tuple(min(point[axis] for point in unity_points) for axis in range(3))
    )
    bounds_max = Vector(
        tuple(max(point[axis] for point in unity_points) for axis in range(3))
    )

    manifest = {
        "source": str(Path(bpy.data.filepath).resolve()),
        "fbx": str(OUTPUT_FBX),
        "collections": sorted(INCLUDED_COLLECTIONS),
        "objectCount": len(instances),
        "uniqueMeshCount": len(unique_meshes),
        "uniqueVertexCount": sum(len(mesh.vertices) for mesh in unique_meshes),
        "uniqueTriangleCount": sum(triangle_count(mesh) for mesh in unique_meshes),
        "instanceTriangleCount": sum(triangle_count(obj.data) for obj in instances),
        "materialCount": len(materials),
        "materials": [material_json(material) for material in sorted(materials, key=lambda item: item.name)],
        "unityBoundsMin": vector_json(bounds_min),
        "unityBoundsMax": vector_json(bounds_max),
        "unityBoundsSize": vector_json(bounds_max - bounds_min),
        "referenceCamera": reference_camera_json(),
        "templates": templates,
        "instances": instance_entries,
        "excludedRuntimeContent": [
            "track",
            "cart",
            "cameras",
            "lights",
            "atmosphere volumes",
            "dinosaurs",
        ],
    }
    OUTPUT_MANIFEST.write_text(
        json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )
    print("VALE_UNITY_ENVIRONMENT_EXPORT_OK", json.dumps(manifest))


export_environment()
