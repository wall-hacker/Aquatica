// Procedural water ripples on a quad: concentric rings emanating from a single central source,
// shaded deep -> mid -> shallow blue, clipped to a soft-edged disc.
// _Swirl > 0 twists the rings into a spiral (used for the water gate at the end of the portal corridor).
Shader "Aquatica/WaterRipple"
{
    Properties
    {
        _DeepColor ("Deep Color", Color) = (0.00, 0.04, 0.22, 1)
        _MidColor ("Mid Color", Color) = (0.00, 0.28, 0.80, 1)
        _ShallowColor ("Shallow Color", Color) = (0.45, 0.88, 1.00, 1)
        _RippleCount ("Ripple Count", Float) = 4
        _RippleSpeed ("Ripple Speed", Float) = 0.6
        _RippleSharpness ("Ripple Sharpness", Range(1, 16)) = 6
        _Wobble ("Wobble", Range(0, 0.1)) = 0.012
        [IntRange] _Swirl ("Swirl (whole turns, keeps the spiral seamless)", Range(0, 8)) = 0
        _SourceGlow ("Source Glow", Range(0, 2)) = 0.8
        _EdgeSoftness ("Edge Softness", Range(0.01, 1)) = 0.35
        _Opacity ("Opacity", Range(0, 1)) = 1
        _Fade ("Fade", Range(0, 1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        // Defaults (Always) ignore the stencil; portal-interior instances use Equal 1
        [IntRange] _StencilRef ("Stencil Ref", Range(0, 255)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _StencilComp ("Stencil Comp", Float) = 8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

        Pass
        {
            Name "WaterRipple"
            Tags { "LightMode" = "UniversalForward" }

            Stencil
            {
                Ref [_StencilRef]
                Comp [_StencilComp]
                Pass Keep
            }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DeepColor;
                half4 _MidColor;
                half4 _ShallowColor;
                float _RippleCount;
                float _RippleSpeed;
                float _RippleSharpness;
                float _Wobble;
                float _Swirl;
                float _SourceGlow;
                float _EdgeSoftness;
                float _Opacity;
                float _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 p = input.uv * 2.0 - 1.0;
                float r = length(p);
                float angle = atan2(p.y, p.x);
                float t = _Time.y;

                // Organic wobble so the rings don't look perfectly mechanical
                float wobble = _Wobble * (sin(angle * 5.0 + t * 1.7) + 0.5 * sin(angle * 9.0 - t * 2.3));
                float rr = r + wobble * r;

                // Rings travel outward from the centre; swirl adds an angular offset
                float phase = frac(rr * _RippleCount - t * _RippleSpeed + round(_Swirl) * angle / TWO_PI);
                float crest = pow(1.0 - abs(phase * 2.0 - 1.0), _RippleSharpness);
                crest *= saturate(1.2 - rr);

                // Base gradient: light at the source, mid blue, deep blue towards the rim
                half3 col = lerp(_ShallowColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.45, rr));
                col = lerp(col, _DeepColor.rgb, smoothstep(0.4, 1.0, rr));
                col += _ShallowColor.rgb * crest * 0.9;
                col += _ShallowColor.rgb * _SourceGlow * exp(-rr * rr * 30.0);

                float edge = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, r);
                float alpha = (0.65 + 0.35 * crest) * edge * _Opacity * _Fade;
                return half4(col, saturate(alpha));
            }
            ENDHLSL
        }
    }
}
