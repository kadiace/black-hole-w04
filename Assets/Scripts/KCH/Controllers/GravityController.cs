using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class GravityController : MonoBehaviour
{
    [SerializeField] private float gravityAcceleration = 9.81f;

    private Rigidbody m_rb;
    private Vector3 m_gravityCenter;

    void Awake()
    {
        m_rb = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        Vector3 direction = m_gravityCenter != null ?
            (m_gravityCenter - transform.position).normalized : Vector3.down;

        m_rb.AddForce(direction * gravityAcceleration, ForceMode.Acceleration);
    }

    public void SetGravityCenter(Vector3 center)
    {
        m_gravityCenter = center;
    }
}
