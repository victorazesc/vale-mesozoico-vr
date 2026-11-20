"""Export the approved, freely licensed crest Pteranodon without changing the flock."""
from pathlib import Path
import shutil
import bpy

ROOT = Path(__file__).resolve().parents[2]
PREVIEW = ROOT / 'work/pteranodon-realism'
OUTPUT = ROOT / 'Assets/_Game/Resources/Models/Dinosaurs/PteranodonCrest'
SOURCE = ROOT / 'SourceAssets/Generated/PteranodonCrest'
OUTPUT.mkdir(parents=True, exist_ok=True)
SOURCE.mkdir(parents=True, exist_ok=True)
bpy.ops.wm.open_mainfile(filepath=str(PREVIEW / 'Pteranodon_Realism_Refined.blend'))
for obj in list(bpy.context.scene.objects):
    if obj.type in {'CAMERA', 'LIGHT'}:
        bpy.data.objects.remove(obj, do_unlink=True)
shutil.copy2(PREVIEW / 'Pteranodon_Realistic_BaseColor.png', OUTPUT / 'PteranodonCrest_BaseColor.png')
normal = bpy.data.images['Image_2']
normal.filepath_raw = str(OUTPUT / 'PteranodonCrest_Normal.png')
normal.file_format = 'PNG'
normal.save()
for action in list(bpy.data.actions):
    if action.name != 'flying':
        bpy.data.actions.remove(action)
bpy.context.scene.frame_set(0)
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(
    filepath=str(OUTPUT / 'PteranodonCrest.fbx'), use_selection=True,
    object_types={'ARMATURE', 'MESH', 'EMPTY'}, use_mesh_modifiers=True,
    mesh_smooth_type='FACE', add_leaf_bones=False, use_armature_deform_only=True,
    bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True, bake_anim_simplify_factor=.1,
    apply_scale_options='FBX_SCALE_ALL', path_mode='RELATIVE', embed_textures=False,
    axis_forward='-Z', axis_up='Y')
bpy.ops.file.pack_all()
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE / 'PteranodonCrest.blend'))
mesh = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
mesh.data.calc_loop_triangles()
print('CREST_EXPORT_OK', len(mesh.data.loop_triangles), 'triangles')
