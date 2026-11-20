from pathlib import Path

root = Path(__file__).resolve().parents[2]
s = (root / 'Tools/Blender/import_downloaded_triceratops.py').read_text()
s = s.replace("'Bip001 L Foot', 0.75", "'Bip001 L Foot', 0.25")
s = s.replace("'Bip001 R Foot', 0.25", "'Bip001 R Foot', 0.75")
s = s.replace("\n\ndef save_textures():", "\n# Runtime contact phases use this same three-feet-supported walk.\nSTANCE_FRACTION = 0.75\n\n\ndef save_textures():")
s = s.replace('''        rig.location = base_location + Vector((0, 0, -0.08 + 0.022 * cos(4 * pi * frame / frames)))
        bpy.context.view_layer.update()''', '''        cycle = 2 * pi * frame / frames
        rig.location = base_location + Vector((0.012 * sin(cycle), 0, -0.05 + 0.012 * cos(2 * cycle)))
        bpy.context.view_layer.update()
        # Small, delayed weight shifts keep the heavy torso stable while the head and tail follow.
        rotate_about_head(rig, rig.pose.bones['Bip001 Pelvis'],
                          Quaternion(Vector((0, 1, 0)), 0.006 * sin(cycle)))
        rotate_about_head(rig, rig.pose.bones['Bip001 Neck'],
                          Quaternion(Vector((1, 0, 0)), 0.012 * sin(2 * cycle - 0.45)))
        rotate_about_head(rig, rig.pose.bones['Bip001 Head'],
                          Quaternion(Vector((0, 0, 1)), 0.007 * sin(cycle - 0.6)))
        for index in range(5):
            tail = 'Bip001 Tail' + (str(index) if index else '')
            rotate_about_head(rig, rig.pose.bones[tail],
                              Quaternion(Vector((0, 0, 1)), (0.012 + index * 0.004) * sin(cycle - index * 0.45)))''')
s = s.replace('''            if phase < 0.65:
                travel, lift = -0.42 + 0.84 * phase / 0.65, 0.0
            else:
                swing = (phase - 0.65) / 0.35
                travel = 0.42 * cos(pi * swing)
                lift = 0.18 * sin(pi * swing) ** 2
            target = location + Vector((0, travel, lift))
            solve_leg(rig, names, target, rotation, scale)''', '''            foot_rotation = rotation
            if phase < STANCE_FRACTION:
                travel, lift = -0.42 + 0.84 * phase / STANCE_FRACTION, 0.0
            else:
                swing = (phase - STANCE_FRACTION) / (1 - STANCE_FRACTION)
                # Hermite tangents match support velocity at both ends of the loop.
                tangent = 0.84 * (1 - STANCE_FRACTION) / STANCE_FRACTION
                travel = (0.42 * (2 * swing**3 - 3 * swing**2 + 1)
                          - 0.42 * (-2 * swing**3 + 3 * swing**2)
                          + tangent * (2 * swing**3 - 3 * swing**2 + swing))
                lift = 0.14 * sin(pi * swing) ** 2
                foot_rotation = Quaternion(Vector((1, 0, 0)),
                                           0.12 * sin(2 * pi * swing) * sin(pi * swing)) @ rotation
            target = location + Vector((0, travel, lift))
            solve_leg(rig, names, target, foot_rotation, scale)''')
(root / 'work/triceratops-walk/import_downloaded_triceratops.py').write_text(s)
