using System.Collections;
using System.Collections.Generic;
//using System.Text.Json.Serialization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class UIController_1 : MonoBehaviour
{
    [SerializeField] private RayTracer_ShaderVer rayTracer = null;
    [SerializeField] private TextMeshProUGUI aoSampleText = null;
    [SerializeField] private TextMeshProUGUI aoRadiusText = null;
    [SerializeField] private TextMeshProUGUI aoIntensityText = null;
    [SerializeField] private TextMeshProUGUI aoPanelText = null;
    [SerializeField] private TextMeshProUGUI jssPanelText = null;
    [SerializeField] private TextMeshProUGUI jssSPPText = null;
    public Slider aoIntensitySlider;
    public Slider jssSppSlider;
    public GameObject aoPanel;
    public GameObject jssPanel;
    void Start()
    {
        aoPanel.SetActive(false);
        jssPanel.SetActive(false);
    }

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
        if (rayTracer.AoParameters._AORadius > 0.01)
        {
            rayTracer.AoParameters._AORadius /= 2;
        }
        aoRadiusText.text = rayTracer.AoParameters._AORadius.ToString("F2");
    }
    public void UpdateIntensity()
    {
        rayTracer.AoParameters._AOIntensity = aoIntensitySlider.value;
        aoIntensityText.text = aoIntensitySlider.value.ToString("F2");
    }

    public void ClickSSAOToggle(bool b)
    {
        rayTracer.DoSSAO = b;
    }

    public void ClickBumpMapToggle(bool b)
    {
        rayTracer.UseBumpMap = b;
    }
    public void ClickJssToggle(bool b)
    {
        rayTracer.jssParameters._JitterOn = b ? 1 : 0;
    }
    public void UpdateSPP()
    {
        rayTracer.jssParameters._SPP = (int)jssSppSlider.value;
        jssSPPText.text = jssSppSlider.value.ToString();
    }
    public void ClickFresnelToggle(bool b)
    {
        rayTracer.UseFresnel = b;
    }
    

    

    public void ClickBackButton()
    {
        SceneManager.LoadScene("Menu");
    }
    public void ClickOpenAOPanelButton()
    {
        if (aoPanel.activeSelf)
        {
            aoPanel.SetActive(false);
            aoPanelText.text = ">";
        }
        else
        {
            aoPanel.SetActive(true);
            aoPanelText.text = "<";
        }
    }
    public void ClickOpenJSSPanelButton()
    {
        if (jssPanel.activeSelf)
        {
            jssPanel.SetActive(false);
            jssPanelText.text = ">";
        }
        else
        {
            jssPanel.SetActive(true);
            jssPanelText.text = "<";
        }
    }
}
