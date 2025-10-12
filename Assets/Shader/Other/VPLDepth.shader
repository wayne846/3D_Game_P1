Shader "Hidden/VPLDepth"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Back ZWrite On ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
            };

            struct v2f {
                float4 posCS : SV_POSITION;
                float3 posWS : TEXCOORD0;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.posCS = UnityObjectToClipPos(v.vertex);
                o.posWS = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            // 輸出線性 0~1 深度：0 近、1 遠
            float4 frag(v2f i) : SV_Target
            {
                // 取 clip-space 深度
                float depth01 = Linear01Depth(UNITY_Z_0_FAR(i.posCS.z));
                return float4(depth01, 0, 0, 1);
            }
            ENDHLSL
        }
    }
}