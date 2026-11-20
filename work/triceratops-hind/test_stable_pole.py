import bpy,importlib.util,runpy
from math import sqrt
from mathutils import Matrix,Vector
from pathlib import Path
root=Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
spec=importlib.util.spec_from_file_location('triceratops_import',root/'Tools/Blender/import_downloaded_triceratops.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=str(m.SOURCE))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
m.join_body(rig)
bpy.context.scene.frame_set(1);bpy.context.view_layer.update()
poles={}
for upper,lower,foot,_ in m.LEGS:
    a,b,c=[rig.matrix_world@rig.pose.bones[n].head for n in [upper,lower,foot]]
    axis=(c-a).normalized();poles[upper]=((b-a)-axis*(b-a).dot(axis)).normalized()

def stable_solve(rig,names,target,foot_rotation,foot_scale):
    upper,lower,foot=[rig.pose.bones[n] for n in names]
    a,b,c=[rig.matrix_world@bone.head for bone in (upper,lower,foot)]
    first,second=(b-a).length,(c-b).length
    direction=(target-a).normalized()
    distance=min(max((target-a).length,abs(first-second)+.001),first+second-.001)
    along=(first*first-second*second+distance*distance)/(2*distance)
    hint=poles[names[0]]
    bend=(hint-direction*hint.dot(direction)).normalized()
    knee=a+direction*along+bend*sqrt(max(0,first*first-along*along))
    m.rotate_about_head(rig,upper,(b-a).rotation_difference(knee-a))
    b,c=[rig.matrix_world@bone.head for bone in (lower,foot)]
    m.rotate_about_head(rig,lower,(c-b).rotation_difference(target-b))
    position=rig.matrix_world@foot.head
    foot.matrix=rig.matrix_world.inverted()@Matrix.LocRotScale(position,foot_rotation,foot_scale)
    bpy.context.view_layer.update()
m.solve_leg=stable_solve
m.add_walk(rig)
bpy.ops.wm.save_as_mainfile(filepath=str(root/'work/triceratops-hind/stable-pole.blend'))
runpy.run_path(str(root/'work/triceratops-hind/rig_audit.py'))
(root/'work/triceratops-hind/rig-cycle.json').rename(root/'work/triceratops-hind/rig-cycle-stable.json')
