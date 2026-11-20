import bpy
from pathlib import Path
from mathutils import Vector

ROOT = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
OUT = ROOT / 'work/pteranodon-realism'
bpy.ops.wm.open_mainfile(filepath=str(OUT / 'original.blend'))
scene = bpy.context.scene
for obj in list(scene.objects):
    if obj.type in {'CAMERA', 'LIGHT'} or 'icosa' in obj.name.lower():
        bpy.data.objects.remove(obj, do_unlink=True)
rig = next(o for o in scene.objects if o.type == 'ARMATURE')
mesh = next(o for o in scene.objects if o.type == 'MESH')
scene.frame_set(22)

mat = mesh.data.materials[0]
nodes, links = mat.node_tree.nodes, mat.node_tree.links
bsdf = next(n for n in nodes if n.type == 'BSDF_PRINCIPLED')
base = next(n for n in nodes if n.type == 'TEX_IMAGE' and n.image and n.image.name == 'Image_0')
original_color = base.image
normal = next(n for n in nodes if n.type == 'NORMAL_MAP')
normal.inputs['Strength'].default_value = .6
for name, value in [('Roughness', .78), ('Specular IOR Level', .26), ('Metallic', 0)]:
    for link in list(bsdf.inputs[name].links):
        links.remove(link)
    bsdf.inputs[name].default_value = value

scene.render.engine = 'CYCLES'
scene.cycles.samples = 48
scene.cycles.use_denoising = True
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
scene.render.resolution_percentage = 100
scene.view_settings.view_transform = 'AgX'
scene.world = bpy.data.worlds.new('Neutral Preview World')
scene.world.use_nodes = True
background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
background.inputs['Color'].default_value = (.22, .25, .28, 1)
background.inputs['Strength'].default_value = .7

def area(name, pos, target, energy, size):
    bpy.ops.object.light_add(type='AREA', location=pos)
    obj = bpy.context.object
    obj.name = name
    obj.data.energy = energy
    obj.data.size = size
    obj.rotation_euler = (Vector(target) - obj.location).to_track_quat('-Z', 'Y').to_euler()

area('Soft daylight', (.4, -.8, 1.5), (0, 0, .4), 65, 1.3)
area('Sky fill', (-.6, -.1, .8), (0, 0, .4), 18, 1.0)
area('Soft rim', (.2, .8, 1.1), (0, 0, .4), 42, .8)

bpy.ops.object.camera_add(location=(.95, -1.1, .75))
camera = bpy.context.object
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 1.25
camera.data.clip_start = .001
camera.data.clip_end = 50
target = Vector((.015, -.015, .38))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.camera = camera

def render(name):
    scene.render.filepath = str(OUT / name)
    bpy.ops.render.render(write_still=True)

render('01-original-2k.png')
base.image = bpy.data.images.load(str(OUT / 'Pteranodon_Realistic_BaseColor.png'))
render('02-realistic-full.png')

head = rig.matrix_world @ rig.pose.bones['head.15'].head
camera.location = head + Vector((.35, -.34, .16))
target = head + Vector((.007, -.045, .006))
camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
camera.data.ortho_scale = .34
scene.render.resolution_x = 1400
scene.render.resolution_y = 1000
render('03-realistic-head.png')

camera.data.ortho_scale = 1.25
camera.location = (.95, -1.1, .75)
camera.rotation_euler = (Vector((.015, -.015, .38)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
scene.render.resolution_x = 1600
scene.render.resolution_y = 1000
bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'Pteranodon_Realism_Preview.blend'))
print('PREVIEW_OK', OUT)
