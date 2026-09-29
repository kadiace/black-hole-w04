using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class FluidBuoyantBody : MonoBehaviour
{
    // 연결
    [SerializeField] private FluidSubmersionSystem submersionSystem;
    [SerializeField] private Collider targetCollider;

    // 부력 설정
    [SerializeField, Min(0f)] private float fluidDensity = 2f;
    [SerializeField, Min(0f)] private float fluidGravity = 9.81f;
    [SerializeField, Min(0f)] private float waterDrag = 2f;

    public float Submersion { get; private set; }

    private Rigidbody body;

    // 컴포넌트 찾기
    private void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    // 측정 등록
    private void OnEnable()
    {
        if (submersionSystem == null || targetCollider == null || targetCollider.isTrigger || targetCollider.attachedRigidbody != body)
        {
            Debug.LogError("부력 대상의 측정 시스템과 Rigidbody에 연결된 Collider를 확인하세요.", this);
            enabled = false;
            return;
        }

        if (!(targetCollider is BoxCollider) && !(targetCollider is SphereCollider) && !(targetCollider is CapsuleCollider))
        {
            Debug.LogError("부력 대상은 Box, Sphere, Capsule Collider만 지원합니다.", this);
            enabled = false;
            return;
        }

        submersionSystem.Register(targetCollider);
    }

    // 부력 적용
    private void FixedUpdate()
    {
        Submersion = submersionSystem.GetSubmersion(targetCollider);

        if (body.isKinematic || Submersion <= 0f)
        {
            return;
        }

        float volume = CalculateVolume(targetCollider);
        float buoyantForce = fluidDensity * fluidGravity * volume * Submersion;

        body.AddForce(Vector3.up * buoyantForce, ForceMode.Force);
        body.AddForce(-body.linearVelocity * waterDrag * Submersion, ForceMode.Acceleration);
    }

    // 부피 계산
    private static float CalculateVolume(Collider target)
    {
        Vector3 scale = target.transform.lossyScale;
        Vector3 absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));

        if (target is BoxCollider box)
        {
            Vector3 size = Vector3.Scale(box.size, absoluteScale);
            return size.x * size.y * size.z;
        }

        if (target is SphereCollider sphere)
        {
            float radius = sphere.radius * Mathf.Max(absoluteScale.x, absoluteScale.y, absoluteScale.z);
            return 4f * Mathf.PI * radius * radius * radius / 3f;
        }

        if (target is CapsuleCollider capsule)
        {
            float axisScale;
            float radiusScale;

            if (capsule.direction == 0)
            {
                axisScale = absoluteScale.x;
                radiusScale = Mathf.Max(absoluteScale.y, absoluteScale.z);
            }
            else if (capsule.direction == 1)
            {
                axisScale = absoluteScale.y;
                radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.z);
            }
            else
            {
                axisScale = absoluteScale.z;
                radiusScale = Mathf.Max(absoluteScale.x, absoluteScale.y);
            }

            float radius = capsule.radius * radiusScale;
            float cylinderHeight = Mathf.Max(0f, capsule.height * axisScale - 2f * radius);
            return Mathf.PI * radius * radius * cylinderHeight + 4f * Mathf.PI * radius * radius * radius / 3f;
        }

        return 0f;
    }

    // 측정 해제
    private void OnDisable()
    {
        if (submersionSystem != null && targetCollider != null)
        {
            submersionSystem.Unregister(targetCollider);
        }

        Submersion = 0f;
    }
}