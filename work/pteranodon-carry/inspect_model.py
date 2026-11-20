import bpy,json
from mathutils import Vector
bpy.ops.wm.read_factory_settings(use_empty=True)
# Blender 5.1 importer still assigns the removed legacy light flag.
from cycles.properties import CyclesLightSettings
CyclesLightSettings.cast_shadow = bpy.props.BoolProperty(default=True)
bpy.ops.import_scene.fbx(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/SourceAssets/Sketchfab/Quetzalcoatlus/source/Pterosaur_Quetzalcoatlus/bird.fbx')
for o in bpy.context.scene.objects:
 print('OBJECT',o.name,o.type,'scale',list(o.scale),'rotation',list(o.rotation_euler))
 if o.type=='MESH':
  pts=[o.matrix_world@Vector(v) for v in o.bound_box]
  print('MESH',len(o.data.vertices),len(o.data.polygons),'bounds',[[min(v[i] for v in pts),max(v[i] for v in pts)] for i in range(3)],'groups',[(g.name,g.index) for g in o.vertex_groups])
 if o.type=='ARMATURE':
  for b in o.data.bones: print('BONE',b.name,'parent',b.parent.name if b.parent else None,'head',list(b.head_local),'tail',list(b.tail_local))
for a in bpy.data.actions: print('ACTION',a.name,list(a.frame_range))
for m in bpy.data.materials:
 print('MAT',m.name)
 for n in m.node_tree.nodes if m.node_tree else []:
  if n.type=='TEX_IMAGE': print('TEXTURE',n.image.name,n.image.filepath,list(n.image.size))
bpy.ops.wm.save_as_mainfile(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/pteranodon-carry/source.blend')
