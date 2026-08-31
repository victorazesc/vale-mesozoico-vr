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
