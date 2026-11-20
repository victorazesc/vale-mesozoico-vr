import bpy,json,math
from pathlib import Path
from mathutils import Vector
out=Path('/Users/victorazevedo/projetos/vale-mesozoico-vr/work/triceratops-hind')
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
legs=[('FL','Bip001 L UpperArm','Bip001 L Forearm','Bip001 L Hand'),('FR','Bip001 R UpperArm','Bip001 R Forearm','Bip001 R Hand'),('BL','Bip001 L Thigh','Bip001 L Calf','Bip001 L Foot'),('BR','Bip001 R Thigh','Bip001 R Calf','Bip001 R Foot')]
rows=[]
for frame in [1]+[300+i/4 for i in range(241)]:
    bpy.context.scene.frame_set(int(frame),subframe=frame%1)
    bpy.context.view_layer.update()
    row={'frame':frame,'legs':{}}
    for label,*names in legs:
        a,b,c=[rig.matrix_world@rig.pose.bones[n].head for n in names]
        d=(c-a).normalized(); bend=b-a-d*(b-a).dot(d)
        # Knee offset components in world axes. Native forward is checked against head.
        row['legs'][label]={'hip':list(a),'knee':list(b),'foot':list(c),'bend':list(bend),'bend_norm':list(bend.normalized()),'flexion':math.degrees((b-a).angle(c-b))}
    rows.append(row)
print('AXES', {n:list(rig.matrix_world@rig.pose.bones[n].head) for n in ['Bip001 Head','Bip001 Pelvis']})
for label,*_ in legs:
    samples=[r['legs'][label] for r in rows[1:]]
    flips=[]
    for i in range(1,len(samples)):
        v=Vector(samples[i-1]['bend_norm']);w=Vector(samples[i]['bend_norm']); angle=math.degrees(v.angle(w))
        if angle>30: flips.append({'frame':rows[i+1]['frame'],'angle':angle,'previous':samples[i-1]['bend_norm'],'now':samples[i]['bend_norm']})
    print(label,'neutral',rows[0]['legs'][label], 'flexion_range', [min(x['flexion'] for x in samples),max(x['flexion'] for x in samples)],'flips',flips)
(out/'rig-cycle.json').write_text(json.dumps(rows,indent=2))
