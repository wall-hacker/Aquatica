// Monochrome text display in the same style as the radar: each texel of the tiny _MainTex bitmap is drawn as a
// glowing phosphor cell with a faint halo, over scanlines, a slow rolling band and a dark vignette.
// _Glitch (0-1) tears and flashes the image when the page changes; _Power fades the whole screen in and out.
Shader "Aquatica/CockpitScreen"
{
    Properties
    {
        [NoScaleOffset] _MainTex ("Display (R)", 2D) = "black" {}
        [HDR] _GlowColor ("Glow Color", Color) = (0.3, 1.4, 0.55, 1)
        _BackColor ("Background", Color) = (0.005, 0.04, 0.025, 1)
        _Halo ("Halo", Range(0, 1)) = 0.35
        _Glitch ("Glitch", Range(0, 1)) = 0
        _Power ("Power", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CockpitScreen"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(s_point_clamp_sampler);
            SAMPLER(s_linear_clamp_sampler);
            float4 _MainTex_TexelSize;

            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
                half4 _BackColor;
                half _Halo;
                float _Glitch;
                float _Power;
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

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            half4 frag(Varyings input) : SV_Target
            {
                float t = _Time.y;
                float2 uv = input.uv;

                // Glitch: horizontal tearing in bands
                float band = Hash(float2(floor(uv.y * 18.0), floor(t * 30.0)));
                uv.x += (band - 0.5) * 0.18 * _Glitch * step(0.45, band);

                float2 size = _MainTex_TexelSize.zw;
                float2 px = uv * size;
                float2 cellUV = (floor(px) + 0.5) / size;
                half value = SAMPLE_TEXTURE2D(_MainTex, s_point_clamp_sampler, cellUV).r;

                // Phosphor cell: lit square with soft edges and a thin dark gap between cells
                float2 cell = abs(frac(px) - 0.5);
                half lit = (1.0 - smoothstep(0.32, 0.5, cell.x)) * (1.0 - smoothstep(0.32, 0.5, cell.y));

                // Halo: a blurred copy of the bitmap
                float2 o = _MainTex_TexelSize.xy * 1.5;
                half halo = SAMPLE_TEXTURE2D(_MainTex, s_linear_clamp_sampler, uv + float2(o.x, o.y)).r
                          + SAMPLE_TEXTURE2D(_MainTex, s_linear_clamp_sampler, uv + float2(-o.x, o.y)).r
                          + SAMPLE_TEXTURE2D(_MainTex, s_linear_clamp_sampler, uv + float2(o.x, -o.y)).r
                          + SAMPLE_TEXTURE2D(_MainTex, s_linear_clamp_sampler, uv + float2(-o.x, -o.y)).r;
                halo *= 0.25;

                float scan = 0.85 + 0.15 * sin(input.uv.y * size.y * 6.2831853);
                float roll = 1.0 + 0.12 * exp(-abs(frac(input.uv.y - t * 0.13) - 0.5) * 14.0);
                float flicker = 0.97 + 0.03 * sin(t * 61.0);
                float2 edge = input.uv * (1.0 - input.uv);
                float vignette = saturate(pow(max(edge.x * edge.y * 18.0, 0.0), 0.35));
                float noise = Hash(floor(input.uv * size * 2.0) + floor(t * 20.0));

                half3 col = _BackColor.rgb * (0.8 + 0.2 * noise)
                          + _GlowColor.rgb * (value * lit + halo * _Halo) * scan * roll * flicker;
                col *= vignette;

                // Glitch: static and a brief flash
                col += _GlowColor.rgb * _Glitch * (noise * 0.35 + 0.15);

                return half4(col * _Power + _BackColor.rgb * 0.15 * (1.0 - _Power), 1);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
