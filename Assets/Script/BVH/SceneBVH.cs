using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEditor.PackageManager;
using UnityEngine;
using UnityEngine.Windows;
using static SceneBVH;

/**
 * 這個 class 會針對每一個 GameObject 中的 Mesh 的每一個 SubMesh 建一個 BVH
 * 當你做 Ray Trace 時，會依序將 Ray 轉到不同 GameObject 的區域座標系中，然後進行 BVH 的測試
 * 
 * 這個 class 適合 GameObject 很少，但三角面數量很多；每個 GameObject 都是剛體，只會移動、旋轉、縮放，裡面的三角型不會變形
 * 
 * 使用方式：
 * - 建構時傳入 GameObject 的列表
 * - 每一幀在使用前呼叫 SyncMeshObjectsTransform 來將每一個 GameObject 現在的 transform 記錄下來
 * - 使用 Trace(ray, rayDistance) 來計算 ray 打到哪個物件
 * 
 * - (Optional) 使用 UploadToShader 來將 BVH 樹和 Mesh 的 transform, indices, vertex, normals, uvs 等傳到 Compute Shader
 */
public class SceneBVH
{
    [StructLayout(LayoutKind.Sequential)]
    public struct BVHNode
    {
        public Vector4 minAABB, maxAABB; // AABB 中 xyz 最小和最大
        // 1. 當 indices_count > 0  -> _Indices [indices_offset, indices_offset + indices_count) 為這個節點包含的所有三角形
        // 2. 當 indices_count <= 0 -> _BVHs[indices_offset] 是左子樹，_BVHs[indices_offset + 1] 是右子樹
        public int indices_offset, indices_count;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MeshObject
    {
        public Matrix4x4 localToWorldMatrix; // 64
        public Matrix4x4 worldToLocalMatrix; // 64
        public int indices_offset;           // 4
        public int indices_count;            // 4
        public int bvhRoot;                  // 4
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ExtraHitInfo
    {
        public PbrtHitInfo hitInfo;
        public int hitMesh;
        public int hitIndexOffset;
        public Vector2 hitUV; // 重心座標，打到的點可以用 (1-u-v) * v0 + u * v1 + v * v2 內插而得
    };

    readonly List<GameObject> ObjectsInScene = new List<GameObject>();

    readonly List<MeshObject> _MeshObjects = new List<MeshObject>();
    readonly List<int> _Indices = new List<int>();
    readonly List<BVHNode> _BVHs = new List<BVHNode>();

    readonly List<Vector3> _Vertices = new List<Vector3>();
    readonly List<Vector3> _Normals = new List<Vector3>();
    readonly List<Vector2> _UVs = new List<Vector2>();

    ComputeBuffer _meshObjBuf, _indicesBuf, _bvhsBuf, _vertsBuf, _normalsBuf, _uvsBuf;

    /**
     * 建構子，傳入場景中的物件，並為其建構 BVH
     */
    public SceneBVH(List<GameObject> objects)
    {
        ObjectsInScene.AddRange(objects);

        for (int i = 0; i < objects.Count; i++)
        {
            Mesh mesh = objects[i].GetComponent<MeshFilter>().mesh;
            int vertex_offset = _Vertices.Count; // Mesh 中 index 0 代表的節點為 _Vertices[vertex_offset]
            
            // Vertex
            _Vertices.AddRange(mesh.vertices);

            // Normal
            if (mesh.normals != null && mesh.normals.Length == mesh.vertices.Length)
                _Normals.AddRange(mesh.normals);
            else
                _Normals.AddRange(from I in Enumerable.Range(0, mesh.vertices.Length) select new Vector3(0, 0, 0)); // 全填 (0,0,0)

            // UV
            if (mesh.uv != null && mesh.uv.Length == mesh.vertices.Length)
                _UVs.AddRange(mesh.uv);
            else
                _UVs.AddRange(from I in Enumerable.Range(0, mesh.vertices.Length) select new Vector2(0, 0)); // 全填 (0, 0)

            // 每個 submesh 自成一個 MeshObject
            for (int sm = 0; sm < mesh.subMeshCount; sm++)
            {
                int indicesOffset = _Indices.Count;
                var ids = mesh.GetTriangles(sm);
                for (int j = 0; j < ids.Length; j++) _Indices.Add(vertex_offset + ids[j]);

                var meshObject = new MeshObject
                {
                    localToWorldMatrix = objects[i].transform.localToWorldMatrix,
                    worldToLocalMatrix = objects[i].transform.localToWorldMatrix.inverse,
                    indices_offset = indicesOffset,
                    indices_count = ids.Length,
                    bvhRoot = _BVHs.Count
                };
                _MeshObjects.Add(meshObject);

                // 建立 BVH Root （包含 meshObject 中所有三角面）
                _BVHs.Add(new BVHNode
                {
                    indices_offset = indicesOffset, indices_count = ids.Length
                });
                InitAABB_SubdivideBVH(meshObject.bvhRoot);
            }
        }

        Debug.Log(string.Format("total mesh objects = {0}, indices= {1}, vertices= {2}", _MeshObjects.Count,_Indices.Count, _Vertices.Count));
    }

    /// <summary>
    /// 將每個 GameObject 的 localToWorld Transform 計錄下來
    /// </summary>
    public void SyncMeshObjectsTransform()
    {
        int meshObjectIdx = 0;

        foreach (var obj in ObjectsInScene)
        {
            Mesh mesh = obj.GetComponent<MeshFilter>().mesh;

            for (int sm = 0; sm < mesh.subMeshCount; ++sm)
            {
                MeshObject meshObject = _MeshObjects[meshObjectIdx];

                meshObject.localToWorldMatrix = obj.transform.localToWorldMatrix;
                meshObject.worldToLocalMatrix = obj.transform.localToWorldMatrix.inverse;

                _MeshObjects[meshObjectIdx] = meshObject;
                ++meshObjectIdx;
            }
        }
    }

    /// <summary>
    /// 將 bvh 儲存的內容傳進 shader，格式如同 SceneData.hlsl 描述的那樣
    /// </summary>
    public void UploadToShader(ComputeShader s, int kernelIndex)
    {
        if (_meshObjBuf == null)
        {
            _meshObjBuf = new ComputeBuffer(_MeshObjects.Count, 64 + 64 + 4 + 4 + 4);
            
            _indicesBuf = new ComputeBuffer(_Indices.Count, sizeof(int));
            _indicesBuf.SetData(_Indices);
            _bvhsBuf = new ComputeBuffer(_BVHs.Count, 16 + 16 + 4 + 4);
            _bvhsBuf.SetData(_BVHs);
            _vertsBuf = new ComputeBuffer(_Vertices.Count, 3 * sizeof(float));
            _vertsBuf.SetData(_Vertices);
            _normalsBuf = new ComputeBuffer(_Normals.Count, 3 * sizeof(float));
            _normalsBuf.SetData(_Normals);
            _uvsBuf = new ComputeBuffer(_UVs.Count, 2 * sizeof(float));
            _uvsBuf.SetData(_UVs);
        }

        _meshObjBuf.SetData(_MeshObjects); // 每幀更新

        s.SetBuffer(kernelIndex, "_MeshObjects", _meshObjBuf);
        s.SetBuffer(kernelIndex, "_Indices", _indicesBuf);
        s.SetBuffer(kernelIndex, "_BVHs", _bvhsBuf);
        s.SetBuffer(kernelIndex, "_Vertices", _vertsBuf);
        s.SetBuffer(kernelIndex, "_Normals", _normalsBuf);
        s.SetBuffer(kernelIndex, "_UVs", _uvsBuf);
    }

#region 建構 BVH

    /// <summary>
    /// 對於 _Indices 陣列中 [indices_offset, indices_offset + indices_count) 內指向的頂點計算 AABB。
    /// 參數 M 可以在計算 AABB 時，將頂點從 Local Space 轉換到 World Space（傳 Identity ，以保持在 Local Space 下計算）
    /// </summary>
    void FindAABB(int indices_offset, int indices_count, Matrix4x4 M, out Vector4 minAABB, out Vector4 maxAABB)
    {
        minAABB = new( Mathf.Infinity,  Mathf.Infinity,  Mathf.Infinity, 1);
        maxAABB = new(-Mathf.Infinity, -Mathf.Infinity, -Mathf.Infinity, 1);

        // 對於包含的每個點
        for (int i = indices_offset; i < indices_offset + indices_count; i++)
        {
            Vector3 pos = M.MultiplyPoint3x4(_Vertices[_Indices[i]]);

            minAABB.x = Mathf.Min(minAABB.x, pos.x);
            minAABB.y = Mathf.Min(minAABB.y, pos.y);
            minAABB.z = Mathf.Min(minAABB.z, pos.z);
            maxAABB.x = Mathf.Max(maxAABB.x, pos.x);
            maxAABB.y = Mathf.Max(maxAABB.y, pos.y);
            maxAABB.z = Mathf.Max(maxAABB.z, pos.z);
        }
    }

    /// <summary>
    /// 對 _BVHs[i] 節點進行初始化 AABB + 切割
    /// </summary>
    void InitAABB_SubdivideBVH(int i)
    {
        BVHNode root = _BVHs[i];

        // AABB
        FindAABB(root.indices_offset, root.indices_count, Matrix4x4.identity,
                    out root.minAABB, out root.maxAABB);

        int triangle_count = root.indices_count / 3;

        // subdivide
        if (triangle_count > 2)
        {
            // 1. 找 AABB 最長邊
            Vector4 extend = root.maxAABB - root.minAABB;
            int axis = 0; // x
            if (extend.y > extend.x) axis = 1; // y
            if (extend.z > extend[axis]) axis = 2; // z

            // 2. 找每個三角面的中點 （只需要在最長軸上的中點即可）
            float[] triangleCenter = new float[triangle_count];
            for (int idx = root.indices_offset; idx < root.indices_offset + root.indices_count; idx += 3)
            {
                int triangleIdx = (idx - root.indices_offset) / 3;
                triangleCenter[triangleIdx] = (_Vertices[_Indices[idx    ]][axis] 
                                             + _Vertices[_Indices[idx + 1]][axis] 
                                             + _Vertices[_Indices[idx + 2]][axis]) / 3.0f;
            }

            // 3. 找分割平面
            System.Array.Sort(triangleCenter);
            float pivot = triangleCenter[triangle_count / 2];

            // 4. 將所有三角形分類，左邊的 <= pivot，右邊的 > pivot
            int right_indices_offset = root.indices_offset;
            for (int idx = root.indices_offset; idx < root.indices_offset + root.indices_count; idx += 3)
            {
                float center = (_Vertices[_Indices[idx    ]][axis]
                              + _Vertices[_Indices[idx + 1]][axis]
                              + _Vertices[_Indices[idx + 2]][axis]) / 3.0f; // 這看起來有點蠢，同樣的東西算兩次，但感覺為此建一個雜湊表反而更耗時、複雜

                if (center <= pivot)
                {
                    // 將三角形的三頂點換到左邊
                    (_Indices[right_indices_offset    ], _Indices[idx    ]) = (_Indices[idx    ], _Indices[right_indices_offset    ]);
                    (_Indices[right_indices_offset + 1], _Indices[idx + 1]) = (_Indices[idx + 1], _Indices[right_indices_offset + 1]);
                    (_Indices[right_indices_offset + 2], _Indices[idx + 2]) = (_Indices[idx + 2], _Indices[right_indices_offset + 2]);
                    right_indices_offset += 3;
                }
                // 否則不動、留在右邊
            }

            //Debug.Log(string.Format("Divide: {0} ~ {1} ~ {2}", root.indices_offset, right_indices_offset, root.indices_offset + root.indices_count));

            // 如果不是全在同一邊
            if (right_indices_offset != root.indices_offset && right_indices_offset != root.indices_offset + root.indices_count)
            {
                // 5. 建立左子樹
                int leftNodeIdx = _BVHs.Count;
                _BVHs.Add(new BVHNode
                {
                    indices_offset = root.indices_offset,
                    indices_count = (right_indices_offset - root.indices_offset)
                });

                // 6. 建立右子樹
                _BVHs.Add(new BVHNode
                {
                    indices_offset = right_indices_offset,
                    indices_count = root.indices_offset + root.indices_count - right_indices_offset
                });

                InitAABB_SubdivideBVH(leftNodeIdx);
                InitAABB_SubdivideBVH(leftNodeIdx + 1);

                root.indices_offset = leftNodeIdx;
                root.indices_count = 0;
            }
        }

        _BVHs[i] = root;
    }

#endregion

    public ExtraHitInfo Trace(PbrtRay ray, float rayDistance)
    {
        ExtraHitInfo bestHit = new ExtraHitInfo
        {
            hitInfo = new PbrtHitInfo { distance = rayDistance, normal = Vector3.zero, position = new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity) },
            hitMesh = -1,
            hitIndexOffset = -1,
            hitUV = new Vector2(-1f,  -1f),
        };

        for (int i = 0; i < _MeshObjects.Count; ++i)
        {
            // 將 Ray 轉到該 Mesh Object 的區域座標系下，然後和 BVH 樹比較
            Matrix4x4 worldToLocal = _MeshObjects[i].worldToLocalMatrix;
            PbrtRay localRay = new PbrtRay
            {
                origin = worldToLocal.MultiplyPoint3x4(ray.origin),
                dir = worldToLocal.MultiplyVector(ray.dir).normalized
            };

            float localDistance = bestHit.hitInfo.distance;
            if (localDistance != Mathf.Infinity)
            {
                Vector3 rayEnd = ray.origin + ray.dir.normalized * bestHit.hitInfo.distance;
                localDistance = Vector3.Distance(localRay.origin, worldToLocal.MultiplyPoint3x4(rayEnd));
            }

            // 每個 MeshObject 自己的 BVH 存放在 Local Space 下，所以 ray 要轉到區域座標
            ExtraHitInfo bvhHit = TraceBVH(_MeshObjects[i].bvhRoot, localRay, localDistance);

            // 將 bvhHit 轉回世界座標
            bvhHit.hitInfo.position = _MeshObjects[i].localToWorldMatrix.MultiplyPoint3x4(bvhHit.hitInfo.position);
            bvhHit.hitInfo.normal = _MeshObjects[i].localToWorldMatrix.MultiplyVector(bvhHit.hitInfo.normal);
            bvhHit.hitInfo.distance = Vector3.Distance(ray.origin, bvhHit.hitInfo.position);
            bvhHit.hitMesh = i;

            // 如果在這個 Mesh 上打得更近
            if (bvhHit.hitInfo.distance < bestHit.hitInfo.distance)
                bestHit = bvhHit;
        }

        return bestHit;
    }

#region 求 Ray 的交點
    /// <summary>
    /// 在 BVHroot 這棵 BVH 樹內進行 Trace
    /// </summary>
    ExtraHitInfo TraceBVH(int BVHroot, PbrtRay ray, float rayDistance)
    {
        ExtraHitInfo bestHit = new ExtraHitInfo
        {
            hitInfo = new PbrtHitInfo { distance = rayDistance, normal = Vector3.zero, position = new Vector3(Mathf.Infinity, Mathf.Infinity, Mathf.Infinity) },
            hitIndexOffset = -1,
            hitMesh = -1,
            hitUV = new Vector2(-1, -1)
        };
        if (BVHroot == -1)
            return bestHit;

        BVHNode root = _BVHs[BVHroot];

        // 如果 ray 打到 AABB
        if (IntersectAABB(ray, rayDistance, root.minAABB, root.maxAABB))
        {
            // 是葉子
            if (root.indices_count > 0)
            {
                // 對於包含的每一個三角形
                for (int i = root.indices_offset; i < root.indices_offset + root.indices_count; i += 3)
                {
                    float u, v;
                    if (IntersectTriangle_MT97(ray, _Vertices[_Indices[i]], _Vertices[_Indices[i + 1]], _Vertices[_Indices[i + 2]]
                                                , ref bestHit.hitInfo, out u, out v))
                    {
                        bestHit.hitIndexOffset = i;
                        bestHit.hitUV = new Vector2(u, v);
                    }
                }

                return bestHit;
            }
            // 是中間節點
            else
            {
                // 拜訪左右子樹，看哪一個子樹的 distance 最小
                bestHit = TraceBVH(root.indices_offset, ray, bestHit.hitInfo.distance); 
                ExtraHitInfo rightHit = TraceBVH(root.indices_offset + 1, ray, bestHit.hitInfo.distance);

                if (rightHit.hitInfo.distance < bestHit.hitInfo.distance)
                    return rightHit;
                else
                    return bestHit;
            }
        }

        return bestHit;
    }

    /// <summary>
    /// 求解 ray 是否通過 AABB，如有通過回傳 true
    /// </summary>
    bool IntersectAABB(PbrtRay ray, float rayDistance, Vector4 minAABB, Vector4 maxAABB)
    {
        // Note: ray上的點 = origin + t * dir, https://www.rose-hulman.edu/class/cs/csse451/AABB/
        //       t = (ray上的點 - origin) / dir
        float tenter = -Mathf.Infinity, tout = Mathf.Infinity;

        // 在每個維度中，t 較小的為 Ray 的入射平面，t 較大的為 Ray 的射出平面
        // 在所有維度中的「入射」平面中選 t 「最大」的那個為 tenter、在所有維度的「射出」平面選 t 「最小」的那個為 tout
        // tenter <= tout && tout > 0    <=>   Ray 在 [tenter, tout] 的範圍位於 AABB 內   <=>  Ray 穿過 AABB

        // X axis
        if (ray.dir.x != 0.0f)
        {
            float tx1 = (minAABB.x - ray.origin.x) / ray.dir.x;
            float tx2 = (maxAABB.x - ray.origin.x) / ray.dir.x;
            tenter = Mathf.Max(tenter, Mathf.Min(tx1, tx2));
            tout = Mathf.Min(tout, Mathf.Max(tx1, tx2));
        }
        else if (ray.origin.x < minAABB.x || ray.origin.x > maxAABB.x)
        {
            return false; // 平行且在盒子外
        }

        // Y axis
        if (ray.dir.y != 0.0f)
        {
            float ty1 = (minAABB.y - ray.origin.y) / ray.dir.y;
            float ty2 = (maxAABB.y - ray.origin.y) / ray.dir.y;
            tenter = Mathf.Max(tenter, Mathf.Min(ty1, ty2));
            tout = Mathf.Min(tout, Mathf.Max(ty1, ty2));
        }
        else if (ray.origin.y < minAABB.y || ray.origin.y > maxAABB.y)
        {
            return false;
        }

        // Z axis
        if (ray.dir.z != 0.0f)
        {
            float tz1 = (minAABB.z - ray.origin.z) / ray.dir.z;
            float tz2 = (maxAABB.z - ray.origin.z) / ray.dir.z;
            tenter = Mathf.Max(tenter, Mathf.Min(tz1, tz2));
            tout = Mathf.Min(tout, Mathf.Max(tz1, tz2));
        }
        else if (ray.origin.z < minAABB.z || ray.origin.z > maxAABB.z)
        {
            return false;
        }

        // Ray 直線在 distanceT 的位置為 Ray 的結尾
        float distanceT = rayDistance / ray.dir.magnitude;
        // distanceT > tenter -> Ray 在結束以前有進入 AABB 的範圍內
        return tout >= tenter && tout > 0 && distanceT > tenter;
    }

    bool IntersectTriangle_MT97(PbrtRay ray, Vector3 vert0, Vector3 vert1, Vector3 vert2,
                                    ref PbrtHitInfo hitInfo, out float u, out float v)
    {
        u = v = -1;

        // find vectors for two edges sharing vert0
        Vector3 edge1 = vert1 - vert0;
        Vector3 edge2 = vert2 - vert0;

        // begin calculating determinant - also used to calculate U parameter
        Vector3 pvec = Vector3.Cross(ray.dir, edge2);

        // if determinant is near zero, ray lies in plane of triangle
        float det = Vector3.Dot(edge1, pvec);

        // 將 abs 刪掉 -> Back Face Culling
        const float EPSILON = 1e-8f;
        if (Mathf.Abs(det) < EPSILON)
            return false;
        float inv_det = 1.0f / det;

        // calculate distance from vert0 to ray origin
        Vector3 tvec = ray.origin - vert0;

        // calculate U parameter and test bounds
        u = Vector3.Dot(tvec, pvec) * inv_det;
        if (u < 0.0 || u > 1.0f)
            return false;

        // prepare to test V parameter
        Vector3 qvec = Vector3.Cross(tvec, edge1);

        // calculate V parameter and test bounds
        v = Vector3.Dot(ray.dir, qvec) * inv_det;
        if (v < 0.0 || u + v > 1.0f)
            return false;

        // calculate distance, ray intersects triangle
        float t = Vector3.Dot(edge2, qvec) * inv_det;
        if (0 < t && t < hitInfo.distance)
        {
            hitInfo.distance = t;
            hitInfo.position = (1.0f - u - v) * vert0 + u * vert1 + v * vert2;
            hitInfo.normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
            if (Vector3.Dot(ray.dir, hitInfo.normal) > 0)
                hitInfo.normal = -hitInfo.normal;

            return true;
        }

        return false;
    }
#endregion

#region Debug
    public void DrawGizmos()
    {
        foreach (var obj in _MeshObjects)
        {
            Vector4 minAABB, maxAABB;
            FindAABB(obj.indices_offset, obj.indices_count, obj.localToWorldMatrix, out minAABB, out maxAABB);
            Gizmos.DrawWireCube((Vector3)((minAABB + maxAABB) / 2f), (Vector3)(maxAABB - minAABB));
        }
    }

    public void DrawTrianglesGizmos()
    {
        foreach (var obj in _MeshObjects)
        {
            DrawBVHNode(obj.bvhRoot, obj.localToWorldMatrix);
        }
    }

    private void DrawBVHNode(int root, Matrix4x4 localToWorld)
    {
        BVHNode node = _BVHs[root];

        // leaf
        if (node.indices_count > 0)
        {
            Gizmos.color = Color.gray;
            Mesh m = CreateRotatedBox(node.minAABB, node.maxAABB, localToWorld);
            Gizmos.DrawWireMesh(m);
            GameObject.Destroy(m);
        }
        else
        {
            DrawBVHNode(node.indices_offset, localToWorld);
            DrawBVHNode(node.indices_offset + 1, localToWorld);

        }
    }

    static private Mesh CreateRotatedBox(Vector3 bmin, Vector3 bmax, Matrix4x4 M)
    {
        Mesh mesh = new Mesh();

        // --- 1️⃣ 八個頂點（未變換前）---
        Vector3[] vertices = new Vector3[8];
        vertices[0] = new Vector3(bmin.x, bmin.y, bmin.z);
        vertices[1] = new Vector3(bmax.x, bmin.y, bmin.z);
        vertices[2] = new Vector3(bmax.x, bmax.y, bmin.z);
        vertices[3] = new Vector3(bmin.x, bmax.y, bmin.z);
        vertices[4] = new Vector3(bmin.x, bmin.y, bmax.z);
        vertices[5] = new Vector3(bmax.x, bmin.y, bmax.z);
        vertices[6] = new Vector3(bmax.x, bmax.y, bmax.z);
        vertices[7] = new Vector3(bmin.x, bmax.y, bmax.z);

        // --- 2️⃣ 每個頂點乘上矩陣 M ---
        for (int i = 0; i < 8; i++)
        {
            vertices[i] = M.MultiplyPoint3x4(vertices[i]);
        }

        // --- 3️⃣ 定義立方體的三角形面 ---
        int[] triangles = new int[]
        {
            // 前面
            0, 2, 1, 0, 3, 2,
            // 後面
            4, 5, 6, 4, 6, 7,
            // 左面
            0, 7, 3, 0, 4, 7,
            // 右面
            1, 2, 6, 1, 6, 5,
            // 上面
            2, 3, 7, 2, 7, 6,
            // 下面
            0, 1, 5, 0, 5, 4
        };

        // --- 4️⃣ 建立 Mesh ---
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
#endregion
}
