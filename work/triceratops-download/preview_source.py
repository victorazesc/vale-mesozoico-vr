from pathlib import Path
import sys
import bpy
from mathutils import Vector

ROOT = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
prepared = '--prepared' in sys.argv
bpy.ops.wm.open_mainfile(filepath=str(ROOT / ('SourceAssets/Downloaded/Triceratops/Triceratops.blend'
    if prepared else 'work/triceratops-download/imported.blend')))
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
for obj in list(scene.objects):
    if obj.name == 'Icosphere' or (obj.type == 'EMPTY' and obj.parent is None):
        bpy.data.objects.remove(obj, do_unlink=True)
for frame in range(0, 217, 12):
    scene.frame_set(frame)
    print('POSE', frame, {b.name: tuple(round(v, 3) for v in rig.matrix_world @ b.head) for b in rig.pose.bones if b.name in ['Bip001 Head','Bip001 L Hand','Bip001 R Hand','Bip001 L Foot','Bip001 R Foot']})
scene.frame_set(1)
bpy.ops.object.camera_add(location=(8, -10, 5))
scene.camera = bpy.context.object
scene.camera.rotation_euler = (Vector((0, 0, 1.5)) - scene.camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera.data.type = 'ORTHO'
scene.camera.data.ortho_scale = 10
scene.world = bpy.data.worlds.new('Preview World')
scene.world.use_nodes = True
background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
background.inputs['Color'].default_value = (0.30, 0.34, 0.40, 1)
background.inputs['Strength'].default_value = 0.7
for loc, energy, size in [((4, -6, 8), 1800, 7), ((-4, 1, 5), 1300, 5)]:
    bpy.ops.object.light_add(type='AREA', location=loc)
    light = bpy.context.object
    light.data.energy = energy
    light.data.shape = 'DISK'
    light.data.size = size
    light.rotation_euler = (Vector((0, 0, 1.5))-light.location).to_track_quat('-Z','Y').to_euler()
scene.render.engine = 'CYCLES'
scene.cycles.samples = 16
scene.cycles.use_denoising = True
scene.render.resolution_x = 1100
scene.render.resolution_y = 800
scene.render.resolution_percentage = 100
scene.render.filepath = str(ROOT / ('work/triceratops-download/prepared.png' if prepared
    else 'work/triceratops-download/source.png'))
bpy.ops.render.render(write_still=True)
