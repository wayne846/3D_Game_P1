using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIController_1 : MonoBehaviour
{
    [SerializeField] private RayTracer_ShaderVer rayTracer = null;
    [SerializeField] private TextMeshProUGUI aoSampleText = null;
    [SerializeField] private TextMeshProUGUI aoRadiusText = null;

    public void ClickRenderButton()
    {
        rayTracer.enabled = true;
    }

    public void ClickAOToggle(bool b)
    {
        rayTracer.AoParameters._AOUse = b ? 1 : 0;
    }
    public void ClickIncreaseAOSampleButton()
    {
        rayTracer.AoParameters._AOSamples *= 2;
        aoSampleText.text = rayTracer.AoParameters._AOSamples.ToString();
    }

    public void ClickDecreaseAOSampleButton()
    {
        if(rayTracer.AoParameters._AOSamples > 1)
        {
            rayTracer.AoParameters._AOSamples /= 2;
        }
        aoSampleText.text = rayTracer.AoParameters._AOSamples.ToString();
    }

    public void ClickIncreaseAORadiusButton()
    {
        rayTracer.AoParameters._AORadius *= 2;
        aoRadiusText.text = rayTracer.AoParameters._AORadius.ToString("F2");
    }

    public void ClickDecreaseAORadiusButton()
    {
        if(rayTracer.AoParameters._AORadius > 0.01)
        {
            rayTracer.AoParameters._AORadius /= 2;
        }
        aoRadiusText.text = rayTracer.AoParameters._AORadius.ToString("F2");
    }

    public void ClickSSAOToggle(bool b)
    {
        rayTracer.DoSSAO = b;
    }

    public void ClickBumpMapToggle(bool b)
    {
        rayTracer.UseBumpMap = b;
    }
}
