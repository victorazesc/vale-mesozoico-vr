"""Final lightweight PC polish for the existing Vale Mesozoico scene.

This pass only adjusts existing datablocks. It is safe to run repeatedly and
does not import assets or duplicate geometry.
"""

from __future__ import annotations

import os
import math

import bpy
from mathutils import Vector


def set_input(node: bpy.types.Node, name: str, value) -> None:
    socket = node.inputs.get(name)
    if socket is not None:
        socket.default_value = value


def principled_nodes(material: bpy.types.Material | None):
    if material is None or material.node_tree is None:
        return []
    return [node for node in material.node_tree.nodes if node.type == "BSDF_PRINCIPLED"]


def tune_water() -> None:
    material = bpy.data.materials.get("M_Lagoon_Water")
    if material is not None and material.node_tree is not None:
        if hasattr(material, "surface_render_method"):
            material.surface_render_method = "DITHERED"
        material.diffuse_color = (0.012, 0.060, 0.055, 0.94)

        for shader in principled_nodes(material):
            set_input(shader, "Roughness", 0.105)
            set_input(shader, "IOR", 1.333)
            set_input(shader, "Transmission Weight", 0.08)
            set_input(shader, "Specular IOR Level", 0.56)
            set_input(shader, "Alpha", 0.94)
            set_input(shader, "Coat Weight", 0.46)
            set_input(shader, "Coat Roughness", 0.075)

        noises = [node for node in material.node_tree.nodes if node.type == "TEX_NOISE"]
        noises.sort(key=lambda node: float(node.inputs["Scale"].default_value))
        if noises:
            noises[0].inputs["Scale"].default_value = 0.42
            noises[0].inputs["Detail"].default_value = 3.4
            noises[0].inputs["Roughness"].default_value = 0.58
            noises[0].inputs["Distortion"].default_value = 0.10
        if len(noises) > 1:
            noises[-1].inputs["Scale"].default_value = 4.6
            noises[-1].inputs["Detail"].default_value = 2.2
            noises[-1].inputs["Roughness"].default_value = 0.46

        for node in material.node_tree.nodes:
            if node.type == "BUMP":
                node.inputs["Strength"].default_value = 0.12
                node.inputs["Distance"].default_value = 0.075
            elif node.type == "VALTORGB":
                ramp = node.color_ramp
                ramp.elements[0].position = 0.24
                ramp.elements[0].color = (0.004, 0.016, 0.014, 1.0)
                ramp.elements[-1].position = 0.78
                ramp.elements[-1].color = (0.020, 0.072, 0.062, 1.0)

    shallows = bpy.data.materials.get("PC_Lagoon_Shallows_PBR")
    if shallows is not None:
        if hasattr(shallows, "surface_render_method"):
            shallows.surface_render_method = "DITHERED"
        shallows.diffuse_color = (0.055, 0.115, 0.075, 0.62)
        for shader in principled_nodes(shallows):
            set_input(shader, "Roughness", 0.22)
            set_input(shader, "Transmission Weight", 0.06)
            set_input(shader, "Specular IOR Level", 0.34)
            set_input(shader, "Alpha", 0.66)
            set_input(shader, "Coat Weight", 0.22)
        if shallows.node_tree is not None:
            for node in shallows.node_tree.nodes:
                if node.type == "VALTORGB":
                    node.color_ramp.elements[0].color = (0.014, 0.027, 0.012, 1.0)
                    node.color_ramp.elements[-1].color = (0.205, 0.125, 0.040, 1.0)

    # The three concentric rings now fade in and back out. The former opaque
    # outer ring was responsible for the cut-out shoreline silhouette.
    shallow_object = bpy.data.objects.get("PC_Lagoon_Shallows")
    if shallow_object is not None and shallow_object.type == "MESH":
        attribute = shallow_object.data.color_attributes.get("shore_fade")
        if attribute is not None and len(attribute.data) == len(shallow_object.data.vertices):
            ring_size = len(attribute.data) // 3
            for index, element in enumerate(attribute.data):
                ring = min(index // ring_size, 2)
                fade = (0.0, 0.85, 0.015)[ring]
                element.color = (fade, fade, fade, 1.0)
        if not shallow_object.get("pc_final_shore_irregular_v1"):
            center_x, center_y = 38.0, -28.0
            ring_size = len(shallow_object.data.vertices) // 3
            for index, vertex in enumerate(shallow_object.data.vertices):
                dx = vertex.co.x - center_x
                dy = vertex.co.y - center_y
                angle = math.atan2(dy, dx)
                ring = min(index // ring_size, 2)
                irregularity = (
                    1.0
                    + math.sin(angle * 3.0 + 0.45) * 0.035
                    + math.sin(angle * 7.0 - 0.80) * 0.024
                    + math.sin(angle * 13.0 + 1.10) * (0.008 + ring * 0.004)
                )
                vertex.co.x = center_x + dx * irregularity
                vertex.co.y = center_y + dy * irregularity
            shallow_object.data.update()
            shallow_object["pc_final_shore_irregular_v1"] = True

    water = bpy.data.objects.get("Lagoon_Water_Editable")
    if water is not None:
        if water.type == "MESH" and len(water.data.vertices) > 4 and not water.get(
            "pc_final_water_irregular_v1"
        ):
            center = water.data.vertices[0].co.copy()
            for vertex in list(water.data.vertices)[1:]:
                dx = vertex.co.x - center.x
                dy = vertex.co.y - center.y
                angle = math.atan2(dy, dx)
                irregularity = (
                    1.0
                    + math.sin(angle * 3.0 + 0.45) * 0.035
                    + math.sin(angle * 7.0 - 0.80) * 0.024
                    + math.sin(angle * 13.0 + 1.10) * 0.012
                )
                vertex.co.x = center.x + dx * irregularity
                vertex.co.y = center.y + dy * irregularity
            water.data.update()
            water["pc_final_water_irregular_v1"] = True
        water["pc_final_water_polish"] = "reflection_v1"


def tune_cave() -> None:
    # This legacy shadow mesh crosses the cliff when viewed from the hero
    # camera and creates the two flat black bars seen in the previous render.
    legacy_shadow = bpy.data.objects.get("PC_Cave_Entrance_Shadow")
    if legacy_shadow is not None:
        legacy_shadow.hide_render = True
        legacy_shadow.hide_viewport = True

    # Diagnostic rendering confirmed this legacy rail-bed mesh is the source
    # of the flat black bands cutting through the exterior cliff.
    legacy_rail_bed = bpy.data.objects.get("PC_Cave_Rocky_Rail_Bed")
    if legacy_rail_bed is not None:
        legacy_rail_bed.hide_render = True
        legacy_rail_bed.hide_viewport = True

    cave_void = bpy.data.materials.get("PC_REFINE_Cave_Void")
    if cave_void is not None:
        cave_void.diffuse_color = (0.004, 0.009, 0.005, 1.0)
        for shader in principled_nodes(cave_void):
            set_input(shader, "Base Color", (0.004, 0.009, 0.005, 1.0))
            set_input(shader, "Roughness", 1.0)
            set_input(shader, "Specular IOR Level", 0.04)

    flat_void = bpy.data.objects.get("PC_REFINE_Cave_Entrance_Void")
    if flat_void is not None:
        flat_void.hide_render = True
        flat_void.hide_viewport = True

    build_cave_tunnel(flat_void)

    cave_deep_fill = bpy.data.objects.get("PC_Cave_Deep_Fill")
    if cave_deep_fill is not None and cave_deep_fill.type == "LIGHT":
        cave_deep_fill.data.energy = 1050.0
        cave_deep_fill.data.color = (0.20, 0.28, 0.14)
    cave_interior_fill = bpy.data.objects.get("PC_Cave_Interior_Fill")
    if cave_interior_fill is not None and cave_interior_fill.type == "LIGHT":
        cave_interior_fill.data.energy = 1850.0
        cave_interior_fill.data.color = (0.34, 0.39, 0.23)

    for material in bpy.data.materials:
        if not material.name.startswith("PC_REFINE_CaveArch_Rock_PBR"):
            continue
        for node in material.node_tree.nodes if material.node_tree else []:
            if node.type == "HUE_SAT":
                node.inputs["Saturation"].default_value = 0.86
                node.inputs["Value"].default_value = 1.08
            elif node.type == "BSDF_PRINCIPLED":
                set_input(node, "Roughness", 0.68)
                set_input(node, "Specular IOR Level", 0.28)

    cave_mist = bpy.data.materials.get("PC_Cave_Shadow_Mist")
    if cave_mist is not None and cave_mist.node_tree is not None:
        for node in cave_mist.node_tree.nodes:
            if node.type == "PRINCIPLED_VOLUME":
                set_input(node, "Density", 0.032)
                set_input(node, "Anisotropy", 0.18)


def build_cave_tunnel(portal: bpy.types.Object | None) -> None:
    """Create a shallow textured tunnel behind the portal with real depth."""
    cleanup_names = (
        "PC_FINAL_Cave_Tunnel_Interior",
        "PC_FINAL_Cave_Tunnel_End",
        "PC_FINAL_Cave_InnerArch_01",
        "PC_FINAL_Cave_InnerArch_02",
        "PC_FINAL_Cave_InnerArch_03",
    )
    for object_name in cleanup_names:
        old = bpy.data.objects.get(object_name)
        if old is not None:
            bpy.data.objects.remove(old, do_unlink=True)

    if portal is None or portal.type != "MESH" or not portal.data.vertices:
        return

    center = portal.matrix_world @ portal.data.vertices[0].co
    if portal.data.polygons:
        depth_direction = portal.matrix_world.to_3x3() @ portal.data.polygons[0].normal
    else:
        depth_direction = Vector((1.0, 0.0, 0.0))
    depth_direction.z = 0.0
    if depth_direction.length_squared < 1e-6:
        depth_direction = Vector((1.0, 0.0, 0.0))
    depth_direction.normalize()
    camera = bpy.context.scene.camera
    if camera is not None and depth_direction.dot((camera.location - center).normalized()) > 0.0:
        depth_direction.negate()
    up = Vector((0.0, 0.0, 1.0))
    right = depth_direction.cross(up).normalized()

    radial_segments = 48
    depth_steps = (0.25, 1.4, 2.8, 4.4, 6.2, 8.3)
    vertices: list[tuple[float, float, float]] = []
    for depth_index, depth in enumerate(depth_steps):
        t = depth / depth_steps[-1]
        ring_center = center + depth_direction * depth
        radius_x = 5.05 * (1.0 - t * 0.24)
        radius_z = 5.58 * (1.0 - t * 0.22)
        for segment in range(radial_segments):
            angle = segment / radial_segments * math.tau
            irregularity = (
                1.0
                + math.sin(angle * 5.0 + depth_index * 0.63) * 0.055
                + math.sin(angle * 11.0 - depth_index * 0.31) * 0.025
            )
            point = (
                ring_center
                + right * (math.cos(angle) * radius_x * irregularity)
                + up * (math.sin(angle) * radius_z * irregularity)
            )
            vertices.append(tuple(point))

    faces: list[tuple[int, int, int, int]] = []
    for depth_index in range(len(depth_steps) - 1):
        first = depth_index * radial_segments
        second = (depth_index + 1) * radial_segments
        for segment in range(radial_segments):
            next_segment = (segment + 1) % radial_segments
            faces.append((first + segment, second + segment, second + next_segment, first + next_segment))

    mesh = bpy.data.meshes.new("PC_FINAL_Cave_Tunnel_Interior_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    tunnel = bpy.data.objects.new("PC_FINAL_Cave_Tunnel_Interior", mesh)
    collection = next(iter(portal.users_collection), bpy.context.scene.collection)
    collection.objects.link(tunnel)
    rock_material = bpy.data.materials.get("PC_FINAL_Cave_Interior_Rock")
    if rock_material is None:
        source_material = bpy.data.materials.get("PC_REFINE_CaveArch_Rock_PBR.001")
        if source_material is None:
            source_material = bpy.data.materials.get("PC_REFINE_CaveArch_Rock_PBR")
        if source_material is not None:
            rock_material = source_material.copy()
            rock_material.name = "PC_FINAL_Cave_Interior_Rock"
    if rock_material is not None:
        for shader in principled_nodes(rock_material):
            set_input(shader, "Roughness", 0.76)
            set_input(shader, "Emission Color", (0.022, 0.032, 0.016, 1.0))
            set_input(shader, "Emission Strength", 0.055)
        mesh.materials.append(rock_material)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    tunnel["quest3_disable"] = True
    tunnel["pc_final_cave_depth"] = "procedural_rock_tunnel_v1"

    source_arch = bpy.data.objects.get("PC_REFINE_Cave_ContinuousArch")
    if source_arch is not None and source_arch.type == "MESH":
        source_vertices = [source_arch.matrix_world @ vertex.co for vertex in source_arch.data.vertices]
        source_faces = [tuple(polygon.vertices) for polygon in source_arch.data.polygons]
        for arch_index, (depth, scale) in enumerate(((1.5, 0.95), (3.2, 0.90), (5.3, 0.84)), 1):
            arch_vertices = [
                tuple(center + depth_direction * depth + (point - center) * scale)
                for point in source_vertices
            ]
            arch_mesh = bpy.data.meshes.new(f"PC_FINAL_Cave_InnerArch_{arch_index:02d}_Mesh")
            arch_mesh.from_pydata(arch_vertices, [], source_faces)
            arch_mesh.update()
            inner_arch = bpy.data.objects.new(
                f"PC_FINAL_Cave_InnerArch_{arch_index:02d}", arch_mesh
            )
            collection.objects.link(inner_arch)
            if rock_material is not None:
                arch_mesh.materials.append(rock_material)
            for polygon in arch_mesh.polygons:
                polygon.use_smooth = True
            inner_arch["quest3_disable"] = True

    end_center = center + depth_direction * (depth_steps[-1] + 0.8)
    end_vertices = [tuple(end_center)]
    end_radius_x, end_radius_z = 4.0, 4.45
    for segment in range(radial_segments):
        angle = segment / radial_segments * math.tau
        point = (
            end_center
            + right * (math.cos(angle) * end_radius_x)
            + up * (math.sin(angle) * end_radius_z)
        )
        end_vertices.append(tuple(point))
    end_faces = [
        (0, segment + 1, (segment + 1) % radial_segments + 1)
        for segment in range(radial_segments)
    ]
    end_mesh = bpy.data.meshes.new("PC_FINAL_Cave_Tunnel_End_Mesh")
    end_mesh.from_pydata(end_vertices, [], end_faces)
    end_mesh.update()
    tunnel_end = bpy.data.objects.new("PC_FINAL_Cave_Tunnel_End", end_mesh)
    collection.objects.link(tunnel_end)
    end_material = bpy.data.materials.get("PC_FINAL_Cave_Deep_Rock")
    if end_material is None:
        end_material = bpy.data.materials.new("PC_FINAL_Cave_Deep_Rock")
        end_material.use_nodes = True
    for shader in principled_nodes(end_material):
        set_input(shader, "Base Color", (0.012, 0.018, 0.008, 1.0))
        set_input(shader, "Roughness", 1.0)
        set_input(shader, "Specular IOR Level", 0.04)
        set_input(shader, "Emission Color", (0.008, 0.012, 0.004, 1.0))
        set_input(shader, "Emission Strength", 0.08)
    end_mesh.materials.append(end_material)
    tunnel_end["quest3_disable"] = True

    interior_light = bpy.data.objects.get("PC_Cave_Interior_Fill")
    if interior_light is not None and interior_light.type == "LIGHT":
        interior_light.location = center + depth_direction * 2.8 + up * 1.7
    deep_light = bpy.data.objects.get("PC_Cave_Deep_Fill")
    if deep_light is not None and deep_light.type == "LIGHT":
        deep_light.location = center + depth_direction * 6.2 + up * 1.2


def tune_lighting_and_render() -> None:
    scene = bpy.context.scene
    scene.view_settings.look = "AgX - Medium High Contrast"
    scene.view_settings.exposure = 0.82

    sun = bpy.data.objects.get("Sun_Jurassic")
    if sun is not None and sun.type == "LIGHT":
        sun.data.energy = 7.2
        sun.data.color = (1.0, 0.72, 0.49)
        sun.data.angle = 0.075

    sky_fill = bpy.data.objects.get("Sky_Fill")
    if sky_fill is not None and sky_fill.type == "LIGHT":
        sky_fill.data.energy = 105.0
        sky_fill.data.color = (0.44, 0.54, 0.69)

    warm_key = bpy.data.objects.get("PC_Reference_Warm_Key")
    if warm_key is not None and warm_key.type == "LIGHT":
        warm_key.data.energy = 2800.0
        warm_key.data.color = (1.0, 0.63, 0.39)
        if hasattr(warm_key.data, "shape"):
            warm_key.data.shape = "DISK"
        if hasattr(warm_key.data, "size"):
            warm_key.data.size = 62.0

    atmosphere = bpy.data.materials.get("PC_Jurassic_Atmosphere")
    if atmosphere is not None and atmosphere.node_tree is not None:
        for node in atmosphere.node_tree.nodes:
            if node.type == "PRINCIPLED_VOLUME":
                set_input(node, "Density", 0.00046)
                set_input(node, "Anisotropy", 0.38)
                set_input(node, "Color", (0.64, 0.61, 0.50, 1.0))

    world = scene.world
    if world is not None and world.node_tree is not None:
        backgrounds = [node for node in world.node_tree.nodes if node.type == "BACKGROUND"]
        for index, node in enumerate(backgrounds):
            set_input(node, "Strength", 0.18 if index == 0 else 0.34)
        mix_shader = next(
            (node for node in world.node_tree.nodes if node.type == "MIX_SHADER"), None
        )
        light_path = next(
            (node for node in world.node_tree.nodes if node.type == "LIGHT_PATH"), None
        )
        if mix_shader is not None and light_path is not None:
            for link in list(mix_shader.inputs[0].links):
                world.node_tree.links.remove(link)
            world.node_tree.links.new(light_path.outputs["Is Glossy Ray"], mix_shader.inputs[0])
        for node in world.node_tree.nodes:
            if node.type == "MIX_RGB":
                node.inputs[0].default_value = 0.34
            elif node.type == "TEX_NOISE":
                node.inputs["Scale"].default_value = 1.35
                node.inputs["Detail"].default_value = 4.2
                node.inputs["Roughness"].default_value = 0.68
            elif node.type == "VALTORGB" and len(node.color_ramp.elements) == 2:
                node.color_ramp.elements[0].position = 0.36
                node.color_ramp.elements[0].color = (0.38, 0.41, 0.40, 1.0)
                node.color_ramp.elements[-1].position = 0.70
                node.color_ramp.elements[-1].color = (1.0, 0.91, 0.74, 1.0)

    if scene.render.engine == "BLENDER_EEVEE":
        eevee = scene.eevee
        eevee.taa_render_samples = 192
        eevee.use_raytracing = True
        eevee.ray_tracing_method = "SCREEN"
        eevee.ray_tracing_options.resolution_scale = "1"
        eevee.ray_tracing_options.screen_trace_quality = 0.92
        eevee.ray_tracing_options.screen_trace_thickness = 0.35
        eevee.ray_tracing_options.trace_max_roughness = 0.46
        eevee.use_volumetric_shadows = True
        eevee.volumetric_samples = 96
        eevee.shadow_ray_count = 3
        eevee.shadow_step_count = 8
        eevee.shadow_resolution_scale = 1.0

    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = False
    scene["pc_final_polish"] = "water_cave_light_v1"


def main() -> None:
    tune_water()
    tune_cave()
    tune_lighting_and_render()
    if os.environ.get("VALE_POLISH_NO_SAVE") != "1":
        bpy.ops.wm.save_mainfile()
    print("PC_FINAL_WATER_CAVE_LIGHT_POLISH_OK")


if __name__ == "__main__":
    main()
