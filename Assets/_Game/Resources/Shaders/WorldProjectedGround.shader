Shader "Vale Mesozoico/World Projected Ground"
{
    Properties
    {
        _BaseMap("Ground Albedo", 2D) = "white" {}
        [Normal] _BumpMap("Ground Normal", 2D) = "bump" {}
        _BumpScale("Ground Relief", Range(0, 1)) = 0.32
        _BaseColor("Ground Tint", Color) = (1, 1, 1, 1)
        _TileScale("World Tile Scale", Float) = 0.0714286
        _LagoonLevel("Lagoon Surface Height", Float) = -10000
        _LagoonBounds("Lagoon XZ Bounds (Min X, Min Z, Max X, Max Z)", Vector) = (-1, -1, -1, -1)
        [HideInInspector] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        HLSLINCLUDE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/MetaInput.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _LagoonBounds;
                float _TileScale;
                float _LagoonLevel;
                half _BumpScale;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 lightmapUV : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                DECLARE_LIGHTMAP_OR_SH(lightmapUV, vertexSH, 3);
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output = (Varyings)0;
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(position.positionCS.z);
                OUTPUT_LIGHTMAP_UV(input.lightmapUV, unity_LightmapST, output.lightmapUV);
                OUTPUT_SH(output.normalWS, output.vertexSH);
                return output;
            }

            float GroundNoise(float2 position)
            {
                float2 cell = floor(position);
                float2 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float4 corners = float4(
                    dot(cell, float2(127.1, 311.7)),
                    dot(cell + float2(1, 0), float2(127.1, 311.7)),
                    dot(cell + float2(0, 1), float2(127.1, 311.7)),
                    dot(cell + 1.0, float2(127.1, 311.7)));
                corners = frac(sin(corners) * 43758.5453);
                return lerp(lerp(corners.x, corners.y, blend.x),
                    lerp(corners.z, corners.w, blend.x), blend.y);
            }

            half3 EvaluateAlbedo(Varyings input)
            {
                float2 uv = input.positionWS.xz * _TileScale;
                float2 alternateUv = float2(uv.y, -uv.x) * 0.81 + float2(0.381, 0.727);
                half patch = GroundNoise(input.positionWS.xz * 0.12);
                half blend = smoothstep(0.15h, 0.85h, patch);
                half3 albedo = lerp(
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv).rgb,
                    SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, alternateUv).rgb,
                    blend) * _BaseColor.rgb;
                // Irregular moss areas retain the litter's fine texture while
                // leaving dark, damp soil between the living groundcover.
                float2 mossUv = input.positionWS.xz * 0.16
                    + float2(patch * 1.7, patch * -0.9) + float2(3.4, 8.2);
                half moss = smoothstep(0.20h, 0.59h, GroundNoise(mossUv));
                half3 surfaceTint = lerp(half3(0.58h, 0.63h, 0.58h), half3(0.43h, 1.16h, 0.76h), moss);
                half inLagoon = step(_LagoonBounds.x, input.positionWS.x)
                    * step(_LagoonBounds.y, input.positionWS.z)
                    * step(input.positionWS.x, _LagoonBounds.z)
                    * step(input.positionWS.z, _LagoonBounds.w);
                half submerged = inLagoon * smoothstep(0.0, 0.35, _LagoonLevel - input.positionWS.y);
                // The underwater floor becomes sediment rather than a continuation
                // of the forest's moss; dry terrain retains its approved palette.
                albedo *= lerp(surfaceTint, half3(0.72h, 0.70h, 0.60h), submerged);
                return albedo;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 albedo = EvaluateAlbedo(input);
                half3 normalWS = normalize(input.normalWS);
                normalWS *= normalWS.y < 0.0h ? -1.0h : 1.0h;
                float2 uv = input.positionWS.xz * _TileScale;
                float2 alternateUv = float2(uv.y, -uv.x) * 0.81 + float2(0.381, 0.727);
                half blend = smoothstep(0.15h, 0.85h, GroundNoise(input.positionWS.xz * 0.12));
                half3 bump = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uv), _BumpScale);
                half3 alternateBump = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, alternateUv), _BumpScale);
                // Rotate the second normal back into the common world-XZ frame.
                alternateBump.xy = half2(-alternateBump.y, alternateBump.x);
                bump = normalize(lerp(bump, alternateBump, blend));
                half3 tangentWS = normalize(half3(normalWS.y, -normalWS.x, 0.0001h));
                half3 bitangentWS = normalize(cross(tangentWS, normalWS));
                normalWS = normalize(tangentWS * bump.x + bitangentWS * bump.y + normalWS * bump.z);
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 indirect = SAMPLE_GI(input.lightmapUV, input.vertexSH, normalWS);
                half3 direct = mainLight.color
                    * (0.18h + diffuse * 0.82h)
                    * mainLight.distanceAttenuation
                    * mainLight.shadowAttenuation;
                half3 color = albedo * max(indirect + direct, 0.28h);
                return half4(MixFog(color, input.fogFactor), 1.0h);
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
                MetaInput meta = (MetaInput)0;
                meta.Albedo = EvaluateAlbedo(input);
                return MetaFragment(meta);
            }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
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
            ENDHLSL
        }

        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
