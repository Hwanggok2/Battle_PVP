Shader "BattlePvp/BladeSweep"
{
    Properties
    {
        _BaseColor ("Blue", Color) = (0.04, 0.28, 1, 0.3)
        _LocalNearFade ("First-person near fade", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _LocalNearFade;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : TEXCOORD0; float viewDepth : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.alpha = input.color.a;
                output.viewDepth = -TransformWorldToView(TransformObjectToWorld(input.positionOS.xyz)).z;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // An authored wind-up can cross the offset FPS camera; do not paint a full-screen sheet.
                half nearFade = lerp(1, smoothstep(.35, .85, input.viewDepth), _LocalNearFade);
                return half4(_BaseColor.rgb, _BaseColor.a * input.alpha * nearFade);
            }
            ENDHLSL
        }
    }
}
