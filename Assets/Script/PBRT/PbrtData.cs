using System.Collections.Generic;
using UnityEngine;

// �q�ΰѼƦr��
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

    // �x�s�۾��Ѽ�
    Vector3 _eye;
    float _fov;
    int _filmWidth;
    int _filmHeight;

    // �۾��y�Шt�򩳦V�q (�@�ɪŶ�)
    Vector3 _forward;
    Vector3 _right;
    Vector3 _up;

    // �p��X���۾������Ѽ�
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

        // 3. �w���p�����|�Ψ쪺�ȡA�קK�b GenerateRay �����ƭp��
        _aspectRatio = (float)_filmWidth / _filmHeight;
        _tanHalfFovY = Mathf.Tan(_fov * 0.5f * Mathf.Deg2Rad);
    }

    /// <summary>
    /// �ھڹ����y�в��ͤ@�����u
    /// </summary>
    /// <param name="x">������ x �y�� (���U���� 0)</param>
    /// <param name="y">������ y �y�� (���U���� 0)</param>
    /// <returns>�@���q�۾����I�X�o�A��L�������ߪ����u</returns>
    public PbrtRay GenerateRay(int x, int y)
    {
        // �B�J 1: �N�����y���ഫ�� [-1, 1] �����W�Ƹ˸m�y�� (NDC)
        // �ϥ� x + 0.5 �M y + 0.5 �ӹ�ǹ�������
        float ndcX = (2.0f * (x + 0.5f) / _filmWidth) - 1.0f;
        float ndcY = (2.0f * (y + 0.5f) / _filmHeight) - 1.0f;

        // �B�J 2: �ھ� FOV �M���e��A�p��b�۾��Ŷ������������������y��
        float cameraX = ndcX * _aspectRatio * _tanHalfFovY;
        float cameraY = ndcY * _tanHalfFovY;

        // �B�J 3: �N�۾��Ŷ���V�ഫ���@�ɪŶ���V
        // ��V = 1.0 * �e�� + cameraX * �k�� + cameraY * �W��
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

// ��s PbrtTriangleMesh �H�]�t����X��ƾ�
public class PbrtTriangleMesh : PbrtShape
{
    public int[] indices { get; set; }
    public Vector3[] vertices { get; set; }
    public Vector3[] normals { get; set; } // �i��
}