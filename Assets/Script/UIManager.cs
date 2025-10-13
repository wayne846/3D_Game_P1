using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class UIManager : MonoBehaviour
{
    [SerializeField] private GameObject uiCtrl = null;

    bool uiActive = false;

    // Start is called before the first frame update
    void Start()
    {
        
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
