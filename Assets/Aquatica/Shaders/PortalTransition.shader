// Full-screen "water iris" used by ScreenTransition. _Progress 0 = fully open, 1 = fully black.
// The iris edge wobbles like a water surface and glows, and a short cyan flash fires as it starts to close.
Shader "Aquatica/PortalTransition"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _Aspect ("Aspect (width / height)", Float) = 0.5
        [HDR] _RimColor ("Rim Color", Color) = (0.35, 0.9, 1.0, 1)
        _FlashColor ("Flash Color", Color) = (0.75, 0.95, 1.0, 1)
        _Wobble ("Edge Wobble", Range(0, 0.1)) = 0.035
    }

    SubShader
    {
        Tags { "Queue" = "Overlay" "RenderType" = "Transparent" "IgnoreProjector" = "True" "PreviewType" = "Plane" }

        Blend SrcAlpha OneMinusSrcAlpha
        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            float _Progress;
            float _Aspect;
            float4 _RimColor;
            float4 _FlashColor;
            float _Wobble;

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

            float4 frag(v2f i) : SV_Target
            {
                float2 p = i.uv - 0.5;
                p.x *= _Aspect;
                float r = length(p);
                float a = atan2(p.y, p.x);

                float t = smoothstep(0.0, 1.0, _Progress);
                float maxRadius = 0.5 * sqrt(_Aspect * _Aspect + 1.0) + 0.08;
                float radius = lerp(maxRadius, -0.06, t);

                // Watery edge: two travelling waves plus a swirl that tightens as the iris closes
                float swirl = a + r * 6.0 * t;
                float wobble = _Wobble * (sin(swirl * 7.0 + _Time.y * 5.0) * 0.6 + sin(swirl * 13.0 - _Time.y * 7.0) * 0.4);
                float d = r - (radius + wobble * saturate(radius * 4.0));

                float dark = smoothstep(-0.012, 0.012, d);
                float rim = exp(-abs(d) * 45.0) * step(0.001, 1.0 - _Progress) * step(0.001, _Progress);

                // Brief flash right as the transition starts (and again at the very end of opening)
                float flash = smoothstep(0.0, 0.08, _Progress) * (1.0 - smoothstep(0.08, 0.35, _Progress));

                float3 col = _RimColor.rgb * rim + _FlashColor.rgb * flash * (1.0 - dark);
                float alpha = saturate(dark + rim + flash * 0.85);
                return float4(col, alpha);
            }
            ENDCG
        }
    }
}
