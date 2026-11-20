"""Build an independent, editable mountain carved along the Unity ride spline.

Run with Blender --background --factory-startup --python this_file.py. This never
loads or changes ValeMesozoico_Editable.blend. The byte asset contains JSON with
Unity world coordinates; no FBX handedness conversion is involved.
"""

from __future__ import annotations

import bisect
import json
import math
import re
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree


ROOT = Path(__file__).resolve().parents[2]
BLEND_PATH = ROOT / "Blender/TrackMountainCave.blend"
ASSET_PATH = ROOT / "Assets/_Game/Resources/Models/Environment/TrackMountainCave.bytes"
START_PROGRESS = 0.410
END_PROGRESS = 0.475
CUTTER_START = 0.350
CUTTER_END = 0.530
LOOKUP_SAMPLES = 1024
HALF_WIDTH = 4.85
HALF_HEIGHT = 4.80
UP_OFFSET = 1.20
UP = Vector((0.0, 1.0, 0.0))


def unity_to_blender(point: Vector) -> Vector:
    return Vector((point.x, -point.z, point.y))


def blender_to_unity(point: Vector) -> Vector:
    return Vector((point.x, point.z, -point.y))


class RideSpline:
    """Mirror RideSpline.cs, including arc-length lookup and bank calculation."""

    def __init__(self) -> None:
        source = (ROOT / "Assets/_Game/Scripts/Runtime/JurassicRideBootstrap.cs").read_text()
        section = source.split("private static Vector3[] CreateTrackPoints()", 1)[1]
        section = section.split("\n        }", 1)[0]
        pattern = r"new Vector3\((-?[\d.]+)f,\s*(-?[\d.]+)f,\s*(-?[\d.]+)f\)"
        self.points = [Vector(tuple(map(float, match))) for match in re.findall(pattern, section)]
        if len(self.points) < 4:
            raise RuntimeError("The active Unity track points could not be read.")
        self.distances = [0.0]
        previous = self.evaluate(0.0)
        for index in range(1, LOOKUP_SAMPLES + 1):
            current = self.evaluate(index / LOOKUP_SAMPLES)
            self.distances.append(self.distances[-1] + (current - previous).length)
            previous = current
        self.length = self.distances[-1]

    def evaluate(self, normalized: float) -> Vector:
        scaled = (normalized % 1.0) * len(self.points)
        index = math.floor(scaled)
        t = scaled - index
        p0, p1, p2, p3 = [self.points[i % len(self.points)] for i in range(index - 1, index + 3)]
        return 0.5 * (
            2.0 * p1 + (-p0 + p2) * t
            + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t * t
            + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t * t * t
        )

    def tangent(self, normalized: float) -> Vector:
        return (self.evaluate(normalized + 0.001) - self.evaluate(normalized - 0.001)).normalized()

    def frame(self, progress: float) -> tuple[Vector, Vector, Vector, Vector]:
        distance = (progress % 1.0) * self.length
        upper = max(1, min(LOOKUP_SAMPLES, bisect.bisect_left(self.distances, distance)))
        lower = upper - 1
        segment = self.distances[upper] - self.distances[lower]
        interpolation = (distance - self.distances[lower]) / segment if segment > 0.0001 else 0.0
        normalized = (lower + interpolation) / LOOKUP_SAMPLES
        position = self.evaluate(normalized)
        tangent = self.tangent(normalized)
        before = self.tangent(normalized - 0.01)
        after = self.tangent(normalized + 0.01)
        before.y = after.y = 0.0
        before.normalize()
        after.normalize()
        turn = math.atan2(UP.dot(before.cross(after)), max(-1.0, min(1.0, before.dot(after))))
        curvature = turn / max(0.01, self.length * 0.02)
        bank = max(math.radians(-12.0), min(math.radians(12.0), -math.atan(64.0 * curvature / 9.81)))
        right = UP.cross(tangent).normalized()
        up = tangent.cross(right).normalized()
        banked_right = right * math.cos(bank) + up * math.sin(bank)
        banked_up = up * math.cos(bank) - right * math.sin(bank)
        return position, tangent, banked_right, banked_up


def object_from_mesh(name: str, vertices: list[Vector], faces: list[tuple[int, ...]]) -> bpy.types.Object:
    mesh = bpy.data.meshes.new(name + " Mesh")
    mesh.from_pydata([unity_to_blender(vertex) for vertex in vertices], [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    bpy.context.scene.collection.objects.link(obj)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    # Rocky relief and a banked sweep produce nonplanar quads. Give the Exact
    # solver explicit planar triangles so a portal intersection cannot create
    # an edge shared by four faces.
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    return obj


def smoothstep(lower: float, upper: float, value: float) -> float:
    t = max(0.0, min(1.0, (value - lower) / (upper - lower)))
    return t * t * (3.0 - 2.0 * t)


def rock_noise(x: float, y: float, z: float = 0.0) -> float:
    """Stable, nonperiodic value noise; no dependence on Blender's noise seed."""
    def lattice(a: int, b: int, c: int) -> float:
        value = (a * 73856093) ^ (b * 19349663) ^ (c * 83492791)
        value = ((value ^ (value >> 13)) * 1274126177) & 0xFFFFFFFF
        return value / 2147483647.5 - 1.0

    ix, iy, iz = math.floor(x), math.floor(y), math.floor(z)
    tx, ty, tz = (smoothstep(0.0, 1.0, value - math.floor(value)) for value in (x, y, z))
    result = 0.0
    for dx in range(2):
        for dy in range(2):
            for dz in range(2):
                weight = (tx if dx else 1.0 - tx) * (ty if dy else 1.0 - ty) * (tz if dz else 1.0 - tz)
                result += lattice(ix + dx, iy + dy, iz + dz) * weight
    return result


def fractured_plane(u: float, height: float) -> float:
    """Tilted mineral faces with short irregular joints, in metres of relief."""
    def value(x: int, y: int, channel: int) -> float:
        seed = (x * 73856093) ^ (y * 19349663) ^ (channel * 83492791)
        seed = ((seed ^ (seed >> 13)) * 1274126177) & 0xFFFFFFFF
        return seed / 4294967295.0

    x, y = (u + height * 0.24) / 4.7, (height - u * 0.12) / 4.9
    ix, iy = math.floor(x), math.floor(y)
    cells = []
    for dx in range(-1, 2):
        for dy in range(-1, 2):
            cx, cy = ix + dx, iy + dy
            px = cx + 0.15 + value(cx, cy, 1) * 0.70
            py = cy + 0.15 + value(cx, cy, 2) * 0.70
            offset_x, offset_y = x - px, y - py
            distance = offset_x * offset_x + offset_y * offset_y
            plane = ((value(cx, cy, 3) - 0.5) * 1.35
                     + offset_x * (value(cx, cy, 4) - 0.5) * 1.40
                     + offset_y * (value(cx, cy, 5) - 0.5) * 1.10)
            cells.append((distance, plane))
    cells.sort(key=lambda cell: cell[0])
    first, second = cells[:2]
    gap = math.sqrt(second[0]) - math.sqrt(first[0])
    blend = 0.5 + 0.5 * smoothstep(0.0, 0.10, gap)
    joint = 0.42 * (1.0 - smoothstep(0.0, 0.13, gap))
    return first[1] * blend + second[1] * (1.0 - blend) - joint


def build_mountain() -> bpy.types.Object:
    """One tapered tropical rock wall, shaped by large planar shear faces.

    Staggered shoulders narrow into an inclined ridge. Shallow weathering and
    short mineral joints break the continuous faces; no stacked boulder volumes
    or isotropic crater displacement define its silhouette.
    """
    center = Vector((37.0, 0.0, 63.5))
    longitudinal = Vector((0.7526, 0.0, -0.6585)).normalized()
    lateral = Vector((0.6585, 0.0, 0.7526)).normalized()
    # Each (u, v, height) corner defines a real rock plane. Shoulder elevations
    # are deliberately staggered so their joins never form horizontal tiers.
    profiles = (
        ((-29,-10,-8), (-26,-14,-8), (-16,-17,-8), (-4,-14,-8),
         (7,-16,-8), (25,-14,-8), (30,-5,-8), (27,7,-8),
         (15,16,-8), (3,17,-8), (-13,14,-8), (-24,10,-8)),
        ((-24,-4,8), (-21,-9,10), (-12,-10,14), (-2,-9,15),
         (8,-10,18), (20,-8,14), (22,-3,17), (20,5,18),
         (12,8,20), (3,9,17), (-10,10,14), (-21,7,12)),
        ((-18,-3,18), (-15,-6,21), (-8,-8,24), (2,-7,28),
         (8,-8,30), (14,-5,29), (16,-2,32), (15,5,30),
         (9,7,33), (2,7,27), (-7,8,23), (-17,5.5,21)),
        ((-9,-1,25), (-8,-3,27), (-3,-4,29), (4,-4,33),
         (8,-4,38), (11,-2,40), (12,0,38), (10,3,36),
         (7,4,37), (2,4,32), (-4,4,28), (-8,2,26)),
    )
    vertices = []
    for profile in profiles:
        for u, v, height in profile:
            point = center + longitudinal * (u * 0.88) + lateral * (v * 0.80)
            point.y = height * 0.75 if height > 0.0 else height
            vertices.append(point)
    sides = len(profiles[0])
    faces = []
    for ring in range(len(profiles) - 1):
        for side in range(sides):
            following = (side + 1) % sides
            faces.append((ring*sides+side, ring*sides+following,
                          (ring+1)*sides+following, (ring+1)*sides+side))
    bottom = len(vertices)
    vertices.append(Vector((center.x, -8.0, center.z)))
    ridge = len(vertices)
    ridge_point = center + longitudinal * (7.0 * 0.88)
    ridge_point.y = 39.5 * 0.75
    vertices.append(ridge_point)
    for side in range(sides):
        following = (side + 1) % sides
        faces.append((bottom, following, side))
        faces.append(((len(profiles)-1)*sides+side,
                      (len(profiles)-1)*sides+following, ridge))
    mountain = object_from_mesh("Mountain Cave Interior", vertices, faces)
    bm = bmesh.new()
    bm.from_mesh(mountain.data)
    # Sampling adds weathering detail inside the authored planes, rather than
    # replacing those planes with a cloud of round procedural rock fragments.
    bmesh.ops.subdivide_edges(bm, edges=list(bm.edges), cuts=16, use_grid_fill=True)
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.normal_update()
    for vertex in bm.verts:
        point = blender_to_unity(vertex.co)
        normal = blender_to_unity(vertex.normal).normalized()
        relative = point - center
        u, v, height = relative.dot(longitudinal), relative.dot(lateral), point.y
        ground_fade = smoothstep(-8.0, -1.0, height)
        side_weight = 1.0 - smoothstep(0.40, 0.82, abs(normal.y))
        # Short angular faces give parallax and broken edges; their seams are
        # staggered instead of carving continuous grooves down the entire wall.
        relief = fractured_plane(u + v * 0.28, height) * side_weight
        weathering = (0.25 * rock_noise(u*0.37, v*0.41, height*0.24)
                      + 0.13 * rock_noise(u*1.6, v*1.6, height*1.20))
        point += normal * (relief + weathering) * ground_fade
        vertex.co = unity_to_blender(point)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mountain.data)
    bm.free()
    mountain.data.update()
    # Consolidate sharp chips at the authored shoulders into one closed volume
    # before the tunnel Boolean. This resolves locally folded surface triangles.
    bpy.context.view_layer.objects.active = mountain
    mountain.select_set(True)
    remesh = mountain.modifiers.new("Consolidate fractured rock surface", "REMESH")
    remesh.mode = "VOXEL"
    remesh.voxel_size = 0.28
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    mountain.data.calc_loop_triangles()
    if len(mountain.data.loop_triangles) > 27000:
        reduction = mountain.modifiers.new("Rock surface runtime budget", "DECIMATE")
        reduction.ratio = 27000.0 / len(mountain.data.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=reduction.name)
    return mountain


def build_cutter(spline: RideSpline) -> bpy.types.Object:
    ring_count = 121
    ring_segments = 48
    vertices = []
    for ring in range(ring_count):
        progress = CUTTER_START + (CUTTER_END - CUTTER_START) * ring / (ring_count - 1)
        position, _tangent, right, up = spline.frame(progress)
        center = position + up * UP_OFFSET
        for step in range(ring_segments):
            angle = step / ring_segments * math.tau
            # All weathering expands the corridor. The inscribed polygon keeps
            # the documented rider envelope clear even between ring vertices.
            wall_point = center + right * math.cos(angle) * HALF_WIDTH + up * math.sin(angle) * HALF_HEIGHT
            broad = rock_noise(wall_point.x * 0.23, wall_point.y * 0.23, wall_point.z * 0.23)
            fractures = rock_noise(wall_point.x * 0.81, wall_point.y * 0.81, wall_point.z * 0.81)
            # Weathering only removes rock: irregular recesses, stepped walls
            # and a broken portal rim cannot reduce the existing safe envelope.
            erosion = 0.12 + 0.29 * (broad + 1.0) + 0.10 * (fractures + 1.0)
            stratum = (wall_point.y + 0.12 * wall_point.x - 0.08 * wall_point.z) / 2.7
            bed_fraction = stratum - math.floor(stratum)
            erosion += 0.15 * (1.0 - smoothstep(0.03, 0.22, min(bed_fraction, 1.0 - bed_fraction)))
            radius_x = HALF_WIDTH + erosion
            radius_y = HALF_HEIGHT + erosion
            vertices.append(center + right * math.cos(angle) * radius_x + up * math.sin(angle) * radius_y)
    faces = []
    for ring in range(ring_count - 1):
        start = ring * ring_segments
        following = start + ring_segments
        for step in range(ring_segments):
            next_step = (step + 1) % ring_segments
            faces.append((start + step, following + step, following + next_step, start + next_step))
    faces.append(tuple(reversed(range(ring_segments))))
    faces.append(tuple((ring_count - 1) * ring_segments + step for step in range(ring_segments)))
    cutter = object_from_mesh("Editable Track Corridor Cutter", vertices, faces)
    cutter["guaranteed_start_progress"] = START_PROGRESS
    cutter["guaranteed_end_progress"] = END_PROGRESS
    cutter["arc_length_lut_samples"] = LOOKUP_SAMPLES
    return cutter


def boolean_carve(mountain: bpy.types.Object, cutter: bpy.types.Object) -> None:
    bpy.context.view_layer.objects.active = mountain
    mountain.select_set(True)
    modifier = mountain.modifiers.new("Carve the actual Unity ride corridor", "BOOLEAN")
    modifier.operation = "DIFFERENCE"
    modifier.solver = "EXACT"
    modifier.object = cutter
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    # A volumetric pass closes tangential, four-face Boolean junctions without
    # moving the track. Retain the original surface tags through nearest faces
    # so moss remains outside and cave walls keep their interior material.
    source = mountain.data.copy()
    source.calc_loop_triangles()
    source_tree = BVHTree.FromPolygons(
        [vertex.co.copy() for vertex in source.vertices],
        [tuple(triangle.vertices) for triangle in source.loop_triangles],
        all_triangles=True)
    surface_tags = [source.polygons[triangle.polygon_index].material_index
                    for triangle in source.loop_triangles]
    sealing = mountain.modifiers.new("Watertight cave junctions", "REMESH")
    sealing.mode = "VOXEL"
    sealing.voxel_size = 0.18
    sealing.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=sealing.name)
    mountain.data.calc_loop_triangles()
    if len(mountain.data.loop_triangles) > 39000:
        reduction = mountain.modifiers.new("Final cave triangle budget", "DECIMATE")
        reduction.ratio = 39000.0 / len(mountain.data.loop_triangles)
        bpy.ops.object.modifier_apply(modifier=reduction.name)
    mountain.data.update()
    for polygon in mountain.data.polygons:
        _point, _normal, face, _distance = source_tree.find_nearest(polygon.center)
        polygon.material_index = surface_tags[face] if face is not None else 0
    bpy.data.meshes.remove(source)
    # Keep the original cutter editable but excluded from the rendered/exported
    # mountain. The original uncut mass remains as a disabled backup object.
    cutter.hide_render = True
    cutter.hide_viewport = True
    cutter.hide_set(True)
    cutter.display_type = "WIRE"
    bm = bmesh.new()
    bm.from_mesh(mountain.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.00001)
    bmesh.ops.dissolve_degenerate(bm, edges=list(bm.edges), dist=0.00001)
    wires = [edge for edge in bm.edges if not edge.link_faces]
    if wires:
        bmesh.ops.delete(bm, geom=wires, context="EDGES")
    # The swept portal can sever small surface chips at a tapered shoulder.
    # Only the connected mountain belongs in the scene; discard minor islands
    # before validating the complete tunnel enclosure and the final solid.
    unseen = set(bm.verts)
    components = []
    while unseen:
        pending = [unseen.pop()]
        component = []
        while pending:
            vertex = pending.pop()
            component.append(vertex)
            for edge in vertex.link_edges:
                neighbour = edge.other_vert(vertex)
                if neighbour in unseen:
                    unseen.remove(neighbour)
                    pending.append(neighbour)
        components.append(component)
    components.sort(key=len, reverse=True)
    chips = [vertex for component in components[1:] for vertex in component]
    if len(chips) > len(bm.verts) * 0.05:
        raise RuntimeError(f"Portal splits the main rock mass: {[len(part) for part in components]}")
    if chips:
        print(f"Removed detached portal chips: islands={len(components)-1}, vertices={len(chips)}", flush=True)
        bmesh.ops.delete(bm, geom=chips, context="VERTS")
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bmesh.ops.triangulate(bm, faces=list(bm.faces))
    bm.to_mesh(mountain.data)
    bm.free()
    for polygon in mountain.data.polygons:
        polygon.use_smooth = True
    mountain.data.update()
    # Keep ledges and fracture faces sharp while sharing smooth normals across
    # coplanar faces and UV projection seams. Blender stores this per edge.
    adjacent = {}
    for polygon in mountain.data.polygons:
        for edge in polygon.edge_keys:
            adjacent.setdefault(tuple(sorted(edge)), []).append(polygon)
    sharp = mountain.data.attributes.get("sharp_edge")
    if sharp is None:
        sharp = mountain.data.attributes.new("sharp_edge", "BOOLEAN", "EDGE")
    # Sharp rock-block intersections survive, while incidental triangulation
    # edges on continuous weathered surfaces share smoothly varying normals.
    threshold = math.cos(math.radians(38.0))
    for edge in mountain.data.edges:
        faces = adjacent.get(tuple(sorted(edge.vertices)), [])
        sharp.data[edge.index].value = len(faces) != 2 or faces[0].normal.dot(faces[1].normal) < threshold
    mountain.data.update()


def verify(mountain: bpy.types.Object, spline: RideSpline) -> dict:
    bm = bmesh.new()
    bm.from_mesh(mountain.data)
    non_manifold = sum(not edge.is_manifold for edge in bm.edges)
    volume = bm.calc_volume(signed=True)
    if non_manifold or volume <= 0.0:
        links = [len(edge.link_faces) for edge in bm.edges if not edge.is_manifold]
        raise RuntimeError(f"Invalid carved solid: non_manifold={non_manifold}, volume={volume}, links={links[:12]}")
    unseen = set(bm.verts)
    components = 0
    while unseen:
        components += 1
        pending = [unseen.pop()]
        while pending:
            for edge in pending.pop().link_edges:
                for vertex in edge.verts:
                    if vertex in unseen:
                        unseen.remove(vertex)
                        pending.append(vertex)
    if components != 1:
        raise RuntimeError(f"Disconnected rock fragments: {components}")
    tree = BVHTree.FromBMesh(bm)
    clearance_minimum = float("inf")
    centerline_hits = 0
    wall_checks = 0
    for index in range(161):
        progress = START_PROGRESS + (END_PROGRESS - START_PROGRESS) * index / 160
        position, tangent, right, up = spline.frame(progress)
        center = position + up * UP_OFFSET
        origin = unity_to_blender(center)
        for step in range(24):
            angle = step / 24 * math.tau
            direction = unity_to_blender(right * math.cos(angle) + up * math.sin(angle)).normalized()
            _hit, _normal, _face, distance = tree.ray_cast(origin, direction, 45.0)
            expected = 1.0 / math.sqrt((math.cos(angle) / 4.5) ** 2 + (math.sin(angle) / 4.8) ** 2)
            if distance is None:
                raise RuntimeError(f"Missing enclosing wall at progress {progress:.6f}, angle {angle:.3f}, origin={tuple(center)}, dir={tuple(blender_to_unity(direction))}")
            if distance < expected - 0.07:
                raise RuntimeError(f"Insufficient corridor at {progress:.6f}: {distance:.3f} < {expected:.3f}")
            clearance_minimum = min(clearance_minimum, distance)
            wall_checks += 1
        next_progress = min(END_PROGRESS, progress + (END_PROGRESS - START_PROGRESS) / 160)
        next_position, _t, _r, next_up = spline.frame(next_progress)
        delta = unity_to_blender(next_position + next_up * UP_OFFSET) - origin
        if delta.length > 0.0001:
            hit = tree.ray_cast(origin, delta.normalized(), delta.length)[0]
            if hit is not None:
                centerline_hits += 1
    if centerline_hits:
        raise RuntimeError(f"Blocked centerline segments: {centerline_hits}")
    # Also follow the open approach and departure. This catches accidental end
    # caps or mountain fragments outside the guaranteed enclosed interval.
    full_path_hits = 0
    enclosed_samples = []
    previous = None
    for index in range(721):
        progress = CUTTER_START + (CUTTER_END - CUTTER_START) * index / 720
        position, _tangent, right, up = spline.frame(progress)
        center = unity_to_blender(position + up * UP_OFFSET)
        if previous is not None:
            delta = center - previous
            if tree.ray_cast(previous, delta.normalized(), delta.length)[0] is not None:
                full_path_hits += 1
        previous = center
        all_walls = True
        for step in range(16):
            angle = step / 16 * math.tau
            direction = unity_to_blender(right * math.cos(angle) + up * math.sin(angle)).normalized()
            location, normal, _face, distance = tree.ray_cast(center, direction, 7.5)
            if location is None or normal.dot(direction) >= 0.0:
                all_walls = False
                break
        if all_walls:
            enclosed_samples.append(progress)
    bm.free()
    if full_path_hits:
        raise RuntimeError(f"Blocked approach/departure segments: {full_path_hits}")
    bounds = [blender_to_unity(vertex.co) for vertex in mountain.data.vertices]
    return {
        "non_manifold_edges": non_manifold,
        "volume_m3": round(volume, 3),
        "radial_wall_checks": wall_checks,
        "minimum_radial_clearance_m": round(clearance_minimum, 3),
        "blocked_centerline_segments": centerline_hits,
        "blocked_approach_departure_segments": full_path_hits,
        "fully_enclosed_progress": [round(enclosed_samples[0], 5), round(enclosed_samples[-1], 5)],
        "unity_bounds_min": [round(min(point[index] for point in bounds), 3) for index in range(3)],
        "unity_bounds_max": [round(max(point[index] for point in bounds), 3) for index in range(3)],
        "track_length_m": round(spline.length, 4),
        "guaranteed_progress": [START_PROGRESS, END_PROGRESS],
        "guaranteed_length_m": round((END_PROGRESS - START_PROGRESS) * spline.length, 3),
    }


def export_json(mountain: bpy.types.Object) -> dict:
    """Share vertices within projection islands; tile the existing PBR every 4m."""
    vertices = []
    uvs = []
    normals = []
    surface = []
    triangles = []
    shared = {}
    for polygon in mountain.data.polygons:
        normal = blender_to_unity(polygon.normal)
        axis = max(range(3), key=lambda index: abs(normal[index]))
        for loop_index in polygon.loop_indices:
            vertex_index = mountain.data.loops[loop_index].vertex_index
            surface_type = min(1, polygon.material_index)
            smooth_normal = blender_to_unity(mountain.data.corner_normals[loop_index].vector).normalized()
            normal_key = tuple(round(component, 5) for component in smooth_normal)
            key = (vertex_index, axis, surface_type, normal_key)
            if key not in shared:
                shared[key] = len(vertices)
                point = blender_to_unity(mountain.data.vertices[vertex_index].co)
                vertices.append({name: round(point[index], 6) for index, name in enumerate("xyz")})
                # Corner normals preserve actual sharp ledges; projection
                # boundaries use the same normal, preventing texture seam lines.
                normals.append({name: round(smooth_normal[index], 6) for index, name in enumerate("xyz")})
                surface.append(surface_type)
                coordinates = (point.z, point.y) if axis == 0 else ((point.x, point.z) if axis == 1 else (point.x, point.y))
                uvs.append({"x": round(coordinates[0] / 4.0, 6), "y": round(coordinates[1] / 4.0, 6)})
            triangles.append(shared[key])
    if len(triangles) // 3 >= 50000:
        raise RuntimeError(f"Triangle budget exceeded: {len(triangles) // 3}")
    interior_triangles = sum(polygon.material_index == 1 for polygon in mountain.data.polygons)
    if interior_triangles < 100:
        raise RuntimeError("The Boolean did not preserve the interior surface label.")
    asset = {"vertices": vertices, "uvs": uvs, "normals": normals, "surface": surface, "triangles": triangles}
    ASSET_PATH.parent.mkdir(parents=True, exist_ok=True)
    ASSET_PATH.write_text(json.dumps(asset, separators=(",", ":")))
    return {"export_vertices": len(vertices), "export_triangles": len(triangles) // 3, "interior_triangles": interior_triangles}


def preview_material(mountain: bpy.types.Object) -> None:
    """Pack the game's rock maps into the editable Blender deliverable."""
    texture_root = ROOT / "Assets/_Game/Resources/Textures/Realistic/Rocks"
    albedo = bpy.data.images.load(str(texture_root / "RockBasaltMoss_Albedo.png"), check_existing=True)
    normal = bpy.data.images.load(str(texture_root / "RockBasaltMoss_Normal.png"), check_existing=True)
    normal.colorspace_settings.name = "Non-Color"
    albedo.pack()
    normal.pack()
    original_indices = [polygon.material_index for polygon in mountain.data.polygons]
    mountain.data.materials.clear()
    for name, color in (("Editable Exterior Rock Preview", (0.28, 0.31, 0.23, 1.0)),
                        ("Editable Interior Rock Preview", (0.24, 0.25, 0.21, 1.0))):
        material = bpy.data.materials.get(name) or bpy.data.materials.new(name)
        material.diffuse_color = color
        material.use_nodes = True
        nodes, links = material.node_tree.nodes, material.node_tree.links
        nodes.clear()
        output = nodes.new("ShaderNodeOutputMaterial")
        output.location = (650, 120)
        shader = nodes.new("ShaderNodeBsdfPrincipled")
        shader.location = (350, 120)
        shader.inputs["Roughness"].default_value = 0.88
        links.new(shader.outputs["BSDF"], output.inputs["Surface"])
        coordinates = nodes.new("ShaderNodeTexCoord")
        coordinates.location = (-900, 180)
        scale = nodes.new("ShaderNodeVectorMath")
        scale.operation = "SCALE"
        scale.inputs[3].default_value = 0.12
        scale.location = (-710, 180)
        links.new(coordinates.outputs["Object"], scale.inputs[0])
        color_map = nodes.new("ShaderNodeTexImage")
        color_map.image = albedo
        color_map.projection = "BOX"
        color_map.projection_blend = 0.22
        color_map.location = (-470, 220)
        links.new(scale.outputs["Vector"], color_map.inputs["Vector"])
        links.new(color_map.outputs["Color"], shader.inputs["Base Color"])
        normal_map = nodes.new("ShaderNodeTexImage")
        normal_map.image = normal
        normal_map.projection = "BOX"
        normal_map.projection_blend = 0.22
        normal_map.location = (-470, -130)
        links.new(scale.outputs["Vector"], normal_map.inputs["Vector"])
        # This resource is a grayscale height field. Unity converts it on import;
        # Blender must decode it through Bump rather than a tangent Normal Map.
        bump = nodes.new("ShaderNodeBump")
        bump.inputs["Strength"].default_value = 0.24
        bump.inputs["Distance"].default_value = 0.052
        bump.location = (100, -120)
        links.new(normal_map.outputs["Color"], bump.inputs["Height"])
        links.new(bump.outputs["Normal"], shader.inputs["Normal"])
        mountain.data.materials.append(material)
    for polygon, material_index in zip(mountain.data.polygons, original_indices):
        polygon.material_index = min(1, material_index)


def preview_uvs(mountain: bpy.types.Object) -> None:
    uv_layer = mountain.data.uv_layers.get("RockPreview") or mountain.data.uv_layers.new(name="RockPreview")
    for polygon in mountain.data.polygons:
        axis = max(range(3), key=lambda index: abs(polygon.normal[index]))
        for loop_index in polygon.loop_indices:
            point = mountain.data.vertices[mountain.data.loops[loop_index].vertex_index].co
            coordinates = (point.y, point.z) if axis == 0 else ((point.x, point.z) if axis == 1 else (point.x, point.y))
            uv_layer.data[loop_index].uv = (coordinates[0] * 0.12, coordinates[1] * 0.12)


def main() -> None:
    # This script is intentionally only used in a separate factory-startup
    # background process, so the user's open Blender document stays untouched.
    if not bpy.app.background or bpy.data.filepath:
        raise RuntimeError("Run this builder with --factory-startup; do not run inside an existing .blend.")
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    spline = RideSpline()
    mountain = build_mountain()
    preview_material(mountain)
    backup = mountain.copy()
    backup.data = mountain.data.copy()
    backup.name = "Editable Original Mountain Solid"
    bpy.context.scene.collection.objects.link(backup)
    backup.hide_render = True
    backup.hide_viewport = True
    backup.hide_set(True)
    cutter = build_cutter(spline)
    for material in mountain.data.materials:
        cutter.data.materials.append(material)
    for polygon in cutter.data.polygons:
        polygon.material_index = 1
    boolean_carve(mountain, cutter)
    preview_uvs(mountain)
    report = verify(mountain, spline)
    report.update(export_json(mountain))
    mountain["qa_report"] = json.dumps(report)
    mountain["unity_resource"] = "Models/Environment/TrackMountainCave"
    mountain["coordinate_mapping"] = "Unity=(Blender.x, Blender.z, -Blender.y)"
    bpy.context.scene.unit_settings.system = "METRIC"
    bpy.context.scene.unit_settings.scale_length = 1.0
    bpy.context.preferences.filepaths.save_version = 0
    bpy.context.view_layer.objects.active = mountain
    mountain.select_set(True)
    BLEND_PATH.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(BLEND_PATH), check_existing=False)
    print("TRACK_MOUNTAIN_CAVE_QA " + json.dumps(report, sort_keys=True), flush=True)


if __name__ == "__main__":
    main()
