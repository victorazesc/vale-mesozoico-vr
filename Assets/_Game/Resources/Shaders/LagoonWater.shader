Shader "Vale Mesozoico/Lagoon Water"
{
    Properties
    {
        _BumpMap("Linear RGB Ripple Normal", 2D) = "bump" {}
        _NormalStrength("Ripple Strength", Range(0, 2)) = 0.8
        _RippleScale("Ripple World Scale", Float) = 0.065
        _FlowSpeed("Ripple Flow Speed", Float) = 1
        _ShallowColor("Shallow Jade", Color) = (0.13, 0.49, 0.40, 1)
        _DeepColor("Deep Water", Color) = (0.07, 0.32, 0.36, 1)
        _Absorption("Depth Absorption", Float) = 0.62
        _AlphaMax("Deep Opacity", Range(0, 1)) = 0.87
        _EdgeFade("Shore Fade Metres", Float) = 0.25
        _DepthFallback("Sky Depth Metres", Float) = 6
        _ReflectionStrength("Reflection Strength", Range(0, 1)) = 0.52
        _ReflectionRoughness("Reflection Roughness", Range(0, 1)) = 0.36
        _SunStrength("Sun Highlight Strength", Range(0, 1)) = 0.17
        _WaveAmplitude("Wave Height Metres", Float) = 0.018
        _WaveLength("Wave Length Metres", Float) = 7.5
        _WaveSpeed("Wave Speed", Float) = 0.65
        _WaveTime("Wave Time Override (-1 = Live)", Float) = -1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "LagoonForward"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP

            #define _SURFACE_TYPE_TRANSPARENT 1
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor;
                half4 _DeepColor;
                float _RippleScale, _FlowSpeed, _Absorption, _EdgeFade, _DepthFallback;
                float _WaveAmplitude, _WaveLength, _WaveSpeed, _WaveTime;
                half _NormalStrength, _AlphaMax, _ReflectionStrength, _ReflectionRoughness, _SunStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 shoreData : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fogFactor : TEXCOORD2;
                float4 rippleUv : TEXCOORD3;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float WaterTime() { return _WaveTime < 0.0 ? _Time.y : _WaveTime; }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                // The lake source is horizontal in world space, including mirrored imports.
                half3 normalWS = half3(0.0h, 1.0h, 0.0h);
                float frequency = TWO_PI / max(_WaveLength, 0.5);
                float time = WaterTime() * _WaveSpeed;
                float2 directionA = float2(0.8, 0.6);
                float2 directionB = float2(0.267, 0.964);
                float2 directionC = float2(-0.94, 0.342);
                float phaseA = dot(positionWS.xz, directionA) * frequency + time;
                float phaseB = dot(positionWS.xz, directionB) * frequency * 1.63 - time * 0.83 + 1.7;
                float phaseC = dot(positionWS.xz, directionC) * frequency * 0.71 + time * 0.57 + 3.1;
                float amplitude = _WaveAmplitude * saturate(input.shoreData.x);
                positionWS.y += amplitude * (sin(phaseA) * 0.45 + sin(phaseB) * 0.35 + sin(phaseC) * 0.2);
                float2 slope = amplitude * frequency * (cos(phaseA) * directionA * 0.45
                    + cos(phaseB) * directionB * (0.35 * 1.63)
                    + cos(phaseC) * directionC * (0.2 * 0.71));
                output.normalWS = normalize(normalWS + half3(-slope.x, 0.0h, -slope.y));
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                // These projections are affine in XZ: interpolation is equivalent
                // to repeating both rotations and the flow calculation per pixel.
                float flowTime = WaterTime() * _FlowSpeed;
                float2 position = positionWS.xz;
                output.rippleUv.xy = float2(dot(position, float2(0.932, 0.362)),
                    dot(position, float2(-0.362, 0.932))) * _RippleScale + flowTime * float2(0.013, 0.008);
                output.rippleUv.zw = float2(dot(position, float2(0.454, -0.891)),
                    dot(position, float2(0.891, 0.454))) * (_RippleScale * 0.713)
                    + flowTime * float2(-0.009, 0.011);
                return output;
            }

            float WaterDepth(float2 screenUv, float waterHeight)
            {
                float rawDepth = SampleSceneDepth(screenUv);
                #if UNITY_REVERSED_Z
                    if (rawDepth < 0.00001) return max(_DepthFallback, 0.0);
                    float deviceDepth = rawDepth;
                #else
                    if (rawDepth > 0.99999) return max(_DepthFallback, 0.0);
                    float deviceDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 bottomWS = ComputeWorldSpacePosition(screenUv, deviceDepth, UNITY_MATRIX_I_VP);
                return max(waterHeight - bottomWS.y, 0.0);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // The shared procedural normal is linear RGB, not an imported
                // DXT/AG normal. Different rotations, scales and a mild flow warp
                // keep the two ripple fields from forming a repeated crosshatch.
                half3 rippleA = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.rippleUv.xy).rgb * 2.0h - 1.0h;
                float2 uvB = input.rippleUv.zw + rippleA.xy * 0.12;
                half3 rippleB = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, uvB).rgb * 2.0h - 1.0h;
                half2 worldA = half2(rippleA.x * 0.932h - rippleA.y * 0.362h,
                    rippleA.x * 0.362h + rippleA.y * 0.932h);
                half2 worldB = half2(rippleB.x * 0.454h + rippleB.y * 0.891h,
                    -rippleB.x * 0.891h + rippleB.y * 0.454h);
                half2 ripple = (worldA + worldB * 0.65h) * _NormalStrength;
                half3 normalWS = normalize(input.normalWS + half3(ripple.x, 0.0h, ripple.y));
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
                float depth = WaterDepth(screenUv, input.positionWS.y);
                half absorption = 1.0h - exp(-depth * max(_Absorption, 0.001));
                half edge = smoothstep(0.0h, max(_EdgeFade, 0.001), depth);
                half alpha = saturate(absorption * _AlphaMax) * edge;
                half3 waterTint = lerp(_ShallowColor.rgb, _DeepColor.rgb, absorption);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 ambient = max(SampleSH(normalWS), half3(0.16h, 0.16h, 0.16h));
                half3 color = waterTint * (ambient + mainLight.color * diffuse * 0.55h
                    * mainLight.distanceAttenuation * mainLight.shadowAttenuation);
                half fresnel = 0.02h + 0.98h * pow(1.0h - saturate(dot(normalWS, viewWS)), 5.0h);
                half3 reflection = GlossyEnvironmentReflection(reflect(-viewWS, normalWS), input.positionWS,
                    _ReflectionRoughness, 1.0h, screenUv);
                half reflectionWeight = _ReflectionStrength * lerp(0.35h, 1.0h, fresnel);
                color = lerp(color, reflection, reflectionWeight);
                half3 halfway = SafeNormalize(mainLight.direction + viewWS);
                half sun = pow(saturate(dot(normalWS, halfway)), 48.0h) * _SunStrength;
                color += mainLight.color * sun * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                return half4(MixFog(color, input.fogFactor), alpha);
            }
            ENDHLSL
        }
    }
}
