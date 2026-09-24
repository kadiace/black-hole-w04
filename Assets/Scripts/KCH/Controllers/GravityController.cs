using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityController : MonoBehaviour
{
    [SerializeField]
    private float gravityAcceleration = 9.81f;
    private Rigidbody _rb;
    private Vector3? _gravityCenter;
    public Vector3 GravityDir { get; private set; }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        GravityDir = _gravityCenter.HasValue ?
            (_gravityCenter.Value - transform.position).normalized : Vector3.down;

        _rb.AddForce(GravityDir * gravityAcceleration, ForceMode.Acceleration);
    }

    public void SetGravityCenter(Vector3? center)
    {
        _gravityCenter = center;
    }
}
