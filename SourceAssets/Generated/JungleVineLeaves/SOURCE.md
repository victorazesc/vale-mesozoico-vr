# Jungle Vine Leaves — atlas gerado

- Origem: ferramenta integrada de geração de imagens da OpenAI (`image_gen`).
- Data: 2026-09-05.
- Natureza: imagem gerada por IA para uso como albedo de folhas de vinha tropical.
- Destino runtime: `Assets/_Game/Resources/Textures/Realistic/JungleVineLeaves_Atlas.png`.
- Composição: atlas 2 × 2 com quatro folhas individuais e fundo transparente para recorte por alpha.
- Importação: `QuestAssetPostprocessor` reconhece o sufixo `_Atlas` e configura `alphaIsTransparency = true`, mipmaps, streaming de mipmaps, wrap Clamp e filtro Trilinear. No Android/Quest, aplica compressão ASTC 6x6 e tamanho máximo de 1024 pixels.

## Prompt exato

```text
Use case: photorealistic-natural. Asset type: square 2x2 leaf texture atlas for a realistic Unity VR tropical jungle vine, diffuse/albedo with transparent alpha. Generate exactly FOUR separate individual natural creeping vine leaves, one fully inside each equal quadrant, no overlap. Each leaf has its stem base pointing straight down, pointed tip up, broad heart-shaped base like wild pothos/philodendron or jungle ivy; mature green, subtle green variation, small imperfections, intricate realistic pale veins, slightly waxy organic surface. All leaves photographed orthogonally from the front, flattened enough for texture mapping, full leaf and a SHORT attached petiole visible, no branches. Occupy approximately 80% width and 84% height of each quadrant, generous transparent margins, centers precisely on the quarter-grid. Top-left medium forest green heart leaf; top-right smaller brighter new green heart leaf; bottom-left dark green broad heart leaf; bottom-right medium green narrower heart leaf with one tiny worn edge. Neutral diffuse ambient daylight, NO cast shadows, NO dark backdrop, NO white matte, NO labels, NO borders, NO text, NO grid lines, NO entire plants. Truly transparent background with clean alpha edges around all four leaves. Photographic botanical texture quality, not illustration, not cartoon. Square image 1024x1024.
```
