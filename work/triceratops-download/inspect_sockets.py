import bpy, bmesh
from mathutils import Vector
bpy.ops.wm.open_mainfile(filepath='/Users/victorazevedo/projetos/vale-mesozoico-vr/work/triceratops-download/imported.blend')
body=bpy.data.objects['3jl001']
bm=bmesh.new();bm.from_mesh(body.data)
bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=0.0001)
remaining={v for e in bm.edges if e.is_boundary for v in e.verts}
while remaining:
    seed=remaining.pop();group={seed};stack=[seed]
    while stack:
        vertex=stack.pop()
        for edge in vertex.link_edges:
            other=edge.other_vert(vertex)
            if edge.is_boundary and other in remaining:
                remaining.remove(other);group.add(other);stack.append(other)
    points=[body.matrix_world@v.co for v in group]
    center=sum(points,Vector())/len(points)
    print('SOCKET',len(group),'center',tuple(round(x,4) for x in center),
          'min',tuple(round(min(p[i] for p in points),4) for i in range(3)),
          'max',tuple(round(max(p[i] for p in points),4) for i in range(3)))
