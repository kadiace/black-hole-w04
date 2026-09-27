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
    private bool _processEliminate;
    private float _timer;
    private bool _isScaling;
    private ScalingType _scalingType;
    private float _scalingElapsed;
    private (float, float, float) _startScale;
    private (float, float, float) _targetScale;
    private bool _isEliminating;

    public System.Action OnRemoved;

    private WallCutter cutter;

    void Awake()
    {
        cutter = _inner.GetComponent<WallCutter>();

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
        SetActive(Managers.Gravity.WhiteHole != null && Managers.Gravity.IsWhiteHoleEnabled);
    }

    public void SetActive(bool isActivated)
    {
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

    private void Eliminate()
    {
        gameObject.SetActive(false);
        OnRemoved?.Invoke();
    }

    private void OuterEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleEnabled || _isScaling)
            return;

        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(this, transform.position);
    }

    private void InnerEnter(Collider other)
    {
        if (!Managers.Gravity.IsWhiteHoleEnabled || _isScaling)
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
        if (!Managers.Gravity.IsWhiteHoleEnabled || _isScaling)
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
