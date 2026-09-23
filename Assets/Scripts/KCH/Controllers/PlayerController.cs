using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PlayerController : MonoBehaviour
{
    [Header("Input")]
    private Vector2 m_lookInput;

    [Header("Camera")]
    [SerializeField]
    private GameObject cameraTarget;
    [SerializeField]
    private float _mouseSensitivity = 0.12f;
    [SerializeField]
    private float _gamepadSensitivity = 0.6f;

    private float _pitch;
    private float _yaw;
    [SerializeField]
    private float _minPitch = -30f;
    [SerializeField]
    private float _maxPitch = 70f;

    Rigidbody m_rb;

    void Awake()
    {
        m_rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        Vector2 lookInput = Managers.Input.LookInput;

        float lookSensitivity = Managers.Input.GamePadConnected ? _gamepadSensitivity * Time.deltaTime : _mouseSensitivity;

        _yaw += lookInput.x * lookSensitivity;
        _pitch -= lookInput.y * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
    }

    void FixedUpdate()
    {
        m_rb.MoveRotation(Quaternion.Euler(0f, _yaw, 0f));
    }

    void LateUpdate()
    {
        cameraTarget.transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
    }
}
