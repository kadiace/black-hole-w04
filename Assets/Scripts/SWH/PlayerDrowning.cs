using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class PlayerDrowning : MonoBehaviour
{
    // 연결
    [SerializeField] private FluidSubmersionSystem submersionSystem;
    [SerializeField] private CapsuleCollider playerCollider;

    // 익사 설정
    [SerializeField, Range(0f, 1f)] private float drownThreshold = 0.9f;
    [SerializeField, Range(0f, 1f)] private float resetThreshold = 0.85f;
    [SerializeField, Min(0f)] private float requiredDuration = 0.75f;
    [SerializeField] private UnityEvent onDrowned = new UnityEvent();

    public float Submersion { get; private set; }
    public bool IsDrowned { get; private set; }

    private float submergedTime;

    // 콜라이더 찾기
    private void Awake()
    {
        if (playerCollider == null)
        {
            playerCollider = GetComponentInChildren<CapsuleCollider>();
        }
    }

    // 측정 등록
    private void OnEnable()
    {
        if (submersionSystem == null || playerCollider == null || playerCollider.isTrigger)
        {            
            enabled = false;
            return;
        }

        Submersion = 0f;
        IsDrowned = false;
        submergedTime = 0f;
        submersionSystem.Register(playerCollider);
    }

    // 익사 판정
    private void Update()
    {
        if (IsDrowned)
        {
            return;
        }

        Submersion = submersionSystem.GetSubmersion(playerCollider);

        if (Submersion >= drownThreshold)
        {
            submergedTime += Time.deltaTime;

            if (submergedTime >= requiredDuration)
            {
                Drown();
            }
        }
        else if (Submersion <= Mathf.Min(resetThreshold, drownThreshold))
        {
            submergedTime = 0f;
        }
    }

    // 판정 전달
    private void Drown()
    {
        IsDrowned = true;
        Debug.Log($"플레이어 익사: 잠김 {Submersion:P0}", this);
        onDrowned?.Invoke();
    }

    // 측정 해제
    private void OnDisable()
    {
        if (submersionSystem != null && playerCollider != null)
        {
            submersionSystem.Unregister(playerCollider);
        }

        Submersion = 0f;
        submergedTime = 0f;
    }
}