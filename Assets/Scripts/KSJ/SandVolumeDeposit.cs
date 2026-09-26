using UnityEngine;

/// <summary>
/// Optional bridge for a falling sand chunk. Add this component to a
/// projectile or trigger volume and set Sand Amount. On first contact it
/// deposits into an existing SandMesh, or creates a runtime patch on an empty
/// floor through SandMesh.AddSandAtWorld.
/// </summary>
public sealed class SandVolumeDeposit : MonoBehaviour
{
    [SerializeField, Min(0f)]
    private float sandAmount = 1f;
    [SerializeField]
    private bool destroyAfterDeposit = true;

    private bool m_deposited;

    public void SetSandAmount(float amount)
    {
        sandAmount = Mathf.Max(0f, amount);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.contactCount > 0)
            Deposit(collision.GetContact(0).point);
    }

    private void OnTriggerEnter(Collider other)
    {
        Deposit(other.ClosestPoint(transform.position));
    }

    private void Deposit(Vector3 worldPoint)
    {
        if (m_deposited || sandAmount <= 0f)
            return;

        float accepted = SandMesh.AddSandAtWorld(worldPoint, sandAmount, true);
        if (accepted <= 0f)
            return;

        sandAmount -= accepted;
        m_deposited = true;
        if (destroyAfterDeposit)
            Destroy(gameObject);
    }
}
