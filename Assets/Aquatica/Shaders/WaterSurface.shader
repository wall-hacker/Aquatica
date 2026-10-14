// The water surface seen from below. Rays that hit it steeper than the critical angle (~48.6 deg from vertical)
// refract out into the bright sky ("Snell's window", with the sun glinting through); shallower rays are
// totally internally reflected and show the dark water. Animated waves and ripples bend the normals so
// the window's edge shimmers.
Shader "Aquatica/WaterSurface"
{
    Properties
    {
        _WindowColor ("Sky Through Window", Color) = (0.55, 0.85, 0.95, 1)
        _ReflectionColor ("Internal Reflection", Color) = (0.03, 0.13, 0.18, 1)
        [HDR] _SunColor ("Sun Glint", Color) = (1.6, 1.5, 1.25, 1)
        _WaveHeight ("Wave Height (m)", Float) = 0.25
        _RippleSize ("Ripple Size (m)", Float) = 2.5
        _WaveSpeed ("Wave Speed", Float) = 0.7
        _RippleStrength ("Ripple Strength", Range(0, 3)) = 1.2
        _WindowSoftness ("Window Edge Softness", Range(0.005, 0.2)) = 0.05
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+50" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "WaterSurfaceForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _WindowColor;
                half4 _ReflectionColor;
                half4 _SunColor;
                float _WaveHeight;
                float _RippleSize;
                float _WaveSpeed;
                float _RippleStrength;
                float _WindowSoftness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            // Long swell: a few directional waves
            float Swell(float2 p, float t)
            {
                return sin(dot(p, float2(0.21, 0.13)) + t * 1.1) * 0.5
                     + sin(dot(p, float2(-0.11, 0.27)) + t * 0.8) * 0.3
                     + sin(dot(p, float2(0.33, -0.19)) + t * 1.6) * 0.2;
            }

            float2 Hash2(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453) * 2.0 - 1.0;
            }

            float GradientNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(dot(Hash2(i), f), dot(Hash2(i + float2(1, 0)), f - float2(1, 0)), u.x),
                            lerp(dot(Hash2(i + float2(0, 1)), f - float2(0, 1)), dot(Hash2(i + float2(1, 1)), f - float2(1, 1)), u.x), u.y);
            }

            // Small ripples: two noise layers drifting in different directions
            float Ripples(float2 p, float t)
            {
                float2 q = p / _RippleSize;
                return GradientNoise(q + float2(t * 0.35, t * 0.2)) * 0.6
                     + GradientNoise(q * 2.3 - float2(t * 0.25, -t * 0.4)) * 0.3;
            }

            float SurfaceHeight(float2 p, float t)
            {
                return Swell(p, t) * _WaveHeight + Ripples(p, t) * _WaveHeight * 0.6;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                positionWS.y += Swell(positionWS.xz, _Time.y * _WaveSpeed) * _WaveHeight;
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y * _WaveSpeed;
                float2 p = input.positionWS.xz;
                const float e = 0.08;
                float hx = (SurfaceHeight(p + float2(e, 0), t) - SurfaceHeight(p - float2(e, 0), t)) / (2.0 * e);
                float hz = (SurfaceHeight(p + float2(0, e), t) - SurfaceHeight(p - float2(0, e), t)) / (2.0 * e);
                float3 normal = normalize(float3(-hx * _RippleStrength, 1.0, -hz * _RippleStrength)); // facing up, out of the water

                float3 ray = normalize(input.positionWS - _WorldSpaceCameraPos); // viewer is below, looking up
                float cosIncidence = dot(ray, normal);
                const float cosCritical = 0.6615; // sin(critical) = 1 / 1.333
                float window = smoothstep(cosCritical - _WindowSoftness, cosCritical + _WindowSoftness, cosIncidence);

                // Inside the window: the sky, brightest straight up, with the sun glinting through
                float3 refracted = refract(ray, -normal, 1.333);
                Light sun = GetMainLight();
                float sunDot = saturate(dot(refracted, sun.direction));
                half3 sky = _WindowColor.rgb * (0.55 + 0.45 * cosIncidence)
                          + _SunColor.rgb * (pow(sunDot, 400.0) * 3.0 + pow(sunDot, 24.0) * 0.35);

                // Outside: total internal reflection of the water below, lightly broken up by the ripples
                half3 reflection = _ReflectionColor.rgb * (0.8 + 0.4 * saturate(0.5 + (hx + hz) * _RippleStrength));

                // The window's rim catches the light
                half rim = exp(-abs(cosIncidence - cosCritical) * 60.0) * 0.35;

                half3 color = lerp(reflection, sky, window) + _WindowColor.rgb * rim;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
