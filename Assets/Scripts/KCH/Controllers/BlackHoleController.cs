using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class BlackHoleController : MonoBehaviour
{
    [Header("Child")]
    [SerializeField]
    private TriggerChecker _outer;
    [SerializeField]
    private TriggerChecker _inner;
    [SerializeField]
    private TriggerChecker _eventHorizon;
    [SerializeField]
    private ParticleSystem _convergence;
    [SerializeField]
    private Volume _volume;
    private LensDistortion _distortion;
    private WallCutter cutter;

    [Header("Activate")]
    private bool _processEliminate;
    private float _timer;
    private bool _isScaling;
    private ScalingType _scalingType;
    private float _scalingElapsed;
    private (float, float, float) _startScale;
    private (float, float, float) _targetScale;
    private bool _isEliminating;

    public System.Action OnRemoved;
    public SphereCollider InnerCollider => _inner.GetComponent<SphereCollider>();
    public SphereCollider EventHorizonCollider => _eventHorizon.GetComponent<SphereCollider>();
    public bool IsFullyExpanded => isActiveAndEnabled && !_isScaling && !_isEliminating && _scalingType == ScalingType.Expand;

    [Header("Teleport")]
    private bool _isTeleporting;

    [Header("Gravity Affected")]
    private readonly HashSet<GravityController> _gravityObjects = new();
    private PlayerController _playerController;

    void Awake()
    {
        cutter = _eventHorizon.GetComponent<WallCutter>();
        _volume.profile.TryGet(out _distortion);

        _processEliminate = false;
        _isEliminating = false;
        _isScaling = false;
        _scalingType = ScalingType.Shrink;
        _outer.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        _inner.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        _eventHorizon.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        ParticleSystem.ShapeModule shape = _convergence.shape;
        shape.scale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        ParticleSystem.MainModule main = _convergence.main;
        main.startLifetime = Managers.Gravity.GravityStat.InitScale / 30;

        _outer.OnTriggerEntered += OuterEnter;
        _inner.OnTriggerEntered += InnerEnter;
        _eventHorizon.OnTriggerEntered += EventHorizonEnter;
        _outer.OnTriggerExited += OuterExit;
        _inner.OnTriggerExited += InnerExit;
    }

    void Update()
    {
        if (_timer >= 0f)
            _timer -= Time.deltaTime;
        else if (!_processEliminate)
        {
            ProcessEliminate();
            _processEliminate = true;
        }

        if (_isScaling)
        {
            _scalingElapsed += Time.deltaTime;
            float duration = _scalingType == ScalingType.Expand ? Managers.Gravity.GravityStat.ExpandDuration : Managers.Gravity.GravityStat.ShrinkDuration;
            float t = Mathf.Clamp01(_scalingElapsed / duration);
            float curveT = _scalingType == ScalingType.Expand ? Managers.Gravity.GravityStat.BlackHoleExpandCurve(t) : Managers.Gravity.GravityStat.BlackHoleShrinkCurve(t);

            (float startOuterScale, float startInnerScale, float startEventHorizonScale) = _startScale;
            (float targetOuterScale, float targetInnerScale, float targetEventHorizonScale) = _targetScale;

            float outerScale = Mathf.LerpUnclamped(startOuterScale, targetOuterScale, curveT);
            float innerScale = Mathf.LerpUnclamped(startInnerScale, targetInnerScale, curveT);
            float eventHorizonScale = Mathf.LerpUnclamped(startEventHorizonScale, targetEventHorizonScale, curveT);

            _outer.transform.localScale = outerScale * Vector3.one;
            _inner.transform.localScale = innerScale * Vector3.one;
            _eventHorizon.transform.localScale = eventHorizonScale * Vector3.one;
            ParticleSystem.ShapeModule shape = _convergence.shape;
            shape.scale = innerScale * Vector3.one;
            ParticleSystem.MainModule main = _convergence.main;
            main.startLifetime = innerScale / 30;

            if (t < 1)
                return;
            _isScaling = false;
            if (_scalingType != ScalingType.Shrink)
            {
                cutter.CutNow();
                return;
            }
            _convergence.gameObject.SetActive(false);

            if (_isEliminating)
                Eliminate();
        }
    }

    void OnEnable()
    {
        _processEliminate = false;
        _isEliminating = false;
        _isScaling = false;
        _scalingType = ScalingType.Shrink;
        _outer.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        _inner.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        _eventHorizon.transform.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        ParticleSystem.ShapeModule shape = _convergence.shape;
        shape.scale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        ParticleSystem.MainModule main = _convergence.main;
        main.startLifetime = Managers.Gravity.GravityStat.InitScale / 30;

        _timer = Managers.Gravity.GravityStat.ExistDuration;
        SetActive(Managers.Gravity.WhiteHole != null && Managers.Gravity.IsWhiteHoleActive);
    }

    void OnDisable()
    {
        foreach (GravityController gravityController in _gravityObjects)
            gravityController.SetGravityCenter(this, null);
        _gravityObjects.Clear();

        if (_playerController != null)
        {
            _playerController.OnGravity = false;
            _playerController = null;
        }
    }

    public void SetActive(bool isActivated)
    {
        if (_processEliminate)
            return;
        ScalingType nextScalingType = isActivated ? ScalingType.Expand : ScalingType.Shrink;
        if (_scalingType != nextScalingType)
        {
            _isScaling = true;
            _scalingType = isActivated ? ScalingType.Expand : ScalingType.Shrink;
            _startScale = (_outer.transform.localScale.x, _inner.transform.localScale.x, _eventHorizon.transform.localScale.x);
            _targetScale = isActivated ? (Managers.Gravity.GravityStat.OuterScale, Managers.Gravity.GravityStat.InnerScale, Managers.Gravity.GravityStat.EventHorizonScale) :
                (Managers.Gravity.GravityStat.InitScale, Managers.Gravity.GravityStat.InitScale, Managers.Gravity.GravityStat.InitScale);
            _scalingElapsed = 0f;
        }
        if (isActivated)
            _convergence.gameObject.SetActive(true);
    }

    public void ProcessEliminate()
    {
        Managers.Gravity.WhiteHole.SetActive(false);
        if (_isEliminating)
            return;
        _isEliminating = true;

        bool isExpanding = _scalingType == ScalingType.Expand;
        if (_isScaling == isExpanding)
        {
            Eliminate();
            return;
        }
        if (!_isScaling && isExpanding)
            SetActive(false);
    }

    public void Eliminate()
    {
        gameObject.SetActive(false);
        Managers.Gravity.WhiteHole.SetActive(false);
        OnRemoved?.Invoke();
    }

    private void OuterEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleActive || _isScaling)
            return;

        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;
        gravityController.SetGravityCenter(this, transform.position);
        _gravityObjects.Add(gravityController);

        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController == null)
            return;
        playerController.OnGravity = true;
        _playerController = playerController;
    }

    private void InnerEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleActive || _isScaling)
            return;

        if (other.CompareTag("Sand"))
        {
            other.GetComponent<SandMesh>().flowTrigger = GetComponentsInChildren<SphereCollider>()[1];
            Managers.Gravity.WhiteHole.IsSand = true;
        }
    }

    private void EventHorizonEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleActive || _isScaling)
            return;

        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (rb == null || gravityController == null)
            return;

        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
        {
            if (_isTeleporting)
                return;
            _isTeleporting = true;
            StartCoroutine(TeleportThroughWhiteHole(rb, gravityController));
        }
        else
        {
            Vector3 blackHoleOffset = transform.position - rb.transform.position;

            rb.position = Managers.Gravity.WhiteHole.transform.position + blackHoleOffset;
            gravityController.SetGravityCenter(this, null);
        }
    }

    private void OuterExit(Collider other)
    {
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;
        gravityController.SetGravityCenter(this, null);
        _gravityObjects.Remove(gravityController);

        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController == null)
            return;
        playerController.OnGravity = false;
        _playerController = null;
    }

    private void InnerExit(Collider other)
    {
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.OnGravity = false;
    }

    private IEnumerator TeleportThroughWhiteHole(Rigidbody rb, GravityController gravityController)
    {
        bool canFireBlackHole = Managers.Gravity.CanFireBlackHole;
        bool canFireWhiteHole = Managers.Gravity.CanFireWhiteHole;
        Managers.Gravity.CanFireBlackHole = false;
        Managers.Gravity.CanFireWhiteHole = false;
        CinemachineCamera cinemachineCamera =
            Camera.main.GetComponent<CinemachineBrain>().ActiveVirtualCamera as CinemachineCamera;
        Transform followTarget = cinemachineCamera.Follow;
        cinemachineCamera.Follow = null;

        Managers.Gravity.WhiteHole.Distortion.intensity.value = 1f;
        Time.timeScale = 0f;

        Vector3 screenPos = Camera.main.WorldToScreenPoint(transform.position);
        _distortion.center.value = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        Managers.Gravity.WhiteHole.Distortion.center.value = new Vector2(screenPos.x / Screen.width, screenPos.y / Screen.height);
        yield return LerpDistortion(_distortion, 0f, 1f, 1f);

        Vector3 blackHoleOffset = transform.position - rb.transform.position;
        Vector3 targetPosition = Managers.Gravity.WhiteHole.transform.position + blackHoleOffset;

        rb.transform.position = targetPosition;
        Physics.SyncTransforms();

        cinemachineCamera.ForceCameraPosition(followTarget.position, followTarget.rotation);
        cinemachineCamera.PreviousStateIsValid = false;

        gravityController.SetGravityCenter(this, null);

        Time.timeScale = 0.2f;
        yield return new WaitForSecondsRealtime(0.2f);

        Time.timeScale = 0f;

        _distortion.intensity.value = 0f;
        yield return LerpDistortion(Managers.Gravity.WhiteHole.Distortion, 1f, 0f, 1f);

        cinemachineCamera.Follow = followTarget;
        cinemachineCamera.PreviousStateIsValid = false;

        Time.timeScale = 1f;
        _isTeleporting = false;

        Managers.Gravity.CanFireBlackHole = canFireBlackHole;
        Managers.Gravity.CanFireWhiteHole = canFireWhiteHole;
    }

    private IEnumerator LerpDistortion(LensDistortion lensDistortion, float from, float to, float duration)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;

            float t = Mathf.Clamp01(elapsed / duration);
            t = Mathf.SmoothStep(0f, 1f, t);
            lensDistortion.intensity.value = Mathf.Lerp(from, to, t);

            yield return null;
        }

        lensDistortion.intensity.value = to;
    }
}
