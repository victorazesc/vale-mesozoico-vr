"""Build the PC cinematic pass inside the currently open Vale Mesozoico .blend."""

from __future__ import annotations

import math
import random
import re
import sys
from pathlib import Path

import bpy
from mathutils import Vector


PROJECT_ROOT = Path(bpy.data.filepath).resolve().parent.parent
SCRIPT_ROOT = PROJECT_ROOT / "Tools" / "Blender"
if str(SCRIPT_ROOT) not in sys.path:
    sys.path.insert(0, str(SCRIPT_ROOT))

import build_editable_scene as base  # noqa: E402


PREVIEW_PATH = PROJECT_ROOT / "Blender" / "ValeMesozoico_PC_Cinematic_preview.png"
AUTO_RENDER_PREVIEW = False
WIREFRAME_PATH = PROJECT_ROOT / "Blender" / "References" / "Gemini_Wireframe_Guide.jpeg"
BEAUTY_PATH = PROJECT_ROOT / "Blender" / "References" / "Beauty_Target.jpeg"
ASSET_ROOT = PROJECT_ROOT / "Blender" / "Assets" / "PolyHaven"
HDRI_PATH = ASSET_ROOT / "misty_dawn" / "misty_dawn_4k.hdr"
LAGOON_CENTER_X = 38.0
LAGOON_CENTER_Z = 28.0
LAGOON_RADIUS_X = 68.0
LAGOON_RADIUS_Z = 49.0
LAGOON_WATER_LEVEL = -0.82
ORIGINAL_HEIGHT_AT = getattr(base, "_pc_original_height_at", base.height_at)
base._pc_original_height_at = ORIGINAL_HEIGHT_AT
PC_COLLECTIONS = (
    "10_PC_CINEMATIC_VEGETATION",
    "11_PC_CINEMATIC_ROCKS",
    "12_PC_CINEMATIC_HERDS",
    "13_PC_CINEMATIC_ATMOSPHERE",
    "14_PC_CINEMATIC_TRACK",
    "15_PC_CINEMATIC_DETAILS",
    "98_PC_ASSET_LIBRARY",
)

# A taller PC-only profile matching the supplied beauty shot. Unity keeps its
# gameplay spline untouched; this profile exists only in the editable .blend.
PC_TRACK_POINTS = [
    (0.0, 4.2, -56.0),
    (34.0, 5.0, -52.0),
    (64.0, 8.0, -34.0),
    (80.0, 15.0, -5.0),
    (82.0, 32.0, 25.0),
    (70.0, 46.0, 48.0),
    (57.0, 32.0, 59.0),
    (39.0, 8.0, 65.0),
    (15.0, 5.5, 76.0),
    (-22.0, 18.0, 72.0),
    (-54.0, 31.0, 50.0),
    (-72.0, 22.0, 16.0),
    (-68.0, 9.0, -20.0),
    (-54.0, 9.0, -43.0),
    (-36.0, 12.0, -57.0),
]


def cinematic_lagoon_contour(angle: float) -> float:
    return (
        1.0
        + math.sin(angle * 3.0 + 0.7) * 0.052
        + math.sin(angle * 7.0 - 0.2) * 0.031
        + math.sin(angle * 11.0 + 1.3) * 0.017
    )


def cinematic_height_at(x: float, z: float) -> float:
    """Expand the lagoon basin while preserving the original valley relief."""
    terrain_height = ORIGINAL_HEIGHT_AT(x, z)
    nx = (x - LAGOON_CENTER_X) / LAGOON_RADIUS_X
    nz = (z - LAGOON_CENTER_Z) / LAGOON_RADIUS_Z
    angle = math.atan2(nz, nx)
    distance = math.sqrt(nx * nx + nz * nz) / cinematic_lagoon_contour(angle)
    floor_progress = base.smoothstep01(base.inverse_lerp(0.10, 1.02, distance))
    lagoon_floor = -5.4 + (-0.72 + 5.4) * floor_progress
    basin_mask = 1.0 - base.smoothstep01(base.inverse_lerp(0.80, 1.08, distance))
    terrain_height = terrain_height * (1.0 - basin_mask) + lagoon_floor * basin_mask

    shore_distance = abs(distance - 1.035)
    shore_mask = 1.0 - base.smoothstep01(min(1.0, shore_distance / 0.18))
    shore_target = -0.68 + 1.34 * base.inverse_lerp(0.92, 1.18, distance)
    return terrain_height * (1.0 - shore_mask * 0.72) + shore_target * shore_mask * 0.72


def remove_collection(name: str) -> None:
    collection = bpy.data.collections.get(name)
    if collection is None:
        return
    for child in list(collection.children):
        remove_collection(child.name)
    for obj in list(collection.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.data.collections.remove(collection)


def new_collection(name: str) -> bpy.types.Collection:
    remove_collection(name)
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def move_to(obj: bpy.types.Object, collection: bpy.types.Collection) -> None:
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)


def load_image(path: Path, non_color: bool = False) -> bpy.types.Image:
    image = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    return image


def set_input(shader: bpy.types.Node, name: str, value) -> None:
    socket = shader.inputs.get(name)
    if socket is not None:
        socket.default_value = value


def image_material(
    name: str,
    diffuse: Path,
    normal: Path | None = None,
    roughness: Path | None = None,
    alpha: Path | None = None,
    roughness_value: float = 0.62,
    normal_strength: float = 0.72,
) -> bpy.types.Material:
    material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = (0.18, 0.32, 0.12, 1.0)
    if alpha is not None and hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", roughness_value)
    set_input(shader, "Specular IOR Level", 0.34)
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    diffuse_node = nodes.new("ShaderNodeTexImage")
    diffuse_node.image = load_image(diffuse)
    links.new(diffuse_node.outputs["Color"], shader.inputs["Base Color"])
    if normal is not None and normal.exists():
        normal_node = nodes.new("ShaderNodeTexImage")
        normal_node.image = load_image(normal, True)
        normal_map = nodes.new("ShaderNodeNormalMap")
        normal_map.inputs["Strength"].default_value = normal_strength
        links.new(normal_node.outputs["Color"], normal_map.inputs["Color"])
        links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    if roughness is not None and roughness.exists():
        rough_node = nodes.new("ShaderNodeTexImage")
        rough_node.image = load_image(roughness, True)
        links.new(rough_node.outputs["Color"], shader.inputs["Roughness"])
    if alpha is not None and alpha.exists():
        alpha_node = nodes.new("ShaderNodeTexImage")
        alpha_node.image = load_image(alpha, True)
        links.new(alpha_node.outputs["Color"], shader.inputs["Alpha"])
    return material


def grade_rock_material(material: bpy.types.Material) -> None:
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
    if shader is None:
        return
    base_socket = shader.inputs.get("Base Color")
    if base_socket is None or not base_socket.links:
        return
    source = base_socket.links[0].from_socket
    links.remove(base_socket.links[0])
    grade = nodes.new("ShaderNodeHueSaturation")
    grade.inputs["Saturation"].default_value = 0.62
    grade.inputs["Value"].default_value = 0.78
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 2.2
    noise.inputs["Detail"].default_value = 7.0
    noise.inputs["Roughness"].default_value = 0.76
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.24, 0.27, 0.18, 1.0)
    ramp.color_ramp.elements[1].color = (0.48, 0.55, 0.31, 1.0)
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.30
    links.new(source, grade.inputs["Color"])
    links.new(grade.outputs["Color"], mix.inputs[1])
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], mix.inputs[2])
    links.new(mix.outputs["Color"], base_socket)


def grade_base_color(material: bpy.types.Material, saturation: float, value: float) -> None:
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
    if shader is None:
        return
    socket = shader.inputs.get("Base Color")
    if socket is None or not socket.links:
        return
    source = socket.links[0].from_socket
    links.remove(socket.links[0])
    grade = nodes.new("ShaderNodeHueSaturation")
    grade.inputs["Saturation"].default_value = saturation
    grade.inputs["Value"].default_value = value
    links.new(source, grade.inputs["Color"])
    links.new(grade.outputs["Color"], socket)


def cart_pbr_material(part: str) -> bpy.types.Material:
    root = PROJECT_ROOT / "Assets/_Game/Resources/Models/Ride/AbandonedCart"
    material = bpy.data.materials.get(f"PC_Cart_{part}_PBR") or bpy.data.materials.new(
        f"PC_Cart_{part}_PBR"
    )
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Specular IOR Level", 0.38)
    base_color = nodes.new("ShaderNodeTexImage")
    base_color.image = load_image(root / f"Cart{part}_BaseColor.png")
    packed = nodes.new("ShaderNodeTexImage")
    packed.image = load_image(root / f"Cart{part}_MetallicSmoothness.png", True)
    normal = nodes.new("ShaderNodeTexImage")
    normal.image = load_image(root / f"Cart{part}_Normal.png", True)
    occlusion = nodes.new("ShaderNodeTexImage")
    occlusion.image = load_image(root / f"Cart{part}_Occlusion.png", True)
    ao_mix = nodes.new("ShaderNodeMixRGB")
    ao_mix.blend_type = "MULTIPLY"
    ao_mix.inputs[0].default_value = 0.72
    channels = nodes.new("ShaderNodeSeparateColor")
    inverse = nodes.new("ShaderNodeMath")
    inverse.operation = "SUBTRACT"
    inverse.inputs[0].default_value = 1.0
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.82
    links.new(base_color.outputs["Color"], ao_mix.inputs[1])
    links.new(occlusion.outputs["Color"], ao_mix.inputs[2])
    links.new(ao_mix.outputs["Color"], shader.inputs["Base Color"])
    links.new(packed.outputs["Color"], channels.inputs["Color"])
    links.new(channels.outputs["Red"], shader.inputs["Metallic"])
    links.new(packed.outputs["Alpha"], inverse.inputs[1])
    links.new(inverse.outputs["Value"], shader.inputs["Roughness"])
    links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    return material


def upgrade_cart() -> None:
    cart = bpy.data.objects.get("Ride_Cart_Editable")
    if cart is None:
        return
    materials = {part: cart_pbr_material(part) for part in ("Body", "Frame", "Trim")}
    for obj in cart.children_recursive:
        if obj.type != "MESH":
            continue
        part = next((key for key in materials if key.lower() in obj.name.lower()), "Body")
        obj.data.materials.clear()
        obj.data.materials.append(materials[part])


def upgrade_dinosaur_materials() -> None:
    def skin_material(
        name: str,
        dark: tuple[float, float, float, float],
        light: tuple[float, float, float, float],
        scale: float,
    ) -> bpy.types.Material:
        material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        material.use_nodes = True
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        set_input(shader, "Roughness", 0.67)
        set_input(shader, "Specular IOR Level", 0.27)
        coordinates = nodes.new("ShaderNodeTexCoord")
        pattern = nodes.new("ShaderNodeTexNoise")
        pattern.inputs["Scale"].default_value = scale
        pattern.inputs["Detail"].default_value = 8.0
        pattern.inputs["Roughness"].default_value = 0.74
        pores = nodes.new("ShaderNodeTexNoise")
        pores.inputs["Scale"].default_value = scale * 7.5
        pores.inputs["Detail"].default_value = 5.0
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].position = 0.26
        ramp.color_ramp.elements[0].color = dark
        ramp.color_ramp.elements[1].position = 0.74
        ramp.color_ramp.elements[1].color = light
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.34
        bump.inputs["Distance"].default_value = 0.055
        links.new(coordinates.outputs["Generated"], pattern.inputs["Vector"])
        links.new(coordinates.outputs["Generated"], pores.inputs["Vector"])
        links.new(pattern.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
        links.new(pores.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], shader.inputs["Normal"])
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        return material

    materials = {
        "Apatosaurus": skin_material(
            "PC_Apatosaurus_Skin_PBR",
            (0.075, 0.082, 0.055, 1.0),
            (0.27, 0.25, 0.17, 1.0),
            3.4,
        ),
        "Triceratops": skin_material(
            "PC_Triceratops_Skin_PBR",
            (0.075, 0.060, 0.042, 1.0),
            (0.30, 0.23, 0.15, 1.0),
            4.8,
        ),
        "Tyrannosaurus": skin_material(
            "PC_Tyrannosaurus_Skin_PBR",
            (0.065, 0.033, 0.018, 1.0),
            (0.27, 0.12, 0.045, 1.0),
            4.2,
        ),
        "Pteranodon": skin_material(
            "PC_Pteranodon_Skin_PBR",
            (0.050, 0.030, 0.024, 1.0),
            (0.24, 0.14, 0.080, 1.0),
            5.2,
        ),
    }
    assignments = {
        "Apatosaurus_01": materials["Apatosaurus"],
        "Apatosaurus_02": materials["Apatosaurus"],
        "Triceratops_01": materials["Triceratops"],
        "Triceratops_02": materials["Triceratops"],
        "Tyrannosaurus": materials["Tyrannosaurus"],
        "Pteranodon_01": materials["Pteranodon"],
        "Pteranodon_02": materials["Pteranodon"],
    }
    for root_name, material in assignments.items():
        root = bpy.data.objects.get(root_name)
        if root is None:
            continue
        for obj in [root] + list(root.children_recursive):
            if obj.type != "MESH":
                continue
            obj.data.materials.clear()
            obj.data.materials.append(material)
            for polygon in obj.data.polygons:
                polygon.use_smooth = True


def upgrade_terrain() -> None:
    terrain = bpy.data.objects.get("Terrain_Editable_129x129")
    if terrain is not None and terrain.type == "MESH":
        for vertex in terrain.data.vertices:
            unity_x = vertex.co.x
            unity_z = -vertex.co.y
            vertex.co.z = cinematic_height_at(unity_x, unity_z)
        terrain.data.update()
        terrain["pc_cinematic_lagoon_radius"] = (LAGOON_RADIUS_X, LAGOON_RADIUS_Z)

    material = bpy.data.materials.get("M_Terrain_Jungle_PBR")
    if material is None:
        return
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", 0.84)
    set_input(shader, "Specular IOR Level", 0.20)

    coordinates = nodes.new("ShaderNodeTexCoord")
    mapping_lush = nodes.new("ShaderNodeMapping")
    mapping_lush.inputs["Scale"].default_value = (31.0, 31.0, 31.0)
    mapping_soil = nodes.new("ShaderNodeMapping")
    mapping_soil.inputs["Scale"].default_value = (18.0, 18.0, 18.0)
    mapping_soil.inputs["Rotation"].default_value[2] = math.radians(37.0)
    lush = nodes.new("ShaderNodeTexImage")
    lush.image = load_image(
        PROJECT_ROOT / "Assets/_Game/Resources/Textures/Realistic/JungleGround_Lush_Albedo.png"
    )
    soil = nodes.new("ShaderNodeTexImage")
    soil.image = load_image(
        PROJECT_ROOT / "Assets/_Game/Resources/Textures/Realistic/JungleGround_Albedo.png"
    )
    normal = nodes.new("ShaderNodeTexImage")
    normal.image = load_image(
        PROJECT_ROOT / "Assets/_Game/Resources/Textures/Realistic/JungleGround_Normal.png", True
    )

    macro = nodes.new("ShaderNodeTexNoise")
    macro.inputs["Scale"].default_value = 1.35
    macro.inputs["Detail"].default_value = 5.5
    macro.inputs["Roughness"].default_value = 0.68
    macro.inputs["Distortion"].default_value = 0.24
    ground_mix = nodes.new("ShaderNodeMixRGB")
    ground_mix.blend_type = "MIX"
    grade = nodes.new("ShaderNodeHueSaturation")
    grade.inputs["Saturation"].default_value = 0.95
    grade.inputs["Value"].default_value = 1.12
    macro_color = nodes.new("ShaderNodeValToRGB")
    macro_color.color_ramp.elements[0].position = 0.20
    macro_color.color_ramp.elements[0].color = (0.10, 0.18, 0.036, 1.0)
    macro_color.color_ramp.elements[1].position = 0.80
    macro_color.color_ramp.elements[1].color = (0.34, 0.48, 0.095, 1.0)
    macro_bias = nodes.new("ShaderNodeMapRange")
    macro_bias.inputs["From Min"].default_value = 0.0
    macro_bias.inputs["From Max"].default_value = 1.0
    macro_bias.inputs["To Min"].default_value = 0.18
    macro_bias.inputs["To Max"].default_value = 0.94
    macro_bias.clamp = True
    natural_tint = nodes.new("ShaderNodeMixRGB")
    natural_tint.blend_type = "MIX"
    natural_tint.inputs[0].default_value = 0.08

    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.92
    micro = nodes.new("ShaderNodeTexNoise")
    micro.inputs["Scale"].default_value = 72.0
    micro.inputs["Detail"].default_value = 3.0
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.26
    bump.inputs["Distance"].default_value = 0.065

    links.new(coordinates.outputs["Generated"], mapping_lush.inputs["Vector"])
    links.new(coordinates.outputs["Generated"], mapping_soil.inputs["Vector"])
    links.new(mapping_lush.outputs["Vector"], lush.inputs["Vector"])
    links.new(mapping_lush.outputs["Vector"], normal.inputs["Vector"])
    links.new(mapping_soil.outputs["Vector"], soil.inputs["Vector"])
    links.new(coordinates.outputs["Generated"], macro.inputs["Vector"])
    links.new(coordinates.outputs["Generated"], micro.inputs["Vector"])
    links.new(macro.outputs["Fac"], macro_bias.inputs["Value"])
    links.new(macro_bias.outputs["Result"], ground_mix.inputs[0])
    links.new(soil.outputs["Color"], ground_mix.inputs[1])
    links.new(lush.outputs["Color"], ground_mix.inputs[2])
    links.new(ground_mix.outputs["Color"], grade.inputs["Color"])
    links.new(macro.outputs["Fac"], macro_color.inputs["Fac"])
    links.new(grade.outputs["Color"], natural_tint.inputs[1])
    links.new(macro_color.outputs["Color"], natural_tint.inputs[2])
    links.new(natural_tint.outputs["Color"], shader.inputs["Base Color"])
    links.new(normal.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bump.inputs["Normal"])
    links.new(micro.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])


def upgrade_track() -> None:
    material = bpy.data.materials.get("M_Track_Red_Steel_PBR")
    if material is not None:
        material.use_nodes = True
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        set_input(shader, "Metallic", 0.74)
        set_input(shader, "Roughness", 0.34)
        set_input(shader, "Coat Weight", 0.18)
        noise = nodes.new("ShaderNodeTexNoise")
        noise.inputs["Scale"].default_value = 5.6
        noise.inputs["Detail"].default_value = 6.0
        ramp = nodes.new("ShaderNodeValToRGB")
        ramp.color_ramp.elements[0].color = (0.03, 0.004, 0.002, 1.0)
        ramp.color_ramp.elements[1].color = (0.22, 0.018, 0.006, 1.0)
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.22
        bump.inputs["Distance"].default_value = 0.08
        links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
        links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
        links.new(noise.outputs["Fac"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], shader.inputs["Normal"])
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    for name, depth in (("Rail_Left", 0.135), ("Rail_Right", 0.135), ("Central_Spine", 0.22)):
        obj = bpy.data.objects.get(name)
        if obj is not None and obj.type == "CURVE":
            obj.data.bevel_depth = depth
            obj.data.bevel_resolution = 5


def build_cinematic_track(
    collection: bpy.types.Collection,
    guide_collection: bpy.types.Collection,
) -> None:
    original = bpy.data.collections.get("03_TRACK_EDITABLE")
    if original is not None:
        original.hide_render = True
        original.hide_viewport = True
        for obj in original.all_objects:
            obj.hide_render = True

    rail = bpy.data.materials.get("M_Track_Red_Steel_PBR")
    grease = bpy.data.materials.get("M_Wheel_Grease")
    supports = bpy.data.materials.get("M_Track_Support_Dark")
    if rail is None or grease is None or supports is None:
        return
    base.create_track(collection, guide_collection, rail, grease, supports)
    for obj in collection.objects:
        if obj.type != "CURVE":
            continue
        if obj.name.startswith(("Rail_Left", "Rail_Right")):
            obj.data.bevel_depth = 0.15
            obj.data.bevel_resolution = 5
        elif obj.name.startswith("Central_Spine"):
            obj.data.bevel_depth = 0.24
            obj.data.bevel_resolution = 5


def upgrade_water() -> None:
    material = bpy.data.materials.get("M_Lagoon_Water")
    if material is None:
        return
    material.use_nodes = True
    if hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Base Color", (0.012, 0.105, 0.095, 1.0))
    set_input(shader, "Roughness", 0.16)
    set_input(shader, "IOR", 1.333)
    set_input(shader, "Transmission Weight", 0.025)
    set_input(shader, "Specular IOR Level", 0.52)
    set_input(shader, "Alpha", 0.96)
    set_input(shader, "Coat Weight", 0.30)
    coordinates = nodes.new("ShaderNodeTexCoord")
    large = nodes.new("ShaderNodeTexNoise")
    large.inputs["Scale"].default_value = 0.7
    large.inputs["Detail"].default_value = 4.0
    small = nodes.new("ShaderNodeTexNoise")
    small.inputs["Scale"].default_value = 8.0
    small.inputs["Detail"].default_value = 3.0
    mix = nodes.new("ShaderNodeMixRGB")
    mix.blend_type = "MULTIPLY"
    mix.inputs[0].default_value = 0.38
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.22
    bump.inputs["Distance"].default_value = 0.12
    water_color = nodes.new("ShaderNodeValToRGB")
    water_color.color_ramp.elements[0].position = 0.28
    water_color.color_ramp.elements[0].color = (0.004, 0.016, 0.015, 1.0)
    water_color.color_ramp.elements[1].position = 0.76
    water_color.color_ramp.elements[1].color = (0.018, 0.060, 0.057, 1.0)
    links.new(coordinates.outputs["Generated"], large.inputs["Vector"])
    links.new(coordinates.outputs["Generated"], small.inputs["Vector"])
    links.new(large.outputs["Fac"], mix.inputs[1])
    links.new(small.outputs["Fac"], mix.inputs[2])
    links.new(large.outputs["Fac"], water_color.inputs["Fac"])
    links.new(water_color.outputs["Color"], shader.inputs["Base Color"])
    links.new(mix.outputs["Color"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    water = bpy.data.objects.get("Lagoon_Water_Editable")
    if water is not None and water.type == "MESH" and len(water.data.vertices) > 4:
        segments = len(water.data.vertices) - 1
        water.data.vertices[0].co = base.unity_to_blender(
            (LAGOON_CENTER_X, LAGOON_WATER_LEVEL, LAGOON_CENTER_Z)
        )
        for index in range(segments):
            angle = index / segments * math.tau
            contour = cinematic_lagoon_contour(angle)
            water.data.vertices[index + 1].co = base.unity_to_blender(
                (
                    LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour,
                    LAGOON_WATER_LEVEL,
                    LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour,
                )
            )
        water.data.update()
        water["pc_cinematic_lagoon"] = "expanded_absolute_v3"

    shore = bpy.data.objects.get("Wet_Shoreline_Transition")
    if shore is not None and shore.type == "MESH" and len(shore.data.vertices) % 2 == 0:
        segments = len(shore.data.vertices) // 2
        for ring, scale in enumerate((0.985, 1.14)):
            for index in range(segments):
                angle = index / segments * math.tau
                contour = cinematic_lagoon_contour(angle)
                x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * scale
                z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * scale
                y = LAGOON_WATER_LEVEL + 0.035 if ring == 0 else cinematic_height_at(x, z) + 0.025
                shore.data.vertices[ring * segments + index].co = base.unity_to_blender((x, y, z))
        shore.data.update()
        shore["pc_cinematic_lagoon"] = "soft_transition_v3"

        bank_material = bpy.data.materials.get("PC_Lagoon_Wet_Bank_PBR") or bpy.data.materials.new(
            "PC_Lagoon_Wet_Bank_PBR"
        )
        bank_material.use_nodes = True
        bank_nodes = bank_material.node_tree.nodes
        bank_links = bank_material.node_tree.links
        bank_nodes.clear()
        bank_output = bank_nodes.new("ShaderNodeOutputMaterial")
        bank_shader = bank_nodes.new("ShaderNodeBsdfPrincipled")
        set_input(bank_shader, "Roughness", 0.70)
        set_input(bank_shader, "Specular IOR Level", 0.24)
        bank_coord = bank_nodes.new("ShaderNodeTexCoord")
        bank_mapping = bank_nodes.new("ShaderNodeMapping")
        bank_mapping.inputs["Scale"].default_value = (22.0, 22.0, 22.0)
        bank_texture = bank_nodes.new("ShaderNodeTexImage")
        bank_texture.image = load_image(
            PROJECT_ROOT / "Assets/_Game/Resources/Textures/Realistic/JungleGround_Lush_Albedo.png"
        )
        bank_grade = bank_nodes.new("ShaderNodeHueSaturation")
        bank_grade.inputs["Saturation"].default_value = 0.72
        bank_grade.inputs["Value"].default_value = 0.66
        bank_noise = bank_nodes.new("ShaderNodeTexNoise")
        bank_noise.inputs["Scale"].default_value = 3.8
        bank_noise.inputs["Detail"].default_value = 5.0
        bank_bump = bank_nodes.new("ShaderNodeBump")
        bank_bump.inputs["Strength"].default_value = 0.24
        bank_bump.inputs["Distance"].default_value = 0.075
        bank_links.new(bank_coord.outputs["Generated"], bank_mapping.inputs["Vector"])
        bank_links.new(bank_mapping.outputs["Vector"], bank_texture.inputs["Vector"])
        bank_links.new(bank_texture.outputs["Color"], bank_grade.inputs["Color"])
        bank_links.new(bank_grade.outputs["Color"], bank_shader.inputs["Base Color"])
        bank_links.new(bank_coord.outputs["Generated"], bank_noise.inputs["Vector"])
        bank_links.new(bank_noise.outputs["Fac"], bank_bump.inputs["Height"])
        bank_links.new(bank_bump.outputs["Normal"], bank_shader.inputs["Normal"])
        bank_links.new(bank_shader.outputs["BSDF"], bank_output.inputs["Surface"])
        shore.data.materials.clear()
        shore.data.materials.append(bank_material)

        old_shallows = bpy.data.objects.get("PC_Lagoon_Shallows")
        if old_shallows is not None:
            bpy.data.objects.remove(old_shallows, do_unlink=True)
        shallows_material = bpy.data.materials.get(
            "PC_Lagoon_Shallows_PBR"
        ) or bpy.data.materials.new("PC_Lagoon_Shallows_PBR")
        shallows_material.use_nodes = True
        if hasattr(shallows_material, "surface_render_method"):
            shallows_material.surface_render_method = "DITHERED"
        shallow_nodes = shallows_material.node_tree.nodes
        shallow_links = shallows_material.node_tree.links
        shallow_nodes.clear()
        shallow_output = shallow_nodes.new("ShaderNodeOutputMaterial")
        shallow_mix = shallow_nodes.new("ShaderNodeMixShader")
        shallow_transparent = shallow_nodes.new("ShaderNodeBsdfTransparent")
        shallow_shader = shallow_nodes.new("ShaderNodeBsdfPrincipled")
        set_input(shallow_shader, "Roughness", 0.54)
        set_input(shallow_shader, "Specular IOR Level", 0.30)
        shallow_fade = shallow_nodes.new("ShaderNodeVertexColor")
        shallow_fade.layer_name = "shore_fade"
        shallow_coord = shallow_nodes.new("ShaderNodeTexCoord")
        shallow_noise = shallow_nodes.new("ShaderNodeTexNoise")
        shallow_noise.inputs["Scale"].default_value = 4.2
        shallow_noise.inputs["Detail"].default_value = 5.0
        shallow_color = shallow_nodes.new("ShaderNodeValToRGB")
        shallow_color.color_ramp.elements[0].position = 0.22
        shallow_color.color_ramp.elements[0].color = (0.022, 0.050, 0.016, 1.0)
        shallow_color.color_ramp.elements[1].position = 0.78
        shallow_color.color_ramp.elements[1].color = (0.155, 0.125, 0.042, 1.0)
        shallow_bump = shallow_nodes.new("ShaderNodeBump")
        shallow_bump.inputs["Strength"].default_value = 0.16
        shallow_bump.inputs["Distance"].default_value = 0.08
        shallow_links.new(shallow_coord.outputs["Generated"], shallow_noise.inputs["Vector"])
        shallow_links.new(shallow_noise.outputs["Fac"], shallow_color.inputs["Fac"])
        shallow_links.new(shallow_color.outputs["Color"], shallow_shader.inputs["Base Color"])
        shallow_links.new(shallow_noise.outputs["Fac"], shallow_bump.inputs["Height"])
        shallow_links.new(shallow_bump.outputs["Normal"], shallow_shader.inputs["Normal"])
        shallow_links.new(shallow_fade.outputs["Color"], shallow_mix.inputs[0])
        shallow_links.new(shallow_transparent.outputs["BSDF"], shallow_mix.inputs[1])
        shallow_links.new(shallow_shader.outputs["BSDF"], shallow_mix.inputs[2])
        shallow_links.new(shallow_mix.outputs["Shader"], shallow_output.inputs["Surface"])

        shallow_vertices: list[Vector] = []
        shallow_faces: list[tuple[int, int, int, int]] = []
        shallow_scales = (0.78, 0.91, 1.015)
        for scale in shallow_scales:
            for index in range(segments):
                angle = index / segments * math.tau
                contour = cinematic_lagoon_contour(angle)
                x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * scale
                z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * scale
                y = LAGOON_WATER_LEVEL + 0.050 + math.sin(angle * 5.0) * 0.006
                shallow_vertices.append(base.unity_to_blender((x, y, z)))
        for ring in range(len(shallow_scales) - 1):
            for index in range(segments):
                next_index = (index + 1) % segments
                current = ring * segments
                following = (ring + 1) * segments
                shallow_faces.append(
                    (current + index, current + next_index, following + next_index, following + index)
                )
        shallow_collection = shore.users_collection[0]
        shallows = base.create_mesh_object(
            "PC_Lagoon_Shallows",
            shallow_collection,
            shallow_vertices,
            shallow_faces,
            shallows_material,
        )
        fade = shallows.data.color_attributes.new(
            name="shore_fade", type="FLOAT_COLOR", domain="POINT"
        )
        fade_values = (0.0, 0.32, 0.96)
        for index, color in enumerate(fade.data):
            opacity = fade_values[min(len(fade_values) - 1, index // segments)]
            color.color = (opacity, opacity, opacity, 1.0)
        shallows["pc_cinematic_lagoon"] = "organic_translucent_shallows_v2"


def normalize_template(obj: bpy.types.Object, collection: bpy.types.Collection, name: str) -> None:
    move_to(obj, collection)
    obj.name = name
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    bounds = [Vector(corner) for corner in obj.bound_box]
    center_x = (min(p.x for p in bounds) + max(p.x for p in bounds)) * 0.5
    center_y = (min(p.y for p in bounds) + max(p.y for p in bounds)) * 0.5
    min_z = min(p.z for p in bounds)
    for vertex in obj.data.vertices:
        vertex.co -= Vector((center_x, center_y, min_z))
    obj.location = (0.0, 0.0, 0.0)
    obj.hide_render = True
    obj.hide_viewport = True


def import_variants(
    path: Path,
    collection: bpy.types.Collection,
    prefix: str,
    material: bpy.types.Material,
) -> list[bpy.types.Object]:
    bpy.ops.object.select_all(action="DESELECT")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    imported = [obj for obj in bpy.data.objects if obj not in before]
    templates = []
    for obj in [candidate for candidate in imported if candidate.type == "MESH"]:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.data.materials.clear()
        obj.data.materials.append(material)
        normalize_template(obj, collection, f"_PC_TEMPLATE_{prefix}_{len(templates) + 1:02}")
        templates.append(obj)
    for obj in imported:
        if obj.type != "MESH":
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    return templates


def import_pachira(
    collection: bpy.types.Collection,
    bark: bpy.types.Material,
    leaves: bpy.types.Material,
) -> list[bpy.types.Object]:
    path = ASSET_ROOT / "pachira_aquatica_01" / "pachira_aquatica_01_2k.fbx"
    bpy.ops.object.select_all(action="DESELECT")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    imported = [obj for obj in bpy.data.objects if obj not in before]
    meshes = [obj for obj in imported if obj.type == "MESH"]

    def source_name(obj: bpy.types.Object) -> str:
        return re.sub(r"\.\d{3}$", "", obj.name).lower()

    groups = {
        suffix: [obj for obj in meshes if source_name(obj).endswith(f"_{suffix}")]
        for suffix in ("a", "b", "c", "d")
    }
    for obj in [candidate for candidate in imported if candidate.type != "MESH"]:
        bpy.data.objects.remove(obj, do_unlink=True)
    templates = []
    for suffix, parts in groups.items():
        if not parts:
            continue
        bpy.ops.object.select_all(action="DESELECT")
        for part in parts:
            world = part.matrix_world.copy()
            part.parent = None
            part.matrix_world = world
            part.data.materials.clear()
            part.data.materials.append(bark if "bark" in part.name.lower() else leaves)
            part.select_set(True)
        bpy.context.view_layer.objects.active = parts[0]
        bpy.ops.object.join()
        joined = bpy.context.object
        normalize_template(joined, collection, f"_PC_TEMPLATE_Pachira_{suffix.upper()}")
        templates.append(joined)
    for obj in [
        candidate
        for candidate in bpy.data.objects
        if candidate not in before and candidate.type == "MESH" and candidate not in templates
    ]:
        bpy.data.objects.remove(obj, do_unlink=True)
    bpy.ops.object.select_all(action="DESELECT")
    return templates


def remove_pachira_import_leaks() -> int:
    leaked = [
        obj
        for obj in bpy.data.objects
        if obj.name.lower().startswith("pachira_aquatica_01_")
    ]
    for obj in leaked:
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in list(bpy.data.meshes):
        if mesh.users == 0 and mesh.name.lower().startswith("pachira_aquatica_01_"):
            bpy.data.meshes.remove(mesh)
    return len(leaked)


def safe_static_template(
    name: str,
    path: Path,
    collection: bpy.types.Collection,
    material: bpy.types.Material | None,
) -> bpy.types.Object | None:
    bpy.ops.object.select_all(action="DESELECT")
    result = base.import_static_template(name, path, collection, material)
    bpy.ops.object.select_all(action="DESELECT")
    return result


def duplicate_template(
    template: bpy.types.Object,
    collection: bpy.types.Collection,
    name: str,
    x: float,
    z: float,
    height: float,
    yaw: float,
    width: float = 1.0,
    depth: float = 1.0,
) -> bpy.types.Object:
    obj = template.copy()
    obj.data = template.data
    collection.objects.link(obj)
    obj.name = name
    obj.hide_render = False
    obj.hide_viewport = False
    obj.location = base.unity_to_blender((x, base.height_at(x, z) - 0.08, z))
    obj.rotation_euler = (0.0, 0.0, math.radians(yaw))
    scale = height / max(0.001, template.dimensions.z)
    obj.scale = (scale * width, scale * depth, scale)
    return obj


def lagoon_distance(x: float, z: float) -> float:
    nx = (x - LAGOON_CENTER_X) / LAGOON_RADIUS_X
    nz = (z - LAGOON_CENTER_Z) / LAGOON_RADIUS_Z
    angle = math.atan2(nz, nx)
    return math.sqrt(
        ((x - LAGOON_CENTER_X) / LAGOON_RADIUS_X) ** 2
        + ((z - LAGOON_CENTER_Z) / LAGOON_RADIUS_Z) ** 2
    ) / cinematic_lagoon_contour(angle)


def create_grass_clump_template(collection: bpy.types.Collection) -> bpy.types.Object:
    material = bpy.data.materials.get("PC_Jurassic_Meadow_Grass_PBR") or bpy.data.materials.new(
        "PC_Jurassic_Meadow_Grass_PBR"
    )
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", 0.76)
    set_input(shader, "Specular IOR Level", 0.18)
    object_info = nodes.new("ShaderNodeObjectInfo")
    color = nodes.new("ShaderNodeValToRGB")
    color.color_ramp.elements[0].color = (0.022, 0.050, 0.009, 1.0)
    color.color_ramp.elements[1].color = (0.105, 0.195, 0.036, 1.0)
    links.new(object_info.outputs["Random"], color.inputs["Fac"])
    links.new(color.outputs["Color"], shader.inputs["Base Color"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    rng = random.Random(73119)
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int, int]] = []
    for _blade in range(14):
        angle = rng.uniform(0.0, math.tau)
        radius = rng.uniform(0.02, 0.42)
        center_x = math.cos(angle) * radius
        center_y = math.sin(angle) * radius
        height = rng.uniform(0.58, 1.18)
        width = rng.uniform(0.035, 0.072)
        lean = rng.uniform(0.04, 0.18)
        top_x = center_x + math.cos(angle) * lean
        top_y = center_y + math.sin(angle) * lean
        for plane_angle in (angle, angle + math.pi * 0.5):
            side_x = math.cos(plane_angle) * width
            side_y = math.sin(plane_angle) * width
            start = len(vertices)
            vertices.extend(
                [
                    (center_x - side_x, center_y - side_y, 0.0),
                    (center_x + side_x, center_y + side_y, 0.0),
                    (top_x + side_x * 0.10, top_y + side_y * 0.10, height),
                    (top_x - side_x * 0.10, top_y - side_y * 0.10, height),
                ]
            )
            faces.append((start, start + 1, start + 2, start + 3))
    mesh = bpy.data.meshes.new("PC_Jurassic_Meadow_Grass_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    grass = bpy.data.objects.new("_PC_TEMPLATE_Jurassic_Meadow_Grass", mesh)
    collection.objects.link(grass)
    grass.data.materials.append(material)
    grass.hide_render = True
    grass.hide_viewport = True
    return grass


def populate_vegetation(
    collection: bpy.types.Collection,
    library: bpy.types.Collection,
) -> None:
    # Replace the original palm-only scatter; keeping it doubled was the main
    # reason the valley looked repetitive in the cinematic composition.
    for obj in bpy.data.objects:
        if obj.name.startswith("Palm_") and not obj.name.startswith("PC_"):
            obj.hide_render = True
            obj.hide_viewport = True

    fern_dir = ASSET_ROOT / "fern_02" / "textures"
    anth_dir = ASSET_ROOT / "anthurium_botany_01" / "textures"
    pachira_dir = ASSET_ROOT / "pachira_aquatica_01" / "textures"
    calathea_dir = ASSET_ROOT / "calathea_orbifolia_01" / "textures"
    shrub_dir = ASSET_ROOT / "shrub_02" / "textures"
    tree_dir = ASSET_ROOT / "tree_small_02" / "textures"
    island_tree_dir = ASSET_ROOT / "island_tree_03" / "textures"
    fern_mat = image_material(
        "PC_PolyHaven_Fern02_CC0",
        fern_dir / "fern_02_diff_2k.jpg",
        fern_dir / "fern_02_nor_gl_2k.exr",
        fern_dir / "fern_02_rough_2k.exr",
        fern_dir / "fern_02_alpha_2k.png",
        0.58,
    )
    anth_mat = image_material(
        "PC_PolyHaven_Anthurium_CC0",
        anth_dir / "anthurium_botany_01_diff_2k.jpg",
        anth_dir / "anthurium_botany_01_nor_gl_2k.exr",
        anth_dir / "anthurium_botany_01_rough_2k.exr",
        anth_dir / "anthurium_botany_01_alpha_2k.png",
        0.48,
    )
    leaf_mat = image_material(
        "PC_PolyHaven_PachiraLeaves_CC0",
        pachira_dir / "pachira_aquatica_01_leaves_diff_2k.png",
        pachira_dir / "pachira_aquatica_01_leaves_nor_gl_2k.png",
        pachira_dir / "pachira_aquatica_01_leaves_rough_2k.png",
        pachira_dir / "pachira_aquatica_01_leaves_alpha_2k.png",
        0.46,
    )
    bark_mat = image_material(
        "PC_PolyHaven_PachiraBark_CC0",
        pachira_dir / "pachira_aquatica_01_bark_diff_2k.png",
        pachira_dir / "pachira_aquatica_01_bark_nor_gl_2k.png",
        pachira_dir / "pachira_aquatica_01_bark_rough_2k.png",
        roughness_value=0.72,
    )
    calathea_mat = image_material(
        "PC_PolyHaven_Calathea_CC0",
        calathea_dir / "calathea_orbifolia_01_diff_2k.jpg",
        calathea_dir / "calathea_orbifolia_01_nor_gl_2k.exr",
        calathea_dir / "calathea_orbifolia_01_rough_2k.exr",
        calathea_dir / "calathea_orbifolia_01_alpha_2k.png",
        0.52,
    )
    shrub_mat = image_material(
        "PC_PolyHaven_Shrub02_CC0",
        shrub_dir / "shrub_02_diff_2k.jpg",
        shrub_dir / "shrub_02_nor_gl_2k.exr",
        shrub_dir / "shrub_02_rough_2k.exr",
        shrub_dir / "shrub_02_alpha_2k.png",
        0.56,
    )
    tree_branch_mat = image_material(
        "PC_PolyHaven_TreeSmall02_Branch_CC0",
        tree_dir / "tree_small_02_branch_diff_1k.png",
        tree_dir / "tree_small_02_branch_nor_gl_1k.png",
        tree_dir / "tree_small_02_branch_rough_1k.png",
        roughness_value=0.70,
        normal_strength=0.84,
    )
    tree_leaf_mat = image_material(
        "PC_PolyHaven_TreeSmall02_Leaves_CC0",
        tree_dir / "tree_small_02_leaves_diff_1k.png",
        tree_dir / "tree_small_02_leaves_nor_gl_1k.png",
        tree_dir / "tree_small_02_leaves_rough_1k.png",
        tree_dir / "tree_small_02_leaves_alpha_1k.png",
        0.50,
        0.72,
    )
    tree_trunk_mat = image_material(
        "PC_PolyHaven_TreeSmall02_Trunk_CC0",
        tree_dir / "tree_small_02_diff_1k.jpg",
        tree_dir / "tree_small_02_nor_gl_1k.exr",
        tree_dir / "tree_small_02_rough_1k.exr",
        roughness_value=0.76,
        normal_strength=0.88,
    )
    island_branch_mat = image_material(
        "PC_PolyHaven_IslandTree03_Branch_CC0",
        island_tree_dir / "island_tree_03_branches_diff_1k.png",
        island_tree_dir / "island_tree_03_branches_nor_gl_1k.png",
        island_tree_dir / "island_tree_03_branches_rough_1k.png",
        roughness_value=0.72,
        normal_strength=0.86,
    )
    island_leaf_mat = image_material(
        "PC_PolyHaven_IslandTree03_Leaves_CC0",
        island_tree_dir / "island_tree_03_leaves_diff_1k.png",
        island_tree_dir / "island_tree_03_leaves_nor_gl_1k.png",
        island_tree_dir / "island_tree_03_leaves_rough_1k.png",
        island_tree_dir / "island_tree_03_leaves_alpha_1k.png",
        0.52,
        0.74,
    )
    island_trunk_mat = image_material(
        "PC_PolyHaven_IslandTree03_Trunk_CC0",
        island_tree_dir / "island_tree_03_diff_1k.jpg",
        island_tree_dir / "island_tree_03_nor_gl_1k.exr",
        island_tree_dir / "island_tree_03_rough_1k.exr",
        roughness_value=0.78,
        normal_strength=0.90,
    )
    palm_mat = image_material(
        "PC_CoconutPalm_LOD0",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/CoconutPalm_Albedo.png",
        alpha=PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/CoconutPalm_Albedo.png",
        roughness_value=0.58,
    )
    grade_base_color(fern_mat, 0.86, 0.86)
    grade_base_color(anth_mat, 0.88, 0.82)
    grade_base_color(leaf_mat, 0.82, 0.80)
    grade_base_color(bark_mat, 0.72, 0.74)
    grade_base_color(calathea_mat, 0.84, 0.80)
    grade_base_color(shrub_mat, 0.78, 0.76)
    grade_base_color(tree_branch_mat, 0.72, 0.72)
    grade_base_color(tree_leaf_mat, 0.82, 0.78)
    grade_base_color(tree_trunk_mat, 0.68, 0.70)
    grade_base_color(island_branch_mat, 0.78, 0.78)
    grade_base_color(island_leaf_mat, 0.90, 0.86)
    grade_base_color(island_trunk_mat, 0.72, 0.76)
    grade_base_color(palm_mat, 0.74, 0.76)
    ferns = import_variants(
        ASSET_ROOT / "fern_02" / "fern_02_2k.fbx", library, "Fern", fern_mat
    )
    anthuriums = import_variants(
        ASSET_ROOT / "anthurium_botany_01" / "anthurium_botany_01_2k.fbx",
        library,
        "Anthurium",
        anth_mat,
    )
    calatheas = import_variants(
        ASSET_ROOT / "calathea_orbifolia_01" / "calathea_orbifolia_01_2k.fbx",
        library,
        "Calathea",
        calathea_mat,
    )
    shrubs = import_variants(
        ASSET_ROOT / "shrub_02" / "shrub_02_2k.fbx",
        library,
        "Shrub02",
        shrub_mat,
    )
    pachiras = import_pachira(library, bark_mat, leaf_mat)
    broadleaf_tree = safe_static_template(
        "PC_TreeSmall02_CC0",
        ASSET_ROOT / "tree_small_02" / "tree_small_02_1k.fbx",
        library,
        None,
    )
    if broadleaf_tree is not None:
        for slot_index, slot in enumerate(broadleaf_tree.data.materials):
            slot_name = slot.name.lower() if slot is not None else ""
            if "leaves" in slot_name:
                broadleaf_tree.data.materials[slot_index] = tree_leaf_mat
            elif "branch" in slot_name:
                broadleaf_tree.data.materials[slot_index] = tree_branch_mat
            else:
                broadleaf_tree.data.materials[slot_index] = tree_trunk_mat
    island_tree = safe_static_template(
        "PC_IslandTree03_CC0",
        ASSET_ROOT / "island_tree_03" / "island_tree_03_1k.fbx",
        library,
        None,
    )
    if island_tree is not None:
        for slot_index, slot in enumerate(island_tree.data.materials):
            slot_name = slot.name.lower() if slot is not None else ""
            if "leaves" in slot_name:
                island_tree.data.materials[slot_index] = island_leaf_mat
            elif "branches" in slot_name:
                island_tree.data.materials[slot_index] = island_branch_mat
            else:
                island_tree.data.materials[slot_index] = island_trunk_mat
    palm = safe_static_template(
        "PC_CoconutPalm_LOD0",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/CoconutPalm_LOD0.fbx",
        library,
        palm_mat,
    )
    high_templates = pachiras[:1]
    if broadleaf_tree is not None:
        high_templates.extend([broadleaf_tree, broadleaf_tree, broadleaf_tree])
    if island_tree is not None:
        high_templates.extend([island_tree, island_tree, island_tree])
    if palm is not None:
        high_templates.extend([palm, palm])
    low_templates = ferns + anthuriums + calatheas + shrubs
    grass_template = create_grass_clump_template(library)
    rng = random.Random(20260831)

    placed = 0
    while placed < 165 and high_templates:
        angle = rng.uniform(0.0, math.tau)
        x = 10.0 + math.cos(angle) * rng.uniform(60.0, 128.0)
        z = 12.0 + math.sin(angle) * rng.uniform(54.0, 116.0)
        if abs(x) > 145.0 or abs(z) > 145.0:
            continue
        if lagoon_distance(x, z) < 1.20 or base.nearest_track_distance(x, z) < 12.0:
            continue
        if -60.0 < x < 86.0 and -96.0 < z < -18.0:
            continue
        if -72.0 < x < 96.0 and 52.0 < z < 138.0:
            continue
        duplicate_template(
            rng.choice(high_templates),
            collection,
            f"PC_Canopy_{placed + 1:03}",
            x,
            z,
            rng.uniform(13.0, 28.0),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.82, 1.18),
            rng.uniform(0.82, 1.18),
        )
        placed += 1

    placed = 0
    while placed < 420 and low_templates:
        x = rng.uniform(-110.0, 116.0)
        z = rng.uniform(-96.0, 110.0)
        if lagoon_distance(x, z) < 1.13 or base.nearest_track_distance(x, z) < 5.6:
            continue
        if -54.0 < x < 84.0 and -94.0 < z < -18.0 and rng.random() < 0.76:
            continue
        if -55.0 < x < 82.0 and -20.0 < z < 78.0 and rng.random() < 0.82:
            continue
        duplicate_template(
            rng.choice(low_templates),
            collection,
            f"PC_Undergrowth_{placed + 1:03}",
            x,
            z,
            rng.uniform(0.8, 2.9),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.78, 1.34),
            rng.uniform(0.78, 1.34),
        )
        placed += 1

    # Dense three-dimensional floor cover removes the empty textured carpet
    # look while preserving the lagoon and roller-coaster sight lines.
    placed = 0
    attempts = 0
    while placed < 420 and low_templates and attempts < 4200:
        attempts += 1
        x = rng.uniform(-86.0, 112.0)
        z = rng.uniform(-58.0, 102.0)
        if lagoon_distance(x, z) < 1.075 or base.nearest_track_distance(x, z) < 4.0:
            continue
        ground_cover = duplicate_template(
            rng.choice(low_templates),
            collection,
            f"PC_Meadow_Groundcover_{placed + 1:03}",
            x,
            z,
            rng.uniform(0.60, 2.15),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.62, 1.28),
            rng.uniform(0.62, 1.24),
        )
        if hasattr(ground_cover, "visible_shadow"):
            ground_cover.visible_shadow = False
        placed += 1

    grass_rng = random.Random(19077)
    placed = 0
    attempts = 0
    while placed < 1050 and attempts < 10500:
        attempts += 1
        x = grass_rng.uniform(-98.0, 118.0)
        z = grass_rng.uniform(-76.0, 106.0)
        if lagoon_distance(x, z) < 1.035 or base.nearest_track_distance(x, z) < 3.1:
            continue
        grass = duplicate_template(
            grass_template,
            collection,
            f"PC_Meadow_Grass_{placed + 1:03}",
            x,
            z,
            grass_rng.uniform(0.72, 1.55),
            grass_rng.uniform(0.0, 360.0),
            grass_rng.uniform(0.96, 1.58),
            grass_rng.uniform(0.92, 1.48),
        )
        if hasattr(grass, "visible_shadow"):
            grass.visible_shadow = False
        placed += 1

    # A broken, organic fringe hides the geometric boundary between terrain
    # and water and gives the enlarged lagoon a believable wet bank.
    bank_templates = ferns + shrubs + calatheas
    for index in range(240):
        angle = index / 240.0 * math.tau + rng.uniform(-0.024, 0.024)
        radius = rng.uniform(1.025, 1.105)
        contour = cinematic_lagoon_contour(angle)
        x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * radius
        z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * radius
        if base.nearest_track_distance(x, z) < 3.8:
            continue
        bank_plant = duplicate_template(
            rng.choice(bank_templates or low_templates),
            collection,
            f"PC_Lagoon_BankPlant_{index + 1:03}",
            x,
            z,
            rng.uniform(1.35, 4.30),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.72, 1.20),
            rng.uniform(0.68, 1.10),
        )
        if hasattr(bank_plant, "visible_shadow"):
            bank_plant.visible_shadow = False

    for index in range(360):
        angle = index / 360.0 * math.tau + rng.uniform(-0.018, 0.018)
        radius = rng.uniform(1.012, 1.085)
        contour = cinematic_lagoon_contour(angle)
        x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * radius
        z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * radius
        if base.nearest_track_distance(x, z) < 3.2:
            continue
        wet_grass = duplicate_template(
            grass_template,
            collection,
            f"PC_Lagoon_WetGrass_{index + 1:03}",
            x,
            z,
            rng.uniform(1.00, 2.35),
            rng.uniform(0.0, 360.0),
            rng.uniform(1.00, 1.70),
            rng.uniform(0.92, 1.48),
        )
        if hasattr(wet_grass, "visible_shadow"):
            wet_grass.visible_shadow = False

    # Dense, camera-side understory frames the valley like the beauty target,
    # while preserving a clean corridor around the procedural track.
    placed = 0
    while placed < 76 and low_templates:
        x = rng.uniform(-132.0, 132.0)
        z = rng.uniform(88.0, 138.0)
        if base.nearest_track_distance(x, z) < 6.4:
            continue
        duplicate_template(
            rng.choice(low_templates),
            collection,
            f"PC_Foreground_Undergrowth_{placed + 1:03}",
            x,
            z,
            rng.uniform(2.2, 6.8),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.92, 1.48),
            rng.uniform(0.88, 1.34),
        )
        placed += 1

    for index in range(145):
        x = rng.uniform(-128.0, 126.0)
        z = rng.uniform(-132.0, -88.0)
        if base.nearest_track_distance(x, z) < 5.5:
            continue
        background_tree = duplicate_template(
            rng.choice(high_templates),
            collection,
            f"PC_Background_Canopy_{index + 1:03}",
            x,
            z,
            rng.uniform(13.0, 24.0),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.78, 1.12),
            rng.uniform(0.78, 1.12),
        )
        if hasattr(background_tree, "visible_shadow"):
            background_tree.visible_shadow = False

    heroes = [
        (-118.0, -46.0, 17.5, 22.0),
        (-96.0, -58.0, 19.0, 58.0),
        (-74.0, -75.0, 14.5, 105.0),
        (-47.0, -92.0, 17.0, 146.0),
        (91.0, -52.0, 16.5, 225.0),
        (113.0, -26.0, 21.0, 278.0),
        (126.0, 7.0, 15.5, 316.0),
    ]
    for index, (x, z, height, yaw) in enumerate(heroes):
        if broadleaf_tree is not None and index in {0, 2, 4, 6}:
            hero_template = broadleaf_tree
        else:
            hero_template = high_templates[(index * 3 + 1) % len(high_templates)]
        duplicate_template(
            hero_template,
            collection,
            f"PC_Hero_Foliage_{index + 1:02}",
            x,
            z,
            height,
            yaw,
            1.10 + (index % 3) * 0.08,
            0.94 + (index % 2) * 0.10,
        )


def build_cinematic_cave(
    collection: bpy.types.Collection,
    boulder: bpy.types.Object,
    moss_rocks: list[bpy.types.Object],
    mountain: bpy.types.Object | None,
) -> None:
    obsolete_cave_parts = (
        "Waterfall_Cave_Editable",
        "Waterfall_Editable",
        "Waterfall_Cliff_1",
        "Waterfall_Cliff_2",
        "Waterfall_Boulder_1",
        "Waterfall_Boulder_2",
        "Waterfall_Boulder_3",
        "Waterfall_Boulder_4",
        "Waterfall_Boulder_5",
        "Waterfall_Boulder_6",
    )
    for object_name in obsolete_cave_parts:
        obj = bpy.data.objects.get(object_name)
        if obj is not None:
            obj.hide_render = True
            obj.hide_viewport = True

    portal_rock = bpy.data.materials.get("PC_Cave_Entrance_Rock_PBR") or bpy.data.materials.new(
        "PC_Cave_Entrance_Rock_PBR"
    )
    portal_rock.use_nodes = True
    nodes = portal_rock.node_tree.nodes
    links = portal_rock.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", 0.84)
    set_input(shader, "Metallic", 0.0)
    set_input(shader, "Coat Weight", 0.08)
    set_input(shader, "Coat Roughness", 0.34)
    coordinates = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (3.2, 3.2, 3.2)
    texture_root = ASSET_ROOT / "rock_moss_set_01" / "textures"
    albedo = nodes.new("ShaderNodeTexImage")
    albedo.image = load_image(texture_root / "rock_moss_set_01_diff_2k.jpg")
    albedo.projection = "BOX"
    albedo.projection_blend = 0.34
    roughness = nodes.new("ShaderNodeTexImage")
    roughness.image = load_image(
        texture_root / "rock_moss_set_01_rough_2k.jpg", non_color=True
    )
    roughness.projection = "BOX"
    roughness.projection_blend = 0.34
    normal_texture = nodes.new("ShaderNodeTexImage")
    normal_texture.image = load_image(
        texture_root / "rock_moss_set_01_nor_gl_2k.exr", non_color=True
    )
    normal_texture.projection = "BOX"
    normal_texture.projection_blend = 0.34
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.82
    detail_noise = nodes.new("ShaderNodeTexNoise")
    detail_noise.inputs["Scale"].default_value = 18.0
    detail_noise.inputs["Detail"].default_value = 6.0
    detail_noise.inputs["Roughness"].default_value = 0.78
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.34
    bump.inputs["Distance"].default_value = 0.08
    links.new(coordinates.outputs["Generated"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], albedo.inputs["Vector"])
    links.new(mapping.outputs["Vector"], roughness.inputs["Vector"])
    links.new(mapping.outputs["Vector"], normal_texture.inputs["Vector"])
    links.new(coordinates.outputs["Generated"], detail_noise.inputs["Vector"])
    links.new(albedo.outputs["Color"], shader.inputs["Base Color"])
    links.new(roughness.outputs["Color"], shader.inputs["Roughness"])
    links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
    links.new(normal_map.outputs["Normal"], bump.inputs["Normal"])
    links.new(detail_noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    tunnel_rock = portal_rock.copy()
    tunnel_rock.name = "PC_Cave_Tunnel_Rock_PBR"
    tunnel_nodes = tunnel_rock.node_tree.nodes
    tunnel_links = tunnel_rock.node_tree.links
    tunnel_shader = next(
        node for node in tunnel_nodes if node.bl_idname == "ShaderNodeBsdfPrincipled"
    )
    tunnel_bump = next(node for node in tunnel_nodes if node.bl_idname == "ShaderNodeBump")
    for link in list(tunnel_links):
        if link.to_node == tunnel_bump and link.to_socket.name == "Normal":
            tunnel_links.remove(link)
    tunnel_bump.inputs["Strength"].default_value = 0.22
    tunnel_bump.inputs["Distance"].default_value = 0.05
    set_input(tunnel_shader, "Coat Weight", 0.03)
    set_input(tunnel_shader, "Specular IOR Level", 0.22)
    tunnel_albedo = next(
        node
        for node in tunnel_nodes
        if node.bl_idname == "ShaderNodeTexImage"
        and node.image is not None
        and "_diff_" in Path(node.image.filepath).name
    )
    for link in list(tunnel_links):
        if link.to_node == tunnel_shader and link.to_socket.name in {
            "Base Color",
            "Emission Color",
        }:
            tunnel_links.remove(link)
    tunnel_grade = tunnel_nodes.new("ShaderNodeHueSaturation")
    tunnel_grade.inputs["Saturation"].default_value = 0.58
    tunnel_grade.inputs["Value"].default_value = 0.52
    tunnel_links.new(tunnel_albedo.outputs["Color"], tunnel_grade.inputs["Color"])
    tunnel_links.new(tunnel_grade.outputs["Color"], tunnel_shader.inputs["Base Color"])
    tunnel_links.new(tunnel_grade.outputs["Color"], tunnel_shader.inputs["Emission Color"])
    set_input(tunnel_shader, "Emission Strength", 0.035)

    portal_liner_rock = tunnel_rock.copy()
    portal_liner_rock.name = "PC_Cave_Portal_Liner_PBR"
    # The ride camera sees the inner side of this shell. Keep it genuinely
    # double-sided so frame twisting cannot create black transparent wedges.

    dark = bpy.data.materials.get("PC_Cave_Interior_PBR") or bpy.data.materials.new(
        "PC_Cave_Interior_PBR"
    )
    dark.use_nodes = True
    nodes = dark.node_tree.nodes
    links = dark.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", 0.94)
    set_input(shader, "Emission Color", (0.025, 0.034, 0.016, 1.0))
    set_input(shader, "Emission Strength", 0.62)
    coordinates = nodes.new("ShaderNodeTexCoord")
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 2.8
    noise.inputs["Detail"].default_value = 7.0
    noise.inputs["Roughness"].default_value = 0.78
    ramp = nodes.new("ShaderNodeValToRGB")
    ramp.color_ramp.elements[0].color = (0.010, 0.014, 0.008, 1.0)
    ramp.color_ramp.elements[1].color = (0.135, 0.150, 0.075, 1.0)
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.38
    bump.inputs["Distance"].default_value = 0.12
    links.new(coordinates.outputs["Generated"], noise.inputs["Vector"])
    links.new(noise.outputs["Fac"], ramp.inputs["Fac"])
    links.new(ramp.outputs["Color"], shader.inputs["Base Color"])
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    geometry = nodes.new("ShaderNodeNewGeometry")
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    sided = nodes.new("ShaderNodeMixShader")
    links.new(geometry.outputs["Backfacing"], sided.inputs[0])
    links.new(shader.outputs["BSDF"], sided.inputs[1])
    links.new(transparent.outputs["BSDF"], sided.inputs[2])
    links.new(sided.outputs["Shader"], output.inputs["Surface"])

    # The mouth sits at the foot of the tall drop. The tunnel then follows the
    # rising track backwards through the cliff instead of floating mid-slope.
    point, tangent, right, up = base.track_frame(0.462)
    portal_up = Vector((0.0, 1.0, 0.0))
    portal_forward = Vector((tangent.x, 0.0, tangent.z))
    if portal_forward.length_squared < 1e-6:
        portal_forward = Vector((0.0, 0.0, 1.0))
    else:
        portal_forward.normalize()
    portal_right = Vector((portal_forward.z, 0.0, -portal_forward.x)).normalized()
    portal_center = point + portal_up * 2.0

    terrain = bpy.data.objects.get("Terrain_Editable_129x129")
    if terrain is not None and terrain.type == "MESH":
        import bmesh

        terrain_mesh = bmesh.new()
        terrain_mesh.from_mesh(terrain.data)
        terrain_mesh.faces.ensure_lookup_table()
        faces_to_remove = []
        tunnel_samples = [
            base.track_frame(0.416 + sample_index / 28.0 * 0.050)
            for sample_index in range(29)
        ]
        for face in terrain_mesh.faces:
            world_center = terrain.matrix_world @ face.calc_center_median()
            unity_center = Vector((world_center.x, world_center.z, -world_center.y))
            inside_tunnel = False
            for sample_point, sample_tangent, sample_right, sample_up in tunnel_samples:
                relative = unity_center - sample_point
                along = relative.dot(sample_tangent)
                if abs(along) > 3.0:
                    continue
                across = relative.dot(sample_right)
                vertical = relative.dot(sample_up)
                if (across / 7.25) ** 2 + (vertical / 6.35) ** 2 < 1.0:
                    inside_tunnel = True
                    break
            if inside_tunnel:
                faces_to_remove.append(face)
        if faces_to_remove:
            bmesh.ops.delete(terrain_mesh, geom=faces_to_remove, context="FACES")
            terrain_mesh.to_mesh(terrain.data)
            terrain.data.update()
        terrain["pc_cave_cut_faces"] = len(faces_to_remove)
        terrain_mesh.free()

    # A continuous, thick rock portal makes the entrance read as a carved tunnel
    # instead of isolated boulders placed around a black opening.
    arch_steps = 37
    arch_depths = (0.0, 0.75, 1.55)
    arch_vertices: list[Vector] = []
    for depth_index, depth in enumerate(arch_depths):
        depth_ratio = depth / arch_depths[-1]
        center = portal_center - portal_forward * depth + portal_up * (depth_ratio * 0.55)
        for outer in (True, False):
            radius_x = (9.10 - depth_ratio * 0.34) if outer else (6.95 - depth_ratio * 0.20)
            radius_y = (10.10 - depth_ratio * 0.42) if outer else (8.10 - depth_ratio * 0.28)
            for angle_index in range(arch_steps):
                angle = -0.18 * math.pi + angle_index / (arch_steps - 1) * 1.36 * math.pi
                phase = 0.0 if outer else 1.85
                irregularity = (
                    1.0
                    + math.sin(angle * 5.0 + depth * 0.63 + phase) * 0.085
                    + math.sin(angle * 11.0 - depth * 0.41 - phase) * 0.038
                )
                axial_warp = math.sin(angle * 3.0 + depth_index * 0.8 + phase) * 0.28
                edge = (
                    center
                    + portal_forward * axial_warp
                    + portal_right * (math.cos(angle) * radius_x * irregularity)
                    + portal_up * (math.sin(angle) * radius_y * irregularity)
                )
                arch_vertices.append(base.unity_to_blender(edge))

    arch_faces: list[tuple[int, int, int, int]] = []
    ring_stride = arch_steps * 2
    for depth_index in range(len(arch_depths) - 1):
        current = depth_index * ring_stride
        following = (depth_index + 1) * ring_stride
        for angle_index in range(arch_steps - 1):
            # Outer cliff skin.
            arch_faces.append(
                (
                    current + angle_index,
                    current + angle_index + 1,
                    following + angle_index + 1,
                    following + angle_index,
                )
            )
            # Inner cave wall, with reversed winding.
            inner = current + arch_steps
            next_inner = following + arch_steps
            arch_faces.append(
                (
                    inner + angle_index,
                    next_inner + angle_index,
                    next_inner + angle_index + 1,
                    inner + angle_index + 1,
                )
            )

    # Front and rear stone faces provide visible thickness around the opening.
    for depth_index in (0, len(arch_depths) - 1):
        start = depth_index * ring_stride
        inner = start + arch_steps
        for angle_index in range(arch_steps - 1):
            arch_faces.append(
                (
                    start + angle_index,
                    inner + angle_index,
                    inner + angle_index + 1,
                    start + angle_index + 1,
                )
            )
    # Close both lower shoulders of the arch along its depth.
    for depth_index in range(len(arch_depths) - 1):
        current = depth_index * ring_stride
        following = (depth_index + 1) * ring_stride
        for angle_index in (0, arch_steps - 1):
            arch_faces.append(
                (
                    current + angle_index,
                    following + angle_index,
                    following + arch_steps + angle_index,
                    current + arch_steps + angle_index,
                )
            )

    portal = base.create_mesh_object(
        "PC_Cave_Continuous_Rock_Portal",
        collection,
        arch_vertices,
        arch_faces,
        mountain.active_material
        if mountain is not None and mountain.active_material is not None
        else portal_liner_rock,
    )
    for polygon in portal.data.polygons:
        polygon.use_smooth = True
    subdivision = portal.modifiers.new("Natural rock subdivision", "SUBSURF")
    subdivision.subdivision_type = "SIMPLE"
    subdivision.levels = 1
    subdivision.render_levels = 2
    rock_texture = bpy.data.textures.get("PC_Cave_Rock_Surface") or bpy.data.textures.new(
        "PC_Cave_Rock_Surface", type="CLOUDS"
    )
    rock_texture.noise_scale = 0.74
    rock_texture.noise_depth = 2
    displacement = portal.modifiers.new("Eroded rock silhouette", "DISPLACE")
    displacement.texture = rock_texture
    displacement.texture_coords = "GLOBAL"
    displacement.strength = 0.48
    displacement.mid_level = 0.50
    portal.hide_render = False
    portal.hide_viewport = False
    portal["pc_cinematic_cave"] = "continuous_weathered_arch_v3"
    portal["quest3_keep"] = True

    cave_void = bpy.data.materials.get("PC_Cave_Entrance_Void") or bpy.data.materials.new(
        "PC_Cave_Entrance_Void"
    )
    cave_void.use_nodes = True
    void_nodes = cave_void.node_tree.nodes
    void_links = cave_void.node_tree.links
    void_nodes.clear()
    void_output = void_nodes.new("ShaderNodeOutputMaterial")
    void_shader = void_nodes.new("ShaderNodeBsdfPrincipled")
    set_input(void_shader, "Roughness", 0.96)
    set_input(void_shader, "Specular IOR Level", 0.16)
    set_input(void_shader, "Emission Strength", 0.07)
    void_coordinates = void_nodes.new("ShaderNodeTexCoord")
    void_noise = void_nodes.new("ShaderNodeTexNoise")
    void_noise.inputs["Scale"].default_value = 3.1
    void_noise.inputs["Detail"].default_value = 5.0
    void_noise.inputs["Roughness"].default_value = 0.72
    void_ramp = void_nodes.new("ShaderNodeValToRGB")
    void_ramp.color_ramp.elements[0].color = (0.0018, 0.0026, 0.0012, 1.0)
    void_ramp.color_ramp.elements[1].color = (0.014, 0.019, 0.008, 1.0)
    void_bump = void_nodes.new("ShaderNodeBump")
    void_bump.inputs["Strength"].default_value = 0.44
    void_bump.inputs["Distance"].default_value = 0.16
    void_links.new(void_coordinates.outputs["Generated"], void_noise.inputs["Vector"])
    void_links.new(void_noise.outputs["Fac"], void_ramp.inputs["Fac"])
    void_links.new(void_ramp.outputs["Color"], void_shader.inputs["Base Color"])
    void_links.new(void_ramp.outputs["Color"], void_shader.inputs["Emission Color"])
    void_links.new(void_noise.outputs["Fac"], void_bump.inputs["Height"])
    void_links.new(void_bump.outputs["Normal"], void_shader.inputs["Normal"])
    void_geometry = void_nodes.new("ShaderNodeNewGeometry")
    void_transparent = void_nodes.new("ShaderNodeBsdfTransparent")
    void_sided = void_nodes.new("ShaderNodeMixShader")
    void_links.new(void_geometry.outputs["Backfacing"], void_sided.inputs[0])
    void_links.new(void_shader.outputs["BSDF"], void_sided.inputs[1])
    void_links.new(void_transparent.outputs["BSDF"], void_sided.inputs[2])
    void_links.new(void_sided.outputs["Shader"], void_output.inputs["Surface"])

    void_center = portal_center + portal_up * 0.35 - portal_forward * 2.20
    void_steps = 48
    void_rings = 12
    void_vertices = [base.unity_to_blender(void_center)]
    for ring_index in range(1, void_rings + 1):
        radius = ring_index / void_rings
        for index in range(void_steps):
            angle = index / void_steps * math.tau
            irregularity = 1.0 + math.sin(angle * 5.0 + 0.8) * 0.055 * radius
            edge = (
                void_center
                + portal_right * (math.cos(angle) * 5.55 * radius * irregularity)
                + portal_up * (math.sin(angle) * 6.65 * radius * irregularity)
            )
            void_vertices.append(base.unity_to_blender(edge))
    void_faces: list[tuple[int, ...]] = [
        (0, index + 1, (index + 1) % void_steps + 1) for index in range(void_steps)
    ]
    for ring_index in range(void_rings - 1):
        current = 1 + ring_index * void_steps
        following = current + void_steps
        for index in range(void_steps):
            next_index = (index + 1) % void_steps
            void_faces.append(
                (current + index, following + index, following + next_index, current + next_index)
            )
    entrance_void = base.create_mesh_object(
        "PC_Cave_Entrance_Shadow",
        collection,
        void_vertices,
        void_faces,
        cave_void,
    )
    for polygon in entrance_void.data.polygons:
        polygon.use_smooth = True
    void_subdivision = entrance_void.modifiers.new("Cave back wall subdivision", "SUBSURF")
    void_subdivision.subdivision_type = "SIMPLE"
    void_subdivision.levels = 2
    void_subdivision.render_levels = 2
    void_surface = bpy.data.textures.get("PC_Cave_Back_Wall_Surface") or bpy.data.textures.new(
        "PC_Cave_Back_Wall_Surface", type="CLOUDS"
    )
    void_surface.noise_scale = 0.72
    void_surface.noise_depth = 3
    void_displacement = entrance_void.modifiers.new("Cave back wall relief", "DISPLACE")
    void_displacement.texture = void_surface
    void_displacement.texture_coords = "GLOBAL"
    void_displacement.strength = 0.82
    void_displacement.mid_level = 0.50
    entrance_void["quest3_disable"] = True
    entrance_void["pc_render_profile"] = "external_beauty_only"
    entrance_void["pc_cinematic_cave"] = "one_sided_depth_mask_v1"

    segments = 64
    rings = 28
    tunnel_vertices: list[Vector] = []
    tunnel_faces: list[tuple[int, int, int, int]] = []
    for ring_index in range(rings):
        ring_ratio = ring_index / (rings - 1)
        progress = 0.472 - ring_ratio * 0.056
        frame_point, _frame_tangent, frame_right, frame_up = base.track_frame(progress)
        center = frame_point + frame_up * (1.35 + math.sin(ring_ratio * 7.4) * 0.18)
        radius_scale = 1.0 - ring_ratio * 0.055
        for index in range(segments):
            angle = index / segments * math.tau
            irregularity = (
                1.0
                + math.sin(angle * 5.0 + ring_index * 0.73) * 0.075
                + math.sin(angle * 11.0 - ring_index * 0.41) * 0.035
            )
            edge = (
                center
                + frame_right * (math.cos(angle) * 6.95 * radius_scale * irregularity)
                + frame_up * (math.sin(angle) * 5.95 * radius_scale * irregularity)
            )
            tunnel_vertices.append(base.unity_to_blender(edge))
    for ring_index in range(rings - 1):
        current = ring_index * segments
        following = (ring_index + 1) * segments
        for index in range(segments):
            next_index = (index + 1) % segments
            tunnel_faces.append(
                (current + index, current + next_index, following + next_index, following + index)
            )
    tunnel = base.create_mesh_object(
        "PC_Cave_Deep_Interior", collection, tunnel_vertices, tunnel_faces, tunnel_rock
    )
    for polygon in tunnel.data.polygons:
        polygon.use_smooth = True
    tunnel_subdivision = tunnel.modifiers.new("Irregular tunnel subdivision", "SUBSURF")
    tunnel_subdivision.subdivision_type = "CATMULL_CLARK"
    tunnel_subdivision.levels = 2
    tunnel_subdivision.render_levels = 2
    tunnel_texture = bpy.data.textures.get("PC_Cave_Interior_Surface") or bpy.data.textures.new(
        "PC_Cave_Interior_Surface", type="CLOUDS"
    )
    tunnel_texture.noise_scale = 0.82
    tunnel_texture.noise_depth = 3
    tunnel_displacement = tunnel.modifiers.new("Natural tunnel erosion", "DISPLACE")
    tunnel_displacement.texture = tunnel_texture
    tunnel_displacement.texture_coords = "GLOBAL"
    tunnel_displacement.strength = 0.16
    tunnel_displacement.mid_level = 0.50
    tunnel.hide_render = True
    tunnel.hide_viewport = False
    tunnel["pc_cinematic_cave"] = "deep_rock_tunnel_pbr_v3"
    tunnel["pc_render_profile"] = "enable_for_ride_interior"
    tunnel["quest3_keep"] = True

    # Match the visible entrance to the tunnel's first ring. This masks the
    # underside of the cut terrain without narrowing the rider clearance.
    mouth_center = sum(tunnel_vertices[:segments], Vector()) / segments
    mouth_tangent = base.unity_to_blender(base.track_frame(0.464)[1]).normalized()
    mouth_vertices: list[Vector] = []
    for scale, forward_offset in ((0.97, -0.55), (1.22, -0.08), (1.62, 0.28)):
        for vertex in tunnel_vertices[:segments]:
            mouth_vertices.append(
                mouth_center + (vertex - mouth_center) * scale + mouth_tangent * forward_offset
            )
    mouth_faces: list[tuple[int, int, int, int]] = []
    for ring_index in range(2):
        current = ring_index * segments
        following = (ring_index + 1) * segments
        for index in range(segments):
            next_index = (index + 1) % segments
            mouth_faces.append(
                (current + index, current + next_index, following + next_index, following + index)
            )
    mouth_portal = base.create_mesh_object(
        "PC_Cave_Matched_Rock_Portal",
        collection,
        mouth_vertices,
        mouth_faces,
        tunnel_rock,
    )
    for polygon in mouth_portal.data.polygons:
        polygon.use_smooth = True
    mouth_subdivision = mouth_portal.modifiers.new("Portal rock subdivision", "SUBSURF")
    mouth_subdivision.subdivision_type = "SIMPLE"
    mouth_subdivision.levels = 1
    mouth_subdivision.render_levels = 2
    mouth_displacement = mouth_portal.modifiers.new("Portal weathering", "DISPLACE")
    mouth_displacement.texture = rock_texture
    mouth_displacement.texture_coords = "GLOBAL"
    mouth_displacement.strength = 0.36
    mouth_displacement.mid_level = 0.50
    mouth_portal["pc_cinematic_cave"] = "spline_matched_portal_v1"
    mouth_portal.hide_render = True
    mouth_portal.hide_viewport = True

    if mountain is not None:
        cliff_point, cliff_tangent, cliff_right, _cliff_up = base.track_frame(0.440)
        cliff_yaw = math.degrees(math.atan2(cliff_tangent.x, cliff_tangent.z))
        for side, yaw_offset in ((-1.0, -24.0), (1.0, 21.0)):
            cliff_position = cliff_point + cliff_right * (side * 14.0)
            cliff = duplicate_template(
                mountain,
                collection,
                f"PC_Cave_Cliff_{'L' if side < 0.0 else 'R'}",
                cliff_position.x,
                cliff_position.z,
                34.0,
                cliff_yaw + yaw_offset,
                0.38,
                0.80,
            )
            cliff.location.z -= 2.4
            cliff["pc_cinematic_cave"] = "external_cliff_mass_v1"

    # Uneven rock bed closes the lower void while keeping the rails exposed.
    floor_rows = 32
    floor_columns = 13
    floor_vertices: list[Vector] = []
    floor_faces: list[tuple[int, int, int, int]] = []
    for row in range(floor_rows):
        row_ratio = row / (floor_rows - 1)
        progress = 0.462 - row_ratio * 0.046
        frame_point, _frame_tangent, frame_right, frame_up = base.track_frame(progress)
        center = frame_point - frame_up * 1.28
        for column in range(floor_columns):
            across = (column / (floor_columns - 1) - 0.5) * 8.1
            height_noise = (
                math.sin(row_ratio * 15.7 + across * 1.34) * 0.12
                + math.sin(row_ratio * 31.2 - across * 0.72) * 0.06
            )
            position = center + frame_right * across + frame_up * height_noise
            floor_vertices.append(base.unity_to_blender(position))
    for row in range(floor_rows - 1):
        for column in range(floor_columns - 1):
            current = row * floor_columns + column
            following = current + floor_columns
            floor_faces.append((current, current + 1, following + 1, following))
    cave_floor = base.create_mesh_object(
        "PC_Cave_Rocky_Rail_Bed", collection, floor_vertices, floor_faces, tunnel_rock
    )
    for polygon in cave_floor.data.polygons:
        polygon.use_smooth = True
    floor_bevel = cave_floor.modifiers.new("Eroded floor edges", "BEVEL")
    floor_bevel.width = 0.08
    floor_bevel.segments = 2
    floor_subdivision = cave_floor.modifiers.new("Smoothed cave floor", "SUBSURF")
    floor_subdivision.subdivision_type = "CATMULL_CLARK"
    floor_subdivision.levels = 1
    floor_subdivision.render_levels = 2
    cave_floor.hide_render = False
    cave_floor.hide_viewport = False
    cave_floor["pc_render_profile"] = "enable_for_ride_interior"
    cave_floor["quest3_keep"] = True

    back_point, _back_tangent, back_right, back_up = base.track_frame(0.424)
    back_center = back_point + back_up * 1.20
    back_vertices = [base.unity_to_blender(back_center)]
    for index in range(segments):
        angle = index / segments * math.tau
        edge = (
            back_center
            + back_right * (math.cos(angle) * 5.05)
            + back_up * (math.sin(angle) * 4.35)
        )
        back_vertices.append(base.unity_to_blender(edge))
    back_faces = [(0, index + 1, (index + 1) % segments + 1) for index in range(segments)]
    back_wall = base.create_mesh_object(
        "PC_Cave_Back_Wall", collection, back_vertices, back_faces, dark
    )
    back_wall.hide_render = True
    back_wall.hide_viewport = True

    cave_light_data = bpy.data.lights.new("PC_Cave_Interior_Fill_Data", type="POINT")
    cave_light_data.energy = 1500.0
    cave_light_data.color = (0.32, 0.38, 0.24)
    cave_light_data.shadow_soft_size = 5.8
    cave_light = bpy.data.objects.new("PC_Cave_Interior_Fill", cave_light_data)
    collection.objects.link(cave_light)
    light_point, _light_tangent, _light_right, light_up = base.track_frame(0.449)
    cave_light.location = base.unity_to_blender(light_point + light_up * 2.0)

    deep_light_data = bpy.data.lights.new("PC_Cave_Deep_Fill_Data", type="POINT")
    deep_light_data.energy = 850.0
    deep_light_data.color = (0.16, 0.24, 0.12)
    deep_light_data.shadow_soft_size = 4.0
    deep_light = bpy.data.objects.new("PC_Cave_Deep_Fill", deep_light_data)
    collection.objects.link(deep_light)
    deep_point, _deep_tangent, _deep_right, deep_up = base.track_frame(0.433)
    deep_light.location = base.unity_to_blender(deep_point + deep_up * 1.7)

    shadow_material = bpy.data.materials.get("PC_Cave_Shadow_Mist") or bpy.data.materials.new(
        "PC_Cave_Shadow_Mist"
    )
    shadow_material.use_nodes = True
    shadow_nodes = shadow_material.node_tree.nodes
    shadow_links = shadow_material.node_tree.links
    shadow_nodes.clear()
    shadow_output = shadow_nodes.new("ShaderNodeOutputMaterial")
    shadow_volume = shadow_nodes.new("ShaderNodeVolumePrincipled")
    shadow_volume.inputs["Density"].default_value = 0.060
    shadow_volume.inputs["Color"].default_value = (0.022, 0.026, 0.016, 1.0)
    shadow_volume.inputs["Anisotropy"].default_value = 0.25
    shadow_links.new(shadow_volume.outputs["Volume"], shadow_output.inputs["Volume"])
    volume_point, volume_tangent, _volume_right, volume_up = base.track_frame(0.440)
    bpy.ops.mesh.primitive_cube_add(location=base.unity_to_blender(volume_point + volume_up * 1.1))
    cave_shadow = bpy.context.object
    cave_shadow.name = "PC_Cave_Shadow_Volume"
    move_to(cave_shadow, collection)
    cave_shadow.scale = (5.7, 11.0, 5.2)
    axis_blender = base.unity_to_blender(volume_tangent).normalized()
    cave_shadow.rotation_mode = "QUATERNION"
    cave_shadow.rotation_quaternion = axis_blender.to_track_quat("Y", "Z")
    cave_shadow.data.materials.append(shadow_material)
    cave_shadow.display_type = "WIRE"
    cave_shadow.hide_render = True
    cave_shadow.hide_viewport = True
    cave_shadow["quest3_disable"] = True

    cave_rng = random.Random(58411)
    templates = moss_rocks or [boulder]
    rock_index = 0

    # Linked photogrammetry rocks continue through the tunnel along the real
    # spline. They supply readable parallax and eliminate the smooth pipe look.
    interior_progress = (0.423, 0.434, 0.445, 0.456, 0.466)
    interior_angles = (0.10, 0.32, 0.68, 0.90)
    for ring_index, progress in enumerate(interior_progress):
        frame_point, _frame_tangent, frame_right, frame_up = base.track_frame(progress)
        center = frame_point + frame_up * 1.20
        for angle_index, normalized_angle in enumerate(interior_angles):
            angle = normalized_angle * math.pi
            radial = 1.0 + cave_rng.uniform(-0.045, 0.055)
            position = (
                center
                + frame_right * (math.cos(angle) * 8.35 * radial)
                + frame_up * (math.sin(angle) * 7.45 * radial)
            )
            height = cave_rng.uniform(1.15, 1.75)
            rock = duplicate_template(
                cave_rng.choice(templates),
                collection,
                f"PC_Cave_Interior_R{ring_index + 1:02}_{angle_index + 1:02}",
                position.x,
                position.z,
                height,
                cave_rng.uniform(0.0, 360.0),
                cave_rng.uniform(0.24, 0.38),
                cave_rng.uniform(0.24, 0.40),
            )
            rock.location = base.unity_to_blender(
                (position.x, position.y - height * 0.46, position.z)
            )
            rock.rotation_euler[0] = math.radians(cave_rng.uniform(-12.0, 12.0))
            rock.rotation_euler[1] = math.radians(cave_rng.uniform(-10.0, 10.0))
            rock.hide_render = False
            rock.hide_viewport = False
            rock["pc_render_profile"] = "enable_for_ride_interior"
            rock["quest3_keep"] = True

    for ring_index, progress in enumerate(()):
        frame_point, _frame_tangent, frame_right, frame_up = base.track_frame(progress)
        for side in (-1.0, 1.0):
            position = frame_point + frame_right * (side * 2.8) - frame_up * 2.25
            height = cave_rng.uniform(1.4, 2.0)
            rock = duplicate_template(
                cave_rng.choice(templates),
                collection,
                f"PC_Cave_Floor_Rock_{ring_index + 1:02}_{'L' if side < 0 else 'R'}",
                position.x,
                position.z,
                height,
                cave_rng.uniform(0.0, 360.0),
                cave_rng.uniform(0.42, 0.66),
                cave_rng.uniform(0.38, 0.62),
            )
            rock.location = base.unity_to_blender(
                (position.x, position.y - height * 0.38, position.z)
            )


def populate_rocks(collection: bpy.types.Collection, library: bpy.types.Collection) -> None:
    moss_dir = ASSET_ROOT / "rock_moss_set_01" / "textures"
    moss_set_mat = image_material(
        "PC_PolyHaven_RockMossSet01_CC0",
        moss_dir / "rock_moss_set_01_diff_2k.jpg",
        moss_dir / "rock_moss_set_01_nor_gl_2k.exr",
        moss_dir / "rock_moss_set_01_rough_2k.jpg",
        roughness_value=0.72,
        normal_strength=0.96,
    )
    boulder_mat = image_material(
        "PC_Boulder_LOD0",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_Albedo.jpg",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_Normal.jpg",
        roughness_value=0.64,
        normal_strength=0.92,
    )
    mountain_mat = image_material(
        "PC_Mountainside_LOD0",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_Albedo.jpg",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_Normal.jpg",
        roughness_value=0.70,
        normal_strength=0.96,
    )
    grade_rock_material(boulder_mat)
    grade_rock_material(mountain_mat)
    grade_rock_material(moss_set_mat)
    for obj in bpy.data.objects:
        if obj.name.startswith(("Waterfall_Boulder_", "Lagoon_Boulder_")) and obj.type == "MESH":
            obj.data.materials.clear()
            obj.data.materials.append(boulder_mat)
    boulder = safe_static_template(
        "PC_Boulder_LOD0",
        PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_LOD0.fbx",
        library,
        boulder_mat,
    )
    mountain = safe_static_template(
        "PC_Mountainside_LOD0",
        PROJECT_ROOT
        / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_LOD0.fbx",
        library,
        mountain_mat,
    )
    moss_rocks = import_variants(
        ASSET_ROOT / "rock_moss_set_01" / "rock_moss_set_01_2k.fbx",
        library,
        "RockMoss",
        moss_set_mat,
    )
    if boulder is None:
        return
    for obj in bpy.data.objects:
        if obj.name.startswith("Hero_Cliff_") or obj.name.startswith("Waterfall_Cliff_"):
            obj.hide_render = True
    formations = [
        (-48.0, -65.0, 22.0, 3),
        (-18.0, -8.0, 27.0, 4),
        (70.0, 47.0, 39.0, 5),
        (79.0, 59.0, 29.0, 3),
    ]
    cluster_rng = random.Random(90210)
    for formation_index, (x, z, height, pieces) in enumerate(formations):
        if mountain is not None:
            hero_width = 1.34 if formation_index == 1 else (0.88 if formation_index == 2 else 1.0)
            hero = duplicate_template(
                mountain,
                collection,
                f"PC_Hero_Cliff_{formation_index + 1:02}",
                x,
                z,
                height,
                (formation_index * 71.0 + 18.0) % 360.0,
                hero_width,
                1.05,
            )
            hero.location.z -= height * 0.07
        for piece in range(pieces):
            piece_height = height * cluster_rng.uniform(0.34, 0.62)
            obj = duplicate_template(
                boulder,
                collection,
                f"PC_Hero_Rock_{formation_index + 1:02}_{piece + 1:02}",
                x + cluster_rng.uniform(-5.5, 5.5),
                z + cluster_rng.uniform(-4.8, 4.8),
                piece_height,
                cluster_rng.uniform(0.0, 360.0),
                cluster_rng.uniform(0.62, 0.96),
                cluster_rng.uniform(0.60, 0.92),
            )
            obj.location.z -= piece_height * cluster_rng.uniform(0.13, 0.24)
    rng = random.Random(81024)
    placed = 0
    while placed < 42:
        x = rng.uniform(-108.0, 112.0)
        z = rng.uniform(-88.0, 103.0)
        if lagoon_distance(x, z) < 1.10 or base.nearest_track_distance(x, z) < 3.4:
            continue
        height = rng.uniform(2.0, 7.5)
        template = rng.choice(moss_rocks) if moss_rocks and rng.random() < 0.78 else boulder
        obj = duplicate_template(
            template,
            collection,
            f"PC_Boulder_{placed + 1:03}",
            x,
            z,
            height,
            rng.uniform(0.0, 360.0),
            rng.uniform(0.82, 1.38),
            rng.uniform(0.80, 1.24),
        )
        obj.location.z -= height * 0.15
        placed += 1

    if moss_rocks:
        shore_rng = random.Random(43021)
        for index in range(9):
            angle = (index / 9.0) * math.tau + shore_rng.uniform(-0.22, 0.22)
            radius = shore_rng.uniform(1.02, 1.12)
            contour = cinematic_lagoon_contour(angle)
            x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * radius
            z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * radius
            if base.nearest_track_distance(x, z) < 4.2:
                continue
            height = shore_rng.uniform(1.6, 4.6)
            obj = duplicate_template(
                shore_rng.choice(moss_rocks),
                collection,
                f"PC_Lagoon_MossRock_{index + 1:02}",
                x,
                z,
                height,
                shore_rng.uniform(0.0, 360.0),
                shore_rng.uniform(0.82, 1.22),
                shore_rng.uniform(0.76, 1.15),
            )
            obj.location.z -= height * 0.18

    build_cinematic_cave(collection, boulder, moss_rocks, mountain)


def populate_bones(collection: bpy.types.Collection) -> None:
    material = bpy.data.materials.get("PC_Fossil_Bone_PBR") or bpy.data.materials.new(
        "PC_Fossil_Bone_PBR"
    )
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Base Color", (0.46, 0.36, 0.23, 1.0))
    set_input(shader, "Roughness", 0.82)
    noise = nodes.new("ShaderNodeTexNoise")
    noise.inputs["Scale"].default_value = 7.0
    noise.inputs["Detail"].default_value = 5.0
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.22
    bump.inputs["Distance"].default_value = 0.035
    links.new(noise.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    def point(x: float, z: float, lift: float = 0.24) -> Vector:
        return base.unity_to_blender((x, base.height_at(x, z) + lift, z))

    spine = [point(17.0 + index * 3.0, 79.0 - index * 1.1, 0.28) for index in range(8)]
    base.create_curve_object("PC_Fossil_Spine", collection, spine, 0.18, material, cyclic=False)
    for index in range(6):
        x = 20.0 + index * 3.1
        z = 78.0 - index * 1.1
        rib = [
            point(x, z, 0.30),
            point(x - 1.1, z + 2.2, 0.48),
            point(x - 0.3, z + 4.1, 0.34),
        ]
        base.create_curve_object(
            f"PC_Fossil_Rib_L_{index + 1:02}", collection, rib, 0.12, material, cyclic=False
        )
        rib_mirror = [
            point(x, z, 0.30),
            point(x + 1.3, z - 2.0, 0.47),
            point(x + 0.7, z - 4.0, 0.33),
        ]
        base.create_curve_object(
            f"PC_Fossil_Rib_R_{index + 1:02}",
            collection,
            rib_mirror,
            0.12,
            material,
            cyclic=False,
        )
    long_bones = [
        (point(12.0, 73.0), point(18.0, 70.0)),
        (point(36.0, 72.0), point(43.0, 68.0)),
        (point(40.0, 83.0), point(47.0, 86.0)),
    ]
    for index, (start, end) in enumerate(long_bones):
        base.create_curve_object(
            f"PC_Fossil_LongBone_{index + 1:02}",
            collection,
            [start, end],
            0.20,
            material,
            cyclic=False,
        )


def duplicate_hierarchy(
    source: bpy.types.Object,
    collection: bpy.types.Collection,
    name: str,
    x: float,
    z: float,
    yaw: float,
    scale: float,
    height: float | None = None,
) -> bpy.types.Object:
    originals = [source] + list(source.children_recursive)
    mapping = {}
    for original in originals:
        copied = original.copy()
        if original.data is not None:
            copied.data = original.data
        copied.hide_render = False
        copied.hide_viewport = False
        collection.objects.link(copied)
        mapping[original] = copied
    for original, copied in mapping.items():
        if original != source and original.parent in mapping:
            copied.parent = mapping[original.parent]
            copied.matrix_parent_inverse = original.matrix_parent_inverse.copy()
            copied.matrix_local = original.matrix_local.copy()
        for modifier in copied.modifiers:
            target = getattr(modifier, "object", None)
            if target in mapping:
                modifier.object = mapping[target]
    root = mapping[source]
    root.name = name
    y = base.height_at(x, z) if height is None else height
    root.location = base.unity_to_blender((x, y, z))
    root.rotation_euler = (0.0, 0.0, math.radians(yaw))
    root.scale = tuple(value * scale for value in source.scale)
    if height is None:
        bpy.context.view_layer.update()
        mesh_bounds = [
            obj.matrix_world @ Vector(corner)
            for obj in [root] + list(root.children_recursive)
            if obj.type == "MESH"
            for corner in obj.bound_box
        ]
        if mesh_bounds:
            root.location.z += y - min(point.z for point in mesh_bounds)
    return root


def populate_herds(collection: bpy.types.Collection) -> None:
    apa = bpy.data.objects.get("Apatosaurus_01")
    tri = bpy.data.objects.get("Triceratops_01")
    trex = bpy.data.objects.get("Tyrannosaurus")
    ptero = bpy.data.objects.get("Pteranodon_01")
    if apa is None or tri is None:
        return
    layout = [
        (apa, "PC_Apatosaurus_03", -46.0, -32.0, 20.0, 0.34),
        (apa, "PC_Apatosaurus_04", 2.0, -45.0, 212.0, 0.30),
        (apa, "PC_Apatosaurus_05", 50.0, -52.0, 168.0, 0.32),
        (tri, "PC_Triceratops_03", -28.0, -24.0, 66.0, 1.18),
        (tri, "PC_Triceratops_04", -10.0, -39.0, 92.0, 1.02),
        (tri, "PC_Triceratops_05", 14.0, -29.0, 244.0, 1.18),
        (tri, "PC_Triceratops_06", 34.0, -46.0, 188.0, 1.06),
        (tri, "PC_Triceratops_07", 57.0, -32.0, 132.0, 1.12),
        (tri, "PC_Triceratops_08", 74.0, -22.0, 155.0, 0.98),
    ]
    for source, name, x, z, yaw, scale in layout:
        duplicate_hierarchy(source, collection, name, x, z, yaw, scale)
    duplicate_hierarchy(apa, collection, "PC_Apatosaurus_Hero_Back", -64.0, -70.0, 32.0, 0.58)
    hero_herd = [
        (-20.0, -50.0, 54.0, 1.30),
        (0.0, -62.0, 128.0, 1.18),
        (22.0, -55.0, 208.0, 1.26),
        (42.0, -67.0, 174.0, 1.16),
        (62.0, -56.0, 112.0, 1.22),
    ]
    for index, (x, z, yaw, scale) in enumerate(hero_herd):
        duplicate_hierarchy(
            tri,
            collection,
            f"PC_Triceratops_Hero_{index + 1:02}",
            x,
            z,
            yaw,
            scale * 1.55,
        )
    back_herd = [
        (-42.0, -78.0, 38.0, 0.98),
        (-24.0, -88.0, 96.0, 0.90),
        (-5.0, -76.0, 152.0, 1.02),
        (16.0, -92.0, 214.0, 0.94),
        (38.0, -80.0, 174.0, 1.00),
        (58.0, -90.0, 118.0, 0.92),
    ]
    for index, (x, z, yaw, scale) in enumerate(back_herd):
        duplicate_hierarchy(
            tri,
            collection,
            f"PC_Triceratops_Back_{index + 1:02}",
            x,
            z,
            yaw,
            scale * 1.55,
        )
    if trex is not None:
        duplicate_hierarchy(trex, collection, "PC_Tyrannosaurus_Hero", -57.0, -14.0, 38.0, 1.10)
    if ptero is not None:
        sky = [
            (-55.0, 42.0, 46.0, 18.0, 0.78),
            (-18.0, 54.0, 50.0, 52.0, 0.62),
            (24.0, 48.0, 58.0, 205.0, 0.76),
            (58.0, 58.0, 72.0, 228.0, 0.98),
            (90.0, 38.0, 61.0, 246.0, 0.58),
        ]
        for index, (x, z, height, yaw, scale) in enumerate(sky):
            duplicate_hierarchy(
                ptero,
                collection,
                f"PC_Pteranodon_{index + 4:02}",
                x,
                z,
                yaw,
                scale,
                height,
            )


def hide_original_dinosaurs() -> None:
    for root_name in (
        "Apatosaurus_01",
        "Apatosaurus_02",
        "Triceratops_01",
        "Triceratops_02",
        "Tyrannosaurus",
        "Pteranodon_01",
        "Pteranodon_02",
        "Pteranodon_03",
    ):
        root = bpy.data.objects.get(root_name)
        if root is None:
            continue
        for obj in [root] + list(root.children_recursive):
            obj.hide_render = True
            obj.hide_viewport = True


def fix_cart_orientation() -> None:
    cart = bpy.data.objects.get("Ride_Cart_Editable")
    if cart is None:
        return
    _point, tangent, _right, _up = base.track_frame(0.0)
    # The Sketchfab cart is modeled lengthwise on local +X (the hood/front is
    # +X). Aligning local -Y made it cross the rails after the composition flip.
    tangent_blender = base.unity_to_blender(tangent).normalized()
    cart.rotation_mode = "QUATERNION"
    cart.rotation_quaternion = tangent_blender.to_track_quat("-X", "Z")
    cart["pc_cinematic_facing_fixed"] = True
    cart["pc_cinematic_facing"] = "reversed_for_composition"
    cart["pc_cinematic_front_axis"] = "-X"


def hide_original_cart() -> None:
    cart = bpy.data.objects.get("Ride_Cart_Editable")
    if cart is None:
        return
    for obj in [cart] + list(cart.children_recursive):
        obj.hide_render = True


def populate_cart_train(collection: bpy.types.Collection) -> None:
    source = bpy.data.objects.get("Ride_Cart_Editable")
    if source is None:
        return
    for index, progress in enumerate((0.648, 0.660, 0.672)):
        point, tangent, _right, up = base.track_frame(progress)
        yaw = math.degrees(math.atan2(tangent.x, tangent.z))
        cart = duplicate_hierarchy(
            source,
            collection,
            f"PC_Ride_Cart_{index + 2:02}",
            point.x,
            point.z,
            yaw,
            2.10,
            point.y + up.y * 0.4,
        )
        tangent_blender = base.unity_to_blender(tangent).normalized()
        cart.rotation_mode = "QUATERNION"
        cart.rotation_quaternion = tangent_blender.to_track_quat("-X", "Z")
        cart["pc_cinematic_front_axis"] = "-X"


def curate_camera_view(collection: bpy.types.Collection) -> None:
    from bpy_extras.object_utils import world_to_camera_view

    scene = bpy.context.scene
    camera = scene.camera
    if camera is None:
        return
    cart_views = [
        world_to_camera_view(scene, camera, obj.matrix_world.translation)
        for obj in bpy.data.objects
        if obj.name.startswith("PC_Ride_Cart_")
    ]
    for obj in collection.objects:
        if not obj.name.startswith("PC_Canopy_"):
            continue
        ndc = world_to_camera_view(scene, camera, obj.matrix_world.translation)
        key = sum(ord(char) for char in obj.name) % 10
        blocks_foreground_track = 0.25 < ndc.x < 0.84 and -0.18 < ndc.y < 0.38
        blocks_lagoon = 0.44 < ndc.x < 0.82 and 0.28 < ndc.y < 0.58
        blocks_cart = any(
            abs(ndc.x - cart_view.x) < 0.11
            and abs(ndc.y - cart_view.y) < 0.18
            and ndc.z < cart_view.z
            for cart_view in cart_views
        )
        if blocks_cart or (blocks_foreground_track and key < 9) or (blocks_lagoon and key < 8):
            obj.hide_render = True


def configure_camera(collection: bpy.types.Collection) -> None:
    scene = bpy.context.scene
    camera = bpy.data.objects.get("Camera_CinematicReference")
    if camera is None:
        camera = bpy.data.objects.new(
            "Camera_CinematicReference", bpy.data.cameras.new("Camera_CinematicReference_Data")
        )
        collection.objects.link(camera)
    else:
        move_to(camera, collection)
    # Opposite side of the valley: cart at left, lagoon and lift hill at right,
    # matching the supplied beauty reference instead of the mirrored draft.
    camera.location = (-138.0, -158.0, 55.0)
    target = Vector((0.0, -3.0, 9.0))
    camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 56.0
    camera.data.sensor_width = 52.0
    camera.data.sensor_fit = "HORIZONTAL"
    camera.data.clip_start = 0.5
    camera.data.clip_end = 1200.0
    camera.data.dof.use_dof = True
    camera.data.dof.focus_object = bpy.data.objects.get("PC_Hero_Rock_02_01")
    camera.data.dof.aperture_fstop = 11.0
    scene.camera = camera
    camera.data.background_images.clear()
    for path, alpha in ((WIREFRAME_PATH, 0.28), (BEAUTY_PATH, 0.20)):
        if path.exists():
            background = camera.data.background_images.new()
            background.image = load_image(path)
            background.alpha = alpha
            background.display_depth = "FRONT"
    camera.data.show_background_images = True


def create_sun_disc(collection: bpy.types.Collection) -> None:
    camera = bpy.context.scene.camera
    if camera is None:
        return
    material = bpy.data.materials.get("PC_Diffuse_Sun") or bpy.data.materials.new(
        "PC_Diffuse_Sun"
    )
    material.use_nodes = True
    if hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    emission = nodes.new("ShaderNodeEmission")
    emission.inputs["Color"].default_value = (1.0, 0.58, 0.28, 1.0)
    emission.inputs["Strength"].default_value = 2.6
    transparent = nodes.new("ShaderNodeBsdfTransparent")
    falloff = nodes.new("ShaderNodeVertexColor")
    falloff.layer_name = "sun_falloff"
    mix_shader = nodes.new("ShaderNodeMixShader")
    links.new(falloff.outputs["Color"], mix_shader.inputs[0])
    links.new(transparent.outputs["BSDF"], mix_shader.inputs[1])
    links.new(emission.outputs["Emission"], mix_shader.inputs[2])
    links.new(mix_shader.outputs["Shader"], output.inputs["Surface"])

    segments = 64
    vertices = [(0.0, 0.0, 0.0)] + [
        (
            math.cos(index / segments * math.tau) * 18.0,
            math.sin(index / segments * math.tau) * 18.0,
            0.0,
        )
        for index in range(segments)
    ]
    faces = [(0, index + 1, (index + 1) % segments + 1) for index in range(segments)]
    mesh = bpy.data.meshes.new("PC_Diffuse_Sun_Mesh")
    mesh.from_pydata(vertices, [], faces)
    colors = mesh.color_attributes.new(name="sun_falloff", type="FLOAT_COLOR", domain="POINT")
    colors.data[0].color = (1.0, 1.0, 1.0, 1.0)
    for color in colors.data[1:]:
        color.color = (0.0, 0.0, 0.0, 1.0)
    mesh.materials.append(material)
    disc = bpy.data.objects.new("PC_Diffuse_Sun", mesh)
    collection.objects.link(disc)

    bpy.context.view_layer.update()
    rotation = camera.rotation_euler.to_quaternion()
    forward = rotation @ Vector((0.0, 0.0, -1.0))
    right = rotation @ Vector((1.0, 0.0, 0.0))
    up = rotation @ Vector((0.0, 1.0, 0.0))
    disc.location = camera.location + forward * 500.0 - right * 158.0 + up * 71.0
    disc.rotation_euler = (camera.location - disc.location).to_track_quat("Z", "Y").to_euler()


def configure_render(collection: bpy.types.Collection) -> None:
    scene = bpy.context.scene
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1880
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 50
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.filepath = str(PREVIEW_PATH)
    scene.render.film_transparent = False
    if hasattr(scene, "eevee") and hasattr(scene.eevee, "use_raytracing"):
        # Dense alpha-cutout foliage can deadlock Eevee/Metal ray tracing on
        # this Mac. Keep interactive renders stable; Cycles remains the final
        # beauty renderer when the composition is approved.
        scene.eevee.use_raytracing = False
    try:
        scene.view_settings.look = "AgX - Medium High Contrast"
    except TypeError:
        pass
    scene.view_settings.exposure = 0.62

    world = scene.world or bpy.data.worlds.new("Vale Mesozoico World")
    scene.world = world
    world.use_nodes = True
    nodes = world.node_tree.nodes
    links = world.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputWorld")
    hdri_background = nodes.new("ShaderNodeBackground")
    coordinates = nodes.new("ShaderNodeTexCoord")
    invert = nodes.new("ShaderNodeVectorMath")
    invert.operation = "MULTIPLY"
    invert.inputs[1].default_value = (1.0, 1.0, -1.0)
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Rotation"].default_value[2] = math.radians(118.0)
    environment = nodes.new("ShaderNodeTexEnvironment")
    environment.image = load_image(HDRI_PATH)
    hdri_background.inputs["Strength"].default_value = 0.66
    links.new(coordinates.outputs["Normal"], invert.inputs[0])
    links.new(invert.outputs["Vector"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], environment.inputs["Vector"])
    links.new(environment.outputs["Color"], hdri_background.inputs["Color"])

    # Keep the HDRI for illumination/reflections, but do not photograph its
    # ground plane behind the valley. Camera rays get a controlled dawn sky.
    geometry = nodes.new("ShaderNodeNewGeometry")
    separate = nodes.new("ShaderNodeSeparateXYZ")
    sky_range = nodes.new("ShaderNodeMapRange")
    sky_range.inputs["From Min"].default_value = 0.0
    sky_range.inputs["From Max"].default_value = 0.43
    sky_range.clamp = True
    sky_ramp = nodes.new("ShaderNodeValToRGB")
    sky_ramp.color_ramp.elements[0].position = 0.0
    sky_ramp.color_ramp.elements[0].color = (0.38, 0.43, 0.47, 1.0)
    sky_ramp.color_ramp.elements[1].position = 1.0
    sky_ramp.color_ramp.elements[1].color = (0.78, 0.53, 0.30, 1.0)
    middle = sky_ramp.color_ramp.elements.new(0.58)
    middle.color = (0.56, 0.51, 0.43, 1.0)
    cloud_noise = nodes.new("ShaderNodeTexNoise")
    cloud_noise.inputs["Scale"].default_value = 1.15
    cloud_noise.inputs["Detail"].default_value = 3.2
    cloud_noise.inputs["Roughness"].default_value = 0.62
    cloud_grade = nodes.new("ShaderNodeValToRGB")
    cloud_grade.color_ramp.elements[0].position = 0.34
    cloud_grade.color_ramp.elements[0].color = (0.62, 0.66, 0.70, 1.0)
    cloud_grade.color_ramp.elements[1].position = 0.72
    cloud_grade.color_ramp.elements[1].color = (1.0, 0.94, 0.82, 1.0)
    sky_cloud_mix = nodes.new("ShaderNodeMixRGB")
    sky_cloud_mix.blend_type = "MULTIPLY"
    sky_cloud_mix.inputs[0].default_value = 0.13
    camera_background = nodes.new("ShaderNodeBackground")
    camera_background.inputs["Strength"].default_value = 0.52
    light_path = nodes.new("ShaderNodeLightPath")
    camera_or_glossy = nodes.new("ShaderNodeMath")
    camera_or_glossy.operation = "MAXIMUM"
    mix = nodes.new("ShaderNodeMixShader")
    links.new(geometry.outputs["Incoming"], separate.inputs["Vector"])
    links.new(separate.outputs["Z"], sky_range.inputs["Value"])
    links.new(sky_range.outputs["Result"], sky_ramp.inputs["Fac"])
    links.new(coordinates.outputs["Normal"], cloud_noise.inputs["Vector"])
    links.new(cloud_noise.outputs["Fac"], cloud_grade.inputs["Fac"])
    links.new(sky_ramp.outputs["Color"], sky_cloud_mix.inputs[1])
    links.new(cloud_grade.outputs["Color"], sky_cloud_mix.inputs[2])
    links.new(sky_cloud_mix.outputs["Color"], camera_background.inputs["Color"])
    links.new(light_path.outputs["Is Camera Ray"], camera_or_glossy.inputs[0])
    links.new(light_path.outputs["Is Glossy Ray"], camera_or_glossy.inputs[1])
    links.new(camera_or_glossy.outputs["Value"], mix.inputs[0])
    links.new(hdri_background.outputs["Background"], mix.inputs[1])
    links.new(camera_background.outputs["Background"], mix.inputs[2])
    links.new(mix.outputs["Shader"], output.inputs["Surface"])

    create_sun_disc(collection)

    sun = bpy.data.objects.get("Sun_Jurassic")
    if sun is not None:
        sun.data.energy = 5.6
        sun.data.angle = math.radians(4.0)
        sun.data.color = (1.0, 0.70, 0.48)
        sun.rotation_euler = (math.radians(51.0), math.radians(-9.0), math.radians(-43.0))
    fill = bpy.data.objects.get("Sky_Fill")
    if fill is not None:
        fill.data.energy = 520.0
        fill.data.color = (0.30, 0.38, 0.46)

    volume_mat = bpy.data.materials.get("PC_Jurassic_Atmosphere") or bpy.data.materials.new(
        "PC_Jurassic_Atmosphere"
    )
    volume_mat.use_nodes = True
    vnodes = volume_mat.node_tree.nodes
    vlinks = volume_mat.node_tree.links
    vnodes.clear()
    voutput = vnodes.new("ShaderNodeOutputMaterial")
    volume = vnodes.new("ShaderNodeVolumePrincipled")
    volume.inputs["Density"].default_value = 0.00048
    volume.inputs["Color"].default_value = (0.60, 0.55, 0.42, 1.0)
    volume.inputs["Anisotropy"].default_value = 0.42
    vlinks.new(volume.outputs["Volume"], voutput.inputs["Volume"])
    bpy.ops.mesh.primitive_cube_add(location=(0.0, 55.0, 70.0), scale=(235.0, 110.0, 105.0))
    fog = bpy.context.object
    fog.name = "PC_Atmospheric_Haze_Volume"
    move_to(fog, collection)
    fog.data.materials.append(volume_mat)
    fog.display_type = "WIRE"

    eevee = scene.eevee
    eevee.taa_render_samples = 128
    eevee.use_raytracing = False
    eevee.ray_tracing_method = "SCREEN"
    eevee.use_fast_gi = True
    eevee.fast_gi_method = "GLOBAL_ILLUMINATION"
    eevee.fast_gi_quality = 1.0
    eevee.use_volumetric_shadows = False
    eevee.volumetric_samples = 64
    eevee.volumetric_tile_size = "4"

    # Blender 5 moved compositor graphs to a different API. Color management
    # and the volumetric pass already provide the intended cinematic response.
    if hasattr(scene, "node_tree"):
        scene.use_nodes = True
        compositor = scene.node_tree
        compositor.nodes.clear()
        render_layers = compositor.nodes.new("CompositorNodeRLayers")
        glare = compositor.nodes.new("CompositorNodeGlare")
        glare.glare_type = "FOG_GLOW"
        glare.quality = "HIGH"
        glare.threshold = 1.15
        glare.size = 6
        glare.mix = -0.92
        composite = compositor.nodes.new("CompositorNodeComposite")
        compositor.links.new(render_layers.outputs["Image"], glare.inputs["Image"])
        compositor.links.new(glare.outputs["Image"], composite.inputs["Image"])


def _linked_reference_instance(
    template: bpy.types.Object,
    collection: bpy.types.Collection,
    name: str,
    position: Vector,
    height: float,
    yaw: float,
    width: float = 1.0,
    depth: float = 1.0,
    tilt_x: float = 0.0,
    tilt_y: float = 0.0,
) -> bpy.types.Object:
    """Place a linked hero asset without duplicating its heavy mesh data."""
    obj = template.copy()
    obj.data = template.data
    collection.objects.link(obj)
    obj.name = name
    obj.hide_render = False
    obj.hide_viewport = False
    obj.location = base.unity_to_blender(position)
    obj.rotation_euler = (
        math.radians(tilt_x),
        math.radians(tilt_y),
        math.radians(yaw),
    )
    scale = height / max(0.001, template.dimensions.z)
    obj.scale = (scale * width, scale * depth, scale)
    # Thousands of linked hero instances can deadlock Eevee/Metal while the
    # shadow atlas is read back. Contact shading and AO still ground them.
    if hasattr(obj, "visible_shadow"):
        obj.visible_shadow = False
    obj["pc_reference_polish"] = True
    return obj


def _set_reference_bottom(
    obj: bpy.types.Object,
    blender_z: float,
    bury: float = 0.0,
) -> None:
    """Anchor an irregular linked mesh by its evaluated lower bound."""
    bpy.context.view_layer.update()
    lower = min((obj.matrix_world @ Vector(corner)).z for corner in obj.bound_box)
    obj.location.z += blender_z - lower - bury


def build_reference_shoreline(collection: bpy.types.Collection) -> None:
    """Replace the hard lagoon cut with a broad wet-to-dry PBR transition."""
    old_shore = bpy.data.objects.get("Wet_Shoreline_Transition")
    if old_shore is not None:
        old_shore.hide_render = True

    material = bpy.data.materials.get("PC_Reference_Shoreline_PBR") or bpy.data.materials.new(
        "PC_Reference_Shoreline_PBR"
    )
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    set_input(shader, "Roughness", 0.78)
    set_input(shader, "Specular IOR Level", 0.24)
    set_input(shader, "Coat Weight", 0.08)
    coordinates = nodes.new("ShaderNodeTexCoord")
    mapping = nodes.new("ShaderNodeMapping")
    mapping.inputs["Scale"].default_value = (17.0, 17.0, 17.0)
    albedo = nodes.new("ShaderNodeTexImage")
    albedo.image = load_image(
        PROJECT_ROOT
        / "Assets/_Game/Resources/Textures/Realistic/Shoreline/WetShore_Albedo.png"
    )
    normal_texture = nodes.new("ShaderNodeTexImage")
    normal_texture.image = load_image(
        PROJECT_ROOT
        / "Assets/_Game/Resources/Textures/Realistic/Shoreline/WetShore_Normal.png",
        non_color=True,
    )
    gradient = nodes.new("ShaderNodeAttribute")
    gradient.attribute_name = "shore_gradient"
    tones = nodes.new("ShaderNodeValToRGB")
    tones.color_ramp.elements.remove(tones.color_ramp.elements[1])
    wet = tones.color_ramp.elements[0]
    wet.position = 0.0
    wet.color = (0.025, 0.020, 0.010, 1.0)
    damp = tones.color_ramp.elements.new(0.30)
    damp.color = (0.105, 0.073, 0.030, 1.0)
    mud = tones.color_ramp.elements.new(0.62)
    mud.color = (0.205, 0.165, 0.075, 1.0)
    dry = tones.color_ramp.elements.new(1.0)
    dry.color = (0.145, 0.185, 0.058, 1.0)
    texture_mix = nodes.new("ShaderNodeMixRGB")
    texture_mix.blend_type = "MULTIPLY"
    texture_mix.inputs[0].default_value = 0.48
    normal_map = nodes.new("ShaderNodeNormalMap")
    normal_map.inputs["Strength"].default_value = 0.72
    micro = nodes.new("ShaderNodeTexNoise")
    micro.inputs["Scale"].default_value = 54.0
    micro.inputs["Detail"].default_value = 4.0
    bump = nodes.new("ShaderNodeBump")
    bump.inputs["Strength"].default_value = 0.22
    bump.inputs["Distance"].default_value = 0.045
    links.new(coordinates.outputs["Generated"], mapping.inputs["Vector"])
    links.new(mapping.outputs["Vector"], albedo.inputs["Vector"])
    links.new(mapping.outputs["Vector"], normal_texture.inputs["Vector"])
    links.new(gradient.outputs["Fac"], tones.inputs["Fac"])
    links.new(albedo.outputs["Color"], texture_mix.inputs[1])
    links.new(tones.outputs["Color"], texture_mix.inputs[2])
    links.new(texture_mix.outputs["Color"], shader.inputs["Base Color"])
    links.new(normal_texture.outputs["Color"], normal_map.inputs["Color"])
    links.new(coordinates.outputs["Generated"], micro.inputs["Vector"])
    links.new(normal_map.outputs["Normal"], bump.inputs["Normal"])
    links.new(micro.outputs["Fac"], bump.inputs["Height"])
    links.new(bump.outputs["Normal"], shader.inputs["Normal"])
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    segments = 192
    ring_scales = (0.978, 0.998, 1.020, 1.052, 1.090, 1.135, 1.185)
    vertices: list[Vector] = []
    faces: list[tuple[int, int, int, int]] = []
    for ring_index, scale in enumerate(ring_scales):
        ratio = ring_index / (len(ring_scales) - 1)
        eased = base.smoothstep01(ratio)
        for index in range(segments):
            angle = index / segments * math.tau
            contour = cinematic_lagoon_contour(angle)
            x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * scale
            z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * scale
            land_y = max(LAGOON_WATER_LEVEL + 0.03, cinematic_height_at(x, z) + 0.035)
            y = (LAGOON_WATER_LEVEL + 0.045) * (1.0 - eased) + land_y * eased
            y += math.sin(angle * 7.0 + ring_index * 0.8) * 0.018 * eased
            vertices.append(base.unity_to_blender((x, y, z)))
    for ring_index in range(len(ring_scales) - 1):
        current = ring_index * segments
        following = (ring_index + 1) * segments
        for index in range(segments):
            next_index = (index + 1) % segments
            faces.append(
                (current + index, current + next_index, following + next_index, following + index)
            )
    shore = base.create_mesh_object(
        "PC_REFINE_Shoreline_Gradient", collection, vertices, faces, material
    )
    color = shore.data.color_attributes.new(
        name="shore_gradient", type="FLOAT_COLOR", domain="POINT"
    )
    for index, item in enumerate(color.data):
        ratio = (index // segments) / (len(ring_scales) - 1)
        item.color = (ratio, ratio, ratio, 1.0)
    bevel = shore.modifiers.new("Soft shoreline edges", "BEVEL")
    bevel.width = 0.035
    bevel.segments = 2
    if hasattr(shore, "visible_shadow"):
        shore.visible_shadow = False
    shore["quest3_keep"] = True

    library = bpy.data.collections.get("98_PC_ASSET_LIBRARY")
    if library is None:
        return
    rocks = [obj for obj in library.objects if "RockMoss" in obj.name]
    if not rocks:
        return
    rng = random.Random(13092026)
    for index in range(28):
        angle = index / 28.0 * math.tau + rng.uniform(-0.075, 0.075)
        radius = rng.uniform(1.025, 1.115)
        contour = cinematic_lagoon_contour(angle)
        x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * radius
        z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * radius
        if base.nearest_track_distance(x, z) < 3.8:
            continue
        height = rng.uniform(0.75, 1.85)
        y = cinematic_height_at(x, z) - height * rng.uniform(0.20, 0.38)
        _linked_reference_instance(
            rng.choice(rocks),
            collection,
            f"PC_REFINE_ShoreRock_{index + 1:02}",
            Vector((x, y, z)),
            height,
            rng.uniform(0.0, 360.0),
            rng.uniform(0.75, 1.35),
            rng.uniform(0.72, 1.28),
            rng.uniform(-8.0, 8.0),
            rng.uniform(-8.0, 8.0),
        )

    bank_templates = [
        obj
        for obj in library.objects
        if any(key in obj.name for key in ("Fern", "Calathea", "Anthurium"))
    ]
    if bank_templates:
        for index in range(168):
            angle = index / 168.0 * math.tau + rng.uniform(-0.035, 0.035)
            radius = rng.uniform(1.018, 1.105)
            contour = cinematic_lagoon_contour(angle)
            x = LAGOON_CENTER_X + math.cos(angle) * LAGOON_RADIUS_X * contour * radius
            z = LAGOON_CENTER_Z + math.sin(angle) * LAGOON_RADIUS_Z * contour * radius
            if base.nearest_track_distance(x, z) < 3.8:
                continue
            _linked_reference_instance(
                rng.choice(bank_templates),
                collection,
                f"PC_REFINE_ShorePlant_{index + 1:03}",
                Vector((x, cinematic_height_at(x, z) - 0.06, z)),
                rng.uniform(1.6, 4.8),
                rng.uniform(0.0, 360.0),
                rng.uniform(0.76, 1.30),
                rng.uniform(0.72, 1.18),
            )


def build_reference_cave_integration(collection: bpy.types.Collection) -> None:
    """Replace the floating draft with grounded cliff masses and a clear portal."""
    obsolete = {
        "PC_Cave_Continuous_Rock_Portal",
        "PC_Cave_Cliff_L",
        "PC_Cave_Cliff_R",
        "PC_Hero_Cliff_03",
    }
    for obj in bpy.data.objects:
        if obj.name in obsolete or obj.name.startswith("PC_Hero_Rock_03_"):
            obj.hide_render = True

    library = bpy.data.collections.get("98_PC_ASSET_LIBRARY")
    if library is None:
        return
    rocks = [obj for obj in library.objects if "RockMoss" in obj.name]
    boulder = next((obj for obj in library.objects if "Boulder01_LOD0" in obj.name), None)
    mountain = next((obj for obj in library.objects if "Mountainside_LOD0" in obj.name), None)
    templates = rocks + ([boulder] if boulder is not None else [])
    if not templates or mountain is None:
        return

    point, tangent, _right, _up = base.track_frame(0.462)
    portal_up = Vector((0.0, 1.0, 0.0))
    portal_forward = Vector((tangent.x, 0.0, tangent.z))
    if portal_forward.length_squared < 1e-6:
        portal_forward = Vector((0.0, 0.0, 1.0))
    else:
        portal_forward.normalize()
    portal_right = Vector((portal_forward.z, 0.0, -portal_forward.x)).normalized()
    portal_ground = cinematic_height_at(point.x, point.z)
    # The opening begins at terrain level and surrounds the elevated rail.
    # Using point + 2 m put the entire former portal several metres in the air.
    portal_center = Vector((point.x, portal_ground + 5.65, point.z))

    void_material = bpy.data.materials.get("PC_REFINE_Cave_Void") or bpy.data.materials.new(
        "PC_REFINE_Cave_Void"
    )
    void_material.use_nodes = True
    void_nodes = void_material.node_tree.nodes
    void_links = void_material.node_tree.links
    void_nodes.clear()
    void_output = void_nodes.new("ShaderNodeOutputMaterial")
    void_shader = void_nodes.new("ShaderNodeBsdfPrincipled")
    set_input(void_shader, "Base Color", (0.0012, 0.0018, 0.0008, 1.0))
    set_input(void_shader, "Roughness", 0.98)
    set_input(void_shader, "Specular IOR Level", 0.08)
    void_links.new(void_shader.outputs["BSDF"], void_output.inputs["Surface"])
    void_center = portal_center - portal_forward * 2.35 + portal_up * 0.28
    segments = 72
    void_vertices = [base.unity_to_blender(void_center)]
    for index in range(segments):
        angle = index / segments * math.tau
        irregularity = 1.0 + math.sin(angle * 5.0 + 0.4) * 0.055
        edge = (
            void_center
            + portal_right * (math.cos(angle) * 3.75 * irregularity)
            + portal_up * (math.sin(angle) * 4.65 * irregularity)
        )
        void_vertices.append(base.unity_to_blender(edge))
    void_faces = [(0, index + 1, (index + 1) % segments + 1) for index in range(segments)]
    cave_void = base.create_mesh_object(
        "PC_REFINE_Cave_Entrance_Void", collection, void_vertices, void_faces, void_material
    )
    cave_void["quest3_disable"] = True

    rng = random.Random(58412026)
    portal_yaw = math.degrees(math.atan2(portal_forward.x, portal_forward.z))
    # One slim, grounded spire supports the high drop. The previous four
    # mountainside meshes were wider than the lagoon and read as floating slabs.
    spire_position = Vector((77.0, 0.0, 51.0))
    spire_ground = cinematic_height_at(spire_position.x, spire_position.z)
    spire = _linked_reference_instance(
        mountain,
        collection,
        "PC_REFINE_LiftHill_GroundedSpire",
        Vector((spire_position.x, spire_ground, spire_position.z)),
        30.0,
        71.0,
        0.56,
        0.82,
        2.0,
        -3.0,
    )
    _set_reference_bottom(spire, spire_ground, bury=1.8)
    spire["quest3_keep"] = True

    # A single closed arch is visually continuous and cannot produce the
    # suspended/stacked-boulder silhouette seen in V4.
    arch_material = None
    if rocks and rocks[0].data.materials:
        source_material = rocks[0].data.materials[0]
        arch_material = source_material.copy()
        arch_material.name = "PC_REFINE_CaveArch_Rock_PBR"
        for node in arch_material.node_tree.nodes:
            if node.type == "HUE_SAT":
                node.inputs["Saturation"].default_value = 0.92
                node.inputs["Value"].default_value = 1.18
            elif node.type == "BSDF_PRINCIPLED":
                set_input(node, "Roughness", 0.70)
                set_input(node, "Specular IOR Level", 0.30)
    if arch_material is not None:
        arch_segments = 112
        depth_values = (-1.8, 1.8)
        arch_vertices: list[Vector] = []
        for depth in depth_values:
            center = portal_center + portal_forward * depth
            for outer in (True, False):
                radius_x = 7.35 if outer else 5.20
                radius_y = 7.75 if outer else 5.85
                for segment in range(arch_segments):
                    angle = segment / arch_segments * math.tau
                    irregularity = (
                        1.0
                        + math.sin(angle * 5.0 + depth * 0.31) * 0.070
                        + math.sin(angle * 13.0 - depth * 0.17) * 0.032
                    )
                    edge = (
                        center
                        + portal_right * math.cos(angle) * radius_x * irregularity
                        + portal_up * math.sin(angle) * radius_y * irregularity
                    )
                    arch_vertices.append(base.unity_to_blender(edge))

        arch_faces: list[tuple[int, int, int, int]] = []
        stride = arch_segments * 2
        for depth_index in range(2):
            offset = depth_index * stride
            for segment in range(arch_segments):
                following = (segment + 1) % arch_segments
                arch_faces.append(
                    (
                        offset + segment,
                        offset + following,
                        offset + arch_segments + following,
                        offset + arch_segments + segment,
                    )
                )
        for segment in range(arch_segments):
            following = (segment + 1) % arch_segments
            front = 0
            back = stride
            arch_faces.append((front + segment, back + segment, back + following, front + following))
            arch_faces.append(
                (
                    front + arch_segments + segment,
                    front + arch_segments + following,
                    back + arch_segments + following,
                    back + arch_segments + segment,
                )
            )
        arch = base.create_mesh_object(
            "PC_REFINE_Cave_ContinuousArch",
            collection,
            arch_vertices,
            arch_faces,
            arch_material,
        )
        for polygon in arch.data.polygons:
            polygon.use_smooth = True
        bevel = arch.modifiers.new("Weathered arch edges", "BEVEL")
        bevel.width = 0.09
        bevel.segments = 2
        texture = bpy.data.textures.get("PC_REFINE_CaveArchRelief") or bpy.data.textures.new(
            "PC_REFINE_CaveArchRelief", type="CLOUDS"
        )
        texture.noise_scale = 0.72
        texture.noise_depth = 2
        relief = arch.modifiers.new("Rock silhouette relief", "DISPLACE")
        relief.texture = texture
        relief.strength = 0.62
        relief.mid_level = 0.50
        arch["quest3_keep"] = True

        # Photogrammetry pieces cover the procedural shell and turn its smooth
        # tube silhouette into a believable fractured cave mouth.
        for index in range(22):
            angle = index / 22.0 * math.tau + rng.uniform(-0.035, 0.035)
            edge = (
                portal_center
                - portal_forward * rng.uniform(0.05, 0.55)
                + portal_right * math.cos(angle) * rng.uniform(6.15, 7.05)
                + portal_up * math.sin(angle) * rng.uniform(6.55, 7.35)
            )
            height = rng.uniform(2.7, 4.4)
            cover = _linked_reference_instance(
                rng.choice(templates),
                collection,
                f"PC_REFINE_CaveArchRock_{index + 1:02}",
                edge - portal_up * height * 0.45,
                height,
                rng.uniform(0.0, 360.0),
                rng.uniform(0.82, 1.22),
                rng.uniform(0.76, 1.14),
                rng.uniform(-10.0, 10.0),
                rng.uniform(-10.0, 10.0),
            )
            cover["quest3_keep"] = True

    for index, side in enumerate((-1.15, -0.72, 0.72, 1.15)):
        xz = portal_center + portal_right * side * rng.uniform(7.0, 11.5)
        ground = cinematic_height_at(xz.x, xz.z)
        height = rng.uniform(2.6, 4.4)
        base_rock = _linked_reference_instance(
            rng.choice(templates),
            collection,
            f"PC_REFINE_CaveBaseRock_{index + 1:02}",
            Vector((xz.x, ground - height * 0.32, xz.z)),
            height,
            rng.uniform(0.0, 360.0),
            rng.uniform(0.90, 1.38),
            rng.uniform(0.84, 1.26),
            rng.uniform(-8.0, 8.0),
            rng.uniform(-8.0, 8.0),
        )
        _set_reference_bottom(base_rock, ground, bury=0.45)

    for index in range(12):
        angle = index / 12.0 * math.tau + rng.uniform(-0.18, 0.18)
        radius = rng.uniform(7.0, 13.5)
        x = spire_position.x + math.cos(angle) * radius
        z = spire_position.z + math.sin(angle) * radius
        ground = cinematic_height_at(x, z)
        height = rng.uniform(2.8, 6.4)
        rock = _linked_reference_instance(
            rng.choice(templates),
            collection,
            f"PC_REFINE_SpireBase_{index + 1:02}",
            Vector((x, ground, z)),
            height,
            rng.uniform(0.0, 360.0),
            rng.uniform(0.82, 1.28),
            rng.uniform(0.78, 1.18),
            rng.uniform(-8.0, 8.0),
            rng.uniform(-8.0, 8.0),
        )
        _set_reference_bottom(rock, ground, bury=rng.uniform(0.25, 0.80))

    plant_templates = [
        obj
        for obj in library.objects
        if any(key in obj.name for key in ("Fern", "Calathea", "Anthurium"))
    ]
    if plant_templates:
        for index in range(20):
            side = -1.0 if index % 2 == 0 else 1.0
            position = portal_center + portal_right * side * rng.uniform(7.0, 14.0)
            ground = cinematic_height_at(position.x, position.z)
            _linked_reference_instance(
                rng.choice(plant_templates),
                collection,
                f"PC_REFINE_CavePlant_{index + 1:02}",
                Vector((position.x, ground - 0.05, position.z)),
                rng.uniform(1.4, 3.8),
                rng.uniform(0.0, 360.0),
                rng.uniform(0.82, 1.24),
                rng.uniform(0.82, 1.18),
            )


def build_reference_sparse_supports(collection: bpy.types.Collection) -> None:
    """Keep structural rhythm without a forest of black support poles."""
    old_supports = bpy.data.objects.get("Track_Supports.001")
    if old_supports is not None:
        old_supports.hide_render = True
    material = bpy.data.materials.get("M_Track_Support_Dark")
    if material is None:
        return
    material.use_nodes = True
    shader = next(
        (node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
        None,
    )
    if shader is not None:
        set_input(shader, "Base Color", (0.028, 0.082, 0.038, 1.0))
        set_input(shader, "Metallic", 0.42)
        set_input(shader, "Roughness", 0.58)

    vertices: list[Vector] = []
    faces: list[tuple[int, int, int, int]] = []
    for index in range(0, 512, 48):
        point, _tangent, right, _up = base.track_frame(index / 512.0)
        for side in (-0.62, 0.62):
            top = point + right * side - Vector((0.0, 0.32, 0.0))
            bottom_y = cinematic_height_at(top.x, top.z) - 0.22
            height = max(0.4, top.y - bottom_y)
            center = Vector((top.x, bottom_y + height * 0.5, top.z))
            base.add_box_geometry(
                vertices,
                faces,
                base.unity_to_blender(center),
                Vector((1.0, 0.0, 0.0)),
                Vector((0.0, 1.0, 0.0)),
                Vector((0.0, 0.0, 1.0)),
                (0.32, 0.32, height),
            )
    supports = base.create_mesh_object(
        "PC_REFINE_SparseTrackSupports", collection, vertices, faces, material
    )
    supports["quest3_keep"] = True


def add_reference_canopy(collection: bpy.types.Collection) -> None:
    """Add a varied high canopy to remove the repeated-palm/open-hill look."""
    library = bpy.data.collections.get("98_PC_ASSET_LIBRARY")
    if library is None:
        return
    # The two million-polygon tree imports expose branch cards as pale slabs in
    # Eevee from this aerial angle. Replace only those old scatter instances.
    vegetation = bpy.data.collections.get("10_PC_CINEMATIC_VEGETATION")
    if vegetation is not None:
        for obj in vegetation.objects:
            data_name = obj.data.name if obj.data is not None else ""
            bad_card_tree = data_name in {"BezierCurve.001", "mesh.003"}
            sparse_old_background = obj.name.startswith("PC_Background_Canopy_") and (
                bad_card_tree or sum(ord(char) for char in obj.name) % 2 == 0
            )
            if (
                (obj.name.startswith("PC_Canopy_") and bad_card_tree)
                or sparse_old_background
                or (obj.name.startswith("PC_Hero_Foliage_") and bad_card_tree)
            ):
                obj.hide_render = True
    tree_templates = [
        obj
        for obj in library.objects
        if any(key in obj.name for key in ("Pachira", "CoconutPalm"))
    ]
    if not tree_templates:
        return
    rng = random.Random(9102026)
    positions = [
        (-128.0, -112.0),
        (-113.0, -121.0),
        (-96.0, -107.0),
        (-78.0, -124.0),
        (-58.0, -111.0),
        (-37.0, -126.0),
        (-16.0, -112.0),
        (7.0, -125.0),
        (30.0, -110.0),
        (53.0, -123.0),
        (75.0, -108.0),
        (96.0, -123.0),
        (116.0, -109.0),
        (132.0, -121.0),
        (-120.0, -82.0),
        (-88.0, -91.0),
        (-52.0, -84.0),
        (88.0, -87.0),
        (116.0, -78.0),
    ]
    for index, (x, z) in enumerate(positions):
        template = tree_templates[(index * 3 + index // 4) % len(tree_templates)]
        y = cinematic_height_at(x, z) - 0.16
        height = rng.uniform(19.0, 33.0)
        tree = _linked_reference_instance(
            template,
            collection,
            f"PC_REFINE_BackgroundTree_{index + 1:02}",
            Vector((x, y, z)),
            height,
            rng.uniform(0.0, 360.0),
            rng.uniform(0.82, 1.22),
            rng.uniform(0.84, 1.18),
        )
        if hasattr(tree, "visible_shadow"):
            tree.visible_shadow = False

    extra_index = len(positions)
    for row_index, z_base in enumerate((-108.0, -88.0, -67.0)):
        for column in range(17):
            x = -132.0 + column * 16.5 + rng.uniform(-4.0, 4.0)
            z = z_base + rng.uniform(-6.0, 6.0)
            if row_index == 2 and -72.0 < x < 78.0:
                continue
            if lagoon_distance(x, z) < 1.20 or base.nearest_track_distance(x, z) < 5.8:
                continue
            template = tree_templates[(column + row_index * 5) % len(tree_templates)]
            y = cinematic_height_at(x, z) - 0.18
            height = rng.uniform(16.0, 29.0) if row_index else rng.uniform(23.0, 36.0)
            _linked_reference_instance(
                template,
                collection,
                f"PC_REFINE_ForestLayer_{extra_index + 1:03}",
                Vector((x, y, z)),
                height,
                rng.uniform(0.0, 360.0),
                rng.uniform(0.78, 1.24),
                rng.uniform(0.80, 1.20),
            )
            extra_index += 1

    # Midground islands close the empty central hillside while keeping the
    # lagoon and herd meadow legible.
    placed_mid = 0
    attempts_mid = 0
    while placed_mid < 74 and attempts_mid < 1500:
        attempts_mid += 1
        x = rng.uniform(-132.0, 138.0)
        z = rng.uniform(-58.0, 48.0)
        if lagoon_distance(x, z) < 1.16 or base.nearest_track_distance(x, z) < 7.0:
            continue
        if -44.0 < x < 78.0 and -54.0 < z < 20.0 and rng.random() < 0.62:
            continue
        template = rng.choice(tree_templates)
        is_palm = "CoconutPalm" in template.name
        _linked_reference_instance(
            template,
            collection,
            f"PC_REFINE_MidgroundTree_{placed_mid + 1:03}",
            Vector((x, cinematic_height_at(x, z) - 0.16, z)),
            rng.uniform(15.0, 25.0) if is_palm else rng.uniform(10.0, 19.0),
            rng.uniform(0.0, 360.0),
            rng.uniform(0.80, 1.24),
            rng.uniform(0.80, 1.18),
        )
        placed_mid += 1

    for side_index, x_base in enumerate((-126.0, 126.0)):
        for row in range(10):
            x = x_base + rng.uniform(-8.0, 8.0)
            z = -62.0 + row * 17.0 + rng.uniform(-5.0, 5.0)
            if base.nearest_track_distance(x, z) < 6.2:
                continue
            template = tree_templates[(row * 2 + side_index) % len(tree_templates)]
            _linked_reference_instance(
                template,
                collection,
                f"PC_REFINE_SideCanopy_{side_index + 1}_{row + 1:02}",
                Vector((x, cinematic_height_at(x, z) - 0.20, z)),
                rng.uniform(21.0, 36.0),
                rng.uniform(0.0, 360.0),
                rng.uniform(0.82, 1.28),
                rng.uniform(0.82, 1.22),
            )

    undergrowth = [
        obj
        for obj in library.objects
        if any(key in obj.name for key in ("Fern", "Calathea", "Anthurium"))
    ]
    if undergrowth:
        placed = 0
        attempts = 0
        while placed < 180 and attempts < 1800:
            attempts += 1
            x = rng.uniform(-138.0, 138.0)
            z = rng.uniform(48.0, 142.0)
            if -28.0 < x < 102.0 and z < 98.0:
                continue
            if lagoon_distance(x, z) < 1.11 or base.nearest_track_distance(x, z) < 4.7:
                continue
            _linked_reference_instance(
                rng.choice(undergrowth),
                collection,
                f"PC_REFINE_ForegroundPlant_{placed + 1:03}",
                Vector((x, cinematic_height_at(x, z) - 0.08, z)),
                rng.uniform(2.4, 6.8),
                rng.uniform(0.0, 360.0),
                rng.uniform(0.84, 1.32),
                rng.uniform(0.82, 1.26),
            )
            placed += 1

    # Hero-sized tree ferns and broadleaf clusters create the layered jungle
    # silhouette visible in the target without duplicating mesh memory.
    hero_templates = [
        obj
        for obj in library.objects
        if any(key in obj.name for key in ("Pachira", "Fern"))
    ]
    if hero_templates:
        placed = 0
        attempts = 0
        while placed < 86 and attempts < 1400:
            attempts += 1
            x = rng.uniform(-142.0, 142.0)
            z = rng.uniform(42.0, 145.0)
            if -24.0 < x < 104.0 and z < 96.0:
                continue
            if lagoon_distance(x, z) < 1.12 or base.nearest_track_distance(x, z) < 5.2:
                continue
            template = rng.choice(hero_templates)
            is_tree = "Pachira" in template.name
            _linked_reference_instance(
                template,
                collection,
                f"PC_REFINE_HeroJungle_{placed + 1:03}",
                Vector((x, cinematic_height_at(x, z) - 0.14, z)),
                rng.uniform(8.5, 16.5) if is_tree else rng.uniform(4.6, 8.8),
                rng.uniform(0.0, 360.0),
                rng.uniform(0.86, 1.34),
                rng.uniform(0.84, 1.26),
            )
            placed += 1


def apply_reference_polish() -> None:
    """Idempotent beauty-reference pass safe to run in the open master file."""
    upgrade_terrain()
    upgrade_water()
    vegetation = bpy.data.collections.get("10_PC_CINEMATIC_VEGETATION")
    if vegetation is not None:
        for obj in vegetation.objects:
            world = obj.matrix_world.translation
            if lagoon_distance(world.x, -world.y) < 0.985:
                obj.hide_render = True
    remove_collection("16_PC_REFERENCE_POLISH")
    collection = new_collection("16_PC_REFERENCE_POLISH")
    build_reference_shoreline(collection)
    build_reference_cave_integration(collection)
    build_reference_sparse_supports(collection)
    add_reference_canopy(collection)

    trex = bpy.data.objects.get("PC_Tyrannosaurus_Hero")
    if trex is not None:
        for obj in [trex] + list(trex.children_recursive):
            obj.hide_render = True
    for index in range(1, 6):
        dinosaur = bpy.data.objects.get(f"PC_Triceratops_Hero_{index:02}")
        if dinosaur is None:
            continue
        original_scale = dinosaur.get("pc_reference_original_scale")
        if original_scale is None:
            original_scale = list(dinosaur.scale)
            dinosaur["pc_reference_original_scale"] = original_scale
        dinosaur.scale = tuple(value * 1.92 for value in original_scale)
    apatosaurus = bpy.data.objects.get("PC_Apatosaurus_Hero_Back")
    if apatosaurus is not None:
        apatosaurus.location = (-15.0, 64.0, 8.0)
        original_scale = apatosaurus.get("pc_reference_original_scale")
        if original_scale is None:
            original_scale = list(apatosaurus.scale)
            apatosaurus["pc_reference_original_scale"] = original_scale
        apatosaurus.scale = tuple(value * 1.58 for value in original_scale)
    pteranodon_heights = (34.0, 38.0, 42.0, 47.0, 41.0)
    for index, height in enumerate(pteranodon_heights, start=4):
        pteranodon = bpy.data.objects.get(f"PC_Pteranodon_{index:02}")
        if pteranodon is not None:
            pteranodon.location.z = height

    key_data = bpy.data.lights.get("PC_Reference_Warm_Key_Data") or bpy.data.lights.new(
        "PC_Reference_Warm_Key_Data", type="AREA"
    )
    key_data.energy = 6200.0
    key_data.shape = "DISK"
    key_data.size = 66.0
    key_data.color = (1.0, 0.46, 0.22)
    key = bpy.data.objects.new("PC_Reference_Warm_Key", key_data)
    collection.objects.link(key)
    key.location = (-112.0, -126.0, 118.0)
    key.rotation_euler = (Vector((8.0, -18.0, 7.0)) - key.location).to_track_quat(
        "-Z", "Y"
    ).to_euler()

    camera = bpy.data.objects.get("Camera_CinematicReference")
    if camera is not None:
        camera.location = (-138.0, -158.0, 54.0)
        target = Vector((18.0, -7.0, 9.0))
        camera.rotation_euler = (target - camera.location).to_track_quat("-Z", "Y").to_euler()
        camera.data.lens = 57.0
        camera.data.dof.aperture_fstop = 12.0
        from bpy_extras.object_utils import world_to_camera_view

        scene = bpy.context.scene
        for obj in collection.objects:
            if not obj.name.startswith(
                ("PC_REFINE_BackgroundTree_", "PC_REFINE_ForestLayer_")
            ):
                continue
            ndc = world_to_camera_view(scene, camera, obj.matrix_world.translation)
            key = sum(ord(char) for char in obj.name) % 10
            if 0.16 < ndc.x < 0.86 and 0.73 < ndc.y < 1.05 and key < 7:
                obj.hide_render = True

        dinosaur_views = [
            world_to_camera_view(scene, camera, dinosaur.matrix_world.translation)
            for dinosaur in bpy.data.objects
            if dinosaur.name.startswith("PC_Triceratops_Hero_")
            or dinosaur.name == "PC_Apatosaurus_Hero_Back"
        ]
        for vegetation_collection_name in (
            "10_PC_CINEMATIC_VEGETATION",
            "16_PC_REFERENCE_POLISH",
        ):
            vegetation_collection = bpy.data.collections.get(vegetation_collection_name)
            if vegetation_collection is None:
                continue
            for obj in vegetation_collection.objects:
                if not any(
                    key in obj.name
                    for key in (
                        "Canopy",
                        "Tree",
                        "Plant",
                        "Jungle",
                        "Undergrowth",
                        "Groundcover",
                    )
                ):
                    continue
                ndc = world_to_camera_view(scene, camera, obj.matrix_world.translation)
                if any(
                    ndc.z < dinosaur_view.z
                    and abs(ndc.x - dinosaur_view.x) < 0.052
                    and abs(ndc.y - dinosaur_view.y) < 0.070
                    for dinosaur_view in dinosaur_views
                ):
                    obj.hide_render = True

    vegetation = bpy.data.collections.get("10_PC_CINEMATIC_VEGETATION")
    if vegetation is not None:
        curate_camera_view(vegetation)

    scene = bpy.context.scene
    scene.view_settings.exposure = 0.54
    scene.view_settings.use_white_balance = True
    scene.view_settings.white_balance_temperature = 7300.0
    scene.view_settings.white_balance_tint = 6.0
    scene.render.resolution_percentage = 50
    scene.render.filepath = str(PREVIEW_PATH)
    scene["pc_reference_polish"] = "cave_shore_canopy_composition_v1"
    sun = bpy.data.objects.get("Sun_Jurassic")
    if sun is not None and sun.type == "LIGHT":
        sun.data.energy = 5.35
        sun.data.color = (1.0, 0.66, 0.43)
        sun.data.angle = math.radians(4.2)
    fill = bpy.data.objects.get("Sky_Fill")
    if fill is not None and fill.type == "LIGHT":
        fill.data.energy = 245.0
        fill.data.color = (0.34, 0.37, 0.38)
    atmosphere = bpy.data.materials.get("PC_Jurassic_Atmosphere")
    if atmosphere is not None and atmosphere.use_nodes:
        volume = next(
            (node for node in atmosphere.node_tree.nodes if node.type == "PRINCIPLED_VOLUME"),
            None,
        )
        if volume is not None:
            volume.inputs["Density"].default_value = 0.00072
            volume.inputs["Color"].default_value = (0.72, 0.61, 0.46, 1.0)
            volume.inputs["Anisotropy"].default_value = 0.48
    if scene.world is not None and scene.world.use_nodes:
        backgrounds = [node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND"]
        for node in backgrounds:
            color_links = node.inputs["Color"].links
            is_hdri = any(link.from_node.type == "TEX_ENVIRONMENT" for link in color_links)
            node.inputs["Strength"].default_value = 0.48 if is_hdri else 0.52
        ramps = [node for node in scene.world.node_tree.nodes if node.type == "VALTORGB"]
        if ramps:
            sky_ramp = ramps[0].color_ramp
            sky_ramp.elements[0].color = (0.22, 0.25, 0.24, 1.0)
            if len(sky_ramp.elements) > 2:
                sky_ramp.elements[1].color = (0.58, 0.43, 0.29, 1.0)
            sky_ramp.elements[-1].color = (0.98, 0.67, 0.36, 1.0)

    # Branch-card imports need their diffuse alpha connected in Eevee; without
    # it they appear as the pale geometric patches visible in the V3 preview.
    for material_name in (
        "PC_PolyHaven_TreeSmall02_Branch_CC0",
        "PC_PolyHaven_IslandTree03_Branch_CC0",
    ):
        material = bpy.data.materials.get(material_name)
        if material is None or not material.use_nodes:
            continue
        if hasattr(material, "surface_render_method"):
            material.surface_render_method = "DITHERED"
        nodes = material.node_tree.nodes
        links = material.node_tree.links
        shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
        diffuse = next(
            (
                node
                for node in nodes
                if node.type == "TEX_IMAGE" and node.image is not None
            ),
            None,
        )
        if shader is not None and diffuse is not None and not shader.inputs["Alpha"].is_linked:
            links.new(diffuse.outputs["Alpha"], shader.inputs["Alpha"])

    water_material = bpy.data.materials.get("M_Lagoon_Water")
    if water_material is not None and water_material.use_nodes:
        water_shader = next(
            (node for node in water_material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"),
            None,
        )
        if water_shader is not None:
            set_input(water_shader, "Roughness", 0.045)
            set_input(water_shader, "Transmission Weight", 0.22)
            set_input(water_shader, "Alpha", 0.965)
            set_input(water_shader, "Coat Weight", 0.56)
            set_input(water_shader, "Coat Roughness", 0.08)
            set_input(water_shader, "Specular IOR Level", 0.64)
        for node in water_material.node_tree.nodes:
            if node.type == "VALTORGB":
                node.color_ramp.elements[0].color = (0.006, 0.030, 0.032, 1.0)
                node.color_ramp.elements[-1].color = (0.040, 0.155, 0.145, 1.0)
            elif node.type == "BUMP":
                node.inputs["Strength"].default_value = 0.34
                node.inputs["Distance"].default_value = 0.085

    terrain = bpy.data.objects.get("Terrain_Editable_129x129")
    if terrain is not None:
        old = terrain.modifiers.get("PC reference micro relief")
        if old is not None:
            terrain.modifiers.remove(old)
        texture = bpy.data.textures.get("PC_Reference_Ground_Relief") or bpy.data.textures.new(
            "PC_Reference_Ground_Relief", type="CLOUDS"
        )
        texture.noise_scale = 0.82
        texture.noise_depth = 2
        relief = terrain.modifiers.new("PC reference micro relief", "DISPLACE")
        relief.texture = texture
        relief.texture_coords = "GLOBAL"
        relief.strength = 0.16
        relief.mid_level = 0.50

    bpy.context.view_layer.update()


def main() -> None:
    removed_pachira_leaks = remove_pachira_import_leaks()
    for name in PC_COLLECTIONS:
        remove_collection(name)
    base.TRACK_POINTS = list(PC_TRACK_POINTS)
    base.height_at = cinematic_height_at
    vegetation = new_collection("10_PC_CINEMATIC_VEGETATION")
    rocks = new_collection("11_PC_CINEMATIC_ROCKS")
    herds = new_collection("12_PC_CINEMATIC_HERDS")
    atmosphere = new_collection("13_PC_CINEMATIC_ATMOSPHERE")
    cinematic_track = new_collection("14_PC_CINEMATIC_TRACK")
    details = new_collection("15_PC_CINEMATIC_DETAILS")
    library = new_collection("98_PC_ASSET_LIBRARY")
    library.hide_render = True
    library.hide_viewport = True

    upgrade_terrain()
    upgrade_track()
    build_cinematic_track(cinematic_track, atmosphere)
    upgrade_water()
    upgrade_cart()
    upgrade_dinosaur_materials()
    populate_vegetation(vegetation, library)
    populate_rocks(rocks, library)
    populate_herds(herds)
    hide_original_dinosaurs()
    fix_cart_orientation()
    populate_cart_train(herds)
    hide_original_cart()
    configure_camera(atmosphere)
    curate_camera_view(vegetation)
    configure_render(atmosphere)
    apply_reference_polish()

    scene = bpy.context.scene
    scene["pc_cinematic_target"] = "Match the supplied high-resolution Jurassic valley reference"
    scene["pc_pass_version"] = 2
    scene["pc_assets"] = (
        "Poly Haven Fern02, Anthurium Botany 01, Pachira Aquatica 01, "
        "Tree Small 02, Rock Moss Set 01 - CC0"
    )
    bpy.ops.object.select_all(action="DESELECT")
    bpy.context.view_layer.update()
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_mainfile()
    if AUTO_RENDER_PREVIEW:
        bpy.ops.render.render(write_still=True)
    bpy.ops.wm.save_mainfile()
    print(
        "PC_CINEMATIC_PASS_OK "
        f"objects={len(bpy.data.objects)} meshes={len(bpy.data.meshes)} "
        f"materials={len(bpy.data.materials)} pachira_leaks_removed={removed_pachira_leaks} "
        f"preview={PREVIEW_PATH}"
    )


if __name__ == "__main__":
    main()
