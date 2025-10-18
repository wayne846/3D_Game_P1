using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIController_3 : MonoBehaviour
{

    public void ClickWhittedButton()
    {
        SceneManager.LoadScene("02_Whitted_Church");
    }
    public void ClickVPLButton()
    {
        SceneManager.LoadScene("03_Virtual_Point_Light");
    }
}