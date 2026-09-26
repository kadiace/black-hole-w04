using UnityEngine;

public class CubeGimmick : MonoBehaviour, IPressable, IInteractable
{
    Rigidbody rb;
    BoxCollider bc;

    [Header("Release")]
    [SerializeField]
    private LayerMask groundMask;

    [SerializeField]
    private float groundCheckDistance = 2f;

    [SerializeField]
    private float groundSkin = 0.02f;

    [SerializeField]
    private float regrabDelay = 0.5f;

    private float nextInteractTime;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        bc = GetComponent<BoxCollider>();
    }

    public void Interact()
    {
        //if (Time.time < nextInteractTime)
        //    return false;
        //
        //transform.SetParent(interactor.SnapAt);
        //transform.localPosition = Vector3.zero;
        //
        //rb.isKinematic = true;
        //rb.angularVelocity = Vector3.zero;
        //rb.linearVelocity = Vector3.zero;
        //
        //return true;
    }

    public void Release()
    {
        //if (!transform.IsChildOf(interactor.SnapAt))
        //    return;
        //
        //transform.SetParent(null);
        //
        //rb.angularVelocity = Vector3.zero;
        //rb.linearVelocity = Vector3.zero;
        //
        //ResolveGroundOverlap();
        //
        //rb.isKinematic = false;
        //
        //nextInteractTime = Time.time + regrabDelay;
    }

    private void ResolveGroundOverlap()
    {
        Vector3 origin = bc.bounds.center + Vector3.up * 0.1f;

        float castDistance = bc.bounds.extents.y + groundCheckDistance;

        RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, castDistance, ~0, QueryTriggerInteraction.Ignore);

        RaycastHit? groundHit = null;

        foreach (RaycastHit hit in hits)
        {
            if (!hit.collider.CompareTag("Ground"))
                continue;

            if (groundHit == null || hit.distance < groundHit.Value.distance)
                groundHit = hit;
        }

        if (groundHit == null)
            return;

        float cubeBottom = bc.bounds.min.y;
        float groundY = groundHit.Value.point.y + groundSkin;

        if (cubeBottom >= groundY)
            return;

        float correction = groundY - cubeBottom;

        transform.position += Vector3.up * correction;
    }
}