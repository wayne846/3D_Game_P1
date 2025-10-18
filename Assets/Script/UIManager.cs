using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject uiCtrl = null;

    bool uiActive = false;

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
    }

    // Update is called once per frame
    void Update()
    {
        if (Input.GetKeyDown(KeyCode.U))
        {
            uiActive = !uiActive;
            if (uiCtrl != null) {
                uiCtrl.SetActive(uiActive);
            }
        }
    }
}
