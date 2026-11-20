import bpy
from math import degrees

rig = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
walk = bpy.data.actions['Walk']
for track in rig.animation_data.nla_tracks:
    track.mute = True
rig.animation_data.action = walk
rig.animation_data.action_slot = walk.slots[0]
poses = []
for frame in [1, 16, 31, 46, 61]:
    bpy.context.scene.frame_set(frame)
    bpy.context.view_layer.update()
    poses.append({bone.name: (rig.matrix_world @ bone.matrix).copy() for bone in rig.pose.bones})
position_error = max((poses[0][name].translation - poses[-1][name].translation).length for name in poses[0])
angle_error = max(degrees(poses[0][name].to_quaternion().rotation_difference(poses[-1][name].to_quaternion()).angle)
                  for name in poses[0])
print('WALK_LOOP position_error_m=', position_error, 'rotation_error_deg=', angle_error)
for name in ['Bip001 Head', 'Bip001 Tail4', 'Bip001 Pelvis']:
    motion = max((pose[name].translation - poses[0][name].translation).length for pose in poses)
    print('SECONDARY_MOTION', name, motion)
assert position_error < 0.0001 and angle_error < 0.05
print('WALK_CYCLE_PASS')
