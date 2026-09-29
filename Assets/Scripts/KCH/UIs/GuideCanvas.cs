using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GuideCanvas : MonoBehaviour
{
    [SerializeField]
    private Text _text;
    [SerializeField]
    private Button _button;

    public string Text { private get; set; }

    private InputAction _confirmAction;

    void Awake()
    {
        _confirmAction = Managers.Input.UIMap.Confirm;
    }

    void Update()
    {
        _text.text = Util.ReplaceBindingName(Text);
    }

    void OnEnable()
    {
        _confirmAction.performed += OnConfirmAction;
    }

    void OnDisable()
    {
        _confirmAction.performed -= OnConfirmAction;
    }

    public void OnButtonClicked()
    {
        gameObject.SetActive(false);
        Managers.Input.SetInputMode(InputMode.Player);
    }

    private void OnConfirmAction(InputAction.CallbackContext context)
    {
        GameObject selectedObject = EventSystem.current.currentSelectedGameObject;
        if (selectedObject == null)
        {
            EventSystem.current.SetSelectedGameObject(_button.gameObject);
            return;
        }
        Button button = selectedObject.GetComponent<Button>();

        if (!button.interactable)
            return;
        button.onClick.Invoke();
    }
}
