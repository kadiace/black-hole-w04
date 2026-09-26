using UnityEngine;

public class PressurePlateTrigger : MonoBehaviour
{
    [SerializeField]
    private PressurePlate pressurePlate;

    private void OnTriggerEnter(Collider other)
    {
        pressurePlate.OnEnter(other);
    }

    private void OnTriggerExit(Collider other)
    {
        pressurePlate.OnExit(other);
    }
}