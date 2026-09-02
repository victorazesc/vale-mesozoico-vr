"""Create a non-destructive Meta Quest 3 copy of the cinematic scene.

The PC master remains untouched. This profile keeps full density close to the
ride, thins only distant decoration, removes beauty-render-only objects and
caps expensive modifiers without replacing nearby 3D vegetation with cards.
"""

from __future__ import annotations

import hashlib
import json
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(bpy.data.filepath).resolve().parent.parent
SCRIPT_ROOT = PROJECT_ROOT / "Tools" / "Blender"
if str(SCRIPT_ROOT) not in sys.path:
    sys.path.insert(0, str(SCRIPT_ROOT))

import build_editable_scene as base  # noqa: E402
import enhance_pc_cinematic as pc  # noqa: E402


OUTPUT_PATH = PROJECT_ROOT / "Blender" / "ValeMesozoico_Quest3_Optimized.blend"
REPORT_PATH = PROJECT_ROOT / "Blender" / "Quest3" / "OptimizationReport.json"
TRACK_SAMPLES = [base.track_frame(index / 240.0)[0] for index in range(241)]

VEGETATION_TOKENS = (
    "palm",
    "pachira",
    "canopy",
    "fern",
    "foliage",
    "undergrowth",
    "groundcover",
    "bankplant",
    "wetgrass",
    "anthurium",
    "calathea",
    "shrub",
    "treesmall",
)
ROCK_TOKENS = ("boulder", "rock", "cliff", "mountain")
HERO_TOKENS = (
    "track",
    "rail",
    "support",
    "cross",
    "tie",
    "spine",
    "cart",
    "cave",
    "tyranno",
    "trex",
    "triceratops",
    "apatosaurus",
    "pteranodon",
    "dinosaur",
    "camera",
    "lagoon",
)
BEAUTY_ONLY_TOKENS = (
    "atmospheric_haze",
    "shadow_volume",
    "entrance_shadow",
    "jurassic_atmosphere",
    "diffuse_sun_mesh",
)


def stable_unit(name: str) -> float:
    digest = hashlib.blake2s(name.encode("utf-8"), digest_size=4).digest()
    return int.from_bytes(digest, "big") / 0xFFFFFFFF


def unity_position(obj: bpy.types.Object) -> Vector:
    world = obj.matrix_world.translation
    return Vector((world.x, world.z, -world.y))


def distance_to_track(obj: bpy.types.Object) -> float:
    point = unity_position(obj)
    return min((point - sample).length for sample in TRACK_SAMPLES)


def mesh_stats() -> dict[str, int]:
    meshes = {obj.data for obj in bpy.data.objects if obj.type == "MESH" and obj.data}
    return {
        "meshes": len(meshes),
        "vertices": sum(len(mesh.vertices) for mesh in meshes),
        "triangles": sum(
            max(1, len(polygon.vertices) - 2)
            for mesh in meshes
            for polygon in mesh.polygons
        ),
    }


def instance_triangle_count() -> int:
    return sum(
        sum(max(1, len(polygon.vertices) - 2) for polygon in obj.data.polygons)
        for obj in bpy.data.objects
        if obj.type == "MESH" and obj.data and not obj.hide_render
    )


def replace_heavy_canopy_meshes() -> tuple[int, int]:
    candidates: list[bpy.types.Mesh] = []
    seen: set[int] = set()
    for obj in bpy.data.objects:
        if obj.type != "MESH" or obj.data is None or "background_canopy" not in obj.name.lower():
            continue
        triangles = sum(max(1, len(polygon.vertices) - 2) for polygon in obj.data.polygons)
        pointer = obj.data.as_pointer()
        if 5_000 <= triangles <= 30_000 and pointer not in seen:
            seen.add(pointer)
            candidates.append(obj.data)
    candidates.sort(key=lambda mesh: mesh.name)
    if not candidates:
        return 0, 0

    heavy_meshes = {
        obj.data
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.data is not None
        and "background_canopy" in obj.name.lower()
        and sum(max(1, len(polygon.vertices) - 2) for polygon in obj.data.polygons) > 120_000
    }
    replaced_objects = 0
    replaced_triangles = 0
    for obj in list(bpy.data.objects):
        if obj.type != "MESH" or obj.data not in heavy_meshes:
            continue
        old_dimensions = obj.dimensions.copy()
        old_triangles = sum(max(1, len(polygon.vertices) - 2) for polygon in obj.data.polygons)
        candidate_index = min(
            len(candidates) - 1,
            int(stable_unit(obj.name) * len(candidates)),
        )
        proxy = candidates[candidate_index]
        obj.data = proxy
        new_dimensions = obj.dimensions.copy()
        for axis in range(3):
            if new_dimensions[axis] > 0.001:
                obj.scale[axis] *= old_dimensions[axis] / new_dimensions[axis]
        obj["quest3_proxy_source"] = proxy.name
        replaced_objects += 1
        replaced_triangles += old_triangles
    return replaced_objects, replaced_triangles


def should_remove(obj: bpy.types.Object) -> tuple[bool, str, float | None]:
    name = obj.name.lower()
    if bool(obj.get("quest3_disable", False)):
        return True, "explicit_quest3_disable", None
    if obj.get("pc_render_profile") == "external_beauty_only":
        return True, "external_beauty_only", None
    if any(token in name for token in BEAUTY_ONLY_TOKENS):
        return True, "cinematic_atmosphere", None
    if name.startswith(("_pc_template", "_template_")):
        return True, "library_template_object", None

    is_hero = bool(obj.get("quest3_keep", False)) or any(
        token in name for token in HERO_TOKENS
    )
    if is_hero:
        return False, "hero", 0.0

    is_vegetation = any(token in name for token in VEGETATION_TOKENS)
    is_rock = any(token in name for token in ROCK_TOKENS)
    if not is_vegetation and not is_rock:
        return False, "other", None

    track_distance = distance_to_track(obj)
    if is_vegetation:
        if track_distance <= 24.0:
            keep_probability = 1.0
        elif track_distance <= 52.0:
            keep_probability = 0.64
        else:
            keep_probability = 0.28
        return stable_unit(obj.name) > keep_probability, "vegetation_density", track_distance

    if track_distance <= 32.0:
        keep_probability = 1.0
    elif track_distance <= 65.0:
        keep_probability = 0.76
    else:
        keep_probability = 0.46
    return stable_unit(obj.name) > keep_probability, "rock_density", track_distance


def cap_modifiers(obj: bpy.types.Object, track_distance: float | None) -> None:
    near_track = track_distance is None or track_distance <= 32.0
    for modifier in obj.modifiers:
        if modifier.type == "SUBSURF":
            modifier.levels = min(modifier.levels, 1 if near_track else 0)
            modifier.render_levels = min(modifier.render_levels, 1 if near_track else 0)
        elif modifier.type == "DISPLACE" and not near_track:
            modifier.show_viewport = False
            modifier.show_render = False
        elif modifier.type == "BEVEL":
            modifier.segments = min(modifier.segments, 2 if near_track else 1)


def configure_remaining_object(obj: bpy.types.Object, reason: str, track_distance: float | None) -> None:
    if obj.get("pc_render_profile") == "enable_for_ride_interior":
        obj.hide_render = False
        obj.hide_viewport = False
    cap_modifiers(obj, track_distance)
    if obj.type == "MESH":
        obj["quest3_lod_tier"] = 0 if reason == "hero" or (track_distance or 0.0) <= 24.0 else 1
        obj["quest3_gpu_instancing"] = True
        if track_distance is not None and track_distance > 34.0 and hasattr(obj, "visible_shadow"):
            obj.visible_shadow = False


def main() -> None:
    base.TRACK_POINTS = list(pc.PC_TRACK_POINTS)
    base.height_at = pc.cinematic_height_at
    global TRACK_SAMPLES
    TRACK_SAMPLES = [base.track_frame(index / 240.0)[0] for index in range(241)]

    before_objects = len(bpy.data.objects)
    before_mesh = mesh_stats()
    removed_by_reason: dict[str, int] = {}
    decisions: list[tuple[bpy.types.Object, str, float | None]] = []

    for obj in list(bpy.data.objects):
        remove, reason, track_distance = should_remove(obj)
        if remove:
            removed_by_reason[reason] = removed_by_reason.get(reason, 0) + 1
            bpy.data.objects.remove(obj, do_unlink=True)
        else:
            decisions.append((obj, reason, track_distance))

    for obj, reason, track_distance in decisions:
        if obj.name in bpy.data.objects:
            configure_remaining_object(obj, reason, track_distance)

    proxy_objects, proxy_source_triangles = replace_heavy_canopy_meshes()

    for material in bpy.data.materials:
        material["quest3_shader_target"] = "URP Simple Lit"
        material["quest3_instancing"] = True
    for image in bpy.data.images:
        image["quest3_max_size"] = 2048 if any(
            token in image.name.lower() for token in ("dino", "trex", "cart", "track")
        ) else 1024
        image["quest3_android_format"] = "ASTC_6x6"

    scene = bpy.context.scene
    scene["quest3_profile"] = "quality_72hz_v1"
    scene["quest3_target_hz"] = 72
    scene["quest3_frame_budget_ms"] = 13.89
    scene["quest3_rendering"] = "Vulkan + MultiView + SRP foveation"
    scene["quest3_near_track_full_3d_m"] = 24.0
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_percentage = 50

    try:
        bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=True, do_recursive=True)
    except TypeError:
        bpy.data.orphans_purge(do_recursive=True)

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_PATH))
    after_mesh = mesh_stats()
    report = {
        "profile": "Meta Quest 3 quality 72 Hz",
        "source": str(PROJECT_ROOT / "Blender" / "ValeMesozoico_Editable.blend"),
        "output": str(OUTPUT_PATH),
        "frame_budget_ms": 13.89,
        "objects_before": before_objects,
        "objects_after": len(bpy.data.objects),
        "objects_removed": before_objects - len(bpy.data.objects),
        "removed_by_reason": dict(sorted(removed_by_reason.items())),
        "mesh_before": before_mesh,
        "mesh_after": after_mesh,
        "visible_instance_triangles_after": instance_triangle_count(),
        "heavy_canopy_proxy_objects": proxy_objects,
        "heavy_canopy_source_triangles_replaced": proxy_source_triangles,
        "source_bytes": (PROJECT_ROOT / "Blender" / "ValeMesozoico_Editable.blend").stat().st_size,
        "output_bytes": OUTPUT_PATH.stat().st_size,
        "quality_guards": [
            "full 3D density within 24 m of the track",
            "hero dinosaurs, cart, track, cave and lagoon never density-thinned",
            "PC master scene is not overwritten",
            "cinematic volumes and external cave mask removed",
            "ASTC and URP targets recorded for Unity import",
        ],
    }
    REPORT_PATH.parent.mkdir(parents=True, exist_ok=True)
    REPORT_PATH.write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    print("QUEST3_PROFILE_OK", json.dumps(report, sort_keys=True))


if __name__ == "__main__":
    main()
