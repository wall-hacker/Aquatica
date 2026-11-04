// A star-shaped glint that flashes now and then (salvage crates): a camera-facing quad (built from a unit quad in
// the vertex shader) that grows with distance so it stays visible. Irregular: each period has only a chance of a
// flash, at a random moment and strength. Additive; shows through the fog a little and fades out beyond _MaxDistance.
Shader "Aquatica/Sparkle"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.55, 0.75, 0.72, 1)
        _Size ("Size (m)", Float) = 0.6
        _Grow ("Extra Size Per Metre Away", Float) = 0.03
        _Period ("Flash Period (s)", Float) = 3.2
        _Chance ("Flash Chance Per Period", Range(0, 1)) = 0.4
        _Flash ("Flash Length (fraction of the period)", Range(0.02, 0.5)) = 0.14
        _Pierce ("Through Fog", Range(0, 1)) = 0.35
        _MaxDistance ("Max Distance (m)", Float) = 55
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "Sparkle"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Size, _Grow, _Period, _Chance, _Flash, _Pierce, _MaxDistance;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 p : TEXCOORD0;
                float strength : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 centre = TransformObjectToWorld(float3(0, 0, 0));
                float distance = length(centre - _WorldSpaceCameraPos);
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float size = _Size * (1.0 + distance * _Grow);
                float3 positionWS = centre + (right * input.positionOS.x + up * input.positionOS.y) * size * 2.0;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.p = input.positionOS.xy * 2.0;

                float phase = frac(sin(dot(centre.xz, float2(12.9898, 78.233))) * 43758.5453);
                float cycles = _Time.y / _Period + phase;
                float roll = frac(sin(floor(cycles) * 17.31 + phase * 91.7) * 43758.5453);
                float start = frac(roll * 7.13) * (1.0 - _Flash);
                float cycle = frac(cycles) - start;
                float flash = smoothstep(0.0, _Flash * 0.3, cycle) * (1.0 - smoothstep(_Flash * 0.3, _Flash, cycle))
                            * step(roll, _Chance) * (0.4 + 0.6 * frac(roll * 13.7));
                float fog = ComputeFogIntensity(ComputeFogFactor(output.positionCS.z));
                float reach = 1.0 - smoothstep(_MaxDistance * 0.75, _MaxDistance, distance);
                output.strength = flash * lerp(fog, 1.0, _Pierce) * reach;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.p;
                float core = exp(-dot(p, p) * 18.0);
                float rays = exp(-abs(p.y) * 40.0) * exp(-abs(p.x) * 3.0) + exp(-abs(p.x) * 40.0) * exp(-abs(p.y) * 3.0);
                float2 d = float2(p.x + p.y, p.x - p.y) * 0.7071;
                rays += 0.4 * (exp(-abs(d.y) * 50.0) * exp(-abs(d.x) * 6.0) + exp(-abs(d.x) * 50.0) * exp(-abs(d.y) * 6.0));
                return half4(_Color.rgb * (core + rays) * input.strength, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
