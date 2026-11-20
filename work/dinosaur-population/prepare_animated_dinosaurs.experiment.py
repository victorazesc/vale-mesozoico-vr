from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[2]
SOURCE = PROJECT_ROOT / "SourceAssets/Quaternius/AnimatedDinosaurs"
OUTPUT = PROJECT_ROOT / "Assets/_Game/Resources/Models/Dinosaurs"


def export_dinosaur(name: str) -> None:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE / f"{name}.fbx"))
    armatures = [obj for obj in bpy.context.scene.objects if obj.type == "ARMATURE"]
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == "MESH"]
    if len(armatures) != 1 or not meshes or not bpy.data.actions:
        raise RuntimeError(f"Invalid animated dinosaur: {name}")
    # Unity strips a root literally named Armature, including its FBX unit scale.
    armatures[0].name = f"{name}_Rig"
    for material in bpy.data.materials:
        material.diffuse_color = (*material.diffuse_color[:3], 1.0)
        if material.use_nodes:
            for node in material.node_tree.nodes:
                if node.type == "BSDF_PRINCIPLED":
                    node.inputs["Alpha"].default_value = 1.0
    for action in bpy.data.actions:
        action.name = action.name.split("|")[-1]
    bpy.context.scene.render.fps = 24
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.export_scene.fbx(
        filepath=str(OUTPUT / f"{name}.fbx"),
        use_selection=True,
        object_types={"ARMATURE", "MESH", "EMPTY"},
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_armature_deform_only=True,
        bake_anim=True,
        bake_anim_use_all_actions=True,
        bake_anim_use_nla_strips=False,
        bake_anim_force_startend_keying=True,
        bake_anim_simplify_factor=0.25,
        path_mode="RELATIVE",
        embed_textures=False,
        axis_forward="-Z",
        axis_up="Y",
    )
    print(f"DINOSAUR_EXPORT_OK {name} meshes={len(meshes)} actions={len(bpy.data.actions)}")


for dinosaur in ("Apatosaurus", "Triceratops", "Trex"):
    export_dinosaur(dinosaur)
