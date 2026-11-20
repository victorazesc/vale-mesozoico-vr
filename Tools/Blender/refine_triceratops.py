"""Refine the CC0 Quaternius mesh, preserving its skeleton and animation clips.

Run with Blender --background --factory-startup --python Tools/Blender/refine_triceratops.py.
The original FBX is retained in SourceAssets; this export is safe to regenerate.
"""
import json
import math
from pathlib import Path

import bmesh
import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceAssets/Quaternius/AnimatedDinosaurs/Triceratops.fbx'
OUTPUT = ROOT / 'Assets/_Game/Resources/Models/Dinosaurs'
TEXTURES = OUTPUT / 'TriceratopsSkin'
PREVIEW = ROOT / 'work/triceratops-realism'


def ramp(nodes, colors):
    node = nodes.new('ShaderNodeValToRGB')
    for element, (position, color) in zip(node.color_ramp.elements, colors):
        element.position = position
        element.color = (*color, 1)
    return node


def skin_material(name, horn=False, dark=False):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes, links = material.node_tree.nodes, material.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = 0.68 if dark else 0.82
    shader.inputs['Specular IOR Level'].default_value = 0.22
    coord = nodes.new('ShaderNodeTexCoord')
    mapping = nodes.new('ShaderNodeVectorMath')
    mapping.operation = 'MULTIPLY'
    mapping.inputs[1].default_value = (0.9, 3.5, 1.4)
    links.new(coord.outputs['Generated'], mapping.inputs[0])
    image = bpy.data.images.load(str(ROOT / 'SourceAssets/Generated/Triceratops/Triceratops_Hide_BaseColor.png'), check_existing=True)
    hide = nodes.new('ShaderNodeTexImage')
    hide.image = image
    hide.projection = 'BOX'
    hide.projection_blend = 0.28
    links.new(mapping.outputs['Vector'], hide.inputs['Vector'])
    noise = nodes.new('ShaderNodeTexNoise')
    noise.inputs['Scale'].default_value = 3.5
    noise.inputs['Detail'].default_value = 3
    links.new(mapping.outputs['Vector'], noise.inputs['Vector'])
    pigments = ramp(nodes, [(0.15, (0.34, 0.38, 0.33)), (0.85, (0.76, 0.68, 0.52))])
    links.new(noise.outputs['Fac'], pigments.inputs['Fac'])
    mix = nodes.new('ShaderNodeMixRGB')
    mix.blend_type = 'MULTIPLY'
    mix.inputs[0].default_value = 0.65
    links.new(hide.outputs['Color'], mix.inputs[1])
    links.new(pigments.outputs['Color'], mix.inputs[2])
    belly = nodes.new('ShaderNodeMixRGB')
    belly.blend_type = 'SCREEN'
    belly.inputs[0].default_value = 0.13 if horn else 0.025
    links.new(mix.outputs[0], belly.inputs[1])
    belly.inputs[2].default_value = (0.36, 0.26, 0.13, 1)
    links.new(belly.outputs[0], shader.inputs['Base Color'])
    bump = nodes.new('ShaderNodeBump')
    bump.inputs['Strength'].default_value = 0.35
    bump.inputs['Distance'].default_value = 0.022
    links.new(hide.outputs['Color'], bump.inputs['Height'])
    links.new(bump.outputs['Normal'], shader.inputs['Normal'])
    if dark:
        # Horns use a separate keratin treatment, with no skin-scale pattern.
        mapping.inputs[1].default_value = (8, 8, 0.18)
        keratin = ramp(nodes, [(0.18, (0.055, 0.038, 0.020)), (0.85, (0.42, 0.33, 0.19))])
        links.new(noise.outputs['Fac'], keratin.inputs['Fac'])
        links.new(keratin.outputs['Color'], shader.inputs['Base Color'])
        links.new(noise.outputs['Fac'], bump.inputs['Height'])
        bump.inputs['Distance'].default_value = 0.008
    return material


def main():
    TEXTURES.mkdir(parents=True, exist_ok=True)
    PREVIEW.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE))
    rig = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
    mesh = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
    rig.name = 'Triceratops_Rig'
    rig.data.pose_position = 'REST'
    original_vertices = len(mesh.data.vertices)
    # Work in skeleton coordinates; preserve the bind pose and the existing weights.
    matrix = rig.matrix_world.inverted() @ mesh.matrix_world
    mesh.data.transform(matrix)
    mesh.matrix_world = rig.matrix_world.copy()
    bm = bmesh.new()
    bm.from_mesh(mesh.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=0.0001)
    bmesh.ops.join_triangles(bm, faces=list(bm.faces), angle_face_threshold=0.8,
                            angle_shape_threshold=0.8, cmp_materials=True)
    bm.to_mesh(mesh.data)
    bm.free()
    bpy.context.view_layer.objects.active = mesh
    mesh.select_set(True)
    # Subdivision is baked once, never evaluated on the headset.
    smooth = mesh.modifiers.new('Organic silhouette', 'SUBSURF')
    smooth.levels = 2
    smooth.render_levels = 2
    bpy.ops.object.modifier_move_up(modifier=smooth.name)
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    for polygon in mesh.data.polygons:
        polygon.use_smooth = True
    materials = [skin_material('Triceratops Skin'), skin_material('Triceratops Horn', horn=True),
                 skin_material('Triceratops Hoof', dark=True)]
    material_indices = [polygon.material_index for polygon in mesh.data.polygons]
    mesh.data.materials.clear()
    for material in materials:
        mesh.data.materials.append(material)
    for polygon, index in zip(mesh.data.polygons, material_indices):
        polygon.material_index = index
    # Small, genuinely three-dimensional eyes and nostrils follow the head bone.
    for label, color, y, z, radius in [
        ('Eye', (0.035, 0.019, 0.008, 1), -3.28, 3.40, 0.075),
        ('Nostril', (0.016, 0.012, 0.008, 1), -4.02, 2.85, 0.055),
    ]:
        detail_material = bpy.data.materials.new('Triceratops ' + label)
        detail_material.diffuse_color = color
        detail_material.use_nodes = True
        detail_shader = detail_material.node_tree.nodes.get('Principled BSDF')
        detail_shader.inputs['Base Color'].default_value = color
        detail_shader.inputs['Roughness'].default_value = 0.24 if label == 'Eye' else 0.8
        materials.append(detail_material)
        for side in (-1, 1):
            hit, position, normal, _ = mesh.ray_cast(Vector((side * 5, y, z)), Vector((-side, 0, 0)))
            if not hit:
                raise RuntimeError('Missing facial surface for ' + label)
            bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8,
                radius=radius * rig.scale.x, location=rig.matrix_world @ (position + normal * radius * 0.25))
            detail = bpy.context.object
            detail.name = label
            detail.rotation_euler = (rig.matrix_world.to_3x3() @ normal).to_track_quat('Z', 'Y').to_euler()
            detail.scale.z = 0.38
            detail.data.materials.append(detail_material)
            detail.vertex_groups.new(name='Head').add(list(range(len(detail.data.vertices))), 1, 'REPLACE')
            for polygon in detail.data.polygons:
                polygon.use_smooth = True
            bpy.ops.object.select_all(action='DESELECT')
            detail.select_set(True)
            mesh.select_set(True)
            bpy.context.view_layer.objects.active = mesh
            bpy.ops.object.join()
            if label == 'Eye':
                pupil_material = bpy.data.materials.get('Triceratops Pupil')
                if pupil_material is None:
                    pupil_material = bpy.data.materials.new('Triceratops Pupil')
                    pupil_material.use_nodes = True
                    pupil_shader = pupil_material.node_tree.nodes.get('Principled BSDF')
                    pupil_shader.inputs['Base Color'].default_value = (0.004, 0.003, 0.002, 1)
                    pupil_shader.inputs['Roughness'].default_value = 0.18
                    materials.append(pupil_material)
                bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8,
                    radius=radius * 0.48 * rig.scale.x,
                    location=rig.matrix_world @ (position + normal * radius * 0.62))
                pupil = bpy.context.object
                pupil.rotation_euler = (rig.matrix_world.to_3x3() @ normal).to_track_quat('Z', 'Y').to_euler()
                pupil.scale.z = 0.25
                pupil.data.materials.append(pupil_material)
                pupil.vertex_groups.new(name='Head').add(list(range(len(pupil.data.vertices))), 1, 'REPLACE')
                for polygon in pupil.data.polygons:
                    polygon.use_smooth = True
                bpy.ops.object.select_all(action='DESELECT')
                pupil.select_set(True)
                mesh.select_set(True)
                bpy.context.view_layer.objects.active = mesh
                bpy.ops.object.join()
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.008)
    bpy.ops.object.mode_set(mode='OBJECT')
    scene = bpy.context.scene
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 8
    scene.render.bake.margin = 12
    scene.render.bake.use_pass_direct = False
    scene.render.bake.use_pass_indirect = False
    scene.render.bake.use_pass_color = True
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
    bpy.context.view_layer.objects.active = mesh
    for suffix, bake_type in [('BaseColor', 'DIFFUSE'), ('Normal', 'NORMAL')]:
        image = bpy.data.images.new('Triceratops_' + suffix, width=2048, height=2048)
        if suffix == 'Normal':
            image.colorspace_settings.name = 'Non-Color'
        for material in materials:
            nodes = material.node_tree.nodes
            texture = nodes.new('ShaderNodeTexImage')
            texture.image = image
            nodes.active = texture
        bpy.ops.object.bake(type=bake_type)
        image.filepath_raw = str(TEXTURES / (image.name + '.png'))
        image.file_format = 'PNG'
        image.save()
    # One atlas and one draw per animal, shared by every herd member.
    baked_material = bpy.data.materials.new('Triceratops Baked Skin')
    baked_material.use_nodes = True
    nodes, links = baked_material.node_tree.nodes, baked_material.node_tree.links
    shader = nodes.get('Principled BSDF')
    shader.inputs['Roughness'].default_value = 0.82
    base = nodes.new('ShaderNodeTexImage')
    base.image = bpy.data.images['Triceratops_BaseColor']
    links.new(base.outputs['Color'], shader.inputs['Base Color'])
    normal_texture = nodes.new('ShaderNodeTexImage')
    normal_texture.image = bpy.data.images['Triceratops_Normal']
    normal = nodes.new('ShaderNodeNormalMap')
    links.new(normal_texture.outputs['Color'], normal.inputs['Color'])
    links.new(normal.outputs['Normal'], shader.inputs['Normal'])
    mesh.data.materials.clear()
    mesh.data.materials.append(baked_material)
    for action in bpy.data.actions:
        action.name = action.name.split('|')[-1]
    rig.data.pose_position = 'POSE'
    idle = next(action for action in bpy.data.actions if 'Idle' in action.name)
    rig.animation_data.action = idle
    if idle.slots:
        rig.animation_data.action_slot = idle.slots[0]
    scene.frame_set(1)
    scene.render.fps = 24
    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    mesh.select_set(True)
    bpy.ops.export_scene.fbx(filepath=str(OUTPUT / 'Triceratops.fbx'), use_selection=True,
        object_types={'ARMATURE', 'MESH'}, use_mesh_modifiers=True, mesh_smooth_type='FACE',
        add_leaf_bones=False, use_armature_deform_only=False, bake_anim=True,
        bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.1,
        path_mode='RELATIVE', embed_textures=False, axis_forward='-Z', axis_up='Y')
    # Studio proof of geometry and materials; the game is validated separately in Unity.
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = mesh.evaluated_get(depsgraph)
    points = [evaluated.matrix_world @ Vector(corner) for corner in evaluated.bound_box]
    low = Vector([min(point[i] for point in points) for i in range(3)])
    high = Vector([max(point[i] for point in points) for i in range(3)])
    center = (low + high) * 0.5
    size = max(high - low)
    bpy.ops.object.camera_add(location=center + Vector((size * 0.95, -size * 0.9, size * 0.42)))
    camera = bpy.context.object
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = size * 1.25
    scene.camera = camera
    for location, energy, light_size in [(center + Vector((size, -size, size)), 3500, size),
                                          (center + Vector((-size, 0, size * 0.5)), 1800, size)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object
        light.data.energy = energy * (size / 10) ** 2
        light.data.shape = 'DISK'
        light.data.size = light_size
        light.rotation_euler = (center - location).to_track_quat('-Z', 'Y').to_euler()
    scene.world = bpy.data.worlds.new('Studio')
    scene.world.use_nodes = True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value = (0.14, 0.16, 0.19, 1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value = 0.45
    scene.render.resolution_x, scene.render.resolution_y = 1400, 900
    scene.render.resolution_percentage = 100
    scene.cycles.samples = 24
    scene.render.filepath = str(PREVIEW / 'model-refined.png')
    bpy.ops.render.render(write_still=True)
    report = {'source_vertices': original_vertices, 'vertices': len(mesh.data.vertices),
              'triangles': sum(len(face.vertices)-2 for face in mesh.data.polygons),
              'posed_dimensions': list(high - low), 'clips': [a.name for a in bpy.data.actions]}
    (PREVIEW / 'refined-model.json').write_text(json.dumps(report, indent=2))
    print('TRICERATOPS_REFINED ' + json.dumps(report))


if __name__ == '__main__':
    main()
