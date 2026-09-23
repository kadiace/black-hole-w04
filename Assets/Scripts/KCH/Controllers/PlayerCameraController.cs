using UnityEngine;

public class PlayerCameraController : MonoBehaviour
{
    [SerializeField] private float _mouseSensitivity = 0.12f;
    [SerializeField] private float _gamepadSensitivity = 0.6f;

    [SerializeField] private float _minPitch = -30f;
    [SerializeField] private float _maxPitch = 70f;

    private float _pitch;

    private void Update()
    {
        Vector2 lookInput = Managers.Input.LookInput;

        float pitchDelta = Managers.Input.GamePadConnected ?
            lookInput.y * _gamepadSensitivity * Time.deltaTime : lookInput.y * _mouseSensitivity;

        _pitch -= pitchDelta;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
    }

    private void LateUpdate()
    {
        transform.rotation = Quaternion.Euler(_pitch, 0f, 0f);
    }
}
