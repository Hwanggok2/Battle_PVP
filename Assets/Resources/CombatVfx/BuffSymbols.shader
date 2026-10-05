Shader "BattlePvp/BuffSymbols"
{
    Properties
    {
        [HDR] _BaseColor ("Light", Color) = (1,1,1,1)
        _Phase ("Rise phase", Float) = 0
        _Offset ("Skill separation", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Phase, _Offset;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 seed : TEXCOORD0; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; half alpha : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                float phase=frac(_Phase+input.seed.y+_Offset);
                float angle=input.seed.x*2*PI+_Offset;
                float3 center=TransformObjectToWorld(float3(cos(angle)*.68,.15+phase*1.8,sin(angle)*.68));
                // Camera-facing glyphs stay legible from either side, including reflected views.
                float3 position=center+UNITY_MATRIX_V[0].xyz*input.positionOS.x+UNITY_MATRIX_V[1].xyz*input.positionOS.y;
                output.positionCS=TransformWorldToHClip(position);
                half fade=smoothstep(0,.12,phase)*(1-smoothstep(.78,1,phase));
                // Keep the owner's FPS view clear while retaining the effect on distant players.
                half nearFade=smoothstep(.7,1.4,distance(_WorldSpaceCameraPos,center));
                output.alpha=input.color.a*fade*nearFade;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            { return half4(_BaseColor.rgb,_BaseColor.a*input.alpha); }
            ENDHLSL
        }
    }
}
