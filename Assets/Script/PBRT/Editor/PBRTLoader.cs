using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

class PBRTLoader
{
    [MenuItem("PBRT/Load Scene From File", priority =1)]
    static void Load()
    {
        string path = PBRTLoaderSettingDialog.ShowDialog();
        if (path == null)
            return;

        // 解析 PBRT 檔
        Debug.Log(path);
        var parser = new PbrtParser();
        var scene = parser.Parse(path);

        // 設置 Camera
        if (Camera.main != null && scene.camera != null)
        {
            var camera = Camera.main;
            camera.transform.position = scene.camera.GetEyePosition();
            camera.transform.rotation = Quaternion.LookRotation(scene.camera.GetForward(), scene.camera.GetUp());
            camera.fieldOfView        = scene.camera.GetFov();
            camera.orthographic       = (scene.camera.type == "orthographic");
        }

        // 擺上讀到的 Mesh
        foreach (var shape in scene.shapes)
        {
            // 建立 Material
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (shape.material != null) 
            {
                if (shape.material.Kd.GetType() == typeof(Texture2D))
                    mat.SetTexture("_BaseMap", (Texture2D)shape.material.Kd);
                else
                {
                    Vector3 Kd = (Vector3)shape.material.Kd;
                    mat.SetColor("_BaseColor", new Color(Kd.x, Kd.y, Kd.z, 1));
                }
                mat.name = "Ks = " + shape.material.Ks.ToString() + "; Kt = " + shape.material.Kt.ToString();
            }

            switch (shape)
            {
                case PbrtTriangleMesh T:
                    GameObject NewMesh = new GameObject("Loaded Mesh");
                    NewMesh.transform.localScale = T.objectToWorld.ExtractScale();
                    NewMesh.transform.position = T.objectToWorld.ExtractPosition();
                    NewMesh.transform.rotation = T.objectToWorld.ExtractRotation();

                    Mesh mesh = new Mesh
                    {
                        vertices = T.vertices,
                        normals = T.normals,
                        triangles = T.indices,
                        uv = T.uvs
                    };
                    NewMesh.AddComponent<MeshFilter>();
                    NewMesh.GetComponent<MeshFilter>().mesh = mesh;
                    NewMesh.AddComponent<MeshRenderer>();
                    NewMesh.GetComponent<MeshRenderer>().material = mat;
                    Undo.RegisterCreatedObjectUndo(NewMesh, "載入PBRT");
                    break;

                case PbrtCylinder C:
                    Debug.LogWarning("Cylinder Not Implemented");
                    break;

                case PbrtSphere S:
                    if (shape.attachedLight != null)
                    {
                        GameObject lightObj = new GameObject("Point Light");
                        // 將它放在場景中的位置
                        lightObj.transform.position = shape.objectToWorld * new Vector4(0, 0, 0, 1);
                        // 加上 Light Component
                        Light lightComp = lightObj.AddComponent<Light>();
                        // 設定為點光
                        lightComp.type = LightType.Point;
                        // 設定顏色
                        Vector3 color = (Vector3)shape.attachedLight.parameters["L"];
                        lightComp.color = new Color(color.x, color.y, color.z, 1);
                    }
                    break;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}