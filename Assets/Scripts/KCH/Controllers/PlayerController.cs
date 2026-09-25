using UnityEngine;

[RequireComponent(typeof(Rigidbody), typeof(GravityController))]
public class PlayerController : MonoBehaviour
{
    [Header("Camera")]
    [SerializeField]
    private GameObject _cameraTarget;
    [SerializeField]
    private float _mouseSensitivity = 0.12f;
    [SerializeField]
    private float _gamepadSensitivity = 0.6f;
    [SerializeField]
    private float _minPitch = -30f;
    [SerializeField]
    private float _maxPitch = 70f;
    private float _pitch;
    private float _yaw;

    [Header("Move")]
    [SerializeField]
    private float _moveSpeed;
    [SerializeField]
    private float _sprintSpeed;
    [SerializeField]
    private float _moveAcceleration = 20f;
    private Vector2 _moveInput;

    [Header("Jump")]
    [SerializeField]
    private float _jumpAcceleration = 24f;
    [SerializeField]
    private float _coyoteTime = 0.1f;
    [SerializeField]
    private float _jumpBufferTime = 0.15f;
    [SerializeField]
    private float _jumpGroundedCheckLockTime = 0.15f;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private float _jumpGroundedCheckLockTimer;

    [Header("Ground")]
    [SerializeField]
    private LayerMask _groundLayer;
    [SerializeField]
    private float _groundCheckDistance = 0.1f;
    [SerializeField, Range(0f, 90f)]
    private float _maxGroundAngle = 50f;
    private bool _isGroundedValue;
    private bool _isGrounded
    {
        get => _isGroundedValue; set
        {
            _isGroundedValue = value;
            _rb.isKinematic = value;
        }
    }
    private Vector3 _groundNormal = Vector3.up;
    private bool _hasGroundContact;
    private Vector3 _contactGroundNormal = Vector3.up;

    [Header("Gravity")]
    [SerializeField]
    private float _gravityRotationDuration;
    [SerializeField]
    private float _gravityRotationThreshold;
    private Quaternion _baseRotation;
    private bool _isGravityRotating;
    private float _gravityRotationElapsed;
    private Quaternion _gravityRotationStart;
    private Quaternion _gravityRotationTarget;
    private Vector3 _up => InInner ? -_gravityController.GravityDir : Vector3.up;
    public bool InInner { get; set; }


    [Header("Component")]
    [SerializeField]
    CapsuleCollider _collider;
    Rigidbody _rb;
    GravityController _gravityController;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _gravityController = GetComponent<GravityController>();
        _baseRotation = _rb.rotation;
    }

    void Update()
    {
        ProcessLookInput();
        ProcessJumpInput();
        ProcessMoveInput();
    }

    void FixedUpdate()
    {
        CheckGround();
        ProcessJump();
        ProcessRotation();
        ProcessMove();

        Debug.Log($"IsGrounded: {_isGrounded}");
    }

    void LateUpdate()
    {
        _cameraTarget.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void ProcessLookInput()
    {
        Vector2 lookInput = Managers.Input.LookInput;

        float lookSensitivity = Managers.Input.GamePadConnected ? _gamepadSensitivity * Time.deltaTime : _mouseSensitivity;

        _yaw += lookInput.x * lookSensitivity;
        _pitch -= lookInput.y * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
    }

    private void ProcessJumpInput()
    {
        if (Managers.Input.JumpPressed)
            _jumpBufferTimer = _jumpBufferTime;
        else
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);
    }

    private void ProcessMoveInput()
    {
        _moveInput = Managers.Input.MoveInput;
    }

    private void CheckGround()
    {
        if (_jumpGroundedCheckLockTimer > 0f)
        {
            _jumpGroundedCheckLockTimer = Mathf.Max(0f, _jumpGroundedCheckLockTimer - Time.deltaTime);
            _coyoteTimer = 0f;
            _hasGroundContact = false;
            _isGrounded = false;
            _groundNormal = -_gravityController.GravityDir;
            return;
        }

        float radius = _collider.radius * transform.lossyScale.x;
        float height = _collider.height * transform.lossyScale.y;

        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 bottomSphereCenter =
            transform.position - transform.up * halfSegment;

        float castDistance = radius + _groundCheckDistance;

        if (Physics.SphereCast(
            bottomSphereCenter,
            radius,
            _gravityController.GravityDir,
            out RaycastHit hit,
            castDistance,
            _groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            _isGrounded = true;
            _groundNormal = hit.normal;
            _coyoteTimer = _coyoteTime;
        }
        else if (_hasGroundContact)
        {
            _isGrounded = true;
            _groundNormal = _contactGroundNormal;
            _coyoteTimer = _coyoteTime;
        }
        else
        {
            _isGrounded = false;
            _groundNormal = -_gravityController.GravityDir;
            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.fixedDeltaTime);
        }

        _hasGroundContact = false;
    }

    private void ProcessJump()
    {
        if (_jumpBufferTimer <= 0f)
            return;

        bool canCoyote = _coyoteTimer > 0f;

        if (!_isGrounded && !canCoyote)
            return;

        _jumpBufferTimer = 0f;

        _jumpGroundedCheckLockTimer = _jumpGroundedCheckLockTime;
        _rb.isKinematic = false;
        Vector3 velocity = _rb.linearVelocity;
        velocity.y = 0f;
        _rb.linearVelocity = velocity;
        _rb.AddForce(_jumpAcceleration * _rb.mass * -_gravityController.GravityDir, ForceMode.Impulse);
    }

    private void ProcessRotation()
    {
        if (!_isGravityRotating)
        {
            Vector3 targetUp = _up;
            Vector3 baseUp = _baseRotation * Vector3.up;

            Quaternion gravityCorrection = Quaternion.FromToRotation(baseUp, targetUp);

            float angle = Quaternion.Angle(_baseRotation, gravityCorrection);

            if (angle > _gravityRotationThreshold)
            {
                _gravityRotationStart = _baseRotation;
                _gravityRotationTarget = gravityCorrection * _baseRotation;
                _gravityRotationElapsed = 0f;
                _isGravityRotating = true;
            }
        }
        else
        {
            _gravityRotationElapsed += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(_gravityRotationElapsed / _gravityRotationDuration);
            t = Mathf.SmoothStep(0f, 1f, t);
            Quaternion rotation = Quaternion.Slerp(_gravityRotationStart, _gravityRotationTarget, t);

            if (t >= 1f)
                _isGravityRotating = false;

            _baseRotation = rotation;
        }

        Vector3 yawAxis = _baseRotation * Vector3.up;
        Quaternion yawRotation = Quaternion.AngleAxis(_yaw, yawAxis);
        Quaternion targetRotation = yawRotation * _baseRotation;

        _rb.MoveRotation(targetRotation);
    }

    private void ProcessMove()
    {
        if (_isGrounded)
            ProcessGroundMove();
        else
            ProcessAirMove();
    }

    private void OnCollisionStay(Collision collision)
    {
        if ((_groundLayer.value & (1 << collision.gameObject.layer)) == 0)
            return;

        float bestGroundDot = Mathf.Cos(_maxGroundAngle * Mathf.Deg2Rad);
        Vector3 bestGroundNormal = Vector3.zero;
        bool hasGroundContact = false;

        foreach (ContactPoint contact in collision.contacts)
        {
            float groundDot = Vector3.Dot(contact.normal, Vector3.up);

            if (groundDot < bestGroundDot)
                continue;

            bestGroundDot = groundDot;
            bestGroundNormal = contact.normal;
            hasGroundContact = true;
        }

        if (!hasGroundContact)
            return;

        _hasGroundContact = true;
        _contactGroundNormal = bestGroundNormal;
    }

    private void ProcessGroundMove()
    {
        Vector3 moveDirection = GetMoveDirection();

        if (moveDirection.sqrMagnitude <= 0.001f)
            return;

        moveDirection.Normalize();
        Vector3 movement = _moveSpeed * Time.fixedDeltaTime * moveDirection;
        _rb.MovePosition(_rb.position + movement);
    }

    private void ProcessAirMove()
    {
        Vector2 moveDirection = GetMoveDirection();
        _rb.AddForce(_moveAcceleration * moveDirection, ForceMode.Acceleration);
    }

    private Vector3 GetMoveDirection()
    {
        Vector3 up = _up;
        Vector3 forward = Vector3.ProjectOnPlane(_cameraTarget.transform.forward, up).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        return right * _moveInput.x + forward * _moveInput.y;
    }
}
