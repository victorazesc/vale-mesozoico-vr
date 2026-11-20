import json
from pathlib import Path
import bpy

root = Path('/Users/victorazevedo/projetos/vale-mesozoico-vr')
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=str(root / 'Assets/_Game/Resources/Models/Dinosaurs/Triceratops.fbx'))
report = {'objects': [], 'actions': []}
for obj in bpy.context.scene.objects:
    item = {'name': obj.name, 'type': obj.type, 'dimensions': list(obj.dimensions), 'scale': list(obj.scale)}
    if obj.type == 'MESH':
        item.update(vertices=len(obj.data.vertices), polygons=len(obj.data.polygons), smooth=sum(p.use_smooth for p in obj.data.polygons), materials=[m.name for m in obj.data.materials], groups=[g.name for g in obj.vertex_groups])
        item['vertex_range'] = [[min(v.co[i] for v in obj.data.vertices), max(v.co[i] for v in obj.data.vertices)] for i in range(3)]
    if obj.type == 'ARMATURE':
        item['bones'] = [{'name': b.name, 'head': list(b.head_local), 'tail': list(b.tail_local)} for b in obj.data.bones]
    report['objects'].append(item)
for act in bpy.data.actions:
    report['actions'].append({'name': act.name, 'range': list(act.frame_range)})
(root / 'work/triceratops-realism/source-report.json').write_text(json.dumps(report, indent=2))
print(json.dumps(report))
