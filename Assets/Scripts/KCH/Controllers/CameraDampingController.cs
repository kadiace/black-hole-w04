using Unity.Cinemachine;
using UnityEngine;

public class CameraDampingController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Rigidbody playerRb;
    private Transform _mainCamTransform;

    [Header("Speed")]
    [SerializeField] private float maxEffectSpeed = 80f;
    [SerializeField] private float effectStartSpeed = 20f;

    [Header("Speed")]
    [SerializeField] private float minDistance = 24f;
    [SerializeField] private float maxDistance = 26f;

    [Header("Damping")]
    [SerializeField] private Vector3 slowDamping = new Vector3(0.05f, 0.05f, 0.1f);
    [SerializeField] private Vector3 fastDamping = new Vector3(0.1f, 0.1f, 0.7f);

    [Header("FOV")]
    [SerializeField] private float slowFOV = 60f;
    [SerializeField] private float fastFOV = 75f;

    [Header("Smooth")]
    [SerializeField] private float effectChangeSpeed = 3f;

    private CinemachineCamera _cinemachineCamera;
    private CinemachineThirdPersonFollow _thirdPersonFollow;

    private void Awake()
    {
        _cinemachineCamera = GetComponent<CinemachineCamera>();
        _thirdPersonFollow = GetComponent<CinemachineThirdPersonFollow>();
        _mainCamTransform = Camera.main.transform;
    }

    private void Update()
    {
        UpdateSpeedCameraEffect();
    }


    private void UpdateSpeedCameraEffect()
    {
        float speed = playerRb.linearVelocity.magnitude;
        float speedRatio = Mathf.InverseLerp(effectStartSpeed, maxEffectSpeed, speed);
        Vector3 targetDamping = Vector3.Lerp(slowDamping, fastDamping, speedRatio);

        if (Vector3.Dot(_mainCamTransform.forward, playerRb.linearVelocity) <= 0)
            targetDamping.z = 0;

        _thirdPersonFollow.Damping = Vector3.Lerp(_thirdPersonFollow.Damping, targetDamping,
            effectChangeSpeed * Time.deltaTime);

        float targetDistance = Mathf.Lerp(minDistance, maxDistance, speedRatio);

        _thirdPersonFollow.CameraDistance = Mathf.Lerp(_thirdPersonFollow.CameraDistance, targetDistance,
                effectChangeSpeed * Time.deltaTime);

        LensSettings lens =
            _cinemachineCamera.Lens;

        float targetFOV;
        if (Vector3.Dot(_mainCamTransform.forward, playerRb.linearVelocity) <= 0)
            targetFOV = slowFOV;
        else
            targetFOV = Mathf.Lerp(slowFOV, fastFOV, speedRatio);

        lens.FieldOfView = Mathf.Lerp(lens.FieldOfView, targetFOV, effectChangeSpeed * Time.deltaTime);

        _cinemachineCamera.Lens = lens;
    }
}