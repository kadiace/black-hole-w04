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
    private GameObject _holePreview;

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
            Managers.Gravity.CreateHole(HoleType.Black, transform.position + transform.forward * _minDistance);

            _lineRenderer.enabled = false;
            _holePreview.SetActive(false);
            _isLProcessed = false;
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
            Managers.Gravity.CreateHole(HoleType.White, transform.position + transform.forward * _minDistance);

            _lineRenderer.enabled = false;
            _holePreview.SetActive(false);
            _isRProcessed = false;
        }
    }

    private void UpdatePreview()
    {
        Vector3 startPosition = transform.position;
        Vector3 previewPosition = startPosition + transform.forward * _minDistance;

        _lineRenderer.SetPosition(0, startPosition);
        _lineRenderer.SetPosition(1, startPosition + transform.forward * _maxDistance);

        _holePreview.transform.position = previewPosition;
    }
}
