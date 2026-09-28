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

    public void Interact(IInteractor interactor)
    {
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
        if (!transform.IsChildOf(interactor.SnapAt))
            return;

        transform.SetParent(null);

        rb.angularVelocity = Vector3.zero;
        rb.linearVelocity = Vector3.zero;

        rb.isKinematic = false;

        nextInteractTime = Time.time + regrabDelay;
    }
}