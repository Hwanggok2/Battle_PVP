Shader "BattlePvp/BuffAura"
{
    Properties
    {
        [HDR] _BaseColor ("Buff light", Color) = (0.08, 0.5, 2.5, 1)
        _Phase ("Outward wave", Range(0,1)) = 0
        _Moving ("Moving shell", Float) = 0
        _Center ("Body center", Vector) = (0,1,0,0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float4 _Center;
                float _Phase, _Moving;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                float3 positionWS=TransformObjectToWorld(input.positionOS.xyz);
                // Radial expansion keeps buttons, fingers and clothing from ballooning individually.
                float3 radial=SafeNormalize(positionWS-_Center.xyz);
                output.positionWS=positionWS+lerp(output.normalWS*.012,radial*lerp(.02,.18,_Phase),_Moving);
                output.positionCS=TransformWorldToHClip(output.positionWS);
                return output;
            }
            half Opacity(Varyings input)
            {
                half facing=saturate(dot(normalize(input.normalWS),GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half nearFade=smoothstep(.18,.65,distance(_WorldSpaceCameraPos,input.positionWS));
                return _BaseColor.a*(.035+.3*pow(1-facing,2.5))*nearFade;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                half wave=sin(_Phase*PI);
                return half4(_BaseColor.rgb,Opacity(input)*lerp(.75+.1*sin(_Phase*2*PI),wave*wave,_Moving));
            }
            ENDHLSL
        }
    }
}
