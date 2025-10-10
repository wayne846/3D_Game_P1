using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using UnityEngine;
using static UnityEngine.UI.Image;


// 通用參數字典
using PbrtParams = System.Collections.Generic.Dictionary<string, object>;

public class PbrtScene
{
    public PbrtCamera camera { get; set; }
    public PbrtFilm film { get; set; }
    public PbrtSampler sampler { get; set; }
    public List<PbrtShape> shapes { get; } = new List<PbrtShape>();
    public Dictionary<string, Texture2D> KnownTexture {  get; } = new Dictionary<string, Texture2D>();

    public void Init()
    {
        film.Init();
        Vector2Int resolution = film.GetResolution();
        camera.Init(resolution.x, resolution.y);
    }

    public bool intersect(PbrtRay ray, Interval rayInterval, out PbrtHitInfo hitInfo)
    {
        bool isHit = false;
        float closest = rayInterval.max;

        PbrtHitInfo tempHitInfo = new PbrtHitInfo();
        PbrtHitInfo resultHitInfo = new PbrtHitInfo();
        tempHitInfo.position = Vector3.zero;
        tempHitInfo.normal = Vector3.zero;
        tempHitInfo.distance = 0;

        foreach (PbrtShape shape in shapes)
        {
            if (shape.intersect(ray, new Interval(rayInterval.min, closest), out tempHitInfo))
            {
                isHit = true;
                closest = tempHitInfo.distance;
                resultHitInfo = tempHitInfo;
            }
        }

        hitInfo = isHit ? resultHitInfo : tempHitInfo;

        return isHit;
    }
}

public struct PbrtRay
{
    public Vector3 origin;
    public Vector3 dir;
}

public struct PbrtHitInfo
{
    public Vector3 position;
    public Vector3 normal;
    public float distance;
}

public class PbrtCamera
{
    public string type { get; set; } // e.g., "perspective"
    
    public PbrtParams parameters { get; set; } = new PbrtParams();

    // 儲存相機參數
    Vector3 _eye;
    float _fov;
    int _filmWidth;
    int _filmHeight;

    // 相機座標系基底向量 (世界空間)
    Vector3 _forward;
    Vector3 _right;
    Vector3 _up;

    // 計算出的相機平面參數
    float _aspectRatio;
    float _tanHalfFovY;

    public void LookAt(Vector3 eye, Vector3 lookAt, Vector3 up)
    {
        _eye = eye;
        _forward = (lookAt - eye).normalized;
        _right = Vector3.Cross(up, _forward).normalized;
        _up = Vector3.Cross(_forward, _right);
    }

    public void Init(int filmWidth, int filmHeight)
    {
        _fov = (float)parameters["fov"];
        _filmWidth = filmWidth;
        _filmHeight = filmHeight;

        // 3. 預先計算後續會用到的值，避免在 GenerateRay 中重複計算
        _aspectRatio = (float)_filmWidth / _filmHeight;
        _tanHalfFovY = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>
    /// 根據像素座標產生一條光線
    /// </summary>
    /// <param name="x">像素的 x 座標 (左下角為 0)</param>
    /// <param name="y">像素的 y 座標 (左下角為 0)</param>
    /// <returns>一條從相機原點出發，穿過像素中心的光線</returns>
    public PbrtRay GenerateRay(int x, int y)
    {
        // 步驟 1: 將像素座標轉換到 [-1, 1] 的正規化裝置座標 (NDC)
        // 使用 x + 0.5 和 y + 0.5 來對準像素中心
        float ndcX = (2.0f * (x + 0.5f) / _filmWidth) - 1.0f;
        float ndcY = (2.0f * (y + 0.5f) / _filmHeight) - 1.0f;

        // 步驟 2: 根據 FOV 和長寬比，計算在相機空間中虛擬平面的對應座標
        float cameraX = ndcX * _aspectRatio * _tanHalfFovY;
        float cameraY = ndcY * _tanHalfFovY;

        // 步驟 3: 將相機空間方向轉換為世界空間方向
        // 方向 = 1.0 * 前方 + cameraX * 右方 + cameraY * 上方
        Vector3 dir = (_forward + cameraX * _right + cameraY * _up).normalized;
        
        return new PbrtRay
        {
            origin = _eye,
            dir = dir
        };
    }

    public Vector3 GetEyePosition()
    { 
        return _eye;
    }

    public Vector3 GetForward()
    {
        return _forward;
    }

    public Vector3 GetUp()
    {
        return _up;
    }

    public Vector3 GetRight()
    {
        return _right;
    }

    public float GetFov()
    {
        if (parameters.TryGetValue("fov", out object fov))
        {
            return (float)fov;
        }
        return _fov;
    }
}

public class PbrtFilm
{
    public string type { get; set; } // e.g., "image"
    public PbrtParams parameters { get; set; } = new PbrtParams();

    int _xresolution;
    int _yresolution;

    public void Init()
    {
        _xresolution = (int)parameters["xresolution"];
        _yresolution = (int)parameters["yresolution"];
    }

    public Vector2Int GetResolution()
    {
        return new Vector2Int(_xresolution, _yresolution);
    }
}

public class PbrtSampler
{
    public string type { get; set; } // e.g., "sobol"
    public PbrtParams parameters { get; set; } = new PbrtParams();
}

public class PbrtLight
{
    public string type { get; set; } // e.g., "area"
    public PbrtParams parameters { get; set; } = new PbrtParams();
}

public class PbrtMaterial
{
    public string type { get; set; } // e.g., "matte"
    public PbrtParams parameters { get; set; } = new PbrtParams();
    public object Kd  { get; set; }
    public Vector3 Ks { get; set; }
    public Vector3 Kt { get; set; }

    public override string ToString()
    {
        return $"Material(Type: {type}, Params: {parameters.Count})";
    }
}

public abstract class PbrtShape
{
    public Matrix4x4 objectToWorld { get; set; }
    public PbrtLight attachedLight { get; set; }
    // --- NEW: Property to hold the shape's material ---
    public PbrtMaterial material { get; set; }
    public PbrtParams parameters { get; set; } = new PbrtParams();

    public abstract bool intersect(PbrtRay ray, Interval rayInterval, out PbrtHitInfo hitInfo);
}
public class PbrtSphere : PbrtShape
{
    public override bool intersect(PbrtRay ray, Interval rayInterval, out PbrtHitInfo hitInfo)
    {
        Vector3 center = objectToWorld.ExtractPosition();
        float radius = (float)parameters["radius"];

        bool isHit = true;

        Vector3 oc = center - ray.origin;
        float a = ray.dir.sqrMagnitude;
        float h = Vector3.Dot(ray.dir, oc);
        float c = oc.sqrMagnitude - radius * radius;

        float discriminant = h * h - a * c;

        if (discriminant < 0)
        {
            isHit = false;
        }

        float sqrtd = Mathf.Sqrt(discriminant);

        // Find the nearest root that lies in the acceptable range.
        float root = (h - sqrtd) / a;
        if (!rayInterval.Surrounds(root))
        {
            root = (h + sqrtd) / a;
            if (!rayInterval.Surrounds(root))
            {
                isHit = false;
            }
        }

        hitInfo.distance = root;
        hitInfo.position = ray.origin + ray.dir.normalized * hitInfo.distance;
        hitInfo.normal = (hitInfo.position - center) / radius;

        return isHit;
    }
}
public class PbrtCylinder : PbrtShape 
{
    public override bool intersect(PbrtRay ray, Interval rayInterval, out PbrtHitInfo hitInfo)
    {
        hitInfo.distance = 0;
        hitInfo.position = Vector3.zero;
        hitInfo.normal = Vector3.zero;
        return false;
    }
}

// 更新 PbrtTriangleMesh 以包含具體幾何數據
public class PbrtTriangleMesh : PbrtShape
{
    public int[] indices { get; set; }
    public Vector3[] vertices { get; set; }
    public Vector3[] normals { get; set; } // 可選
    public Vector2[] uvs { get; set; } // 可選

    public override bool intersect(PbrtRay ray, Interval rayInterval, out PbrtHitInfo hitInfo)
    {
        bool isHit = false;

        // 將ray轉到物體的坐標系
        Matrix4x4 worldToObject = objectToWorld.inverse;
        PbrtRay objectRay = new PbrtRay
        {
            origin = worldToObject.MultiplyPoint3x4(ray.origin),
            dir = worldToObject.MultiplyVector(ray.dir)
        };

        hitInfo.distance = 0;
        hitInfo.position = Vector3.zero;
        hitInfo.normal = Vector3.zero;

        PbrtHitInfo tempHitInfo;
        float closest = rayInterval.max;

        for (int i = 0; i < indices.Length; i += 3)
        {
            Vector3[] point = { vertices[indices[i]], vertices[indices[i + 1]], vertices[indices[i + 2]] };
            Vector3[] normal = { normals[indices[i]], normals[indices[i + 1]], normals[indices[i + 2]] };

            if(intersectTriangle(objectRay, new Interval(rayInterval.min, closest), point, normal, out tempHitInfo))
            {
                isHit = true;
                closest = tempHitInfo.distance;
                hitInfo = tempHitInfo;
            }
        }

        if (isHit)
        {
            //將交點資訊轉換回世界座標系

            // 轉換位置
            hitInfo.position = objectToWorld.MultiplyPoint3x4(hitInfo.position);

            // 轉換法向量
            Matrix4x4 normalTransform = worldToObject.transpose;
            hitInfo.normal = Vector3.Normalize(normalTransform.MultiplyVector(hitInfo.normal));

            // 重新計算世界座標下的距離
            // 因為縮放會影響 t 值的尺度，最穩健的方式是直接計算世界座標中的距離
            hitInfo.distance = (hitInfo.position - ray.origin).magnitude;

            // 最後再檢查一次距離是否真的在區間內 (可選，但更穩健)
            if (!rayInterval.Contains(hitInfo.distance))
            {
                return false;
            }
        }

        return isHit;
    }

    bool intersectTriangle(PbrtRay ray, Interval rayInterval, Vector3[] point, Vector3[] normal, out PbrtHitInfo hitInfo)
    {
        hitInfo.position = Vector3.zero;
        hitInfo.normal = Vector3.zero;
        hitInfo.distance = 0;

        Vector3 edge1 = point[1] - point[0];
        Vector3 edge2 = point[2] - point[0];

        Vector3 pvec = Vector3.Cross(ray.dir, edge2);
        float det = Vector3.Dot(edge1, pvec);

        float epsilon = 1e-8f;
        if (Mathf.Abs(det) < epsilon)
        {
            return false;
        }

        float invDet = 1.0f / det;
        Vector3 tvec = ray.origin - point[0];

        // 計算重心座標 u
        float u = Vector3.Dot(tvec, pvec) * invDet;
        if (u < 0.0f || u > 1.0f)
        {
            return false;
        }

        // 計算重心座標 v
        Vector3 qvec = Vector3.Cross(tvec, edge1);
        float v = Vector3.Dot(ray.dir, qvec) * invDet;
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        float t = Vector3.Dot(edge2, qvec) * invDet;

        if (!rayInterval.Contains(t))
        {
            return false;
        }


        float w = 1.0f - u - v;
        hitInfo.position = ray.origin + ray.dir * t;
        hitInfo.distance = t;
        hitInfo.normal = Vector3.Normalize(w * normal[0] + u * normal[1] + v * normal[2]);

        return true;
    }
}