int _UseBumpMap;
Texture2D _BumpMap;
SamplerState sampler_BumpMap;

/**
給定一個 uv 座標，從 _BumpMap 取出相鄰區域的高度變化並推測 Normal
這函數會依據原始的 Normal 計算出 TBN space 轉 world space 的旋轉，在將取樣出來的 Normal 轉 world space
*/
float3 GetBumpMapNormal(float2 uv, float3 oldNormal)
{
    if (_UseBumpMap == 0)
        return oldNormal;
    
        int width, height;
    _BumpMap.GetDimensions(width, height);
    
    float du = 1.f / height, dv = 1.f / width;
    float CenterHeight = _BumpMap.SampleLevel(sampler_BumpMap, uv, 0);
    float duHeight = _BumpMap.SampleLevel(sampler_BumpMap, uv + float2(du, 0), 0);
    float dvHeight = _BumpMap.SampleLevel(sampler_BumpMap, uv + float2(0, dv), 0);

    float3 normalInTBN = normalize(cross(float3(1, 0, duHeight - CenterHeight), float3(0, 1, dvHeight - CenterHeight)));
    
    /// TBN -> world
    oldNormal = normalize(oldNormal);
    float3 up = abs(oldNormal.y) < 0.999 ? float3(0, 1, 0) : float3(0, 0, -1); // 如果原本的 normal 朝正負 Y，那 (0, 0, -1) 和 normal 外積會變右邊的向量
    float3 B = normalize(cross(up, oldNormal)); // Right ?
    float3 T = normalize(cross(B, oldNormal));
    float3x3 TBNtoWorld = float3x3(T.x, B.x, oldNormal.x,
                                   T.y, B.y, oldNormal.y,
                                   T.z, B.z, oldNormal.z);
    
    return mul(TBNtoWorld, normalInTBN);
}
