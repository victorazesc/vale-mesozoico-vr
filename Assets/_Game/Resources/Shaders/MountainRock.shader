Shader "ValeMesozoico/MountainRock"
{
    Properties
    {
        [MainTexture] _BaseMap("Rock Albedo", 2D) = "white" {}
        [Normal] _BumpMap("Rock Normal", 2D) = "bump" {}
        _MossMap("Moss Albedo", 2D) = "white" {}
        [NoScaleOffset] _RockAtlasMap("Original Rock Atlas", 2D) = "white" {}
        _RockDetailStrength("Rock Atlas Detail", Range(0, 1)) = 0.65
        _RockAtlasDesaturation("Rock Atlas Desaturation", Range(0, 1)) = 0.22
        [MainColor] _BaseColor("Rock Tint", Color) = (1, 1, 1, 1)
        _StoneColor("Weathered Stone", Color) = (0.58, 0.60, 0.61, 1)
        _MossColor("Living Moss", Color) = (0.24, 0.32, 0.065, 1)
        _BumpScale("Normal Strength", Range(0, 2)) = 0.45
        _Smoothness("Smoothness", Range(0, 1)) = 0.14
        _WorldScale("Tiles Per Meter", Float) = 0.2
        [HideInInspector] _Cull("Cull", Float) = 2
        [HideInInspector] _Cutoff("Alpha Cutoff", Float) = 0.5
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "Lit"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_MossMap);
            SAMPLER(sampler_MossMap);
            TEXTURE2D(_RockAtlasMap);
            SAMPLER(sampler_RockAtlasMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _StoneColor;
                half4 _MossColor;
                half _BumpScale;
                half _Smoothness;
                half _RockDetailStrength;
                half _RockAtlasDesaturation;
                float _WorldScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                #if defined(_ROCK_ATLAS_DETAIL)
                    float2 uv : TEXCOORD0;
                #else
                    half4 color : COLOR;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half4 color : TEXCOORD2;
                half4 fogAndVertexLight : TEXCOORD3;
                float4 shadowCoord : TEXCOORD4;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 6);
                #if defined(_ROCK_ATLAS_DETAIL)
                    float2 atlasUV : TEXCOORD5;
                #endif
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_ROCK_ATLAS_DETAIL)
                    output.color = half4(1, 1, 1, 0);
                    output.atlasUV = input.uv;
                #else
                    output.color = input.color;
                #endif
                output.fogAndVertexLight = half4(ComputeFogFactor(position.positionCS.z),
                    VertexLighting(position.positionWS, output.normalWS));
                output.shadowCoord = GetShadowCoord(position);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            SurfaceData EvaluateSurface(Varyings input, out half3 surfaceNormal)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half3 geometricNormal = normalize(input.normalWS);
                half3 weights = abs(geometricNormal);
                weights *= weights;
                weights *= weights;
                weights *= weights;
                weights /= max(dot(weights, half3(1, 1, 1)), 0.0001h);
                half3 orientation = half3(geometricNormal.x < 0 ? -1 : 1,
                    geometricNormal.y < 0 ? -1 : 1, geometricNormal.z < 0 ? -1 : 1);
                float3 position = GetAbsolutePositionWS(input.positionWS) * _WorldScale;
                // Each projection has an outward, right-handed tangent frame.
                float2 uvX = float2(-position.z * orientation.x, position.y);
                float2 uvY = float2(position.x, -position.z * orientation.y);
                float2 uvZ = float2(position.x * orientation.z, position.y);

                half3 rock = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvX).rgb * weights.x
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvY).rgb * weights.y
                    + SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uvZ).rgb * weights.z;
                #if defined(_ROCK_ATLAS_DETAIL)
                // The atlas provides broad color variation; world-scale stone
                // supplies the visible fractures instead of magnifying blurry UV islands.
                half detailLuma = dot(rock, half3(0.2126h, 0.7152h, 0.0722h));
                half mineral = saturate(detailLuma * 8.0h);
                half3 detailedStone = min(_StoneColor.rgb * pow(mineral, 1.05h) * 3.0h, 0.90h);
                half3 atlas = SAMPLE_TEXTURE2D(_RockAtlasMap, sampler_RockAtlasMap, input.atlasUV).rgb;
                half atlasLuma = dot(atlas, half3(0.2126h, 0.7152h, 0.0722h));
                atlas = lerp(atlas, half3(atlasLuma, atlasLuma, atlasLuma), saturate(_RockAtlasDesaturation));
                rock = lerp(atlas, detailedStone, saturate(_RockDetailStrength));
                half3 moss = half3(0, 0, 0);
                const half mossBlend = 0.0h;
                #else
                // Retain the existing mineral cracks while matching the lighter
                // exposed stone of the reference, instead of dark basalt soil.
                half mineral = saturate(dot(rock, half3(0.2126h, 0.7152h, 0.0722h)) * 8.0h);
                rock = min(_StoneColor.rgb * pow(mineral, 1.05h) * 3.00h, 0.90h);
                // Fine moss growth remains at a smaller scale than the rock fractures.
                half3 moss = SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvX * 3.5).rgb * weights.x
                    + SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvY * 3.5).rgb * weights.y
                    + SAMPLE_TEXTURE2D(_MossMap, sampler_MossMap, uvZ * 3.5).rgb * weights.z;
                half mossDetail = saturate(dot(moss, half3(0.2126h, 0.7152h, 0.0722h)) * 8.0h);
                moss = _MossColor.rgb * (0.42h + 1.15h * mossDetail);

                // Texture-scale gaps keep growth from looking airbrushed onto
                // the stone; the vertex mask still excludes the tunnel walls.
                half mossBlend = smoothstep(0.28h, 0.70h,
                    saturate(input.color.a + (mossDetail - 0.40h) * 0.55h))
                    * saturate(input.color.a * 8.0h);
                #endif
                half normalStrength = _BumpScale * lerp(1.0h, 0.55h, mossBlend);
                half3 normalX = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvX), normalStrength);
                half3 normalY = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvY), normalStrength);
                half3 normalZ = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvZ), normalStrength);
                // Blend surface gradients, preserving the mesh normal when the map is flat.
                // This avoids projection seams and rounded-out faces around the tunnel.
                half3 gradient = half3(0, normalX.y, -normalX.x * orientation.x)
                        * (weights.x / max(normalX.z, 0.2h))
                    + half3(normalY.x, 0, -normalY.y * orientation.y)
                        * (weights.y / max(normalY.z, 0.2h))
                    + half3(normalZ.x * orientation.z, normalZ.y, 0)
                        * (weights.z / max(normalZ.z, 0.2h));
                gradient -= geometricNormal * dot(gradient, geometricNormal);

                SurfaceData surface = (SurfaceData)0;
                surface.albedo = lerp(rock, moss, mossBlend) * _BaseColor.rgb * input.color.rgb;
                surface.metallic = 0;
                surface.specular = half3(0, 0, 0);
                surface.smoothness = _Smoothness * lerp(1.0h, 0.6h, mossBlend);
                surface.normalTS = half3(0, 0, 1);
                surface.occlusion = 1;
                surface.alpha = 1;
                surfaceNormal = normalize(geometricNormal + gradient);
                return surface;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 surfaceNormal;
                SurfaceData surface = EvaluateSurface(input, surfaceNormal);

                InputData lighting = (InputData)0;
                lighting.positionWS = input.positionWS;
                lighting.normalWS = surfaceNormal;
                lighting.viewDirectionWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    lighting.shadowCoord = input.shadowCoord;
                #else
                    lighting.shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                lighting.fogCoord = InitializeInputDataFog(float4(input.positionWS, 1), input.fogAndVertexLight.x);
                lighting.vertexLighting = input.fogAndVertexLight.yzw;
                lighting.bakedGI = SAMPLE_GI(input.lightmapUV, input.vertexSH, lighting.normalWS);
                lighting.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                lighting.shadowMask = half4(1, 1, 1, 1);

                half4 color = UniversalFragmentPBR(lighting, surface);
                color.rgb = MixFog(color.rgb, lighting.fogCoord);
                return color;
            }

            Varyings MetaVert(Attributes input)
            {
                Varyings output = Vert(input);
                output.positionCS = UnityMetaVertexPosition(input.positionOS.xyz,
                    input.lightmapUV, input.lightmapUV);
                return output;
            }

            half4 MetaFrag(Varyings input) : SV_Target
            {
                half3 normalWS;
                SurfaceData surface = EvaluateSurface(input, normalWS);
                MetaInput meta = (MetaInput)0;
                meta.Albedo = surface.albedo;
                return MetaFragment(meta);
            }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile_local _ _ROCK_ATLAS_DETAIL
            ENDHLSL
        }

        Pass
        {
            Name "Meta"
            Tags { "LightMode" = "Meta" }
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex MetaVert
            #pragma fragment MetaFrag
            #pragma multi_compile_local _ _ROCK_ATLAS_DETAIL
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack Off
}
