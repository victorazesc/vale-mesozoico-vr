import bpy
from pathlib import Path
from mathutils import Vector

root = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
out = root / 'work/pteranodon-realism'
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.gltf(filepath=str(next((root / 'SourceAssets/Sketchfab/Pteranodon/Original/source').glob('*.glb'))))
scene = bpy.context.scene
scene.render.fps = 24
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
print('ACTIONS', [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
print('OBJECTS', [(o.name, o.type, tuple(o.dimensions)) for o in scene.objects])
scene.frame_set(12)
for b in rig.pose.bones:
    if any(t in b.name.lower() for t in ['head', 'neck', 'pelvis', 'ankle', 'eye', 'wing01', 'jaw']):
        print('BONE', b.name, 'head', tuple(rig.matrix_world @ b.head), 'tail', tuple(rig.matrix_world @ b.tail))
print('MATERIALS', [(m.name, [(n.type, n.image.name if n.type == 'TEX_IMAGE' and n.image else '') for n in m.node_tree.nodes]) for m in bpy.data.materials if m.use_nodes])
bpy.ops.wm.save_as_mainfile(filepath=str(out / 'original.blend'))
