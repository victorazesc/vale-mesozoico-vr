"""Prepare seirogan's CC-BY model; retain its original flight and leg rig."""
from pathlib import Path
import bpy
from cycles.properties import CyclesLightSettings

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'SourceAssets/Sketchfab/Quetzalcoatlus/source/Pterosaur_Quetzalcoatlus/bird.fbx'
OUTPUT = ROOT / 'Assets/_Game/Resources/Models/Dinosaurs/Quetzalcoatlus'

bpy.ops.wm.read_factory_settings(use_empty=True)
# The Blender 5.1 FBX reader assigns this obsolete flag on imported lights.
if not hasattr(CyclesLightSettings, 'cast_shadow'):
    CyclesLightSettings.cast_shadow = bpy.props.BoolProperty(default=True)
bpy.ops.import_scene.fbx(filepath=str(SOURCE))
for obj in list(bpy.context.scene.objects):
    if obj.type not in {'ARMATURE', 'MESH'}:
        bpy.data.objects.remove(obj, do_unlink=True)
rig = next(obj for obj in bpy.context.scene.objects if obj.type == 'ARMATURE')
body = next(obj for obj in bpy.context.scene.objects if obj.type == 'MESH')
flight = next(action for action in bpy.data.actions if action.frame_range[1] > 100)
rig.animation_data.action = flight
for action in list(bpy.data.actions):
    if action != flight:
        bpy.data.actions.remove(action)
flight.name = 'Flight'
rig.name = 'Quetzalcoatlus_Rig'
body.name = 'Quetzalcoatlus_Body'
bpy.context.scene.frame_set(1)
bpy.context.view_layer.objects.active = body
body.select_set(True)
modifier = body.modifiers.new('Preserve silhouette and skin weights', 'DECIMATE')
modifier.ratio = 0.24
bpy.ops.object.modifier_apply(modifier=modifier.name)
for polygon in body.data.polygons:
    polygon.use_smooth = True
OUTPUT.mkdir(parents=True, exist_ok=True)
image = bpy.data.images.get('b48.png')
image.filepath_raw = str(OUTPUT / 'Quetzalcoatlus_BaseColor.png')
image.file_format = 'PNG'
image.save()
for material in body.data.materials:
    material.name = 'Quetzalcoatlus Leather'
    if material.use_nodes:
        bsdf = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
        bsdf.inputs['Metallic'].default_value = 0
        bsdf.inputs['Roughness'].default_value = 0.78
        # The source also uses its colour palette as a normal map; discard that slot.
        for link in list(material.node_tree.links):
            if link.to_socket == bsdf.inputs['Normal']:
                material.node_tree.links.remove(link)
bpy.context.scene.render.fps = 24
bpy.ops.object.select_all(action='SELECT')
bpy.ops.export_scene.fbx(filepath=str(OUTPUT / 'Quetzalcoatlus.fbx'),
    use_selection=True, object_types={'ARMATURE','MESH'},
    use_mesh_modifiers=True, mesh_smooth_type='FACE', add_leaf_bones=False,
    use_armature_deform_only=False, bake_anim=True, bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False, bake_anim_force_startend_keying=True,
    bake_anim_simplify_factor=0.15, path_mode='RELATIVE', embed_textures=False,
    axis_forward='-Z', axis_up='Y', apply_scale_options='FBX_SCALE_ALL')
print('QUETZALCOATLUS_EXPORT_OK', 'triangles',sum(len(p.vertices)-2 for p in body.data.polygons),
      'bones',len(rig.data.bones),'animation',flight.name,'frames',tuple(flight.frame_range))
