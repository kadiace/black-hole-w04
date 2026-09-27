using UnityEngine;

public class CuttableWall : Sliceable, IInteractable
{
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

    Rigidbody rb;
    Collider col;
    bool isStatic;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        isStatic = rb != null;
        col = GetComponent<Collider>();
    }

    public void Interact(IInteractor interactor)
    {
        if (!isStatic)
            return;
        if (Time.time < nextInteractTime)
            return;

        transform.SetParent(interactor.SnapAt);
        transform.localPosition = Vector3.zero;

        rb.isKinematic = true;
        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;

        return;
    }

    public void Release(IInteractor interactor)
    {
        if (!isStatic)
            return;
        if (!transform.IsChildOf(interactor.SnapAt))
            return;

        transform.SetParent(null);

        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;

        ResolveGroundOverlap();

        rb.isKinematic = false;

        nextInteractTime = Time.time + regrabDelay;
    }

    private void ResolveGroundOverlap()
    {
        // 절단으로 콜라이더가 MeshCollider 로 교체됐을 수 있으므로 다시 가져온다
        if (col == null)
            col = GetComponent<Collider>();

        Vector3 origin = col.bounds.center + Vector3.up * 0.1f;

        float castDistance = col.bounds.extents.y + groundCheckDistance;

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

        float cubeBottom = col.bounds.min.y;
        float groundY = groundHit.Value.point.y + groundSkin;

        if (cubeBottom >= groundY)
            return;

        float correction = groundY - cubeBottom;

        transform.position += Vector3.up * correction;
    }
}
