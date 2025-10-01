using System.Collections.Generic;
using UnityEngine;

// �q�ΰѼƦr��
using PbrtParams = System.Collections.Generic.Dictionary<string, object>;

public class PbrtScene
{
    public PbrtCamera Camera { get; set; }
    public PbrtFilm Film { get; set; }
    public PbrtSampler Sampler { get; set; }
    public List<PbrtShape> Shapes { get; } = new List<PbrtShape>();
}

public class PbrtCamera
{
    public Matrix4x4 WorldToCameraMatrix { get; set; }
    public string Type { get; set; } // e.g., "perspective"
    public PbrtParams Parameters { get; set; } = new PbrtParams();
}

public class PbrtFilm
{
    public string Type { get; set; } // e.g., "image"
    public PbrtParams Parameters { get; set; } = new PbrtParams();
}

public class PbrtSampler
{
    public string Type { get; set; } // e.g., "sobol"
    public PbrtParams Parameters { get; set; } = new PbrtParams();
}

public class PbrtLight
{
    public string Type { get; set; } // e.g., "area"
    public PbrtParams Parameters { get; set; } = new PbrtParams();
}

public class PbrtMaterial
{
    public string Type { get; set; } // e.g., "matte"
    public PbrtParams Parameters { get; set; } = new PbrtParams();

    public override string ToString()
    {
        return $"Material(Type: {Type}, Params: {Parameters.Count})";
    }
}

public abstract class PbrtShape
{
    public Matrix4x4 ObjectToWorld { get; set; }
    public PbrtLight AttachedLight { get; set; }
    // --- NEW: Property to hold the shape's material ---
    public PbrtMaterial Material { get; set; }
    public PbrtParams Parameters { get; set; } = new PbrtParams();
}
public class PbrtSphere : PbrtShape { }
public class PbrtCylinder : PbrtShape { }

// ��s PbrtTriangleMesh �H�]�t����X��ƾ�
public class PbrtTriangleMesh : PbrtShape
{
    public int[] Indices { get; set; }
    public Vector3[] Vertices { get; set; }
    public Vector3[] Normals { get; set; } // �i��
}