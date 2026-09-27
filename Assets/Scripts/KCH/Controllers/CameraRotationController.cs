using Unity.Cinemachine;
using UnityEngine;

[RequireComponent(typeof(CinemachineCamera))]
public class CameraRotationController : MonoBehaviour
{
    [Header("Target")]
    [SerializeField]
    private PlayerController _playerController;
    [SerializeField]
    private Transform _cameraTarget;

    [Header("Gravity Rotation")]
    [SerializeField]
    private float _gravityRotationDamping = 5f;

    private Quaternion _gravityRotation;

    void Awake()
    {
        _gravityRotation = _playerController.BaseRotation;
    }

    private void LateUpdate()
    {
        Quaternion targetGravityRotation = _playerController.BaseRotation;
        float t = 1f - Mathf.Exp(-Time.deltaTime / _gravityRotationDamping);
        _gravityRotation = Quaternion.Slerp(_gravityRotation, targetGravityRotation, t);

        Vector3 yawAxis = _gravityRotation * Vector3.up;
        Quaternion yawRotation = Quaternion.AngleAxis(_playerController.Yaw, yawAxis);

        transform.rotation = yawRotation * _gravityRotation * _cameraTarget.localRotation;
    }
}

