using System.Collections.Generic;
using UnityEngine;

// 通用參數字典
using PbrtParams = System.Collections.Generic.Dictionary<string, object>;

public class PbrtScene
{
    public PbrtCamera camera { get; set; }
    public PbrtFilm film { get; set; }
    public PbrtSampler sampler { get; set; }
    public List<PbrtShape> shapes { get; } = new List<PbrtShape>();

    public void Init()
    {
        film.Init();
        Vector2Int resolution = film.GetResolution();
        camera.Init(resolution.x, resolution.y);
    }
}

public class PbrtRay
{
    public Vector3 origin;
    public Vector3 dir;
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
}
public class PbrtSphere : PbrtShape { }
public class PbrtCylinder : PbrtShape { }

// 更新 PbrtTriangleMesh 以包含具體幾何數據
public class PbrtTriangleMesh : PbrtShape
{
    public int[] indices { get; set; }
    public Vector3[] vertices { get; set; }
    public Vector3[] normals { get; set; } // 可選
}