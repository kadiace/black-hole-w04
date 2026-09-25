using UnityEngine;

public class BlackHoleController : MonoBehaviour
{
    [SerializeField]
    private TriggerChecker _outer;
    [SerializeField]
    private TriggerChecker _inner;
    [SerializeField]
    private TriggerChecker _eventHorizon;

    public System.Action OnRemoved;

    void Awake()
    {
        Managers.Gravity.BlackHole = this;
        _outer.transform.localScale = Managers.Gravity.GravityStat.OuterScale * Vector3.one;
        _inner.transform.localScale = Managers.Gravity.GravityStat.InnerScale * Vector3.one;

        _outer.OnTriggerEntered += OuterEnter;
        _inner.OnTriggerEntered += InnerEnter;
        _eventHorizon.OnTriggerEntered += EventHorizonEnter;
        _outer.OnTriggerExited += OuterExit;
        _inner.OnTriggerExited += InnerExit;

        Destroy(gameObject, 10f);
    }

    void OnDestroy()
    {
        OnRemoved?.Invoke();
    }

    private void OuterEnter(Collider other)
    {
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(this, transform.position);
    }

    private void InnerEnter(Collider other)
    {
        // 1. Rotate Player up to -GravityDir
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.InInner = true;

        // 2. Cut Rigid Body object

        // 3. Affect fluid 
    }

    private void EventHorizonEnter(Collider other)
    {
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
