Shader "BattlePVP/FresnelAura"
{
    Properties
    {
        _AuraColor ("Aura Color", Color) = (1, 0.18, 0.08, 0.52)
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.4
        _Intensity ("Intensity", Range(0, 5)) = 1.8
        _Pulse ("Pulse", Range(0, 1.5)) = 1
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Cull Front
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _AuraColor;
            float _FresnelPower;
            float _Intensity;
            float _Pulse;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
                float3 viewDir : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                o.viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float fresnel = pow(1.0 - saturate(dot(normalize(i.worldNormal), normalize(i.viewDir))), _FresnelPower);
                fixed4 color = _AuraColor;
                color.rgb *= fresnel * _Intensity * _Pulse;
                color.a *= fresnel * _Pulse;
                return color;
            }
            ENDCG
        }
    }
}
