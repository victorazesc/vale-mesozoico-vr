import bpy,math
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/pteranodon-carry/source.blend')
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
rig.animation_data.action=bpy.data.actions['Armature|Armature|bird.001']
bpy.context.scene.frame_set(40)
for n in ['Bone','Bone.001','Bone.005','Bone_L.001','Bone_L.002','Bone_L.003','Bone_L.004']:
 b=rig.pose.bones[n]
 print('POSE',n,'head',tuple(rig.matrix_world@b.head),'tail',tuple(rig.matrix_world@b.tail))
for o in list(bpy.context.scene.objects):
 if o.type in ['CAMERA','LIGHT']: bpy.data.objects.remove(o,do_unlink=True)
scene=bpy.context.scene
scene.render.engine='CYCLES';scene.cycles.samples=20
scene.render.resolution_x=1200;scene.render.resolution_y=800;scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('Preview World');scene.world.color=(.35,.35,.35)
bpy.ops.object.light_add(type='AREA', location=(2,-8,13));bpy.context.object.data.energy=2600;bpy.context.object.data.shape='DISK';bpy.context.object.data.size=8
bpy.ops.object.camera_add(location=(11,-14,10));cam=bpy.context.object
cam.rotation_euler=(Vector((0,-1,4))-cam.location).to_track_quat('-Z','Y').to_euler();cam.data.type='ORTHO';cam.data.ortho_scale=17;scene.camera=cam
scene.render.filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/pteranodon-carry/source-preview.png';bpy.ops.render.render(write_still=True)
