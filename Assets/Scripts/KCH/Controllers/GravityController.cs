using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityController : MonoBehaviour
{
    private Rigidbody _rb;
    private Vector3? _gravityCenter = null;
    public Vector3 GravityDir { get; private set; }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        GravityDir = _gravityCenter.HasValue ?
            (_gravityCenter.Value - transform.position).normalized : Vector3.down;

        float gravityAcceleration = _gravityCenter.HasValue ?
            Managers.Gravity.GravityStat.BlackHoleGravity : Managers.Gravity.GravityStat.NormalGravity;

        _rb.AddForce(GravityDir * gravityAcceleration, ForceMode.Acceleration);
    }

    public void SetGravityCenter(Vector3? center)
    {
        _gravityCenter = center;
    }
}
