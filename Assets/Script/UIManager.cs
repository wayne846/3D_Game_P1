using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[DefaultExecutionOrder(-100)]
public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject uiCtrl = null;

    bool uiActive = false;

    public List<GameObject> objectsToResetPos = new();
    private List<Matrix4x4> objectTransform = new();
    private RayTracer_ShaderVer tracer = null;

    // Start is called before the first frame update
    void Start()
    {
        if(SceneManager.GetActiveScene().name == "Menu")
        {
            uiActive = true;
        }
        if (uiCtrl != null)
        {
            uiCtrl.SetActive(uiActive);
        }

        objectTransform.Clear();
        foreach (GameObject o in objectsToResetPos)
        {
            objectTransform.Add(Matrix4x4.TRS(o.transform.localPosition, o.transform.localRotation, o.transform.localScale));
        }

        tracer = FindObjectOfType<RayTracer_ShaderVer>();
    }

    private void OnDisable()
    {
        Time.timeScale = 1;
    }


    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
        {
            uiActive = !uiActive;
            if (uiCtrl != null)
            {
                uiCtrl.SetActive(uiActive);
            }
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            for (int i = 0; i < objectsToResetPos.Count; i++)
            {
                var T = objectsToResetPos[i].transform;
                T.SetLocalPositionAndRotation(objectTransform[i].GetPosition(), objectTransform[i].rotation);
                T.localScale = objectTransform[i].lossyScale;
            }
        }
        else if (Input.GetKeyDown(KeyCode.T))
        {
            Time.timeScale = (Time.timeScale == 0 ? 1 : 0);
        }
        else if (Input.GetKeyDown(KeyCode.Escape))
        {
            if (tracer != null)
                tracer.enabled = !tracer.enabled;
        }
        else if (Input.GetKeyDown(KeyCode.Alpha1))
        {
            if (tracer != null)
            {
                if (!tracer.enabled)
                {
                    tracer.OnlyRenderOneTime = true;
                    tracer.enabled = true;
                }
                else
                {
                    tracer.OnlyRenderOneTime = !tracer.OnlyRenderOneTime;
                }
            }
        }
        else if (Input.GetKeyDown(KeyCode.Alpha2))
        {
            if (tracer != null)
                tracer.ExportTexture();
        }
    }
}
