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
        Debug.Log("Outer Enter");
    }

    private void InnerEnter(Collider other)
    {
        Debug.Log("Inner Enter");
    }

    private void EventHorizonEnter(Collider other)
    {
        Debug.Log("Event Horizon Enter");
    }

    private void OuterExit(Collider other)
    {
        Debug.Log("Outer Exit");
    }

    private void InnerExit(Collider other)
    {
        Debug.Log("Inner Exit");
    }

    private void EventHorizonExit(Collider other)
    {
        Debug.Log("Event Horizon Exit");
    }
}
