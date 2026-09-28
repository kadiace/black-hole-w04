
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputMode
{
    Player,
    UI
}

public class InputManager
{
    [Header("Input System")]
    private InputSystem_Actions _inputActions;
    public InputSystem_Actions.PlayerActions PlayerMap;
    public InputSystem_Actions.UIActions UIMap;
    private InputMode _inputMode = InputMode.Player;

    [Header("Player Mode")]
    public Vector2 MoveInput => _inputMode == InputMode.Player ? PlayerMap.Move.ReadValue<Vector2>() : Vector2.zero;
    public Vector2 LookInput => _inputMode == InputMode.Player ? PlayerMap.Look.ReadValue<Vector2>() : Vector2.zero;
    public bool InteractPressed => _inputMode == InputMode.Player && PlayerMap.Interact.WasPressedThisFrame();
    public bool JumpPressed => _inputMode == InputMode.Player && PlayerMap.Jump.WasPressedThisFrame();
    public bool JumpHeld => _inputMode == InputMode.Player && PlayerMap.Jump.IsPressed();
    public bool SprintHeld => _inputMode == InputMode.Player && PlayerMap.Sprint.IsPressed();
    public bool PausePressed => _inputMode == InputMode.Player && PlayerMap.Pause.WasPressedThisFrame();
    public bool RestartPressed => _inputMode == InputMode.Player && PlayerMap.Restart.WasPressedThisFrame();

    public bool BlackHolePressed => _inputMode == InputMode.Player && PlayerMap.BlackHole.WasPressedThisFrame();
    public bool BlackHoleHeld => _inputMode == InputMode.Player && PlayerMap.BlackHole.IsPressed();
    public bool BlackHoleReleased => _inputMode == InputMode.Player && PlayerMap.BlackHole.WasReleasedThisFrame();

    public bool WhiteHolePressed => _inputMode == InputMode.Player && PlayerMap.WhiteHole.WasPressedThisFrame();
    public bool WhiteHoleHeld => _inputMode == InputMode.Player && PlayerMap.WhiteHole.IsPressed();
    public bool WhiteHoleReleased => _inputMode == InputMode.Player && PlayerMap.WhiteHole.WasReleasedThisFrame();

    public bool RetrievePressed => _inputMode == InputMode.Player && PlayerMap.Retrieve.WasPressedThisFrame();

    [Header("UI Mode")]
    public bool ConfirmPressed => _inputMode == InputMode.UI && UIMap.Confirm.WasPressedThisFrame();

    public bool GamePadConnected { get; private set; }

    public void Init()
    {
        _inputActions = new InputSystem_Actions();

        PlayerMap = _inputActions.Player;
        UIMap = _inputActions.UI;

        PlayerMap.Look.performed += CheckDeviceType;
        PlayerMap.Look.canceled += CheckDeviceType;

        SetInputMode(InputMode.Player);
    }

    public void Clear()
    {

    }

    public void SetInputMode(InputMode mode)
    {
        _inputMode = mode;

        if (_inputActions == null)
            return;

        PlayerMap.Disable();
        UIMap.Disable();

        if (mode == InputMode.Player)
        {
            PlayerMap.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
        }
        else
        {
            UIMap.Enable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
    }

    private void CheckDeviceType(InputAction.CallbackContext ctx) => GamePadConnected = ctx.control.device is Gamepad;
}
