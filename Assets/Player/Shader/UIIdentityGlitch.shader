Shader "UI/IdentityGlitch"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255

        _ColorMask ("Color Mask", Float) = 15

        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0

        // Custom properties for Glitch
        _GlitchAmount ("Glitch Amount", Range(0, 1)) = 0
        _StatColor ("Stat Influence Color", Color) = (1, 1, 1, 1)
        _EmissionPulse ("Emission Pulse", Float) = 1
        _OverlapPercent ("Overlap Percent", Range(0, 1)) = 0
        _ReassembleProgress ("Reassemble Progress", Range(0, 1)) = 1
        _MirrorActive ("Mirror Active (0 or 1)", Float) = 0

        // Vignette Erosion
        _VignetteRadius ("Vignette Radius", Range(0, 1)) = 1.0
        _VignetteSoftness ("Vignette Softness", Range(0, 1)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
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
        ZTest [ZTest]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;

            // Combat VFX Params
            float _GlitchAmount;
            fixed4 _StatColor;
            float _EmissionPulse;
            float _OverlapPercent;
            float _ReassembleProgress;
            float _MirrorActive;

            // Vignette Mask
            float _VignetteRadius;
            float _VignetteSoftness;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.worldPosition = v.vertex;
                o.vertex = UnityObjectToClipPos(o.worldPosition);
                o.uv = v.uv;
                o.color = v.color * _Color;
                return o;
            }

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float t = _Time.y;

                // Sync with reference-vfx-params.md
                float pulse = 1.0 + sin(t * 8.0) * 0.15 * _EmissionPulse;
                
                // Final glitch intensity includes strategist overlap and reassemble damping.
                float overlapPercentRaw = saturate(_OverlapPercent);
                float reassembleDamp = 1.0 - saturate(_ReassembleProgress);
                float glitch = saturate(_GlitchAmount + overlapPercentRaw * 0.5 + reassembleDamp * 0.35);

                // --- Vignette Mask Logic (Edge Only) ---
                fixed d = distance(uv, fixed2(0.5, 0.5));
                
                // Radius와 Softness는 인스펙터 값(기본 1)을 그대로 사용 (더 이상 체력에 따라 반경이 변하지 않음)
                fixed baseR = _VignetteRadius * 0.75;
                fixed baseS = _VignetteSoftness * 0.5;
                
                fixed noiseMask = smoothstep(baseR - baseS, baseR, d);
                // 극중앙 보호는 유지
                noiseMask *= smoothstep(0.0, 0.15, d);
                // ----------------------------------------

                float band = floor(uv.y * 140.0 + t * 20.0);
                float jitter = (hash21(fixed2(band, 0)) - 0.5) * (0.04 * glitch * noiseMask);
                
                // Mirror effect
                if (_MirrorActive > 0.5)
                {
                    uv.x = uv.x > 0.5 ? 1.0 - uv.x : uv.x;
                }

                // UV 변형
                uv.x += jitter;

                fixed4 baseCol = tex2D(_MainTex, uv) + _TextureSampleAdd;
                
                // 글리치 색상 효과에도 마스크 적용
                fixed3 glitchTint = _StatColor.rgb * glitch;
                float lineMask = smoothstep(0.8, 0.95, hash21(fixed2(band, t))) * noiseMask;
                
                float overlapBoost = overlapPercentRaw * 1.5;
                baseCol.rgb += glitchTint * lineMask * (0.45 + overlapBoost * 0.35);
                
                fixed3 pulseCol = baseCol.rgb + glitchTint * 0.12;
                baseCol.rgb = lerp(baseCol.rgb, pulseCol, glitch * pulse * noiseMask);

                // 최종 출력: i.color(틴트)와 noiseMask(침식)를 곱해 중앙을 완전히 투명하게 만듭니다.
                baseCol.rgb *= i.color.rgb;
                baseCol.a *= i.color.a * noiseMask;

                #ifdef UNITY_UI_CLIP_RECT
                baseCol.a *= UnityGet2DClipping(i.worldPos.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip (baseCol.a - 0.001);
                #endif

                return baseCol;
            }
            ENDCG
        }
    }

    FallBack "UI/Default"
}
