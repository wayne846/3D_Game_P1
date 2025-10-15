Shader "Hidden/SSAO"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _WorldPosTex ("World Position Texture", 2D) = "white" {}
        _NormalTex ("Normal Texture", 2D) = "white" {}
        _TexSize ("Texture Size", Vector) = (0, 0, 0, 0)
        _CameraPos ("Camera Pos", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        // No culling or depth
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            // 說明：在每一個點沿法向量的方向生成一個半圓並隨機向外取樣，將取樣點轉到 Clip Space 後取出對應位置的世界座標以此來判斷是否有遮蔽
            // 每個取樣反向會經過一個隨機的反射向量進行反射，讓每一個點的取樣向量都不一樣
            // Reference: https://ieeexplore.ieee.org/document/5974405

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            sampler2D _WorldPosTex;
            sampler2D _NormalTex;
            float2 _TexSize;
            float3 _CameraPos;
            float4x4 _Camera_VP;

            static const float3 HemiSphereSample[64] = {
                float3(0.0507347, 0.0109197, 0.0654152),
                float3(-0.0111959, 0.0145402, 0.0124074),
                float3(0.0162076, 0.0188678, -0.0492818),
                float3(-0.0914583, 0.0388915, 0.0193574),
                float3(0.0558366, 0.0608068, 0.0567375),
                float3(-0.0516686, 0.0547671, 0.0710158),
                float3(0.0854656, 0.0102691, -0.00273397),
                float3(0.000324449, 0.000160468, -0.000386937),
                float3(-0.0133785, 0.00961545, 0.0710888),
                float3(0.0247813, 0.0372486, 0.0389682),
                float3(0.0108653, 0.027834, -0.0323914),
                float3(0.0531107, 0.0161193, 0.0660204),
                float3(0.0468896, 0.0523027, 0.0676158),
                float3(0.0394781, 0.0385439, -0.0174994),
                float3(0.017958, 0.0100409, -0.0379787),
                float3(0.0150301, 0.0290789, -0.0341506),
                float3(-0.00778256, 0.0152184, -0.0158329),
                float3(-0.0755153, 0.0931661, 0.0606303),
                float3(0.0928125, 0.0298173, -0.0871302),
                float3(0.0773614, 0.0421488, -0.0799959),
                float3(-0.0164075, 0.0168608, -0.0317248),
                float3(0.00478841, 0.000461826, 0.00532373),
                float3(-0.0791111, 0.0516326, -0.00258601),
                float3(-0.0322718, 0.144584, 0.0867753),
                float3(0.0676395, 0.148754, 0.0822816),
                float3(-0.000329293, 0.000518822, 0.000264203),
                float3(0.0482969, 0.110655, -0.105061),
                float3(-0.172779, 0.103402, -0.000741888),
                float3(0.194266, 0.121221, -0.0674456),
                float3(0.000868732, 0.00411696, -0.00281388),
                float3(0.113701, 0.185812, -0.110822),
                float3(0.00149837, 0.118229, 0.0500738),
                float3(0.115475, 0.0625028, 0.135678),
                float3(0.0327217, 0.0547691, -0.250443),
                float3(-0.227631, 0.0746932, -0.157393),
                float3(0.0644647, 0.0935156, -0.0464905),
                float3(0.00378577, 0.00180584, -0.00308941),
                float3(0.333921, 0.084494, -0.116697),
                float3(-0.209219, 0.292539, -0.171645),
                float3(0.122174, 0.410021, -0.0281221),
                float3(-0.145987, 0.0332615, 0.32558),
                float3(0.0936312, 0.326422, 0.054603),
                float3(0.236905, 0.197395, -0.121612),
                float3(0.177867, 0.135655, 0.175467),
                float3(-0.192162, 0.167216, 0.109011),
                float3(-0.125857, 0.0599706, -0.132357),
                float3(0.0151833, 0.146132, 0.137631),
                float3(0.05252, 0.0583857, -0.0447854),
                float3(0.0764653, 0.219378, -0.034008),
                float3(-0.18893, 0.140693, -0.0630457),
                float3(-0.148357, 0.20366, 0.129239),
                float3(-0.166721, 0.380927, 0.0251984),
                float3(-0.308869, 0.055218, 0.0942095),
                float3(-0.272998, 0.195924, 0.177461),
                float3(0.291663, 0.551915, 0.382512),
                float3(-0.0624498, 0.454874, -0.525497),
                float3(-0.215058, 0.200558, 0.327986),
                float3(-0.242851, 0.175766, 0.227618),
                float3(0.0253666, 0.180807, 0.328233),
                float3(-0.3655, 0.37018, -0.0496679),
                float3(-0.523619, 0.331828, 0.614873),
                float3(-0.783519, 0.0271398, 0.434825),
                float3(0.392268, 0.224496, 0.455801),
                float3(-0.749664, 0.312297, -0.180789),
            };

            static const float3 RandomReflect[16] = {
                float3(0.607594, 0.130774, 0.783408),
                float3(0.577859, 0.10952, 0.808754),
                float3(0.922924, 0.246746, 0.295512),
                float3(-0.942714, 0.239669, 0.232054),
                float3(-0.903278, 0.384108, 0.191181),
                float3(0.581427, 0.564761, 0.585651),
                float3(0.617137, 0.642305, -0.454517),
                float3(0.313792, 0.674294, 0.668477),
                float3(0.992358, 0.119237, -0.0317446),
                float3(0.5534, 0.742816, -0.376792),
                float3(-0.976935, 0.00652464, -0.213436),
                float3(-0.629195, 0.743385, 0.226917),
                float3(0.417683, 0.627817, 0.656801),
                float3(0.0082673, 0.740067, 0.672482),
                float3(-0.763194, 0.296946, 0.573897),
                float3(-0.49849, 0.808097, 0.313827),
            };


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

            float3x3 RotateYToN(float3 N)
            {
                float3 up = abs(N.y) < 0.999 ? float3(0,1,0) : float3(1,0,0);
                float3 R = normalize(cross(up, N));
                float3 F = cross(R, N);
                return float3x3(R, N, F); // 每一個是column (HLSL預設列向量右乘)
            }

            fixed4 frag (v2f I) : SV_Target
            {
                // 取樣的中心點
                const float3 CenterPos = tex2D(_WorldPosTex, I.uv);
                const float3 CenterNormal = normalize(tex2D(_NormalTex, I.uv));
                const float3x3 Rot = RotateYToN(CenterNormal);
                const float3 Reflect = RandomReflect[int(I.uv.x * _TexSize.x) % 4 * 4 + int(I.uv.y * _TexSize.y) % 4];
                const float baseDepth = distance(CenterPos, _CameraPos);

                // 半球的大小
                float radius = 0.2;
                int sampleCount = 64;
                int occlusion = 0;

                for (int i = 0; i < sampleCount; ++i)
                {
                    float3 SampleDir = reflect(HemiSphereSample[i], Reflect);
                    if (dot(SampleDir, CenterNormal) < 0)
                        SampleDir = -SampleDir;
                    //SampleDir = HemiSphereSample[i];

                    float3 SamplePos = CenterPos + normalize(mul(Rot, SampleDir)) * radius;
                    float4 SampleClipPos = mul(_Camera_VP, float4(SamplePos, 1));
                    float2 sampleUV = SampleClipPos.xy / SampleClipPos.w * 0.5 + 0.5;

                    if (any(sampleUV < 0.0) || any(sampleUV > 1.0)) continue;

                    float3 SampleWorldPos = tex2D(_WorldPosTex, sampleUV);
                    float SampleDepth = distance(SampleWorldPos, _CameraPos);
                    float diff = baseDepth - SampleDepth;

                    if (diff > 0)
                        occlusion++;
                }

                float ao = 1.0 - (float)occlusion / (float)sampleCount;
                ao = max(ao, 0.1); // 防止全黑
                return float4(tex2D(_MainTex, I.uv).xyz * ao, 1);
            }
            ENDCG
        }
    }
}
