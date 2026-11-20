# Rusted track steel

- Created: 2026-09-06, built-in imagegen tool.
- Runtime assets: `Assets/_Game/Resources/Textures/Realistic/Track/TrackRustedSteel_{Albedo,Normal}.png`.
- The normal source is the same generated surface image. Unity's existing height-to-normal importer converts it at import time, with height scale 0.035; it is not a precomputed tangent-space normal image.
- Import: repeating texture, trilinear filtering, mipmaps, Android ASTC 6x6 at 1024 pixels.
- Consumers: running rails and central spine/crossmembers in `ProceduralWorld.CreateMaterials`.

## Generation prompt

Use case: photorealistic-natural. Asset type: seamless tileable 1024x1024 RGB PBR base-color texture for old tubular steel roller-coaster rails in a tropical jungle, physically plausible close-up rusted steel material. Create ONLY a square flat orthographic surface texture covering the entire image, ready to wrap onto small steel tubes. Muted natural reddish brown and umber iron oxide, irregular burnt sienna rust mottling, charcoal brown embedded pitting and small sparse desaturated gray bare-metal flecks from abrasion. Mostly rich medium rusty brown, not too dark. Very fine granular micro-corrosion and subtle flaking, multiscale irregular patches without large unique recognizable islands. Real photographic material detail, restrained saturation, aged rather than painted orange. Lighting: flat diffuse delighting, no highlights, no directional shadows, no ambient occlusion baked in, no vignette, even exposure edge to edge. Seamless in BOTH axes with matching opposite edges. No objects, no actual rails, no perspective, no seams, no panels, no bolts, no cracks wider than pin scratches, no text, no symbols, no watermark. Output the single albedo tile only.
