using UnityEngine;

public class BlackHoleController : MonoBehaviour
{
    [SerializeField] private TriggerChecker outer;
    [SerializeField] private TriggerChecker inner;
    [SerializeField] private TriggerChecker eventHorizon;

    void Awake()
    {
        outer.OnTriggerEntered += OuterEnter;
        inner.OnTriggerEntered += InnerEnter;
        eventHorizon.OnTriggerEntered += EventHorizonEnter;
        outer.OnTriggerExited += OuterExit;
        inner.OnTriggerExited += InnerExit;
        eventHorizon.OnTriggerExited += EventHorizonExit;
    }

    private void OuterEnter(Collider other)
    {
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(transform.position);
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
        Debug.Log("Event Horizon Enter");
    }

    private void OuterExit(Collider other)
    {
        GravityController gravityController = other.GetComponentInParent<GravityController>();
        if (gravityController == null)
            return;

        gravityController.SetGravityCenter(null);
    }

    private void InnerExit(Collider other)
    {
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.InInner = false;
    }

    private void EventHorizonExit(Collider other)
    {
        Debug.Log("Event Horizon Exit");
    }
}
