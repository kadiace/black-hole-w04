using UnityEngine;

public class TriggerChecker : MonoBehaviour
{
    public System.Action<Collider> OnTriggerEntered;
    public System.Action<Collider> OnTriggerExited;

    void OnTriggerEnter(Collider other) => OnTriggerEntered?.Invoke(other);
    void OnTriggerExit(Collider other) => OnTriggerExited?.Invoke(other);
}
