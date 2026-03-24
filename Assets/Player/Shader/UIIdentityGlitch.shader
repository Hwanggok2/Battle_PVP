Shader "BattlePVP/UI/IdentityGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        // Reference VFX params
        _GlitchAmount ("Glitch Amount", Range(0, 1)) = 0
        _StatColor ("Stat Color", Color) = (1, 0, 0, 1)
        _EmissionPulse ("Emission Pulse", Float) = 4
        _OverlapPercent ("Overlap Percent", Range(0, 1)) = 0
        _ReassembleProgress ("Reassemble Progress", Range(0, 1)) = 1
        [Toggle] _MirrorActive ("Mirror Active", Float) = 0

        // UI masking compatibility
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "UIIdentityGlitch"

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            float4 _TextureSampleAdd;

            float4 _ClipRect;
            float _UseUIAlphaClip;

            float _GlitchAmount;
            float4 _StatColor;
            float _EmissionPulse;
            float _OverlapPercent;
            float _ReassembleProgress;
            float _MirrorActive;

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv       : TEXCOORD0;
                float4 worldPos : TEXCOORD1;
            };

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.worldPos = v.vertex;
                o.uv = TRANSFORM_TEX(v.texcoord, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;

                if (_MirrorActive > 0.5)
                {
                    uv.x = 1.0 - uv.x;
                }

                float t = _Time.y;
                float pulse = 0.5 + 0.5 * sin(t * max(_EmissionPulse, 0.001));

                // Final glitch intensity includes strategist overlap and reassemble damping.
                float overlapBoost = saturate(_OverlapPercent);
                float reassembleDamp = 1.0 - saturate(_ReassembleProgress);
                float glitch = saturate(_GlitchAmount + overlapBoost * 0.5 + reassembleDamp * 0.35);

                float band = floor(uv.y * 140.0 + t * 20.0);
                float jitter = (hash21(float2(band, floor(t * 30.0))) - 0.5) * (0.03 * glitch);
                uv.x += jitter;

                float2 rgbShift = float2((hash21(float2(band * 0.37, t)) - 0.5) * 0.012 * glitch, 0.0);

                fixed4 baseCol = tex2D(_MainTex, uv) + _TextureSampleAdd;
                fixed r = tex2D(_MainTex, uv + rgbShift).r;
                fixed b = tex2D(_MainTex, uv - rgbShift).b;
                baseCol.r = lerp(baseCol.r, r, glitch);
                baseCol.b = lerp(baseCol.b, b, glitch);

                float lineNoise = hash21(float2(floor(uv.y * 240.0), floor(t * 50.0)));
                float lineMask = step(0.92, lineNoise) * glitch;
                float3 glitchTint = _StatColor.rgb * (0.2 + pulse * 0.8);

                baseCol.rgb += glitchTint * lineMask * (0.45 + overlapBoost * 0.35);
                baseCol.rgb = lerp(baseCol.rgb, baseCol.rgb + glitchTint * 0.12, glitch * pulse);
                baseCol *= i.color;

                #ifdef UNITY_UI_CLIP_RECT
                baseCol.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(baseCol.a - 0.001);
                #endif

                return baseCol;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
