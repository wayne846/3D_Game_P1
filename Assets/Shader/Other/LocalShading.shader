Shader "Custom/LocalShading"
{
     Properties
    {
        _Color("Diffuse Color", Color) = (1,1,1,1)
        _MainTex("Texture", 2D) = "white" {}
        _UseTexture("Whether to use texture", Range(0, 1)) = 0
        _SpecColor("Specular Color", Color) = (1,1,1,1)
        _Shininess("Shininess", Range(1,256)) = 32
    }
    SubShader
    {
        ZTest Less
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityLightingCommon.cginc"

            float4 _Color;
            //float4 _SpecColor;
            float _Shininess;
            sampler2D _MainTex;
            int _UseTexture;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };

            v2f vert(appdata v)
            {
                v2f o;
                // Unity 提供函式把 Object 空間頂點轉到 Clip 空間
                o.pos = UnityObjectToClipPos(v.vertex);
                
                o.uv = v.uv;

                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                o.worldNormal = mul((float3x3)unity_ObjectToWorld, v.normal);
                o.worldNormal = normalize(o.worldNormal);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 baseColor = tex2D(_MainTex, i.uv);
                if (_UseTexture == 0)
                    baseColor = _Color;

                float3 N = normalize(i.worldNormal);
                float3 L = normalize(_WorldSpaceLightPos0.xyz);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                float3 diff = max(dot(N, L), 0.0f);
                float3 diffuse = baseColor.rgb * _LightColor0.rgb * diff;

                float3 R = normalize(reflect(-L, N));
                float spec = max(dot(R, V), 0.0f);
                spec = pow(spec, _Shininess);
                //float spec = 1;
                float3 specular = _SpecColor.rgb * _LightColor0.rgb * spec;

                return float4(diffuse + specular, _Color.a);
            }
            ENDCG
        }
    }
}
