using UnityEngine;

public class WhiteHoleController : MonoBehaviour
{
    [Header("Child")]
    [SerializeField]
    private Transform _eventHorizon;
    [SerializeField]
    private ParticleSystem _emission;

    [Header("Renderer")]
    [SerializeField]
    private Renderer _renderer;
    [SerializeField]
    private Material _onMaterial;
    [SerializeField]
    private Material _offMaterial;

    [Header("Activate")]
    private bool _processEliminate;
    private bool _isScaling;
    private ScalingType _scalingType;
    private float _scalingElapsed;
    private float _startScale;
    private float _targetScale;
    private bool _isEliminating;

    public bool ProcessEliminated => _processEliminate;

    [Header("Sand")]
    [SerializeField]
    private GameObject _sand;
    private bool _isSand;

    public bool IsSand
    {
        get { return _isSand; }
        set
        {
            _sand.SetActive(value);
            _isSand = value;
        }
    }

    void Awake()
    {
        _processEliminate = false;
        _isEliminating = false;
        _isScaling = false;
        _scalingType = ScalingType.Shrink;
        _eventHorizon.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
    }

    void Update()
    {
        if (_isScaling)
        {
            _scalingElapsed += Time.deltaTime;
            float duration = _scalingType == ScalingType.Expand ? Managers.Gravity.GravityStat.ExpandDuration : Managers.Gravity.GravityStat.ShrinkDuration;
            float t = Mathf.Clamp01(_scalingElapsed / duration);
            float curveT = Managers.Gravity.GravityStat.WhiteHoleCurve(t);

            float eventHorizonScale = Mathf.LerpUnclamped(_startScale, _targetScale, curveT);
            _eventHorizon.localScale = eventHorizonScale * Vector3.one;

            if (t < 1)
                return;
            _isScaling = false;
            if (_scalingType != ScalingType.Shrink)
                return;
            _emission.gameObject.SetActive(false);

            if (_isEliminating)
                Eliminate();
        }
    }

    void OnEnable()
    {
        Managers.Gravity.ProcessWhiteHoleEliminate = false;
        _isEliminating = false;
        _isScaling = false;
        _scalingType = ScalingType.Shrink;
        _eventHorizon.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        SetActive(Managers.Gravity.BlackHole == null ? false : Managers.Gravity.IsBlackHoleActive);
    }

    public void SetActive(bool isActivated)
    {
        ScalingType nextScalingType = isActivated ? ScalingType.Expand : ScalingType.Shrink;
        if (_scalingType != nextScalingType)
        {
            _isScaling = true;
            _scalingType = nextScalingType;
            _startScale = _eventHorizon.localScale.x;
            _targetScale = isActivated ? Managers.Gravity.GravityStat.EventHorizonScale : Managers.Gravity.GravityStat.InitScale;
            _scalingElapsed = 0f;
        }

        _renderer.sharedMaterial = isActivated ? _onMaterial : _offMaterial;
        if (isActivated)
        {
            _emission.gameObject.SetActive(true);
            _sand.SetActive(true);
        }
    }

    public void ProcessEliminate()
    {
        Managers.Gravity.BlackHole.SetActive(false);
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
        _sand.SetActive(false);
        IsSand = false;
        gameObject.SetActive(false);
    }
}
