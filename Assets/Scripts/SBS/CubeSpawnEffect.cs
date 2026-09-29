using UnityEngine;

public class CubeSpawnEffect : LogicBase
{
    [SerializeField]
    MeshRenderer mr;
    [SerializeField]
    Rigidbody rb;

    private void Awake()
    {
        SetActive(false);
    }

    public override void SetActive(bool active)
    {
        mr.enabled = active;
        rb.useGravity = active;
        rb.constraints = active ? RigidbodyConstraints.None : RigidbodyConstraints.FreezePositionY;
    }

    public override void SetForceActive(bool active)
    {
        SetActive(active);
    }
}
