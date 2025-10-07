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
                        triangles = T.indices
                    };
                    NewMesh.AddComponent<MeshFilter>();
                    NewMesh.GetComponent<MeshFilter>().mesh = mesh;
                    NewMesh.AddComponent<MeshRenderer>();
                    Undo.RegisterCreatedObjectUndo(NewMesh, "載入PBRT");
                    break;

                case PbrtCylinder C:
                    Debug.LogWarning("Cylinder Not Implemented");
                    break;

                case PbrtSphere S:
                    Debug.LogWarning("Sphere Not Implemented");
                    break;
            }
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }
}