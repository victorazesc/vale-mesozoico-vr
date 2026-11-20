"""Import the user's GLB, retain its idle and bake a four-beat walking cycle.

Run with Blender --background --python Tools/Blender/import_downloaded_triceratops.py.
The original download is preserved in SourceAssets/Downloaded/Triceratops.
"""
from math import cos, pi, sin, sqrt
from pathlib import Path
import json
import struct

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceAssets/Downloaded/Triceratops/Triceratops.glb'
OUTPUT = ROOT / 'Assets/_Game/Resources/Models/Dinosaurs'
TEXTURES = OUTPUT / 'TriceratopsSkin'
LEGS = [
    ('Bip001 L UpperArm', 'Bip001 L Forearm', 'Bip001 L Hand', 0.0),
    ('Bip001 R UpperArm', 'Bip001 R Forearm', 'Bip001 R Hand', 0.5),
    ('Bip001 L Thigh', 'Bip001 L Calf', 'Bip001 L Foot', 0.25),
    ('Bip001 R Thigh', 'Bip001 R Calf', 'Bip001 R Foot', 0.75),
]

# Runtime contact phases use this same three-feet-supported walk.
STANCE_FRACTION = 0.75


def save_textures():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    for source, suffix in [('三角龙_b', 'BaseColor'), ('三角龙_n', 'Normal')]:
        image = bpy.data.images[source]
        image.filepath_raw = str(TEXTURES / f'Triceratops_{suffix}.png')
        image.file_format = 'PNG'
        image.save()
    packed = bpy.data.images['三角龙_m']
    pixels = np.empty(len(packed.pixels), dtype=np.float32)
    packed.pixels.foreach_get(pixels)
    pixels = pixels.reshape((-1, 4))
    # glTF stores roughness in green; Unity Lit expects smoothness in alpha.
    pixels[:, 3] = 1.0 - pixels[:, 1]
    pixels[:, :3] = 0.0
    smoothness = bpy.data.images.new('Triceratops_MetallicSmoothness',
                                    width=packed.size[0], height=packed.size[1], alpha=True)
    smoothness.colorspace_settings.name = 'Non-Color'
    smoothness.pixels.foreach_set(pixels.ravel())
    smoothness.filepath_raw = str(TEXTURES / 'Triceratops_MetallicSmoothness.png')
    smoothness.file_format = 'PNG'
    smoothness.save()


def source_transforms():
    raw = SOURCE.read_bytes()
    length = struct.unpack_from('<I', raw, 12)[0]
    document = json.loads(raw[20:20 + length])
    nodes = document['nodes']
    parents = {child: i for i, node in enumerate(nodes) for child in node.get('children', [])}
    def world(index):
        node = nodes[index]
        if 'matrix' in node:
            values = node['matrix']
            local = Matrix([values[i:i + 4] for i in range(0, 16, 4)]).transposed()
        else:
            x, y, z, w = node.get('rotation', [0, 0, 0, 1])
            local = Matrix.LocRotScale(Vector(node.get('translation', [0, 0, 0])),
                                      Quaternion((w, x, y, z)), Vector(node.get('scale', [1, 1, 1])))
        return world(parents[index]) @ local if index in parents else local
    coordinates = Matrix.Rotation(pi / 2, 4, 'X')
    # Imported mesh vertices already use Blender's Z-up coordinate system.
    return {node.get('name'): coordinates @ world(i) @ coordinates.inverted()
            for i, node in enumerate(nodes) if 'mesh' in node}


def join_body(rig):
    """Keep eyes and mouth attached to the head in one skinned draw call."""
    rig.data.pose_position = 'REST'
    bpy.context.view_layer.update()
    # The source's separate eye glint cards render as white patches in URP.
    # Keep the textured eyeballs; omit those cards and the unrelated helper sphere.
    meshes = [o for o in bpy.context.scene.objects
              if o.type == 'MESH' and o.name not in {'Icosphere', 'Object002'}]
    body = max(meshes, key=lambda o: len(o.data.vertices))
    source_world = source_transforms()
    for obj in meshes:
        if obj.parent_type == 'BONE':
            bone = obj.parent_bone
            # glTF bone parenting can inherit Blender's generated bone-tail offset.
            # Recover the rigid eyes' exact bind transforms from the source nodes.
            world = source_world[obj.name]
            obj.parent_type = 'OBJECT'
            obj.parent = rig
            obj.matrix_parent_inverse = Matrix.Identity(4)
            bpy.context.view_layer.update()
            obj.matrix_world = world
            obj.vertex_groups.new(name=bone).add(list(range(len(obj.data.vertices))), 1.0, 'REPLACE')
            obj.modifiers.new('Head skinning', 'ARMATURE').object = rig
            bpy.context.view_layer.update()
    bpy.ops.object.select_all(action='DESELECT')
    for obj in meshes:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    body.name = 'Triceratops_Body'
    body.data.name = 'Triceratops_Body_Mesh'
    for obj in list(bpy.context.scene.objects):
        if obj not in {rig, body}:
            bpy.data.objects.remove(obj, do_unlink=True)
    rig.data.pose_position = 'POSE'
    bpy.context.view_layer.update()
    return body


def rotate_about_head(rig, bone, rotation):
    world = rig.matrix_world @ bone.matrix
    point = world.translation.copy()
    bone.matrix = rig.matrix_world.inverted() @ (
        Matrix.Translation(point) @ rotation.to_matrix().to_4x4()
        @ Matrix.Translation(-point) @ world)
    bpy.context.view_layer.update()


def solve_leg(rig, names, target, foot_rotation, foot_scale, knee_pole=None):
    upper, lower, foot = [rig.pose.bones[name] for name in names]
    a, b, c = [rig.matrix_world @ bone.head for bone in (upper, lower, foot)]
    first, second = (b - a).length, (c - b).length
    direction = (target - a).normalized()
    distance = min(max((target - a).length, abs(first - second) + 0.001), first + second - 0.001)
    along = (first * first - second * second + distance * distance) / (2 * distance)
    hint = b - a if knee_pole is None else knee_pole
    bend = hint - direction * hint.dot(direction)
    if bend.length < 0.001:
        bend = Vector((0, 1, 0)) - direction * direction.y
    knee = a + direction * along + bend.normalized() * sqrt(max(0, first * first - along * along))
    rotate_about_head(rig, upper, (b - a).rotation_difference(knee - a))
    b, c = [rig.matrix_world @ bone.head for bone in (lower, foot)]
    rotate_about_head(rig, lower, (c - b).rotation_difference(target - b))
    position = rig.matrix_world @ foot.head
    foot.matrix = rig.matrix_world.inverted() @ Matrix.LocRotScale(position, foot_rotation, foot_scale)
    bpy.context.view_layer.update()


def add_walk(rig):
    scene = bpy.context.scene
    scene.frame_set(1)
    idle = rig.animation_data.action
    idle_slot = rig.animation_data.action_slot
    idle.name = 'Idle'
    for track in list(rig.animation_data.nla_tracks):
        rig.animation_data.nla_tracks.remove(track)
    base = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
    base_location = rig.location.copy()
    feet = {names[2]: (rig.matrix_world @ rig.pose.bones[names[2]].matrix).decompose() for names in LEGS}
    # Keep the rear knee on its original side even as the thigh passes vertical.
    knee_poles = {}
    for upper, lower, foot, _ in LEGS:
        if not upper.endswith('Thigh'):
            continue
        a, b, c = [rig.matrix_world @ rig.pose.bones[name].head for name in (upper, lower, foot)]
        axis = (c - a).normalized()
        knee_poles[upper] = ((b - a) - axis * (b - a).dot(axis)).normalized()
    rig.animation_data.action = None
    for bone in rig.pose.bones:
        bone.rotation_mode = 'QUATERNION'
    frames = 60
    for frame in range(frames + 1):
        scene.frame_set(frame + 1)
        for name, matrix in base.items():
            rig.pose.bones[name].matrix_basis = matrix
        cycle = 2 * pi * frame / frames
        rig.location = base_location + Vector((0.012 * sin(cycle), 0, -0.05 + 0.012 * cos(2 * cycle)))
        bpy.context.view_layer.update()
        # Small, delayed weight shifts keep the heavy torso stable while the head and tail follow.
        rotate_about_head(rig, rig.pose.bones['Bip001 Pelvis'],
                          Quaternion(Vector((0, 1, 0)), 0.006 * sin(cycle)))
        rotate_about_head(rig, rig.pose.bones['Bip001 Neck'],
                          Quaternion(Vector((1, 0, 0)), 0.012 * sin(2 * cycle - 0.45)))
        rotate_about_head(rig, rig.pose.bones['Bip001 Head'],
                          Quaternion(Vector((0, 0, 1)), 0.007 * sin(cycle - 0.6)))
        for index in range(5):
            tail = 'Bip001 Tail' + (str(index) if index else '')
            rotate_about_head(rig, rig.pose.bones[tail],
                              Quaternion(Vector((0, 0, 1)), (0.012 + index * 0.004) * sin(cycle - index * 0.45)))
        for *names, offset in LEGS:
            phase = (frame / frames + offset) % 1.0
            location, rotation, scale = feet[names[2]]
            foot_rotation = rotation
            if phase < STANCE_FRACTION:
                travel, lift = -0.42 + 0.84 * phase / STANCE_FRACTION, 0.0
            else:
                swing = (phase - STANCE_FRACTION) / (1 - STANCE_FRACTION)
                # Hermite tangents match support velocity at both ends of the loop.
                tangent = 0.84 * (1 - STANCE_FRACTION) / STANCE_FRACTION
                travel = (0.42 * (2 * swing**3 - 3 * swing**2 + 1)
                          - 0.42 * (-2 * swing**3 + 3 * swing**2)
                          + tangent * (2 * swing**3 - 3 * swing**2 + swing))
                lift = 0.14 * sin(pi * swing) ** 2
                foot_rotation = Quaternion(Vector((1, 0, 0)),
                                           0.12 * sin(2 * pi * swing) * sin(pi * swing)) @ rotation
            target = location + Vector((0, travel, lift))
            solve_leg(rig, names, target, foot_rotation, scale, knee_poles.get(names[0]))
        for bone in rig.pose.bones:
            for channel in ('location', 'rotation_quaternion', 'scale'):
                bone.keyframe_insert(channel, frame=frame + 1, group=bone.name)
        for channel in ('location', 'rotation_quaternion', 'scale'):
            rig.keyframe_insert(channel, frame=frame + 1)
    walk = rig.animation_data.action
    walk.name = 'Walk'
    walk_slot = rig.animation_data.action_slot
    rig.animation_data.action = None
    for name, action, slot, start in [('Idle', idle, idle_slot, 1), ('Walk', walk, walk_slot, 300)]:
        track = rig.animation_data.nla_tracks.new()
        track.name = name
        strip = track.strips.new(name, start, action)
        strip.action_slot = slot
        strip.action_frame_start = action.frame_range[0]
        strip.action_frame_end = action.frame_range[1]
        strip.frame_end = start + action.frame_range[1] - action.frame_range[0]
        strip.extrapolation = 'NOTHING'
    scene.frame_set(1)
    return idle, walk


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(SOURCE))
    rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
    body = join_body(rig)
    rig.name = 'Triceratops_Rig'
    save_textures()
    idle, walk = add_walk(rig)
    evaluated = body.evaluated_get(bpy.context.evaluated_depsgraph_get())
    posed = evaluated.to_mesh()
    points = [body.matrix_world @ vertex.co for vertex in posed.vertices]
    bounds = [(min(p[axis] for p in points), max(p[axis] for p in points)) for axis in range(3)]
    evaluated.to_mesh_clear()
    print('IMPORT_BOUNDS', bounds)
    if bounds[2][1] - bounds[2][0] > 5 or bounds[2][0] < -0.5:
        raise RuntimeError('Imported geometry is displaced from the source feet; export aborted.')
    bpy.context.scene.render.fps = 24
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    body.select_set(True)
    bpy.context.view_layer.objects.active = rig
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT / 'Triceratops.fbx'), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True,
        mesh_smooth_type='FACE', add_leaf_bones=False,
        use_armature_deform_only=False, bake_anim=True,
        bake_anim_use_all_actions=False, bake_anim_use_nla_strips=True,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.05,
        path_mode='RELATIVE', embed_textures=False,
        axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL')
    report = (f'TRICERATOPS_IMPORT_OK triangles={sum(len(p.vertices)-2 for p in body.data.polygons)} '
              f'vertices={len(body.data.vertices)} bones={len(rig.data.bones)} '
              f'idle_frames={tuple(idle.frame_range)} walk_frames={tuple(walk.frame_range)}')
    print(report)
    bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE.parent / 'Triceratops.blend'))


if __name__ == '__main__':
    main()
