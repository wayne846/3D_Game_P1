Shader "Custom/VPLShading"
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
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5 // 需要較高的 Shader Target 來支援 Texture2DArray

            #include "UnityCG.cginc"
            #include "HLSLSupport.cginc"

            struct VPLDataForGPU
            {
                float3 position;
                float4 color;
            };

            StructuredBuffer<VPLDataForGPU> _VPLs; // 所有 VPL 的座標和顏色
            int _VPLCount; // 有幾個 VPl （_VPLs 的長度）

            // 宣告 Texture2DArray
            UNITY_DECLARE_TEX2DARRAY(_ShadowMapArray); // shadow map, 每個 VPL 有 6 個面
            StructuredBuffer<float4x4> _ShadowCamera_VP; // 拍攝 shadow map 時，shadow camera 的 Projection * View matrix

            float4 _Color;
            float4 _SpecColor;
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
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            // 陰影計算函式
            bool InShadow(float3 worldPos, float3 worldNormal, float3 vplPos, int vplIndex)
            {   
                float3 lightVec = worldPos - vplPos;

                // 決定要取樣 Cubemap 的哪個面 (哪個 slice)
                float3 absVec = abs(lightVec);
                int faceIndex;
                float2 uv;
                
                if (absVec.x > absVec.y && absVec.x > absVec.z) // X-face
                {
                    if (lightVec.x > 0) 
                    {
                        faceIndex = 0;
                        uv = float2(-lightVec.z, lightVec.y) / absVec.x;
                    }
                    else
                    {
                        faceIndex = 1;
                        uv = float2(lightVec.z, lightVec.y) / absVec.x;
                    }
                }
                else if (absVec.y > absVec.z) // Y-face
                {
                    if (lightVec.y > 0) 
                    {
                        faceIndex = 2;
                        uv = float2(lightVec.x, -lightVec.z) / absVec.y;
                    }
                    else
                    {
                        faceIndex = 3;
                        uv = float2(lightVec.x, lightVec.z) / absVec.y;
                    }
                }
                else // Z-face
                {
                    if (lightVec.z > 0)
                    {
                        faceIndex = 4;
                        uv = float2(lightVec.x, lightVec.y) / absVec.z;
                    }
                    else
                    {
                        faceIndex = 5;
                        uv = float2(-lightVec.x, lightVec.y) / absVec.z;
                    }
                }

                uv = (uv + 1.0) * 0.5; // 將 UV [-1, 1] 映射到 [0,1]
                int sliceIndex = vplIndex * 6 + faceIndex;

                // 從紋理陣列中取樣深度值
                float shadowDepth = UNITY_SAMPLE_TEX2DARRAY(_ShadowMapArray, float3(uv, sliceIndex)).r;

                // 避免 Shadow Acne，Slope-Scaled Depth Bias
                float bias = lerp(0.01f, 0.001f , dot(-normalize(lightVec), normalize(worldNormal)));

                // 將點稍微往光的方向拉近
                float4 clipPos = mul(_ShadowCamera_VP[sliceIndex], float4(worldPos - bias * normalize(lightVec), 1));
                float depth = clipPos.z / clipPos.w;
                // OpenGL 中 NDC.z : [-1, 1]，映射到 [0, 1]
                if (UNITY_NEAR_CLIP_VALUE == -1)
                    depth = depth * 0.5f + 0.5f;

                // Direct 3D : 近 -> 1, 遠 -> 0
                if (UNITY_REVERSED_Z)
                    return shadowDepth > depth;
                // OpenGL : 近 -> 0, 遠 -> 1
                else
                    return shadowDepth < depth;
                // 如果 shadow map 取到的深度比較近，則 worldPos 在 shadow 裡
            }


            float4 frag(v2f i) : SV_Target
            {
                float4 baseColor = tex2D(_MainTex, i.uv);
                if (_UseTexture == 0) baseColor = _Color;

                float3 N = normalize(i.worldNormal);
                float3 V = normalize(_WorldSpaceCameraPos - i.worldPos);

                float3 finalDiffuse = float3(0,0,0);
                float3 finalSpecular = float3(0,0,0);

                for (int j = 0; j < _VPLCount; j++)
                {
                    float3 lightDir = _VPLs[j].position - i.worldPos;
                    float distance = length(lightDir);
                    float3 L = normalize(lightDir);
                    
                    // 只有不在陰影中時才計算光照
                    if (!InShadow(i.worldPos, i.worldNormal, _VPLs[j].position, j))
                    {
                        float attenuation = 1.0 / (distance * distance + 1.0);
                        
                        // Diffuse
                        float diff = max(dot(N, L), 0.0f);
                        finalDiffuse += _VPLs[j].color.rgb * diff * attenuation;

                        // Specular
                        float3 R = normalize(reflect(-L, N));
                        float spec = pow(max(dot(R, V), 0.0f), _Shininess);
                        finalSpecular += _SpecColor.rgb * _VPLs[j].color.rgb * spec * attenuation;
                    }
                }
                
                float3 finalColor = (baseColor.rgb * finalDiffuse) + finalSpecular;
                return float4(finalColor, baseColor.a);
            }
            ENDCG
        }
    }
}