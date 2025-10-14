using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class UIController_2 : MonoBehaviour
{
    [SerializeField] private VPL_Render rayTracer = null;
    [SerializeField] private TextMeshProUGUI indexText = null;
    
    public void ClickRenderButton()
    {
        //rayTracer.Render();
    }

    public void ClickVisualizeVPLToggle(bool b)
    {
        rayTracer.SetVplVisualizeSphereActive(b);
    }

    public void ClickOnlyOneVPLToggle(bool b)
    {
        rayTracer.SetIsOnlyOneVpl(b);
    }

    public void ClickIncreaseButton()
    {
        rayTracer.IncreaseOnlyOneVplIndex();

        if (indexText != null) 
        {
            indexText.text = rayTracer.GetOnlyOneVplIndex().ToString();
        }
    }

    public void ClickDecreaseButton()
    {
        rayTracer.DecreseOnlyOneVplIndex();

        if (indexText != null)
        {
            indexText.text = rayTracer.GetOnlyOneVplIndex().ToString();
        }
    }
}
