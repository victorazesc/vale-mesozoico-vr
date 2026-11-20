from pathlib import Path
import bpy
from mathutils import Vector

ROOT = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(ROOT / 'SourceAssets/Downloaded/Triceratops/Triceratops.glb'))
scene = bpy.context.scene
print('FPS', scene.render.fps)
for obj in scene.objects:
    print('OBJECT', obj.name, obj.type, 'parent', obj.parent.name if obj.parent else None,
          'scale', tuple(obj.scale), 'dim', tuple(obj.dimensions), 'location', tuple(obj.location))
    if obj.animation_data:
        print('ANIM', obj.name, 'action', obj.animation_data.action,
              'NLA', [(t.name, [(s.action.name, s.frame_start, s.frame_end) for s in t.strips]) for t in obj.animation_data.nla_tracks])
    if obj.type == 'ARMATURE':
        for bone in obj.data.bones:
            if any(t in bone.name for t in ['Bip001', 'Root']):
                print('BONE', bone.name, 'head', tuple(round(v, 3) for v in obj.matrix_world @ bone.head_local))
for action in bpy.data.actions:
    print('ACTION', action.name, tuple(action.frame_range), 'slots', [s.identifier for s in action.slots])
for image in bpy.data.images:
    print('IMAGE', image.name, tuple(image.size), image.colorspace_settings.name)
for frame in [0, 20, 40, 60, 80, 100]:
    scene.frame_set(frame)
    rig = next(o for o in scene.objects if o.type == 'ARMATURE')
    print('POSE', frame, [(b.name, tuple(round(v, 3) for v in rig.matrix_world @ b.head)) for b in rig.pose.bones if b.name in ['Bip001', 'Bip001 Head', 'Bip001 L Hand', 'Bip001 R Hand', 'Bip001 L Foot', 'Bip001 R Foot']])
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT / 'work/triceratops-download/imported.blend'))
