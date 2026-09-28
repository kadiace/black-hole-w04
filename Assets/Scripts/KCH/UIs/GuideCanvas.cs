using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GuideCanvas : MonoBehaviour
{
    [SerializeField]
    private Button _button;

    private InputAction _confirmAction;

    void Awake()
    {

        _confirmAction = Managers.Input.UIMap.Confirm;
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
