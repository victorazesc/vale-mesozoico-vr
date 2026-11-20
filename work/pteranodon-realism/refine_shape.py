import bpy
import math
from pathlib import Path
from mathutils import Vector

out = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr/work/pteranodon-realism')
bpy.ops.wm.open_mainfile(filepath=str(out / 'Pteranodon_Realism_Preview.blend'))
scene = bpy.context.scene
mesh = next(o for o in scene.objects if o.type == 'MESH')
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
head_group = mesh.vertex_groups['head.15'].index
eye_groups = {mesh.vertex_groups[n].index for n in ['L_eye.30', 'R_eye.29']}
eye_centers = [mesh.matrix_world.inverted() @ rig.matrix_world @ rig.data.bones[n].head_local
               for n in ['L_eye.30', 'R_eye.29']]
for vertex in mesh.data.vertices:
    head_weight = sum(g.weight for g in vertex.groups if g.group == head_group)
    eye_weight = sum(g.weight for g in vertex.groups if g.group in eye_groups)
    if head_weight < .4 and eye_weight < .4:
        continue
    p = vertex.co
    if head_weight > .8 and p.z > 8.4:
        rise = p.z - 8.4
        p.y += rise * .82
        p.z -= rise * .32
    center = eye_centers[0] if p.x >= 0 else eye_centers[1]
    delta = p - center
    distance = (delta.x / .78) ** 2 + (delta.y / 1.4) ** 2 + (delta.z / 1.1) ** 2
    shrink = .18 * math.exp(-2.0 * distance) * head_weight + .20 * eye_weight
    p -= delta * shrink
mesh.data.update()

# Identical light and camera let the preview expose the actual mesh changes.
camera = scene.camera
head = rig.matrix_world @ rig.pose.bones['head.15'].head
camera.location = head + Vector((.35, -.34, .16))
target = head + Vector((.007, -.045, .006))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.ortho_scale = .31
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
scene.render.filepath = str(out / '04-refined-head.png')
bpy.ops.render.render(write_still=True)

evaluated = mesh.evaluated_get(bpy.context.evaluated_depsgraph_get())
points = [evaluated.matrix_world @ Vector(p) for p in evaluated.bound_box]
center = sum(points, Vector()) / len(points)
camera.location = center + Vector((.78, -.92, .72))
camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.ortho_scale = 1.15
scene.render.resolution_x = 1600
scene.render.resolution_y = 850
scene.render.filepath = str(out / '05-refined-full.png')
bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'Pteranodon_Realism_Refined.blend'))
print('REFINED_PREVIEW_OK')
