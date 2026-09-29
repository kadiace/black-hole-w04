using System;
using System.Collections.Generic;
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

    private List<Action<InputAction.CallbackContext>> _confirmActions = new();

    public string Text { private get; set; }
    public Button Button => _button;

    void Update()
    {
        _text.text = Util.ReplaceBindingName(Text);
    }

    void OnEnable()
    {
        Managers.Input.UIMap.Confirm.performed += OnConfirmAction;
    }

    void OnDisable()
    {
        Managers.Input.UIMap.Confirm.performed -= OnConfirmAction;
        foreach (Action<InputAction.CallbackContext> action in _confirmActions)
            Managers.Input.UIMap.Confirm.performed -= action;
        _confirmActions.Clear();
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

    public void RegisterConfirmAction(Action<InputAction.CallbackContext> action)
    {
        Managers.Input.UIMap.Confirm.performed += action;
        _confirmActions.Add(action);
    }
}
