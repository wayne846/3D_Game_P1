using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIController_1 : MonoBehaviour
{
    [SerializeField] private RayTracer_ShaderVer rayTracer = null;

    public void ClickRenderButton()
    {
        rayTracer.enabled = true;
    }
}
