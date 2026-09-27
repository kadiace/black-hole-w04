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
    [SerializeField]
    private float _scalingDuration;
    private bool _isScaling;
    private ScalingType _scalingType;
    private float _scalingElapsed;
    private float _startScale;
    private float _targetScale;
    private bool _isEliminating;

    void Awake()
    {
        _eventHorizon.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
    }

    void Update()
    {
        if (_isScaling)
        {
            _scalingElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(_scalingElapsed / _scalingDuration);

            t = Mathf.Pow(t, 20f);
            float eventHorizonScale = Mathf.Lerp(_startScale, _targetScale, t);
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
        _isEliminating = false;
        _isScaling = false;
        _eventHorizon.localScale = Managers.Gravity.GravityStat.InitScale * Vector3.one;
        SetActive(Managers.Gravity.BlackHole == null ? false : Managers.Gravity.IsBlackHoleEnabled);
    }

    public void SetActive(bool isActivated)
    {
        _isScaling = true;
        _scalingType = isActivated ? ScalingType.Expand : ScalingType.Shrink;
        _startScale = _eventHorizon.localScale.x;
        _targetScale = isActivated ? Managers.Gravity.GravityStat.EventHorizonScale : Managers.Gravity.GravityStat.InitScale;
        _scalingElapsed = 0f;
        _renderer.sharedMaterial = isActivated ? _onMaterial : _offMaterial;
        if (isActivated)
            _emission.gameObject.SetActive(true);
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
        Managers.Gravity.BlackHole.SetActive(false);
    }
}
