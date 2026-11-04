// Full-screen "looking at a cockpit CRT" overlay (CrtOverlay): the camera feed (_MainTex) is bent onto a curved,
// rounded tube inside a dark bezel, tinted towards the cockpit's phosphor green, with scanlines, a slot mask, a
// rolling bar, noise and a vignette. A low-resolution HUD bitmap (_HudTex, one channel) glows on top in the
// phosphor colour. _Static (0-1) floods it with snow and tears it; _Fade (0-1) fades the tube to _FadeColor.
Shader "Aquatica/CrtOverlay"
{
    Properties
    {
        [PerRendererData] _MainTex ("Feed", 2D) = "black" {}
        _HudTex ("HUD", 2D) = "black" {}
        [HDR] _GlowColor ("Phosphor Color", Color) = (0.45, 1.4, 0.7, 1)
        _Aspect ("Screen Aspect (w/h)", Float) = 0.5
        _Feed ("Has Feed", Float) = 1
        _Tint ("Phosphor Tint On Feed", Range(0, 1)) = 0.45
        _Curvature ("Curvature", Range(0, 0.2)) = 0.06
        _Bezel ("Bezel Width (fraction of the short side)", Range(0, 0.2)) = 0.05
        _Static ("Static", Range(0, 1)) = 0
        _Fade ("Fade", Range(0, 1)) = 0
        _FadeColor ("Fade Color", Color) = (0, 0, 0, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Opaque" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _HudTex;
            float4 _HudTex_TexelSize;
            half4 _GlowColor;
            float _Aspect, _Feed, _Tint, _Curvature, _Bezel, _Static, _Fade;
            half4 _FadeColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(443.897, 441.423));
                p += dot(p, p.yx + 19.19);
                return frac((p.x + p.y) * p.x);
            }

            half4 frag(v2f i) : SV_Target
            {
                float t = _Time.y;
                // Square units: x spans +-aspect, y spans +-1 (so the bezel and corners are even all round)
                float2 half_ = float2(_Aspect, 1.0);
                float2 p = (i.uv * 2.0 - 1.0) * half_;
                float bezel = _Bezel * 2.0 * min(_Aspect, 1.0);
                float2 inner = half_ - bezel;

                // Tube coordinates (-1..1 inside the bezel), bulged outwards like curved glass
                float2 s = p / inner;
                s *= 1.0 + _Curvature * (s.yx * s.yx);

                // Static tears lines sideways
                float tear = (Hash(float2(floor(s.y * 40.0), floor(t * 20.0))) - 0.5) * 0.08 * _Static;
                s.x += tear;

                float2 sp = s * inner;
                float radius = 0.12;
                float2 q = abs(sp) - (inner - radius);
                float d = length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - radius;
                float insideMask = 1.0 - smoothstep(-0.004, 0.004, d);

                float2 uv = s * 0.5 + 0.5;

                // Feed: slight colour fringing, pulled towards the phosphor tint
                half3 feed;
                feed.r = tex2D(_MainTex, uv + float2(0.0015, 0)).r;
                feed.g = tex2D(_MainTex, uv).g;
                feed.b = tex2D(_MainTex, uv - float2(0.0015, 0)).b;
                half lum = dot(feed, half3(0.3, 0.59, 0.11));
                feed = lerp(feed, lum * half3(0.7, 1.05, 0.9), _Tint) * 1.2 * _Feed;
                half3 col = feed + 0.012;

                // HUD in phosphor, with a little bloom from the neighbouring pixels
                float2 texel = _HudTex_TexelSize.xy;
                half hud = tex2D(_HudTex, uv).r;
                half bloom = (tex2D(_HudTex, uv + float2(texel.x, 0)).r + tex2D(_HudTex, uv - float2(texel.x, 0)).r
                            + tex2D(_HudTex, uv + float2(0, texel.y)).r + tex2D(_HudTex, uv - float2(0, texel.y)).r) * 0.25;
                col += _GlowColor.rgb * (hud + bloom * 0.35);

                // Tube: scanlines, slot mask, a slow rolling bar, grain, vignette
                col *= 0.8 + 0.2 * sin(uv.y * 700.0);
                col *= 0.93 + 0.07 * sin(i.uv.x * _ScreenParams.x * 2.1);
                col *= 1.0 + 0.06 * smoothstep(0.0, 0.1, 0.1 - abs(frac(uv.y * 0.5 - t * 0.12) - 0.5));
                float grain = Hash(floor(uv * float2(320, 320 / max(_Aspect, 0.1))) + frac(t * 37.0) * 100.0);
                col += (grain - 0.5) * 0.012; // linear colour: small values show up strongly
                col *= 1.0 - 0.45 * pow(saturate(dot(s, s) * 0.5), 1.5);

                // Static snow
                float snow = Hash(floor(i.uv * float2(220.0 * _Aspect, 220.0)) + floor(t * 30.0));
                col = lerp(col, snow * half3(0.75, 0.9, 0.85), _Static * 0.85);

                // Glass: a soft reflection in the upper corner
                col += 0.035 * exp(-length(s - float2(-0.55, 0.65)) * 3.0);

                col = lerp(col, _FadeColor.rgb, _Fade);

                // Bezel: dark plastic, a lit inner lip, faint glow spilling from the tube
                float shade = 0.035 + 0.02 * (i.uv.y);
                half3 bezelCol = shade;
                bezelCol += 0.08 * (1.0 - smoothstep(0.0, 0.02, d));
                bezelCol += _GlowColor.rgb * 0.025 * exp(-max(d, 0.0) * 25.0) * (1.0 - _Fade);
                col = lerp(bezelCol, col, insideMask);
                return half4(col, 1);
            }
            ENDCG
        }
    }
}
