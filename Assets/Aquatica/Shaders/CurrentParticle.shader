// Additive particle for the currents at the edge of the area. _Shape 0: a soft streak (for stretched
// billboards: bright core along the length, tapered ends); 1: a bubble (thin bright rim, faint centre, a glint).
// The shape is computed from the UVs, so no texture is needed. Fades into the fog like everything else.
Shader "Aquatica/CurrentParticle"
{
    Properties
    {
        [HDR] _Color ("Tint", Color) = (0.6, 0.9, 1, 1)
        [Toggle] _Shape ("Bubble (off = streak)", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "CurrentParticle"
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
                float _Shape;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float fogFactor : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                half shape;
                if (_Shape < 0.5)
                {
                    // Streak: soft across, tapered at both ends
                    half across = 1.0 - smoothstep(0.0, 1.0, abs(p.y));
                    half along = 1.0 - smoothstep(0.4, 1.0, abs(p.x));
                    shape = across * across * along;
                }
                else
                {
                    float r = length(p);
                    half rim = smoothstep(0.55, 0.85, r) * (1.0 - smoothstep(0.85, 1.0, r));
                    half glint = 1.0 - smoothstep(0.0, 0.22, length(p - float2(-0.35, 0.35)));
                    shape = rim + glint * 0.8 + 0.08 * (1.0 - smoothstep(0.0, 1.0, r));
                }

                half3 col = _Color.rgb * input.color.rgb * shape * input.color.a;
                col = MixFogColor(col, half3(0, 0, 0), input.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
