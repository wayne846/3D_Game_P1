Shader "Hidden/SSAO"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _WorldPosTex ("World Position Texture", 2D) = "white" {}
        //_NormalTex ("Normal Texture", 2D) = "white" {}
        _TexSize ("Texture Size", Vector) = (0, 0, 0, 0)
        _CameraPos ("Camera Pos", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _WorldPosTex;
            //sampler2D _NormalTex;
            float2 _TexSize;
            float3 _CameraPos;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }


            fixed4 frag (v2f i) : SV_Target
            {
                const float deltaX = 1.0 / _TexSize.x;
                const float deltaY = 1.0 / _TexSize.y;

                const float3 CenterPos = tex2D(_WorldPosTex, i.uv);
                const float baseDepth = distance(CenterPos, _CameraPos);

                // 在 65 * 65 的範圍內採樣
                int sample = 0;
                int occlusion = 0;
                for (int dx = -32; dx <= 32; ++dx) 
                {
                    for (int dy = -32; dy <= 32; ++dy)
                    {
                        float2 sampleUV = i.uv + float2(dx * deltaX, dy * deltaY);
                        if (any(sampleUV < float2(0, 0)) || any(sampleUV > float2(1, 1)))
                            continue;
                        ++sample;

                        float3 WorldPos = tex2D(_WorldPosTex, sampleUV).xyz;
                        float depth = distance(WorldPos, _CameraPos);

                        if (depth < baseDepth)
                        {
                            ++occlusion;
                        }
                    }
                }

                return float4(tex2D(_MainTex, i.uv).xyz * max(1.0f - (float)occlusion / (float)sample, 0.1f), 1);
            }
            ENDCG
        }
    }
}
