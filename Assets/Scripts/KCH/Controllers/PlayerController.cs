using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Rigidbody), typeof(GravityController))]
public class PlayerController : MonoBehaviour, IPressable, IInteractor
{
    [Header("Camera")]
    [SerializeField]
    private GameObject _cameraTarget;
    [SerializeField]
    private float _mouseSensitivity;
    [SerializeField]
    private float _gamepadSensitivity;
    [SerializeField]
    private float _minPitch;
    [SerializeField]
    private float _maxPitch;
    private float _pitch;
    public float Yaw { get; private set; }

    [Header("Move")]
    [SerializeField]
    private float _moveSpeed;
    [SerializeField]
    private float _sprintSpeed;
    [SerializeField]
    private float _moveAcceleration;
    private Vector2 _moveInput;
    private bool _sprintInput;

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

    private Vector3 _moveVelocity; // 현재 이동 속도 (월드 기준)

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
        get => _isGroundedValue;
        set
        {
            _isGroundedValue = value;
        }
    }
    private Vector3 _groundNormal = Vector3.up;
    private bool _hasGroundContact;
    private Vector3 _contactGroundNormal = Vector3.up;

    [Header("Gravity")]
    private Quaternion _baseRotation;
    private Vector3 _up => OnGravity ? -_gravityController.GravityDir : Vector3.up;
    public bool OnGravity { private get; set; }
    public Quaternion BaseRotation => _baseRotation;

    [Header("Interactive")]
    [SerializeField]
    private float interactDistance = 5f;
    [SerializeField]
    private Transform snapAt;
    [SerializeField]
    private Image crosshair;

    public Transform SnapAt => snapAt;

    [Header("Component")]
    [SerializeField]
    CapsuleCollider _collider;
    Rigidbody _rb;
    GravityController _gravityController;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _gravityController = GetComponent<GravityController>();
        _rb.useGravity = false;

        SwitchFreezeRotation(true);
        _baseRotation = _rb.rotation;
    }

    void Update()
    {
        ProcessLookInput();
        ProcessJumpInput();
        ProcessMoveInput();
        ProcessSprintInput();
        ProcessInteract();
    }

    void FixedUpdate()
    {
        CheckGround();
        ProcessJump();
        ProcessRotation();
        ProcessMove();

        Debug.Log($"IsGrounded: {_isGrounded}");
    }

    private void ProcessLookInput()
    {
        Vector2 lookInput = Managers.Input.LookInput;

        float lookSensitivity = Managers.Input.GamePadConnected ? _gamepadSensitivity * Time.deltaTime : _mouseSensitivity;

        Yaw += lookInput.x * lookSensitivity;
        _pitch -= lookInput.y * lookSensitivity;
        _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);
        _cameraTarget.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    private void ProcessJumpInput()
    {
        if (Managers.Input.JumpPressed)
            _jumpBufferTimer = _jumpBufferTime;
        else
            _jumpBufferTimer = Mathf.Max(0f, _jumpBufferTimer - Time.deltaTime);
    }

    private void SwitchFreezeRotation(bool enable)
    {
        if (enable)
        {
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
        else
        {
            _rb.constraints = RigidbodyConstraints.FreezeRotationZ;
        }
    }

    private void ProcessMoveInput()
    {
        _moveInput = Managers.Input.MoveInput;
    }

    private void ProcessSprintInput()
    {
        _sprintInput = Managers.Input.SprintHeld;
    }

    private void CheckGround()
    {
        if (_jumpGroundedCheckLockTimer > 0f)
        {
            _jumpGroundedCheckLockTimer = Mathf.Max(0f, _jumpGroundedCheckLockTimer - Time.deltaTime);
            _coyoteTimer = 0f;
            _hasGroundContact = false;
            _isGrounded = false;
            SwitchFreezeRotation(false);
            _groundNormal = -_gravityController.GravityDir;
            return;
        }

        float radius = _collider.radius * transform.lossyScale.x;
        float height = _collider.height * transform.lossyScale.y;

        float halfSegment = Mathf.Max(0f, height * 0.5f - radius);

        Vector3 bottomSphereCenter = transform.position - transform.up * halfSegment;

        if (Physics.SphereCast(
            bottomSphereCenter,
            radius,
            _gravityController.GravityDir,
            out RaycastHit hit,
            _groundCheckDistance,
            _groundLayer,
            QueryTriggerInteraction.Ignore))
        {
            _isGrounded = true;
            _groundNormal = hit.normal;
            _coyoteTimer = _coyoteTime;
            SwitchFreezeRotation(true);
        }
        else if (_hasGroundContact)
        {
            _isGrounded = true;
            _groundNormal = _contactGroundNormal;
            _coyoteTimer = _coyoteTime;
            SwitchFreezeRotation(true);
        }
        else
        {
            _isGrounded = false;
            _groundNormal = -_gravityController.GravityDir;
            _coyoteTimer = Mathf.Max(0f, _coyoteTimer - Time.fixedDeltaTime);
            SwitchFreezeRotation(false);
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
        SwitchFreezeRotation(false);
        Vector3 velocity = _rb.linearVelocity;
        velocity.y = 0f;
        _rb.linearVelocity = velocity;
        _rb.AddForce(_jumpAcceleration * _rb.mass * -_gravityController.GravityDir, ForceMode.Impulse);
    }

    private void ProcessRotation()
    {
        Vector3 targetUp = _up;
        Vector3 baseUp = _baseRotation * Vector3.up;
        Quaternion gravityCorrection = Quaternion.FromToRotation(baseUp, targetUp);
        _baseRotation = gravityCorrection * _baseRotation;

        Vector3 yawAxis = _baseRotation * Vector3.up;
        Quaternion yawRotation = Quaternion.AngleAxis(Yaw, yawAxis);
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
        {
            _rb.linearVelocity = Vector3.zero;
            return;
        }

        moveDirection.Normalize();

        float speed = _sprintInput ? _sprintSpeed : _moveSpeed;
        Vector3 movement = speed * moveDirection;// * Time.fixedDeltaTime;
        _rb.linearVelocity = movement;
        //_rb.MovePosition(_rb.position + movement);
    }

    private void ProcessAirMove()
    {
        Vector3 moveDirection = GetMoveDirection();
        _rb.AddForce(_moveAcceleration * moveDirection, ForceMode.Acceleration);
    }

    private Vector3 GetMoveDirection()
    {
        Vector3 up = _up;
        Vector3 forward = Vector3.ProjectOnPlane(_cameraTarget.transform.forward, up).normalized;
        Vector3 right = Vector3.Cross(up, forward).normalized;
        return right * _moveInput.x + forward * _moveInput.y;
    }

    private void ProcessInteract()
    {
        Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));

        Debug.DrawRay(ray.origin, ray.direction * interactDistance, Color.green);
        IInteractable interactable = null;
        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance, LayerMask.NameToLayer("interactive"), QueryTriggerInteraction.Ignore))
        {
            interactable = hit.collider.GetComponentInParent<IInteractable>();

            if (interactable != null)
            {
                if (crosshair != null)
                    crosshair.enabled = true;
            }
            else
            {
                if (crosshair != null)
                    crosshair.enabled = false;
            }
        }

        if (!Managers.Input.InteractPressed)
        {
            return;
        }

        if (snapAt.childCount != 0)
            ProcessDeInteract();

        if (interactable != null)
        {
            interactable.Interact(this);
        }
    }

    private void ProcessDeInteract()
    {
        for (int i = snapAt.childCount - 1; i >= 0; i--)
        {
            Transform child = snapAt.GetChild(i);

            if (child.TryGetComponent<IInteractable>(out var interactable))
            {
                interactable.Release(this);
            }
        }
    }
}

