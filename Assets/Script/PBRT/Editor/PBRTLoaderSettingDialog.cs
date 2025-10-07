using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public class PBRTLoaderSettingDialog : EditorWindow
{
    [SerializeField]
    private VisualTreeAsset m_VisualTreeAsset = default;
    private bool m_IsSettingValid = false;

    public string FilePath;

    //[MenuItem("Window/UI Toolkit/PBRTLoaderSettingDialog")]
    public static string ShowDialog()
    {
        PBRTLoaderSettingDialog wnd = GetWindow<PBRTLoaderSettingDialog>();
        wnd.position = new Rect(200, 100, 500, 400);
        wnd.titleContent = new GUIContent("PBRTLoaderSettingDialog");
        wnd.ShowModalUtility();

        if (wnd.m_IsSettingValid)
        {
            return wnd.FilePath;
        }

        return null;
    }

    public void CreateGUI()
    {
        // Each editor window contains a root VisualElement object
        VisualElement root = rootVisualElement;

        // Instantiate UXML
        VisualElement labelFromUXML = m_VisualTreeAsset.Instantiate();
        root.Add(labelFromUXML);
        root.Q<TextField>("textfield_pbrtfile").value = Application.dataPath + "/StreamingAssets/sibenik-whitted.pbrt";

        // bind Event
        root.Q<Button>("button_selectpbrtfile").RegisterCallback<ClickEvent>(OnSelectFileClicked);
        root.Q<Button>("button_confirm").RegisterCallback<ClickEvent>(OnConfirmClicked);
    }

    private void OnSelectFileClicked(ClickEvent evt)
    {
        string path = EditorUtility.OpenFilePanelWithFilters("PBRT to load", Application.dataPath, new string[] { "PBRT scene file", "pbrt" });
        if (path.Length > 0)
            rootVisualElement.Q<TextField>("textfield_pbrtfile").value = path;
    }

    private void OnConfirmClicked(ClickEvent evt)
    {
        string message = "";

        string path = rootVisualElement.Q<TextField>("textfield_pbrtfile").value;
        if (path.Length == 0)
        {
            message += "Please select the pbrt file\n";
        }

        if (message.Length > 0)
        {
            EditorUtility.DisplayDialog("Error", message, "OK");
            return;
        }

        m_IsSettingValid = true;
        FilePath = path;
        Close();
    }
}
