
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
    private InputActionMap _playerMap;
    private InputActionMap _uiMap;
    private InputMode _inputMode = InputMode.Player;

    [Header("Player Mode")]
    public Vector2 MoveInput => _inputMode == InputMode.Player ? _inputActions.Player.Move.ReadValue<Vector2>() : Vector2.zero;
    public Vector2 LookInput => _inputMode == InputMode.Player ? _inputActions.Player.Look.ReadValue<Vector2>() : Vector2.zero;
    public bool InteractPressed => _inputMode == InputMode.Player && _inputActions.Player.Interact.WasPressedThisFrame();
    public bool JumpPressed => _inputMode == InputMode.Player && _inputActions.Player.Jump.WasPressedThisFrame();
    public bool JumpHeld => _inputMode == InputMode.Player && _inputActions.Player.Jump.IsPressed();
    public bool SprintHeld => _inputMode == InputMode.Player && _inputActions.Player.Sprint.IsPressed();
    public bool PausePressed => _inputMode == InputMode.Player && _inputActions.Player.Pause.WasPressedThisFrame();

    public bool BlackHolePressed => _inputMode == InputMode.Player && _inputActions.Player.BlackHole.WasPressedThisFrame();
    public bool BlackHoleHeld => _inputMode == InputMode.Player && _inputActions.Player.BlackHole.IsPressed();
    public bool BlackHoleReleased => _inputMode == InputMode.Player && _inputActions.Player.BlackHole.WasReleasedThisFrame();

    public bool WhiteHolePressed => _inputMode == InputMode.Player && _inputActions.Player.WhiteHole.WasPressedThisFrame();
    public bool WhiteHoleHeld => _inputMode == InputMode.Player && _inputActions.Player.WhiteHole.IsPressed();
    public bool WhiteHoleReleased => _inputMode == InputMode.Player && _inputActions.Player.WhiteHole.WasReleasedThisFrame();

    public bool RetrievePressed => _inputMode == InputMode.Player && _inputActions.Player.Retrieve.WasPressedThisFrame();

    [Header("UI Mode")]

    public bool GamePadConnected { get; private set; }

    public void Init()
    {
        _inputActions = new InputSystem_Actions();

        _playerMap = _inputActions.Player;
        _uiMap = _inputActions.UI;

        _inputActions.Player.Look.performed += CheckDeviceType;
        _inputActions.Player.Look.canceled += CheckDeviceType;

        SetInputMode(InputMode.Player);
    }

    public void Clear()
    {
        _inputActions.Player.Look.performed -= CheckDeviceType;
        _inputActions.Player.Look.canceled -= CheckDeviceType;

        _inputActions.Disable();
        _inputActions.Dispose();

        _inputActions = null;
        _playerMap = null;
        _uiMap = null;
    }

    public void SetInputMode(InputMode mode)
    {
        _inputMode = mode;

        if (_inputActions == null)
            return;

        _playerMap.Disable();
        _uiMap.Disable();

        if (mode == InputMode.Player)
        {
            _playerMap.Enable();
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            Time.timeScale = 1f;
        }
        else
        {
            _uiMap.Enable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Time.timeScale = 0f;
        }
    }

    private void CheckDeviceType(InputAction.CallbackContext ctx) => GamePadConnected = ctx.control.device is Gamepad;
}
