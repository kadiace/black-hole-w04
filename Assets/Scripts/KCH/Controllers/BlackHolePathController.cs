using UnityEngine;

[RequireComponent(typeof(LineRenderer))]
public class BlackHolePathController : MonoBehaviour
{
    [Header("Input")]
    private bool _isLProcessed;
    private bool _isRProcessed;

    [Header("Preview")]
    [SerializeField]
    private float _minDistance = 3f;
    [SerializeField]
    private float _maxDistance = 20f;
    [SerializeField]
    private float _moveSpeed;
    private GameObject _holePreview;
    private float _distance;

    [Header("Line Renderer")]
    [SerializeField]
    private float _lineWidth;
    private LineRenderer _lineRenderer;

    void Awake()
    {
        _holePreview = Instantiate(Resources.Load<GameObject>("KCH/Prefabs/HolePreview"));
        _holePreview.SetActive(false);

        _lineRenderer = GetComponent<LineRenderer>();
        _lineRenderer.useWorldSpace = true;
        _lineRenderer.alignment = LineAlignment.View;
        _lineRenderer.startWidth = _lineWidth;
        _lineRenderer.endWidth = _lineWidth;
        _lineRenderer.enabled = false;
    }

    void Update()
    {
        if (!_isRProcessed)
            ProcessLMouse();
        if (!_isLProcessed)
            ProcessRMouse();
    }

    private void ProcessLMouse()
    {
        if (Managers.Input.BlackHolePressed)
        {
            _lineRenderer.enabled = true;
            _holePreview.SetActive(true);
            _isLProcessed = true;
        }
        if (Managers.Input.BlackHoleHeld)
        {
            UpdatePreview();
        }
        if (Managers.Input.BlackHoleReleased)
        {
            Managers.Gravity.CreateHole(HoleType.Black, transform.position + transform.forward * _distance);

            _lineRenderer.enabled = false;
            _holePreview.SetActive(false);
            _isLProcessed = false;
            _distance = _minDistance;
        }
    }

    private void ProcessRMouse()
    {
        if (Managers.Input.WhiteHolePressed)
        {
            _lineRenderer.enabled = true;
            _holePreview.SetActive(true);
            _isRProcessed = true;
        }
        if (Managers.Input.WhiteHoleHeld)
        {
            UpdatePreview();
        }
        if (Managers.Input.WhiteHoleReleased)
        {
            Managers.Gravity.CreateHole(HoleType.White, transform.position + transform.forward * _distance);

            _lineRenderer.enabled = false;
            _holePreview.SetActive(false);
            _isRProcessed = false;
            _distance = _minDistance;
        }
    }

    private void UpdatePreview()
    {
        _distance += _moveSpeed * Time.deltaTime;
        if (_distance >= _maxDistance)
            _distance = _minDistance;

        Vector3 previewPosition = transform.position + transform.forward * _distance;

        _lineRenderer.SetPosition(0, transform.position);
        _lineRenderer.SetPosition(1, transform.position + transform.forward * _maxDistance);

        _holePreview.transform.position = previewPosition;
    }
}
