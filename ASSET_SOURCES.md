# Origem dos assets

## Imagens geradas

Gerados com a ferramenta integrada de geração de imagens da OpenAI em 2026-08-30. Os PNGs finais foram redimensionados para 1024 x 1024 e preservam transparência.

- `Assets/_Game/Resources/Generated/Trex.png`: T. rex original, corpo inteiro, render 3D realista leve, luz diurna neutra, fundo transparente, sem texto, logo ou referência a franquias.
- `Assets/_Game/Resources/Generated/Sauropod.png`: saurópode original, corpo inteiro, render 3D realista leve, luz diurna neutra, fundo transparente, sem texto, logo ou referência a franquias.

Texturas geradas em modo texto-para-imagem em 2026-08-30 e reduzidas para 512 x 512:

- `Assets/_Game/Resources/Textures/JurassicGround.png`: albedo tileable de musgo, solo úmido, folhas e pequenos seixos; vista ortográfica, iluminação difusa e sem sombras direcionais.
- `Assets/_Game/Resources/Textures/VolcanicRock.png`: albedo tileable de basalto tropical escuro, fissuras, minerais e musgo; vista frontal plana, iluminação difusa e sem sombras direcionais.

Texturas geradas em 2026-08-30 para a revisão realista, reduzidas para 1024 x 1024 e otimizadas em ASTC no Android:

- `Assets/_Game/Resources/Textures/Realistic/MossyBasalt_Albedo.png` e normal derivado.
- `Assets/_Game/Resources/Textures/Realistic/JungleGround_Albedo.png` e normal derivado.
- `Assets/_Game/Resources/Textures/Realistic/JungleGround_Lush_Albedo.png`: variação úmida com musgo, pequenas samambaias, raízes e pedras, usada na execução atual.
- `Assets/_Game/Resources/Textures/Realistic/PrehistoricFoliage_Atlas.png`: atlas RGBA original de samambaias, cicadáceas e folhagem tropical.
- `Assets/_Game/Resources/Textures/Realistic/PalmBark_Albedo.png` e `PalmLeaf_Albedo.png`: materiais originais usados no coqueiro procedural otimizado.

Textura gerada com a ferramenta integrada de imagens da OpenAI em 2026-08-31 para o novo trilho tubular procedural:

- `Assets/_Game/Resources/Textures/Realistic/Track/TrackPaintedSteel_Albedo.png`: aço estrutural pintado em branco-cinza, tileable, com microtextura, riscos finos e desgaste discreto.
- `TrackPaintedSteel_Normal.png` e `TrackPaintedSteel_SpecGloss.png`: mapas derivados localmente para relevo da pintura e resposta PBR.
- Prompt final: `seamless light gray-white painted structural steel, subtle orange-peel coating, fine scratches, sparse tiny chips and restrained grime, neutral scan-like lighting, no baked shadows or seams`.

Os PNGs de dinossauro permanecem apenas como fallback legado; a execução atual usa os modelos 3D abaixo.

## Modelos 3D importados

### Quaternius — Animated Dinosaur Pack

- Fonte oficial: https://quaternius.com/packs/animateddinosaurs.html
- Licença: CC0 1.0; cópia local em `Assets/_Game/ThirdParty/Quaternius/LICENSE.txt`.
- `Assets/_Game/Resources/Models/Dinosaurs/Trex.fbx`
- `Assets/_Game/Resources/Models/Dinosaurs/Apatosaurus.fbx`
- `Assets/_Game/Resources/Models/Dinosaurs/Triceratops.fbx`

### Chistodrako._. — Pteranodon (Animated)

- Fonte oficial: https://sketchfab.com/3d-models/pteranodon-animated-7d7683df41d1405283f160e81a5dff1b
- Autor: Chistodrako._. (`@oscar.lopez.riviello`).
- Licença: CC BY 4.0; cópia local em `Assets/_Game/ThirdParty/Sketchfab/Pteranodon/LICENSE-CC-BY-4.0.txt`.
- Crédito: `Pteranodon (Animated) by Chistodrako._. — Sketchfab — CC BY 4.0.`
- Alterações: versão GLB 1K convertida para FBX, câmera/objeto auxiliar removidos, nomes normalizados, mapas PBR extraídos, roughness convertido para smoothness, texturas configuradas em ASTC 6x6 e integração de voo/culling própria.
- Fonte preservada em `SourceAssets/Sketchfab/Pteranodon/Pteranodon_Animated_1K.glb`.
- Runtime: `Assets/_Game/Resources/Models/Dinosaurs/Pteranodon/Pteranodon.fbx` e quatro mapas PBR 1024 x 1024 na mesma pasta.

### local.yany — Coconut Tree

- Fonte oficial: https://sketchfab.com/3d-models/coconut-tree-2f3162bb723c4fe49af74465eae1629d
- Autor: local.yany.
- Licença: CC BY 4.0; cópia local em `Assets/_Game/ThirdParty/Sketchfab/CoconutPalm/LICENSE-CC-BY-4.0.txt`.
- Crédito: `Coconut Tree by local.yany — Sketchfab — CC BY 4.0.`
- Alterações: GLB 4K convertido para três FBX com LODs de 20.260, 11.198 e 4.598 triângulos; atlas reduzido para 2K no editor e 1K ASTC no Android; materiais de tronco/folhas separados no runtime; animação própria sincronizada ao campo de vento da cena.
- Fonte preservada em `SourceAssets/Sketchfab/CoconutPalm/CoconutTree_4K.glb`.
- Runtime: `Assets/_Game/Resources/Models/EnvironmentHero/CoconutPalm/`.

### Kenney — Nature Kit

- Fonte oficial: https://kenney.nl/assets/nature-kit
- Licença: CC0 1.0; cópia local em `Assets/_Game/ThirdParty/Kenney/LICENSE-NATURE.txt`.
- `Assets/_Game/Resources/Models/Environment/PalmDetailedShort.fbx`
- `Assets/_Game/Resources/Models/Environment/PalmDetailedTall.fbx`
- `Assets/_Game/Resources/Models/Environment/PalmBend.fbx`
- `Assets/_Game/Resources/Models/Environment/RockLargeA.fbx`
- `Assets/_Game/Resources/Models/Environment/RockLargeD.fbx`
- `Assets/_Game/Resources/Models/Environment/RockLargeF.fbx`

### Quaternius — Palm Tree

- Fonte: https://poly.pizza/m/A6cKJYFsIb
- Licença: CC0 1.0; autor Quaternius, com licença local em `Assets/_Game/ThirdParty/Quaternius/LICENSE.txt`.
- O GLB original foi convertido localmente para OBJ, preservando UV e atlas, sem alterar a geometria de 3.134 triângulos.
- `Assets/_Game/Resources/Models/Environment/PalmHero/PalmHero.obj`
- `Assets/_Game/Resources/Models/Environment/PalmHero/PalmHeroAtlas.png`

### Kenney — Coaster Kit

- Fonte oficial: https://kenney.nl/assets/coaster-kit
- Licença: CC0 1.0; cópia local em `Assets/_Game/ThirdParty/Kenney/LICENSE-COASTER.txt`.
- `Assets/_Game/Resources/Models/Ride/CoasterTrainFront.fbx`
- `Assets/_Game/Resources/Models/Ride/CoasterColorMap.png`

### Matt Rafferty — Abandonded Roller Coaster Cart

- Fonte oficial: https://sketchfab.com/3d-models/abandonded-roller-coaster-cart-e557ecfcacb74d148ca25422d7685331
- Autor: Matt Rafferty (`@Matt-AeroLab`).
- Licença: CC BY 4.0; cópia local em `Assets/_Game/ThirdParty/Sketchfab/AbandonedCoasterCart/LICENSE-CC-BY-4.0.txt`.
- Crédito: `Abandonded Roller Coaster Cart by Matt Rafferty — Sketchfab — CC BY 4.0.`
- Alterações: GLB com texturas 1K convertido para FBX; cubo auxiliar removido; três meshes e materiais preservados; mapas ORM separados em oclusão e metallic/smoothness para URP; compressão de mesh e ASTC 6x6 aplicadas para Quest.
- Fonte preservada em `SourceAssets/Sketchfab/AbandonedCoasterCart/AbandonedCoasterCart_1K.glb`.
- Runtime: `Assets/_Game/Resources/Models/Ride/AbandonedCart/`; o carrinho Kenney permanece como fallback.

### Poly Haven — rochas e vegetação

- Fonte oficial: https://polyhaven.com/
- Licença: CC0; referência local em `Assets/_Game/ThirdParty/PolyHaven/LICENSE.txt`.
- `Rock Face 01`: https://polyhaven.com/a/rock_face_01
- `Rock Moss Set 01`: https://polyhaven.com/a/rock_moss_set_01
- `Fern 02`: https://polyhaven.com/a/fern_02
- `Boulder 01`: https://polyhaven.com/a/boulder_01
- `Mountainside`: https://polyhaven.com/a/mountainside
- `Anthurium Botany 01`: https://polyhaven.com/a/anthurium_botany_01
- As variantes `*_Quest.fbx` de Rock Face, Rock Moss Set e Fern foram decimadas localmente no Blender, preservando UV e materiais. Boulder 01 e Mountainside foram preparados em três LODs com albedo, normal, oclusão e specular/smoothness PBR 2K no editor e 1K ASTC no Android. Os FBX integrais e o Anthurium foram arquivados fora de `Resources`, em `Assets/_Game/ThirdParty/PolyHaven/SourceModels`, para não inflar o APK. A transparência da vegetação foi consolidada nos mapas `*_Cutout.png`.

Os elementos não listados acima continuam sendo gerados proceduralmente pelo projeto.

## Áudio da corrente da montanha-russa

- Fonte: https://pixabay.com/sound-effects/film-special-effects-rollercoaster-ratchet-33962/
- Título: `Rollercoaster ratchet`.
- Autor original: esperar (distribuído por `freesound_community`).
- Licença: Pixabay Content License; referência local em `Assets/_Game/ThirdParty/Pixabay/RollercoasterRatchet_33962/SOURCE.txt`.
- Alterações: áudio mono reprocessado com tom mais grave, equalização, compressão e divisão em início (1–10 s), loop sustentado (10–16 s) e desengate final (16–19 s).
- Fonte preservada em `SourceAssets/Audio/Pixabay/RollercoasterRatchet_33962/original.mp3`.
- Runtime: `Assets/_Game/Resources/Audio/LiftChainStart.wav`, `LiftChainLoop.wav` e `LiftChainEnd.wav`.

## Áudio de rolamento do carrinho

- Fonte solicitada: https://www.youtube.com/watch?v=iTGoYPTXLFU
- Vídeo: `PSVR2 - Epic Roller Coasters - T-Rex Kingdom`.
- Recorte: 3:43–3:45, usado somente no som de rolamento do carrinho.
- Alterações: mono 48 kHz, redução leve de ruído, equalização, compressão e loop de 2 s com crossfade e bordas anticlique.
- Direitos de redistribuição do áudio não verificados; revisar antes de uma publicação comercial.
- Runtime: `Assets/_Game/Resources/Audio/Enhanced Wheel Rail Roll.wav`.
