# Vale Mesozoico — cena editável

Abra `ValeMesozoico_Editable.blend` no Blender 5.1 ou superior.

## Estrutura

- `01_TERRAIN`: terreno editável com 129 × 129 vértices.
- `02_WATER_AND_SHORE`: lago e transição de margem.
- `03_TRACK_EDITABLE`: trilhos, marcas das rodas, travessas e suportes.
- `04_CAVE_AND_WATERFALL`: túnel da caverna, cachoeira e rochas principais.
- `05_ROCKS`, `06_VEGETATION`, `07_DINOSAURS`: cenário organizado por tipo.
- `08_RIDE_CART`: carrinho posicionado no início do spline.

As curvas `Rail_Left`, `Rail_Right`, `Central_Spine` e `GUIDE_RideSpline` podem ser alteradas no **Edit Mode**.

Para reconstruir o arquivo a partir dos valores atuais do Unity:

```bash
/Applications/Blender.app/Contents/MacOS/Blender --background \
  --python Tools/Blender/build_editable_scene.py
```

O Blender é a base visual. Física, áudio, XR e eventos da experiência continuam no Unity.

## Passe cinematográfico para PC

O mesmo `ValeMesozoico_Editable.blend` contém o passe de render PC. A câmera
ativa é `Camera_CinematicReference`; as coleções `10_PC_` até `13_PC_` guardam
vegetação, rochas, manadas e atmosfera. As referências ficam anexadas à câmera
somente como guias e não aparecem no render.

Para reconstruir o passe dentro do arquivo aberto, execute no Console Python:

```python
exec(compile(open("/Users/victorazevedo/projetos/vale-mesozoico-vr/Tools/Blender/enhance_pc_cinematic.py").read(), "enhance_pc_cinematic.py", "exec"))
```
