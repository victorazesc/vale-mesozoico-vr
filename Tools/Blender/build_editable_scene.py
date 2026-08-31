"""Build an editable Blender authoring scene from the Unity Jurassic Ride layout.

Run with:
    /Applications/Blender.app/Contents/MacOS/Blender --background \
      --python Tools/Blender/build_editable_scene.py

The generated .blend is an art-authoring companion. Unity remains the source of
truth for ride physics, audio, XR, encounters and runtime optimization.
"""

from __future__ import annotations

import math
import random
from pathlib import Path

import bpy
from mathutils import Vector
from mathutils import noise as blender_noise


PROJECT_ROOT = Path(__file__).resolve().parents[2]
OUTPUT_DIR = PROJECT_ROOT / "Blender"
OUTPUT_BLEND = OUTPUT_DIR / "ValeMesozoico_Editable.blend"

TRACK_POINTS = [
    (0.0, 4.2, -56.0),
    (34.0, 5.0, -52.0),
    (64.0, 8.0, -34.0),
    (80.0, 14.0, -5.0),
    (76.0, 24.0, 27.0),
    (58.0, 35.0, 48.0),
    (44.0, 18.0, 56.0),
    (38.0, 6.5, 64.0),
    (15.0, 5.5, 76.0),
    (-22.0, 7.0, 72.0),
    (-54.0, 10.0, 50.0),
    (-72.0, 7.0, 16.0),
    (-68.0, 5.0, -20.0),
    (-49.0, 4.5, -45.0),
    (-26.0, 4.2, -57.0),
]


def reset_scene() -> None:
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for material in list(bpy.data.materials):
        bpy.data.materials.remove(material)


def new_collection(name: str) -> bpy.types.Collection:
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def move_to_collection(obj: bpy.types.Object, collection: bpy.types.Collection) -> None:
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)


def unity_to_blender(value: Vector | tuple[float, float, float]) -> Vector:
    x, y, z = value
    return Vector((x, -z, y))


def smoothstep01(value: float) -> float:
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def inverse_lerp(a: float, b: float, value: float) -> float:
    if abs(b - a) < 1e-8:
        return 0.0
    return max(0.0, min(1.0, (value - a) / (b - a)))


def perlin01(x: float, z: float, seed: float) -> float:
    vector = blender_noise.noise_vector(
        Vector((x, z, seed)),
        noise_basis="PERLIN_ORIGINAL",
    )
    return max(0.0, min(1.0, vector.x * 0.5 + 0.5))


def elliptical_hill(
    x: float,
    z: float,
    center_x: float,
    center_z: float,
    radius_x: float,
    radius_z: float,
    height: float,
) -> float:
    nx = (x - center_x) / radius_x
    nz = (z - center_z) / radius_z
    distance = math.sqrt(nx * nx + nz * nz)
    falloff = 1.0 - smoothstep01(min(1.0, distance))
    return falloff * falloff * height


def height_at(x: float, z: float) -> float:
    broad = perlin01((x + 180.0) * 0.0085, (z + 210.0) * 0.0085, 2.1) * 8.2
    rolling = perlin01((x - 74.0) * 0.019, (z + 96.0) * 0.019, 7.4) * 3.6
    detail = perlin01((x - 35.0) * 0.052, (z + 10.0) * 0.052, 13.7) * 1.35
    ridge_noise = abs(perlin01((x + 21.0) * 0.014, (z - 48.0) * 0.014, 21.3) * 2.0 - 1.0)
    ridges = ridge_noise**1.8 * 3.8

    radial_distance = math.sqrt(x * x + z * z)
    edge = smoothstep01(inverse_lerp(72.0, 148.0, radial_distance)) * 18.0
    valley_walls = (
        elliptical_hill(x, z, -111.0, 34.0, 48.0, 88.0, 12.0)
        + elliptical_hill(x, z, 112.0, 26.0, 48.0, 82.0, 13.0)
        + elliptical_hill(x, z, 22.0, 126.0, 94.0, 43.0, 17.0)
        + elliptical_hill(x, z, -12.0, -126.0, 112.0, 39.0, 10.0)
    )
    terrain_height = broad + rolling + detail + ridges + edge + valley_walls - 6.4

    lagoon_x = (x - 38.0) / 40.5
    lagoon_z = (z - 28.0) / 30.5
    lagoon_distance = math.sqrt(lagoon_x * lagoon_x + lagoon_z * lagoon_z)
    floor_progress = smoothstep01(inverse_lerp(0.12, 1.03, lagoon_distance))
    lagoon_floor = -4.8 + (-0.62 + 4.8) * floor_progress
    basin_mask = 1.0 - smoothstep01(inverse_lerp(0.92, 1.22, lagoon_distance))
    terrain_height = terrain_height * (1.0 - basin_mask) + lagoon_floor * basin_mask

    shore_distance = abs(lagoon_distance - 1.055)
    shore_mask = 1.0 - smoothstep01(shore_distance / 0.19)
    shore_target = -0.48 + (0.78 + 0.48) * inverse_lerp(0.94, 1.19, lagoon_distance)
    return terrain_height * (1.0 - shore_mask * 0.78) + shore_target * shore_mask * 0.78


def catmull_rom(normalized: float) -> Vector:
    count = len(TRACK_POINTS)
    wrapped = normalized % 1.0
    scaled = wrapped * count
    index = math.floor(scaled)
    t = scaled - index
    p0 = Vector(TRACK_POINTS[(index - 1) % count])
    p1 = Vector(TRACK_POINTS[index % count])
    p2 = Vector(TRACK_POINTS[(index + 1) % count])
    p3 = Vector(TRACK_POINTS[(index + 2) % count])
    t2 = t * t
    t3 = t2 * t
    return 0.5 * (
        2.0 * p1
        + (-p0 + p2) * t
        + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
        + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
    )


def track_frame(normalized: float) -> tuple[Vector, Vector, Vector, Vector]:
    point = catmull_rom(normalized)
    tangent = (catmull_rom(normalized + 0.001) - catmull_rom(normalized - 0.001)).normalized()
    right = Vector((tangent.z, 0.0, -tangent.x))
    if right.length_squared < 1e-6:
        right = Vector((1.0, 0.0, 0.0))
    else:
        right.normalize()
    up = tangent.cross(right).normalized()
    return point, tangent, right, up


def load_image(path: Path, non_color: bool = False) -> bpy.types.Image | None:
    if not path.exists():
        return None
    image = bpy.data.images.load(str(path), check_existing=True)
    if non_color:
        image.colorspace_settings.name = "Non-Color"
    return image


def make_material(
    name: str,
    color: tuple[float, float, float, float],
    roughness: float,
    metallic: float = 0.0,
    albedo: Path | None = None,
    normal: Path | None = None,
    alpha: float = 1.0,
) -> bpy.types.Material:
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    material.diffuse_color = color
    if alpha < 1.0 and hasattr(material, "surface_render_method"):
        material.surface_render_method = "DITHERED"
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    shader = nodes.new("ShaderNodeBsdfPrincipled")
    shader.inputs["Base Color"].default_value = color
    shader.inputs["Roughness"].default_value = roughness
    shader.inputs["Metallic"].default_value = metallic
    shader.inputs["Alpha"].default_value = alpha
    links.new(shader.outputs["BSDF"], output.inputs["Surface"])

    if albedo is not None:
        image = load_image(albedo)
        if image is not None:
            texture = nodes.new("ShaderNodeTexImage")
            texture.image = image
            links.new(texture.outputs["Color"], shader.inputs["Base Color"])
            if alpha < 1.0:
                links.new(texture.outputs["Alpha"], shader.inputs["Alpha"])

    if normal is not None:
        image = load_image(normal, non_color=True)
        if image is not None:
            texture = nodes.new("ShaderNodeTexImage")
            texture.image = image
            normal_map = nodes.new("ShaderNodeNormalMap")
            normal_map.inputs["Strength"].default_value = 0.85
            links.new(texture.outputs["Color"], normal_map.inputs["Color"])
            links.new(normal_map.outputs["Normal"], shader.inputs["Normal"])

    return material


def assign_material(obj: bpy.types.Object, material: bpy.types.Material) -> None:
    if obj.type != "MESH":
        return
    obj.data.materials.clear()
    obj.data.materials.append(material)


def create_mesh_object(
    name: str,
    collection: bpy.types.Collection,
    vertices: list[tuple[float, float, float] | Vector],
    faces: list[tuple[int, ...]],
    material: bpy.types.Material | None = None,
) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    if material is not None:
        obj.data.materials.append(material)
    return obj


def create_terrain(collection: bpy.types.Collection, material: bpy.types.Material) -> bpy.types.Object:
    resolution = 129
    size = 320.0
    vertices: list[tuple[float, float, float]] = []
    faces: list[tuple[int, int, int, int]] = []
    for z_index in range(resolution):
        unity_z = -size * 0.5 + size * z_index / (resolution - 1)
        for x_index in range(resolution):
            unity_x = -size * 0.5 + size * x_index / (resolution - 1)
            vertices.append(tuple(unity_to_blender((unity_x, height_at(unity_x, unity_z), unity_z))))
    for z_index in range(resolution - 1):
        for x_index in range(resolution - 1):
            a = z_index * resolution + x_index
            faces.append((a, a + 1, a + resolution + 1, a + resolution))

    terrain = create_mesh_object("Terrain_Editable_129x129", collection, vertices, faces, material)
    uv_layer = terrain.data.uv_layers.new(name="TerrainUV")
    for polygon in terrain.data.polygons:
        for loop_index in polygon.loop_indices:
            vertex = terrain.data.vertices[terrain.data.loops[loop_index].vertex_index].co
            uv_layer.data[loop_index].uv = ((vertex.x + 160.0) / 18.0, (-vertex.y + 160.0) / 18.0)
    terrain["unity_source"] = "ProceduralWorld.HeightAt"
    terrain["editable_resolution"] = resolution
    return terrain


def lagoon_contour(angle: float) -> float:
    return 1.0 + math.sin(angle * 3.0 + 0.7) * 0.025 + math.sin(angle * 7.0 - 0.2) * 0.013


def create_lagoon(
    collection: bpy.types.Collection,
    water_material: bpy.types.Material,
    shore_material: bpy.types.Material,
) -> None:
    center_x, center_z = 38.0, 28.0
    radius_x, radius_z = 40.5, 30.5
    segments = 96

    water_vertices = [tuple(unity_to_blender((center_x, -1.05, center_z)))]
    for index in range(segments):
        angle = index / segments * math.tau
        contour = lagoon_contour(angle)
        water_vertices.append(
            tuple(
                unity_to_blender(
                    (
                        center_x + math.cos(angle) * radius_x * contour,
                        -1.05,
                        center_z + math.sin(angle) * radius_z * contour,
                    )
                )
            )
        )
    water_faces = [(0, index + 1, (index + 1) % segments + 1) for index in range(segments)]
    water = create_mesh_object("Lagoon_Water_Editable", collection, water_vertices, water_faces, water_material)
    bevel = water.modifiers.new("Soft water edge", "BEVEL")
    bevel.width = 0.18
    bevel.segments = 2

    shore_vertices: list[tuple[float, float, float]] = []
    shore_faces: list[tuple[int, int, int, int]] = []
    for ring, scale in enumerate((0.98, 1.16)):
        for index in range(segments):
            angle = index / segments * math.tau
            contour = lagoon_contour(angle)
            x = center_x + math.cos(angle) * radius_x * contour * scale
            z = center_z + math.sin(angle) * radius_z * contour * scale
            y = -0.76 if ring == 0 else height_at(x, z) + 0.03
            shore_vertices.append(tuple(unity_to_blender((x, y, z))))
    for index in range(segments):
        next_index = (index + 1) % segments
        shore_faces.append((index, next_index, segments + next_index, segments + index))
    create_mesh_object("Wet_Shoreline_Transition", collection, shore_vertices, shore_faces, shore_material)


def create_curve_object(
    name: str,
    collection: bpy.types.Collection,
    points: list[Vector],
    bevel_depth: float,
    material: bpy.types.Material,
    cyclic: bool = True,
) -> bpy.types.Object:
    curve = bpy.data.curves.new(name=f"{name}_Curve", type="CURVE")
    curve.dimensions = "3D"
    curve.resolution_u = 2
    curve.bevel_depth = bevel_depth
    curve.bevel_resolution = 3
    spline = curve.splines.new("POLY")
    spline.points.add(len(points) - 1)
    for index, point in enumerate(points):
        spline.points[index].co = (*point, 1.0)
    spline.use_cyclic_u = cyclic
    obj = bpy.data.objects.new(name, curve)
    collection.objects.link(obj)
    curve.materials.append(material)
    return obj


def add_box_geometry(
    vertices: list[Vector],
    faces: list[tuple[int, int, int, int]],
    center: Vector,
    axis_x: Vector,
    axis_y: Vector,
    axis_z: Vector,
    dimensions: tuple[float, float, float],
) -> None:
    base = len(vertices)
    half_x, half_y, half_z = (dimension * 0.5 for dimension in dimensions)
    for sx, sy, sz in (
        (-1, -1, -1),
        (1, -1, -1),
        (1, 1, -1),
        (-1, 1, -1),
        (-1, -1, 1),
        (1, -1, 1),
        (1, 1, 1),
        (-1, 1, 1),
    ):
        vertices.append(center + axis_x * (sx * half_x) + axis_y * (sy * half_y) + axis_z * (sz * half_z))
    faces.extend(
        [
            (base, base + 1, base + 2, base + 3),
            (base + 4, base + 7, base + 6, base + 5),
            (base, base + 4, base + 5, base + 1),
            (base + 1, base + 5, base + 6, base + 2),
            (base + 2, base + 6, base + 7, base + 3),
            (base + 4, base, base + 3, base + 7),
        ]
    )


def create_track(
    track_collection: bpy.types.Collection,
    guide_collection: bpy.types.Collection,
    rail_material: bpy.types.Material,
    grease_material: bpy.types.Material,
    support_material: bpy.types.Material,
) -> list[Vector]:
    samples = 512
    centerline: list[Vector] = []
    left_rail: list[Vector] = []
    right_rail: list[Vector] = []
    spine: list[Vector] = []
    grease_left: list[Vector] = []
    grease_right: list[Vector] = []
    frames: list[tuple[Vector, Vector, Vector, Vector]] = []
    for index in range(samples):
        point, tangent, right, up = track_frame(index / samples)
        frames.append((point, tangent, right, up))
        centerline.append(unity_to_blender(point))
        left = point - right * 0.72 + up * 0.30
        right_point = point + right * 0.72 + up * 0.30
        left_rail.append(unity_to_blender(left))
        right_rail.append(unity_to_blender(right_point))
        spine.append(unity_to_blender(point - up * 0.20))
        grease_left.append(unity_to_blender(left + up * 0.092))
        grease_right.append(unity_to_blender(right_point + up * 0.092))

    create_curve_object("Rail_Left", track_collection, left_rail, 0.09, rail_material)
    create_curve_object("Rail_Right", track_collection, right_rail, 0.09, rail_material)
    create_curve_object("Central_Spine", track_collection, spine, 0.16, rail_material)
    create_curve_object("Wheel_Grease_Left", track_collection, grease_left, 0.018, grease_material)
    create_curve_object("Wheel_Grease_Right", track_collection, grease_right, 0.018, grease_material)
    guide = create_curve_object("GUIDE_RideSpline", guide_collection, centerline, 0.025, grease_material)
    guide.hide_render = True
    guide["unity_control_points"] = str(TRACK_POINTS)

    sleeper_vertices: list[Vector] = []
    sleeper_faces: list[tuple[int, int, int, int]] = []
    support_vertices: list[Vector] = []
    support_faces: list[tuple[int, int, int, int]] = []
    for index in range(0, samples, 4):
        point, tangent, right, up = frames[index]
        center = unity_to_blender(point - up * 0.03)
        add_box_geometry(
            sleeper_vertices,
            sleeper_faces,
            center,
            unity_to_blender(right),
            unity_to_blender(tangent),
            unity_to_blender(up),
            (1.85, 0.16, 0.13),
        )

    for index in range(0, samples, 16):
        point, _, right, _ = frames[index]
        for side in (-0.62, 0.62):
            top = point + right * side - Vector((0.0, 0.32, 0.0))
            bottom_y = height_at(top.x, top.z) - 0.25
            height = max(0.4, top.y - bottom_y)
            center_unity = Vector((top.x, bottom_y + height * 0.5, top.z))
            add_box_geometry(
                support_vertices,
                support_faces,
                unity_to_blender(center_unity),
                Vector((1.0, 0.0, 0.0)),
                Vector((0.0, 1.0, 0.0)),
                Vector((0.0, 0.0, 1.0)),
                (0.16, 0.16, height),
            )

    create_mesh_object("Track_CrossTies", track_collection, sleeper_vertices, sleeper_faces, rail_material)
    create_mesh_object("Track_Supports", track_collection, support_vertices, support_faces, support_material)
    return centerline


def create_cave(
    collection: bpy.types.Collection,
    rock_material: bpy.types.Material,
    water_material: bpy.types.Material,
) -> None:
    rings = 56
    sides = 18
    vertices: list[Vector] = []
    faces: list[tuple[int, int, int, int]] = []
    for ring in range(rings):
        progress = 0.395 + (0.482 - 0.395) * ring / (rings - 1)
        point, _, right, up = track_frame(progress)
        for side in range(sides):
            angle = side / sides * math.tau
            rough = 1.0 + math.sin(angle * 5.0 + ring * 0.71) * 0.06
            position = point + right * (math.cos(angle) * 5.4 * rough) + up * (math.sin(angle) * 4.35 * rough)
            position += up * 0.65
            vertices.append(unity_to_blender(position))
    for ring in range(rings - 1):
        for side in range(sides):
            next_side = (side + 1) % sides
            a = ring * sides + side
            b = ring * sides + next_side
            c = (ring + 1) * sides + next_side
            d = (ring + 1) * sides + side
            faces.append((a, d, c, b))
    cave = create_mesh_object("Waterfall_Cave_Editable", collection, vertices, faces, rock_material)
    solidify = cave.modifiers.new("Rock shell thickness", "SOLIDIFY")
    solidify.thickness = 0.42
    bevel = cave.modifiers.new("Soft rock facets", "BEVEL")
    bevel.width = 0.08
    bevel.segments = 2
    cave["unity_progress_range"] = "0.395 - 0.482"

    waterfall_vertices: list[Vector] = []
    waterfall_faces: list[tuple[int, int, int, int]] = []
    columns, rows = 12, 30
    for row in range(rows + 1):
        t = row / rows
        unity_y = 19.2 + (-1.1 - 19.2) * t
        for column in range(columns + 1):
            u = column / columns
            unity_x = 35.5 + 11.0 * u
            unity_z = 60.0 + math.sin(t * math.pi * 5.0 + u * 3.0) * 0.24
            waterfall_vertices.append(unity_to_blender((unity_x, unity_y, unity_z)))
    stride = columns + 1
    for row in range(rows):
        for column in range(columns):
            a = row * stride + column
            waterfall_faces.append((a, a + 1, a + stride + 1, a + stride))
    waterfall = create_mesh_object("Waterfall_Editable", collection, waterfall_vertices, waterfall_faces, water_material)
    waterfall["note"] = "Animate material UV offset in Unity"


def import_static_template(
    name: str,
    path: Path,
    library_collection: bpy.types.Collection,
    material: bpy.types.Material | None,
) -> bpy.types.Object | None:
    if not path.exists():
        return None
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=False)
    except Exception as error:
        print(f"WARN: failed to import {path.name}: {error}")
        return None
    imported = [obj for obj in bpy.data.objects if obj not in before]
    imported_names = [obj.name for obj in imported]
    meshes = [obj for obj in imported if obj.type == "MESH"]
    if not meshes:
        for obj in imported:
            bpy.data.objects.remove(obj, do_unlink=True)
        return None

    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.matrix_world = world
        obj.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    template = bpy.context.view_layer.objects.active
    template.name = f"_TEMPLATE_{name}"
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)
    if material is not None:
        assign_material(template, material)

    bounds = [template.matrix_world @ Vector(corner) for corner in template.bound_box]
    center_x = (min(point.x for point in bounds) + max(point.x for point in bounds)) * 0.5
    center_y = (min(point.y for point in bounds) + max(point.y for point in bounds)) * 0.5
    min_z = min(point.z for point in bounds)
    for vertex in template.data.vertices:
        vertex.co -= Vector((center_x, center_y, min_z))
    template.data.update()

    for object_name in imported_names:
        existing = bpy.data.objects.get(object_name)
        if existing is not None and existing != template:
            bpy.data.objects.remove(existing, do_unlink=True)
    move_to_collection(template, library_collection)
    template.hide_render = True
    template.hide_viewport = True
    return template


def duplicate_static(
    template: bpy.types.Object | None,
    name: str,
    collection: bpy.types.Collection,
    unity_position: tuple[float, float, float],
    target_height: float,
    yaw_degrees: float,
    width_scale: float = 1.0,
    depth_scale: float = 1.0,
) -> bpy.types.Object:
    if template is None:
        bpy.ops.mesh.primitive_ico_sphere_add(subdivisions=2, radius=1.0)
        obj = bpy.context.object
        move_to_collection(obj, collection)
        base_height = 2.0
    else:
        obj = template.copy()
        obj.data = template.data
        collection.objects.link(obj)
        obj.hide_render = False
        obj.hide_viewport = False
        base_height = max(0.001, template.dimensions.z)
    obj.name = name
    obj.location = unity_to_blender(unity_position)
    obj.rotation_euler = (0.0, 0.0, math.radians(yaw_degrees))
    height_scale = target_height / base_height
    obj.scale = (height_scale * width_scale, height_scale * depth_scale, height_scale)
    return obj


def nearest_track_distance(x: float, z: float, samples: int = 160) -> float:
    minimum = 1e9
    for index in range(samples):
        point = catmull_rom(index / samples)
        minimum = min(minimum, math.hypot(point.x - x, point.z - z))
    return minimum


def populate_environment(
    vegetation_collection: bpy.types.Collection,
    rock_collection: bpy.types.Collection,
    cave_collection: bpy.types.Collection,
    library_collection: bpy.types.Collection,
    palm_material: bpy.types.Material,
    boulder_material: bpy.types.Material,
    mountain_material: bpy.types.Material,
) -> None:
    palm_template = import_static_template(
        "CoconutPalm_LOD1",
        PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/CoconutPalm_LOD1.fbx",
        library_collection,
        palm_material,
    )
    boulder_template = import_static_template(
        "Boulder01_LOD1",
        PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_LOD1.fbx",
        library_collection,
        boulder_material,
    )
    mountain_template = import_static_template(
        "Mountainside_LOD1",
        PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_LOD1.fbx",
        library_collection,
        mountain_material,
    )

    randomizer = random.Random(8128)
    placed = 0
    attempts = 0
    while placed < 74 and attempts < 900:
        attempts += 1
        if placed < 16:
            angle = placed / 16.0 * math.tau + randomizer.uniform(-0.18, 0.18)
            radius = randomizer.uniform(36.0, 55.0)
            x = 38.0 + math.cos(angle) * radius
            z = 28.0 + math.sin(angle) * radius * 0.80
        else:
            progress = randomizer.uniform(0.02, 0.98)
            point, tangent, right, _ = track_frame(progress)
            side = -1.0 if placed % 2 == 0 else 1.0
            candidate = point + right * side * randomizer.uniform(10.0, 21.0) + tangent * randomizer.uniform(-7.0, 7.0)
            x, z = candidate.x, candidate.z
        if nearest_track_distance(x, z) < 7.5:
            continue
        y = height_at(x, z)
        duplicate_static(
            palm_template,
            f"Palm_{placed + 1:03}",
            vegetation_collection,
            (x, y, z),
            randomizer.uniform(8.0, 14.0),
            randomizer.uniform(0.0, 360.0),
            randomizer.uniform(0.90, 1.12),
            randomizer.uniform(0.90, 1.12),
        )
        placed += 1

    randomizer = random.Random(9631)
    for index in range(9):
        progress = 0.06 + (0.94 - 0.06) * index / 8.0
        point, tangent, right, _ = track_frame(progress)
        side = -1.0 if index % 2 == 0 else 1.0
        target_height = randomizer.uniform(8.8, 13.5)
        position = point + right * side * randomizer.uniform(21.0, 29.0) + tangent * randomizer.uniform(-4.5, 4.5)
        position.y = height_at(position.x, position.z) - target_height * 0.24
        duplicate_static(
            mountain_template,
            f"Hero_Cliff_{index + 1:02}",
            rock_collection,
            tuple(position),
            target_height,
            randomizer.uniform(0.0, 360.0),
            randomizer.uniform(2.15, 2.90),
            randomizer.uniform(1.08, 1.42),
        )

    for index in range(22):
        progress = randomizer.uniform(0.04, 0.96)
        point, tangent, right, _ = track_frame(progress)
        side = -1.0 if index % 2 == 0 else 1.0
        position = point + right * side * randomizer.uniform(9.0, 18.0) + tangent * randomizer.uniform(-3.0, 3.0)
        position.y = height_at(position.x, position.z) - randomizer.uniform(0.2, 0.8)
        duplicate_static(
            boulder_template,
            f"Hero_Boulder_{index + 1:02}",
            rock_collection,
            tuple(position),
            randomizer.uniform(2.1, 5.2),
            randomizer.uniform(0.0, 360.0),
            randomizer.uniform(0.9, 1.35),
            randomizer.uniform(0.85, 1.20),
        )

    waterfall_cliffs = [
        ((26.8, -3.8, 63.7), 25.5, 151.0),
        ((55.2, -3.6, 63.5), 25.2, 198.0),
    ]
    for index, (position, height, yaw) in enumerate(waterfall_cliffs):
        duplicate_static(
            mountain_template,
            f"Waterfall_Cliff_{index + 1}",
            cave_collection,
            position,
            height,
            yaw,
            1.45,
            1.16,
        )
    waterfall_boulders = [
        ((33.1, -0.75, 56.7), 4.8, 31.0),
        ((44.6, -0.68, 56.5), 5.2, 84.0),
        ((35.7, 15.2, 59.5), 6.4, 137.0),
        ((47.2, 15.4, 59.4), 6.1, 190.0),
        ((41.0, 8.0, 61.7), 9.7, 243.0),
        ((41.5, 19.0, 65.0), 4.1, 296.0),
    ]
    for index, (position, height, yaw) in enumerate(waterfall_boulders):
        duplicate_static(
            boulder_template,
            f"Waterfall_Boulder_{index + 1}",
            cave_collection,
            position,
            height,
            yaw,
        )


def import_group(
    name: str,
    path: Path,
    collection: bpy.types.Collection,
    unity_position: tuple[float, float, float],
    target_height: float,
    yaw_degrees: float,
    fallback_material: bpy.types.Material,
) -> bpy.types.Object | None:
    if not path.exists():
        return None
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=str(path), use_anim=True)
    except Exception as error:
        print(f"WARN: failed to import animated model {path.name}: {error}")
        return None
    imported = [obj for obj in bpy.data.objects if obj not in before]
    if not imported:
        return None

    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.location = unity_to_blender(unity_position)
    root.rotation_euler = (0.0, 0.0, math.radians(yaw_degrees))
    imported_set = set(imported)
    for obj in imported:
        move_to_collection(obj, collection)
        if obj.parent not in imported_set:
            world = obj.matrix_world.copy()
            obj.parent = root
            obj.matrix_world = root.matrix_world.inverted() @ world
        if obj.type == "MESH":
            assign_material(obj, fallback_material)

    bpy.context.view_layer.update()
    mesh_objects = [obj for obj in imported if obj.type == "MESH"]
    if mesh_objects:
        world_points = [obj.matrix_world @ Vector(corner) for obj in mesh_objects for corner in obj.bound_box]
        height = max(point.z for point in world_points) - min(point.z for point in world_points)
        if height > 0.001:
            scale = target_height / height
            root.scale = (scale, scale, scale)
    root["source_fbx"] = str(path.relative_to(PROJECT_ROOT))
    return root


def populate_ride_assets(
    dinosaur_collection: bpy.types.Collection,
    cart_collection: bpy.types.Collection,
    dinosaur_materials: dict[str, bpy.types.Material],
    cart_material: bpy.types.Material,
) -> None:
    dinosaur_root = PROJECT_ROOT / "Assets/_Game/Resources/Models/Dinosaurs"
    placements = [
        ("Tyrannosaurus", "trex", dinosaur_root / "Trex.fbx", (-17.0, height_at(-17.0, 43.0), 43.0), 5.8, 42.0),
        ("Apatosaurus_01", "sauropod", dinosaur_root / "Apatosaurus.fbx", (5.0, height_at(5.0, 38.0), 38.0), 8.5, -28.0),
        ("Apatosaurus_02", "sauropod", dinosaur_root / "Apatosaurus.fbx", (66.0, height_at(66.0, 18.0), 18.0), 7.8, 138.0),
        ("Triceratops_01", "triceratops", dinosaur_root / "Triceratops.fbx", (-47.0, height_at(-47.0, 31.0), 31.0), 3.3, 80.0),
        ("Triceratops_02", "triceratops", dinosaur_root / "Triceratops.fbx", (23.0, height_at(23.0, -18.0), -18.0), 3.1, 210.0),
        ("Pteranodon_01", "pteranodon", dinosaur_root / "Pteranodon/Pteranodon.fbx", (20.0, 31.0, 18.0), 2.8, 24.0),
        ("Pteranodon_02", "pteranodon", dinosaur_root / "Pteranodon/Pteranodon.fbx", (-34.0, 27.0, 9.0), 2.5, 116.0),
        ("Pteranodon_03", "pteranodon", dinosaur_root / "Pteranodon/Pteranodon.fbx", (52.0, 35.0, 42.0), 3.0, 244.0),
    ]
    for name, material_key, path, position, height, yaw in placements:
        import_group(name, path, dinosaur_collection, position, height, yaw, dinosaur_materials[material_key])

    start, tangent, _, up = track_frame(0.0)
    cart = import_group(
        "Ride_Cart_Editable",
        PROJECT_ROOT / "Assets/_Game/Resources/Models/Ride/AbandonedCart/AbandonedCoasterCart.fbx",
        cart_collection,
        tuple(start + up * 0.4),
        1.9,
        math.degrees(math.atan2(tangent.x, tangent.z)),
        cart_material,
    )
    if cart is not None:
        cart["unity_scale"] = 1.6
        cart["unity_vertical_offset"] = 5.9


def configure_scene(guide_collection: bpy.types.Collection) -> None:
    scene = bpy.context.scene
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.resolution_percentage = 100
    scene.world.color = (0.12, 0.18, 0.22)
    scene.world.use_nodes = True
    background = scene.world.node_tree.nodes.get("Background")
    if background is not None:
        background.inputs["Color"].default_value = (0.12, 0.18, 0.22, 1.0)
        background.inputs["Strength"].default_value = 0.55
    scene["authoring_note"] = "Edit visuals here; keep ride physics, XR, audio and runtime encounters in Unity."
    scene["unity_scene"] = "Assets/_Game/Scenes/JurassicRide.unity"
    scene["unity_version"] = "6000.3.23f1"

    readme = bpy.data.objects.new("README_EDIT_FIRST", None)
    guide_collection.objects.link(readme)
    readme["01"] = "Collections are grouped by terrain, water, track, cave, vegetation and dinosaurs."
    readme["02"] = "Rail and spline curves are directly editable in Edit Mode."
    readme["03"] = "Re-run Tools/Blender/build_editable_scene.py to rebuild from Unity layout values."
    readme["04"] = "Export selected art back as FBX/glTF; do not replace Unity ride scripts."

    bpy.ops.object.light_add(type="SUN", location=(20.0, -30.0, 75.0))
    sun = bpy.context.object
    sun.name = "Sun_Jurassic"
    sun.rotation_euler = (math.radians(28.0), math.radians(-18.0), math.radians(-32.0))
    sun.data.energy = 3.0
    sun.data.color = (1.0, 0.83, 0.64)
    move_to_collection(sun, guide_collection)

    bpy.ops.object.light_add(type="AREA", location=(10.0, -15.0, 115.0))
    fill = bpy.context.object
    fill.name = "Sky_Fill"
    fill.data.energy = 2200.0
    fill.data.shape = "DISK"
    fill.data.size = 120.0
    fill.data.color = (0.55, 0.72, 0.82)
    move_to_collection(fill, guide_collection)

    bpy.ops.object.camera_add(location=(176.0, 198.0, 138.0))
    camera = bpy.context.object
    camera.name = "Camera_Overview"
    move_to_collection(camera, guide_collection)
    direction = Vector((15.0, -20.0, 7.0)) - camera.location
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    camera.data.lens = 48.0
    scene.camera = camera


def main() -> None:
    reset_scene()
    collections = {
        "guides": new_collection("00_GUIDES_AND_LIGHTS"),
        "terrain": new_collection("01_TERRAIN"),
        "water": new_collection("02_WATER_AND_SHORE"),
        "track": new_collection("03_TRACK_EDITABLE"),
        "cave": new_collection("04_CAVE_AND_WATERFALL"),
        "rocks": new_collection("05_ROCKS"),
        "vegetation": new_collection("06_VEGETATION"),
        "dinosaurs": new_collection("07_DINOSAURS"),
        "cart": new_collection("08_RIDE_CART"),
        "library": new_collection("99_ASSET_LIBRARY"),
    }

    texture_root = PROJECT_ROOT / "Assets/_Game/Resources/Textures/Realistic"
    terrain_material = make_material(
        "M_Terrain_Jungle_PBR",
        (0.16, 0.24, 0.09, 1.0),
        0.88,
        albedo=texture_root / "JungleGround_Lush_Albedo.png",
        normal=texture_root / "JungleGround_Normal.png",
    )
    shore_material = make_material(
        "M_Wet_Shore_PBR",
        (0.12, 0.10, 0.07, 1.0),
        0.48,
        albedo=texture_root / "Shoreline/WetShore_Albedo.png",
        normal=texture_root / "Shoreline/WetShore_Normal.png",
    )
    rock_material = make_material(
        "M_Mossy_Rock_PBR",
        (0.18, 0.20, 0.15, 1.0),
        0.72,
        albedo=texture_root / "MossyBasalt_Albedo.png",
        normal=texture_root / "MossyBasalt_Normal.png",
    )
    boulder_material = make_material(
        "M_Boulder_Photogrammetry",
        (0.24, 0.22, 0.17, 1.0),
        0.66,
        albedo=PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_Albedo.jpg",
        normal=PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Boulder01/Boulder01_Normal.jpg",
    )
    mountain_material = make_material(
        "M_Mountainside_Photogrammetry",
        (0.20, 0.22, 0.17, 1.0),
        0.69,
        albedo=PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_Albedo.jpg",
        normal=PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/Mountainside/Mountainside_Normal.jpg",
    )
    palm_material = make_material(
        "M_CoconutPalm_Atlas",
        (0.15, 0.38, 0.08, 0.99),
        0.72,
        albedo=PROJECT_ROOT / "Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/CoconutPalm_Albedo.png",
        alpha=0.99,
    )
    rail_material = make_material(
        "M_Track_Red_Steel_PBR",
        (0.42, 0.025, 0.015, 1.0),
        0.31,
        metallic=0.72,
        normal=texture_root / "Track/TrackPaintedSteel_Normal.png",
    )
    grease_material = make_material("M_Wheel_Grease", (0.012, 0.014, 0.012, 1.0), 0.20, metallic=0.58)
    support_material = make_material("M_Track_Support_Dark", (0.025, 0.045, 0.038, 1.0), 0.52, metallic=0.65)
    water_material = make_material("M_Lagoon_Water", (0.025, 0.22, 0.26, 0.76), 0.12, metallic=0.10, alpha=0.76)
    dinosaur_materials = {
        "trex": make_material(
            "M_Tyrannosaurus",
            (0.25, 0.14, 0.07, 1.0),
            0.62,
            albedo=PROJECT_ROOT / "Assets/_Game/Resources/Generated/Trex.png",
        ),
        "sauropod": make_material(
            "M_Apatosaurus",
            (0.20, 0.25, 0.14, 1.0),
            0.68,
            albedo=PROJECT_ROOT / "Assets/_Game/Resources/Generated/Sauropod.png",
        ),
        "triceratops": make_material("M_Triceratops", (0.30, 0.21, 0.11, 1.0), 0.67),
        "pteranodon": make_material(
            "M_Pteranodon_PBR",
            (0.26, 0.18, 0.12, 1.0),
            0.59,
            albedo=PROJECT_ROOT / "Assets/_Game/Resources/Models/Dinosaurs/Pteranodon/Pteranodon_BaseColor.png",
            normal=PROJECT_ROOT / "Assets/_Game/Resources/Models/Dinosaurs/Pteranodon/Pteranodon_Normal.png",
        ),
    }
    cart_material = make_material("M_Cart_Red_Weathered", (0.28, 0.025, 0.018, 1.0), 0.44, metallic=0.58)

    configure_scene(collections["guides"])
    create_terrain(collections["terrain"], terrain_material)
    create_lagoon(collections["water"], water_material, shore_material)
    create_track(collections["track"], collections["guides"], rail_material, grease_material, support_material)
    create_cave(collections["cave"], rock_material, water_material)
    populate_environment(
        collections["vegetation"],
        collections["rocks"],
        collections["cave"],
        collections["library"],
        palm_material,
        boulder_material,
        mountain_material,
    )
    populate_ride_assets(collections["dinosaurs"], collections["cart"], dinosaur_materials, cart_material)

    collections["library"].hide_render = True
    for material in list(bpy.data.materials):
        if material.users == 0:
            bpy.data.materials.remove(material)
    for image in list(bpy.data.images):
        if image.users == 0:
            bpy.data.images.remove(image)
    OUTPUT_DIR.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    bpy.ops.file.make_paths_relative()
    bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT_BLEND), check_existing=False)
    print(
        "BLEND_EXPORT_OK "
        f"path={OUTPUT_BLEND} objects={len(bpy.data.objects)} "
        f"meshes={len(bpy.data.meshes)} materials={len(bpy.data.materials)}"
    )


if __name__ == "__main__":
    main()
