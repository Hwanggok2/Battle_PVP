Shader "BattlePvp/DefenseAura"
{
    Properties
    {
        [HDR] _BaseColor ("Glow", Color) = (0.05, 1, 1.4, 0.3)
        _Expand ("World shell width", Float) = 0
        _VertexAlpha ("Use vertex opacity", Float) = 0
        _ImpactPoint ("Local impact point", Vector) = (0,0,0,0)
        _ImpactAge ("Impact elapsed seconds", Float) = -1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Expand, _VertexAlpha;
                float4 _ImpactPoint;
                float _ImpactAge;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; half4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; half3 normalWS : TEXCOORD1; half alpha : TEXCOORD2; float3 positionOS : TEXCOORD3; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionOS=input.positionOS.xyz;
                output.normalWS=TransformObjectToWorldNormal(input.normalOS);
                output.positionWS=TransformObjectToWorld(input.positionOS.xyz)+output.normalWS*_Expand;
                output.positionCS=TransformWorldToHClip(output.positionWS);
                output.alpha=lerp(1,input.color.a,_VertexAlpha);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float3 view=GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim=pow(1-saturate(dot(normalize(input.normalWS),view)),2);
                half pulse=.92+.08*sin(_Time.y*3.5);
                // A local FPS camera can sit inside the shell. Nearby fragments stay unobtrusive.
                half nearFade=smoothstep(.12,.4,distance(_WorldSpaceCameraPos,input.positionWS));
                half impact=0;
                if(_ImpactAge>=0 && _ImpactAge<.35)
                {
                    float d=distance(input.positionOS,_ImpactPoint.xyz);
                    float phase=_ImpactAge/.35;
                    half ring=1-smoothstep(.025,.09,abs(d-(.06+phase*.85)));
                    half flash=(1-smoothstep(0,.24,d))*saturate(1-phase*4);
                    impact=max(ring,flash)*(1-phase);
                }
                half alpha=max(_BaseColor.a*input.alpha*(.35+.65*rim)*pulse,impact*.85)*nearFade;
                return half4(_BaseColor.rgb*(.8+rim*1.2)+half3(3,2.5,.5)*impact,alpha);
            }
            ENDHLSL
        }
    }
}
