using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[RequireComponent(typeof(Camera))]
public class SceneBVHTest : MonoBehaviour
{
    public List<GameObject> ObjectsToRayTrace = new List<GameObject>();
    public bool AutomaticAddObjects = false;

    Camera _camera;
    SceneBVH _bvh;
    GameObject _mouse;
    List<Material> _materials;

    // Start is called before the first frame update
    void Start()
    {
        if (AutomaticAddObjects)
        {
            ObjectsToRayTrace.Clear();
            var renderers = GameObject.FindObjectsOfType<MeshRenderer>();
            ObjectsToRayTrace.AddRange(from R in renderers select R.gameObject);
        }

        _camera = GetComponent<Camera>();
        _mouse = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _mouse.transform.localScale = new Vector3(0.1f, 0.1f, 0.1f);
        _materials = new List<Material>();

        foreach (var obj in ObjectsToRayTrace)
        {
            Mesh m = obj.GetComponent<MeshFilter>().mesh;
            MeshRenderer mr = obj.GetComponent<MeshRenderer>();

            for (int sm = 0; sm < m.subMeshCount; sm++)
            {
                mr.materials[sm] = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                _materials.Add(mr.materials[sm]);
            }
        }

        _bvh = new SceneBVH(ObjectsToRayTrace);
    }

    // Update is called once per frame
    void Update()
    {
        foreach (var mat in _materials)
        {
            mat.color = Color.red;
        }
        _bvh.SyncMeshObjectsTransform();

        Ray cameraRay = _camera.ScreenPointToRay(Input.mousePosition);
        SceneBVH.ExtraHitInfo hit = _bvh.Trace(new PbrtRay { origin = cameraRay.origin, dir = cameraRay.direction }, Mathf.Infinity);

        if (hit.hitMesh != -1)
        {
            _mouse.SetActive(true);
            _mouse.transform.position = hit.hitInfo.position;
            _materials[hit.hitMesh].color = Color.green;
        }
        else
        {
            _mouse.SetActive(false);
        }
    }

    private void OnDrawGizmos()
    {
        if (_bvh  != null)
        {
            _bvh.DrawGizmos();
            _bvh.DrawTrianglesGizmos();
        }
    }
}
