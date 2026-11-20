import bpy
from mathutils import Matrix
bpy.ops.wm.open_mainfile(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/triceratops-download/imported.blend')
s=bpy.context.scene
s.frame_set(1)
r=next(o for o in s.objects if o.type=='ARMATURE')
r.data.pose_position='REST'
bpy.context.view_layer.update()
b=bpy.data.objects['3jl001']
o=bpy.data.objects['Object003']
def report(label):
    ev=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=ev.to_mesh()
    print(label, 'parent',o.parent.name,'bone',o.parent_bone, 'type',o.parent_type,
          'world_vertex',tuple(o.matrix_world@m.vertices[0].co),'rest_vertex',tuple(o.matrix_world@o.data.vertices[0].co))
    ev.to_mesh_clear()
report('ORIGINAL')
bone=o.parent_bone
world=o.matrix_world.copy()
o.parent_type='OBJECT';o.parent=r;o.matrix_parent_inverse=Matrix.Identity(4)
bpy.context.view_layer.update();o.matrix_world=world;bpy.context.view_layer.update()
report('DETACHED')
o.vertex_groups.new(name=bone).add(list(range(len(o.data.vertices))),1,'REPLACE')
o.modifiers.new('armature','ARMATURE').object=r
bpy.context.view_layer.update()
report('MODIFIER')
print('BODY_MODIFIERS',[(x.type,x.object.name) for x in b.modifiers if x.type=='ARMATURE'])
bpy.ops.object.select_all(action='DESELECT');o.select_set(True);b.select_set(True)
bpy.context.view_layer.objects.active=b
bpy.ops.object.join();o=b
report('JOINED_REST')
r.data.pose_position='POSE';bpy.context.view_layer.update()
report('JOINED_POSE')
e=o.evaluated_get(bpy.context.evaluated_depsgraph_get());m=e.to_mesh()
print('BOUND',min((o.matrix_world@v.co).z for v in m.vertices))
