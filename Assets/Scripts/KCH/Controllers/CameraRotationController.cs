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

    [Header("Rotation")]
    [SerializeField]
    private float _gravityRotationDamping = 1f;
    [SerializeField]
    private float _yawRotationDamping = 0.1f;

    private Quaternion _gravityRotation;
    private Quaternion _yawRotation;

    void Awake()
    {
        _gravityRotation = _playerController.BaseRotation;
        _yawRotation = Quaternion.AngleAxis(_playerController.Yaw, _gravityRotation * Vector3.up);
    }

    private void LateUpdate()
    {
        Quaternion targetGravityRotation = _playerController.BaseRotation;
        float t = 1f - Mathf.Exp(-Time.deltaTime / _gravityRotationDamping);
        _gravityRotation = Quaternion.Slerp(_gravityRotation, targetGravityRotation, t);

        Quaternion targetYawRotation = Quaternion.AngleAxis(_playerController.Yaw, _gravityRotation * Vector3.up);
        float u = 1f - Mathf.Exp(-Time.deltaTime / _yawRotationDamping);
        _yawRotation = Quaternion.Slerp(_yawRotation, targetYawRotation, u);


        transform.rotation = _yawRotation * _gravityRotation * _cameraTarget.localRotation;
    }
}
