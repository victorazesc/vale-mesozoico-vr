import os
from pathlib import Path

import bpy


ROOT = Path(os.getcwd())
SOURCES = (
    ROOT / "SourceAssets/Sketchfab/CoconutPalm/CoconutTree_4K.glb",
    ROOT / "SourceAssets/PolyHaven/Boulder01/boulder_01_2k.gltf",
    ROOT / "SourceAssets/PolyHaven/Mountainside/mountainside_2k.gltf",
)


def triangle_count(mesh):
    return sum(max(0, len(polygon.vertices) - 2) for polygon in mesh.polygons)


for source in SOURCES:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    print(f"ENV_SOURCE {source.name} meshes={len(meshes)} tris={sum(triangle_count(obj.data) for obj in meshes)}")
    for obj in meshes:
        materials = [slot.material.name if slot.material else "none" for slot in obj.material_slots]
        print(
            f"ENV_MESH name={obj.name!r} tris={triangle_count(obj.data)} "
            f"dimensions={tuple(round(value, 3) for value in obj.dimensions)} materials={materials}"
        )
    for material in bpy.data.materials:
        texture_nodes = []
        if material.use_nodes and material.node_tree:
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and node.image:
                    texture_nodes.append((node.name, node.image.name, node.image.filepath))
        print(f"ENV_MATERIAL name={material.name!r} textures={texture_nodes}")
    for image in bpy.data.images:
        print(
            f"ENV_IMAGE name={image.name!r} size={tuple(image.size)} "
            f"packed={image.packed_file is not None} colorspace={image.colorspace_settings.name!r}"
        )
