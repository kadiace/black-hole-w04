
using UnityEngine;
using UnityEngine.InputSystem;

public enum InputMode
{
    Player,
    UI
}

public class InputManager
{
    private InputSystem_Actions _inputActions;

    private InputActionMap _playerMap;
    private InputActionMap _uiMap;

    private InputMode _inputMode = InputMode.Player;

    public Vector2 MoveInput { get; private set; }
    public Vector2 LookInput { get; private set; }

    public bool JumpPressed { get; private set; }
    public bool JumpHeld { get; private set; }
    public bool InteractPressed { get; private set; }
    public bool SprintPressed { get; private set; }

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

    public void Update()
    {
        if (_inputActions == null)
            return;

        if (_inputMode == InputMode.Player)
        {
            MoveInput = _inputActions.Player.Move.ReadValue<Vector2>();
            LookInput = _inputActions.Player.Look.ReadValue<Vector2>();

            JumpPressed =
                _inputActions.Player.Jump.WasPressedThisFrame();

            JumpHeld =
                _inputActions.Player.Jump.IsPressed();

            InteractPressed =
                _inputActions.Player.Interact.WasPressedThisFrame();
        }
        else
        {
            MoveInput = Vector2.zero;
            LookInput = Vector2.zero;

            JumpPressed = false;
            JumpHeld = false;
            InteractPressed = false;
        }
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

        MoveInput = Vector2.zero;
        LookInput = Vector2.zero;

        JumpPressed = false;
        JumpHeld = false;
        InteractPressed = false;
        SprintPressed = false;
    }

    public void SetInputMode(InputMode mode)
    {
        _inputMode = mode;

        MoveInput = Vector2.zero;
        LookInput = Vector2.zero;

        JumpPressed = false;
        JumpHeld = false;
        InteractPressed = false;
        SprintPressed = false;

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
