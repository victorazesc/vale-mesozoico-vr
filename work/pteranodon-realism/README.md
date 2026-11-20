# Pteranodon — variante gratuita aprovada

Visual aprovado pelo usuário em 2026-09-06 e integrado ao encontro do pico na cena Unity.

Variante da base de Chistodrako._., com pele cinza-amarronzada, olhos menores e crista inclinada para trás. O exemplar do encontro usa 14 m de envergadura e permanece separado dos animais de fundo.

## Fonte e crédito

[Pteranodon (Animated)](https://sketchfab.com/3d-models/pteranodon-animated-7d7683df41d1405283f160e81a5dff1b), por [Chistodrako._.](https://sketchfab.com/oscar.lopez.riviello), sob [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/).

Download original autorizado pelo botão oficial do Sketchfab em 2026-09-06. Arquivo preservado em `SourceAssets/Sketchfab/Pteranodon/Original/`. Texturas originais de 2048 × 2048; animações verificadas no GLB: flying, walking e standing. O modelo possui controles de olhos, pálpebras, mandíbula, pernas e dedos.

Textura de cor retrabalhada com a ferramenta integrada imagegen; prompt completo em `texture-prompt.txt`. O albedo gerado tem 1254 × 1254 pixels, importado pelo Unity em 2K; o mapa normal original tem 2048 × 2048. Malha e esqueleto preservam 13.494 triângulos e 124 ossos. A animação de voo vem do modelo gratuito; pegada, virada, abertura das garras e soltura foram criadas no projeto.

## Arquivos para revisão

- `04-refined-head.png`: detalhe da cabeça com as alterações de malha.
- `05-refined-full.png`: corpo completo.
- `Pteranodon_Realism_Refined.blend`: cena editável da prévia.
- `Pteranodon_Realistic_BaseColor.png`: fonte do albedo aplicado ao exemplar do pico.
- `inspect_original.py`, `render_preview.py`, `refine_shape.py`: reprodução da importação e das prévias, nesta ordem.
- `../../Tools/Blender/export_pteranodon_crest.py`: exporta a variante aprovada para `Assets/_Game/Resources/Models/Dinosaurs/PteranodonCrest/` e preserva o Blender em `SourceAssets/Generated/PteranodonCrest/`.

## Estado da sequência

A cena usa `PteranodonDropSequence.cs` e `PteranodonGrabRig.cs`: frenagem progressiva, duas voltas, grito, aproximação frontal, pegada medida na malha do carrinho, voo de costas sobre o lago, subida, ameaça com uma garra e virada de 180°, retorno alinhado ao trilho e soltura. A batida de asas é elevada durante a virada para liberar o espaço do passageiro. Toda a sequência acompanha o relógio da ride, incluindo pausa e repetição após seek.

Validação em Play Mode pelo menu `Vale Mesozoico > Preview > Cave Drop Encounter` (Cmd+Alt+G). Relatório e capturas: `Builds/Previews/CaveDrop/`. Os testes verificam frenagem, contato da pele/garras, espaço da cabeça, amostras do percurso contra a geometria do ambiente, sobrevoo da água, alinhamento da devolução, queda, recuperação após inversões, pausa, seek e replay. A checagem das asas usa segmentos do esqueleto com margem. Nenhum teste de headset/APK nesta revisão.

Resultado de 2026-09-06: **PASS**. Desaceleração máxima de 0,491 m/s², deriva na espera de 0 m, erro máximo da pegada de 5 mm e contato da pele a até 1 mm do ponto da garra. Distância mínima da cabeça à pele: 0,41 m. Passagem baixa a 6,56 m da altura da água, 3.519 amostras de voo contra 157 obstáculos e retorno ao trilho com erro de posição/rotação zero. Entrada na caverna a 93,6 km/h; pausa, seek e replay completos passaram.
