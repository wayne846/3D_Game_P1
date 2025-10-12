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
            #pragma target 4.5 // �ݭn������ Shader Target �Ӥ䴩 Texture2DArray

            #include "UnityCG.cginc"

            struct VPLDataForGPU
            {
                float3 position;
                float4 color;
            };

            StructuredBuffer<VPLDataForGPU> _VPLs;
            int _VPLCount;
            float _ShadowFarPlane;

            // �ŧi Texture2DArray
            UNITY_DECLARE_TEX2DARRAY(_ShadowMapArray);

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

            // ���v�p��禡
            float GetShadow(float3 worldPos, float3 vplPos, int vplIndex)
            {
                float3 lightVec = worldPos - vplPos;
                float currentDepth = length(lightVec) / _ShadowFarPlane; // �N��e�`�ץ��W�ƨ� [0,1]
                
                // �M�w�n���� Cubemap �����ӭ� (���� slice)
                float3 absVec = abs(lightVec);
                int faceIndex;
                float2 uv;
                
                if (absVec.x > absVec.y && absVec.x > absVec.z) // X-face
                {
                    faceIndex = lightVec.x > 0 ? 0 : 1;
                    uv = float2(-lightVec.z, -lightVec.y) / absVec.x;
                }
                else if (absVec.y > absVec.z) // Y-face
                {
                    faceIndex = lightVec.y > 0 ? 2 : 3;
                    uv = float2(lightVec.x, lightVec.z) / absVec.y;
                }
                else // Z-face
                {
                    faceIndex = lightVec.z > 0 ? 4 : 5;
                    uv = float2(lightVec.x, -lightVec.y) / absVec.z;
                }

                uv = (uv + 1.0) * 0.5; // �N UV �M�g�� [0,1]
                int sliceIndex = vplIndex * 6 + faceIndex;

                // �q���z�}�C�����˲`�׭�
                float shadowDepth = UNITY_SAMPLE_TEX2DARRAY(_ShadowMapArray, float3(uv, sliceIndex)).r;
                
                // ����`�סA�[�W�@�Ӥp�� bias �קK self-shadowing artifacts
                float bias = 0.005;
                return currentDepth - bias > shadowDepth ? 0.0 : 1.0; // �b���v���h��^ 0
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
                    
                    // �p�⳱�v
                    float shadow = GetShadow(i.worldPos, _VPLs[j].position, j);
                    
                    // �u�����b���v���ɤ~�p�����
                    if (shadow > 0.5)
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