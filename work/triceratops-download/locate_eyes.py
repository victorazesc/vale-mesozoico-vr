import bpy,numpy as np
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/triceratops-download/imported.blend')
body=bpy.data.objects['3jl001'];mesh=body.data
image=bpy.data.images['三角龙_b'];pixels=np.empty(len(image.pixels),dtype=np.float32);image.pixels.foreach_get(pixels)
pixels=pixels.reshape((image.size[1],image.size[0],4))
faces=[]
for face in mesh.polygons:
    p=body.matrix_world@face.center
    if p.x < 0.20 or not (-2.85<p.y<-1.65 and 1.2<p.z<2.5): continue
    uv=sum((mesh.uv_layers.active.data[i].uv for i in face.loop_indices),Vector((0,0)))/len(face.loop_indices)
    color=pixels[int(uv.y*(image.size[1]-1))%image.size[1],int(uv.x*(image.size[0]-1))%image.size[0],:3]
    faces.append((float(sum(color)),face.index,tuple(round(v,4) for v in p)))
for item in sorted(faces)[:25]: print('EYE_FACE',item)
eye=bpy.data.objects['Object003'].data
for sign in [-1,1]:
    vs=[v.co for v in eye.vertices if v.co.z*sign>0]
    center=sum(vs,Vector())/len(vs)
    colored=[]
    for face in eye.polygons:
        if face.center.z*sign<0:continue
        uv=sum((eye.uv_layers.active.data[i].uv for i in face.loop_indices),Vector((0,0)))/len(face.loop_indices)
        color=pixels[int(uv.y*(image.size[1]-1))%image.size[1],int(uv.x*(image.size[0]-1))%image.size[0],:3]
        colored.append((float(color[0]-color[1]),face.center.copy()))
    iris=sum((p for _,p in sorted(colored,key=lambda x:x[0],reverse=True)[:12]),Vector())/12
    print('IRIS',sign,'center',tuple(center),'direction',tuple((iris-center).normalized()))
