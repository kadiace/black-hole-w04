using UnityEngine;

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

    [Header("Activate")]
    [SerializeField]
    private float _time;
    [SerializeField]
    private float _scalingDuration;
    private float _timer;
    private bool _isScaling;
    private ScalingType _scalingType;
    private float _scalingElapsed;
    private (float, float) _startScale;
    private (float, float) _targetScale;
    private bool _isEliminating;

    public System.Action OnRemoved;

    void Awake()
    {
        _outer.transform.localScale = Vector3.one;
        _inner.transform.localScale = Vector3.one;
        ParticleSystem.ShapeModule shape = _convergence.shape;
        shape.scale = Vector3.one;
        ParticleSystem.MainModule main = _convergence.main;
        main.startLifetime = 1 / 30;

        _outer.OnTriggerEntered += OuterEnter;
        _inner.OnTriggerEntered += InnerEnter;
        _eventHorizon.OnTriggerEntered += EventHorizonEnter;
        _outer.OnTriggerExited += OuterExit;
        _inner.OnTriggerExited += InnerExit;
    }

    void Update()
    {
        _timer -= Time.deltaTime;
        if (_timer < 0f)
            ProcessEliminate();

        if (_isScaling)
        {
            _scalingElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_scalingElapsed / _scalingDuration);

            t = Mathf.Pow(t, 20f);

            (float startOuterScale, float startInnerScale) = _startScale;
            (float targetOuterScale, float targetInnerScale) = _targetScale;

            float outerScale = Mathf.Lerp(startOuterScale, targetOuterScale, t);
            float innerScale = Mathf.Lerp(startInnerScale, targetInnerScale, t);

            _outer.transform.localScale = outerScale * Vector3.one;
            _inner.transform.localScale = innerScale * Vector3.one;
            ParticleSystem.ShapeModule shape = _convergence.shape;
            shape.scale = innerScale * Vector3.one;
            ParticleSystem.MainModule main = _convergence.main;
            main.startLifetime = innerScale / 30;

            if (t < 1)
                return;
            _isScaling = false;

            if (_scalingType != ScalingType.Shrink)
                return;
            _convergence.gameObject.SetActive(false);

            if (_isEliminating)
                Eliminate();
        }
    }

    void OnEnable()
    {
        _isEliminating = false;
        _isScaling = false;
        _outer.transform.localScale = Vector3.one;
        _inner.transform.localScale = Vector3.one;
        ParticleSystem.ShapeModule shape = _convergence.shape;
        shape.scale = Vector3.one;
        ParticleSystem.MainModule main = _convergence.main;
        main.startLifetime = 1 / 30;

        _timer = _time;
        SetActive(Managers.Gravity.WhiteHole == null ? false : Managers.Gravity.IsWhiteHoleEnabled);
    }

    public void SetActive(bool isActivated)
    {
        _isScaling = true;
        _scalingType = isActivated ? ScalingType.Expand : ScalingType.Shrink;
        _startScale = (_outer.transform.localScale.x, _inner.transform.localScale.x);
        _targetScale = isActivated ? (Managers.Gravity.GravityStat.OuterScale, Managers.Gravity.GravityStat.InnerScale) : (1f, 1f);
        _scalingElapsed = 0f;
        if (isActivated)
            _convergence.gameObject.SetActive(true);
    }

    public void ProcessEliminate()
    {
        if (_isEliminating)
            return;
        _isEliminating = true;

        if (_isScaling)
            return;
        else if (_scalingType == ScalingType.Shrink)
        {
            Eliminate();
            return;
        }

        SetActive(false);
    }

    private void Eliminate()
    {
        gameObject.SetActive(false);
        OnRemoved?.Invoke();
        Managers.Gravity.WhiteHole.SetActive(false);
    }

    private void OuterEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleEnabled)
            return;

        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(this, transform.position);
    }

    private void InnerEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleEnabled)
            return;

        // 1. Rotate Player up to -GravityDir
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.InInner = true;

        // 2. Cut Rigid Body object

        // 3. Affect fluid 
    }

    private void EventHorizonEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleEnabled)
            return;

        Rigidbody rb = other.GetComponentInParent<Rigidbody>();
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (rb == null || gravityController == null)
            return;

        Vector3 blackHoleOffset = transform.position - rb.transform.position;

        rb.position = Managers.Gravity.WhiteHole.transform.position + blackHoleOffset;
        gravityController.SetGravityCenter(this, null);
    }

    private void OuterExit(Collider other)
    {
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(this, null);
    }

    private void InnerExit(Collider other)
    {
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.InInner = false;
    }
}
