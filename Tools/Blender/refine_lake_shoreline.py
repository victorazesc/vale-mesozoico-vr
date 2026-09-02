"""Refine only the lagoon shoreline in the existing cinematic scene."""

from __future__ import annotations

import math
import os

import bpy


CENTER_X = 38.0
CENTER_Y = -28.0
SEGMENTS = 96


def set_input(node: bpy.types.Node, name: str, value) -> None:
    socket = node.inputs.get(name)
    if socket is not None:
        socket.default_value = value


def angular_distance(angle: float, target: float) -> float:
    return math.atan2(math.sin(angle - target), math.cos(angle - target))


def lobe(angle: float, target: float, width: float) -> float:
    distance = angular_distance(angle, target)
    return math.exp(-((distance / width) ** 2))


def shoreline_shape(angle: float) -> float:
    """Large smooth lobes plus small breakup, avoiding a geometric oval."""
    return (
        1.0
        + 0.060 * math.sin(3.0 * angle + 0.35)
        + 0.036 * math.sin(5.0 * angle - 1.10)
        + 0.022 * math.sin(9.0 * angle + 0.80)
        + 0.012 * math.sin(14.0 * angle - 0.30)
        + 0.050 * lobe(angle, -2.45, 0.38)
        + 0.052 * lobe(angle, 2.48, 0.30)
        + 0.054 * lobe(angle, -2.70, 0.30)
        - 0.085 * lobe(angle, 3.06, 0.24)
        - 0.075 * lobe(angle, -0.62, 0.30)
        - 0.060 * lobe(angle, 1.48, 0.34)
    )


def shore_width(angle: float) -> float:
    width = (
        0.92
        + 0.28 * math.sin(2.0 * angle + 0.55)
        + 0.18 * math.sin(6.0 * angle - 0.85)
        + 0.48 * lobe(angle, 2.55, 0.32)
        + 0.35 * lobe(angle, -1.85, 0.42)
        - 0.28 * lobe(angle, 0.10, 0.30)
    )
    return max(0.42, min(1.65, width))


def shoreline_point(angle: float, offset: float = 0.0) -> tuple[float, float]:
    factor = shoreline_shape(angle)
    return (
        CENTER_X + math.cos(angle) * (69.5 * factor + offset),
        CENTER_Y + math.sin(angle) * (49.0 * factor + offset * 0.72),
    )


def set_ring_vertex(vertex, angle: float, offset: float, ring: int) -> None:
    breakup = (0.12 + ring * 0.16) * math.sin(11.0 * angle + ring * 0.9)
    x, y = shoreline_point(angle, offset + breakup)
    vertex.co.x = x
    vertex.co.y = y
    vertex.co.z = -0.775 + 0.009 * math.sin(4.0 * angle + ring * 0.55)


def reshape_water() -> None:
    water = bpy.data.objects.get("Lagoon_Water_Editable")
    if water is None or water.type != "MESH" or len(water.data.vertices) != SEGMENTS + 1:
        raise RuntimeError("Lagoon_Water_Editable topology was not found as expected")

    water.data.vertices[0].co.x = CENTER_X
    water.data.vertices[0].co.y = CENTER_Y
    water.data.vertices[0].co.z = -0.82
    for index, vertex in enumerate(water.data.vertices[1:]):
        angle = -math.tau * index / SEGMENTS
        vertex.co.x, vertex.co.y = shoreline_point(angle)
        vertex.co.z = -0.82

    water.data.update()
    water["pc_natural_shoreline_v2"] = True


def reshape_shallows() -> None:
    shallows = bpy.data.objects.get("PC_Lagoon_Shallows")
    if shallows is None or shallows.type != "MESH" or len(shallows.data.vertices) != SEGMENTS * 3:
        raise RuntimeError("PC_Lagoon_Shallows topology was not found as expected")

    for ring in range(3):
        offset = ring * SEGMENTS
        for index in range(SEGMENTS):
            angle = -math.tau * index / SEGMENTS
            width = shore_width(angle)
            ring_offset = (
                -3.8,
                0.35 + 0.85 * width,
                3.5 + 3.1 * width,
            )[ring]
            set_ring_vertex(shallows.data.vertices[offset + index], angle, ring_offset, ring)

    attribute = shallows.data.color_attributes.get("shore_fade")
    if attribute is None:
        attribute = shallows.data.color_attributes.new(
            name="shore_fade", type="FLOAT_COLOR", domain="POINT"
        )
    for index, element in enumerate(attribute.data):
        ring = min(index // SEGMENTS, 2)
        fade = (0.08, 0.92, 0.025)[ring]
        element.color = (fade, fade, fade, 1.0)

    shallows.data.update()
    shallows["pc_natural_shoreline_v2"] = True


def tune_shore_material() -> None:
    material = bpy.data.materials.get("PC_Lagoon_Shallows_PBR")
    if material is None or material.node_tree is None:
        raise RuntimeError("PC_Lagoon_Shallows_PBR was not found")

    if hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    material.diffuse_color = (0.055, 0.070, 0.035, 0.76)

    nodes = material.node_tree.nodes
    links = material.node_tree.links
    shader = next((node for node in nodes if node.type == "BSDF_PRINCIPLED"), None)
    noise = next((node for node in nodes if node.type == "TEX_NOISE"), None)
    vertex_color = next((node for node in nodes if node.type == "VERTEX_COLOR"), None)
    mix_shader = next((node for node in nodes if node.type == "MIX_SHADER"), None)

    if shader is not None:
        set_input(shader, "Roughness", 0.48)
        set_input(shader, "Transmission Weight", 0.0)
        set_input(shader, "Specular IOR Level", 0.28)
        set_input(shader, "Alpha", 0.78)
        set_input(shader, "Coat Weight", 0.06)
        set_input(shader, "Coat Roughness", 0.32)

    if noise is not None:
        set_input(noise, "Scale", 1.65)
        set_input(noise, "Detail", 4.6)
        set_input(noise, "Roughness", 0.68)
        set_input(noise, "Distortion", 0.12)

    for node in nodes:
        if node.type == "VALTORGB":
            node.color_ramp.elements[0].position = 0.26
            node.color_ramp.elements[0].color = (0.010, 0.020, 0.010, 1.0)
            node.color_ramp.elements[-1].position = 0.76
            node.color_ramp.elements[-1].color = (0.145, 0.078, 0.025, 1.0)
        elif node.type == "BUMP":
            set_input(node, "Strength", 0.16)
            set_input(node, "Distance", 0.07)

    if noise is not None and vertex_color is not None and mix_shader is not None:
        alpha_variation = nodes.get("PC_Shore_AlphaVariation")
        if alpha_variation is None:
            alpha_variation = nodes.new("ShaderNodeMath")
            alpha_variation.name = "PC_Shore_AlphaVariation"
        alpha_variation.operation = "MULTIPLY_ADD"
        alpha_variation.inputs[1].default_value = 0.34
        alpha_variation.inputs[2].default_value = 0.66

        alpha_multiply = nodes.get("PC_Shore_AlphaMultiply")
        if alpha_multiply is None:
            alpha_multiply = nodes.new("ShaderNodeMath")
            alpha_multiply.name = "PC_Shore_AlphaMultiply"
        alpha_multiply.operation = "MULTIPLY"

        for socket in (alpha_variation.inputs[0], alpha_multiply.inputs[0], alpha_multiply.inputs[1], mix_shader.inputs[0]):
            for link in list(socket.links):
                links.remove(link)
        links.new(noise.outputs["Fac"], alpha_variation.inputs[0])
        links.new(vertex_color.outputs["Color"], alpha_multiply.inputs[0])
        links.new(alpha_variation.outputs[0], alpha_multiply.inputs[1])
        links.new(alpha_multiply.outputs[0], mix_shader.inputs[0])

    material["pc_natural_shoreline_v2"] = True


def add_shore_detail_rocks() -> None:
    """Break long shoreline arcs with linked, already-licensed rock meshes."""
    collection = bpy.data.collections.get("PC_Lagoon_Shore_Details")
    if collection is None:
        collection = bpy.data.collections.new("PC_Lagoon_Shore_Details")
        bpy.context.scene.collection.children.link(collection)

    for obj in list(collection.objects):
        if obj.name.startswith("PC_Shore_DetailRock_"):
            bpy.data.objects.remove(obj, do_unlink=True)

    placements = (
        (2.96, 2, 0.38, 0.31, 0.34, 1.10),
        (2.57, 4, 0.52, 0.37, 0.42, 0.75),
        (-2.78, 6, 0.42, 0.34, 0.30, 1.30),
        (-2.20, 3, 0.34, 0.29, 0.28, 0.55),
        (-1.54, 8, 0.46, 0.32, 0.35, 1.05),
        (0.42, 5, 0.39, 0.30, 0.31, 0.80),
        (1.28, 7, 0.48, 0.36, 0.38, 1.20),
    )
    for number, (angle, source_number, sx, sy, sz, turn) in enumerate(placements, 1):
        source = bpy.data.objects.get(f"PC_Lagoon_MossRock_{source_number:02d}")
        if source is None or source.type != "MESH":
            continue
        rock = source.copy()
        rock.data = source.data
        rock.name = f"PC_Shore_DetailRock_{number:02d}"
        rock.parent = None
        rock.animation_data_clear()
        rock.hide_render = False
        rock.hide_viewport = False
        x, y = shoreline_point(angle, 1.0 + 1.35 * shore_width(angle))
        rock.location = (x, y, -0.80)
        rock.rotation_euler = (0.0, 0.0, angle + turn)
        rock.scale = (sx, sy, sz)
        rock["pc_natural_shoreline_detail"] = True
        collection.objects.link(rock)


def refine_lake_shoreline() -> None:
    reshape_water()
    reshape_shallows()
    tune_shore_material()
    add_shore_detail_rocks()
    bpy.context.scene["pc_lake_shoreline"] = "natural_v2"


refine_lake_shoreline()

if os.environ.get("VALE_SHORELINE_NO_SAVE") != "1":
    bpy.ops.wm.save_as_mainfile(filepath=bpy.data.filepath)

print("VALE_NATURAL_SHORELINE_V2_OK")
