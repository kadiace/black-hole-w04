
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
    private InputSystem_Actions.PlayerActions _playerMap;
    public InputSystem_Actions.UIActions UIMap;
    private InputMode _inputMode = InputMode.Player;

    [Header("Player Mode")]
    public Vector2 MoveInput => _inputMode == InputMode.Player ? _playerMap.Move.ReadValue<Vector2>() : Vector2.zero;
    public Vector2 LookInput => _inputMode == InputMode.Player ? _playerMap.Look.ReadValue<Vector2>() : Vector2.zero;
    public bool InteractPressed => _inputMode == InputMode.Player && _playerMap.Interact.WasPressedThisFrame();
    public bool JumpPressed => _inputMode == InputMode.Player && _playerMap.Jump.WasPressedThisFrame();
    public bool JumpHeld => _inputMode == InputMode.Player && _playerMap.Jump.IsPressed();
    public bool SprintHeld => _inputMode == InputMode.Player && _playerMap.Sprint.IsPressed();
    public bool PausePressed => _inputMode == InputMode.Player && _playerMap.Pause.WasPressedThisFrame();

    public bool BlackHolePressed => _inputMode == InputMode.Player && _playerMap.BlackHole.WasPressedThisFrame();
    public bool BlackHoleHeld => _inputMode == InputMode.Player && _playerMap.BlackHole.IsPressed();
    public bool BlackHoleReleased => _inputMode == InputMode.Player && _playerMap.BlackHole.WasReleasedThisFrame();

    public bool WhiteHolePressed => _inputMode == InputMode.Player && _playerMap.WhiteHole.WasPressedThisFrame();
    public bool WhiteHoleHeld => _inputMode == InputMode.Player && _playerMap.WhiteHole.IsPressed();
    public bool WhiteHoleReleased => _inputMode == InputMode.Player && _playerMap.WhiteHole.WasReleasedThisFrame();

    public bool RetrievePressed => _inputMode == InputMode.Player && _playerMap.Retrieve.WasPressedThisFrame();

    [Header("UI Mode")]
    public bool ConfirmPressed => _inputMode == InputMode.UI && UIMap.Confirm.WasPressedThisFrame();

    public bool GamePadConnected { get; private set; }

    public void Init()
    {
        _inputActions = new InputSystem_Actions();

        _playerMap = _inputActions.Player;
        UIMap = _inputActions.UI;

        _playerMap.Look.performed += CheckDeviceType;
        _playerMap.Look.canceled += CheckDeviceType;

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

        _playerMap.Disable();
        UIMap.Disable();

        if (mode == InputMode.Player)
        {
            _playerMap.Enable();
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
