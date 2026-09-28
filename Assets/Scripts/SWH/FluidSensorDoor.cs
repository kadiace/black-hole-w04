using UnityEngine;

public sealed class FluidSensorDoor : MonoBehaviour
{
    [SerializeField] private FluidLevelSensor sensor;
    [SerializeField] private Rigidbody doorL;
    [SerializeField] private Rigidbody doorR;
    [SerializeField, Min(0f)] private float openDistance = 4f;
    [SerializeField, Min(0.01f)] private float moveSpeed = 4f;

    private Vector3 closedL;
    private Vector3 closedR;

    // 닫힌 위치
    private void Awake()
    {
        closedL = doorL.position;
        closedR = doorR.position;
    }

    // 문 이동
    private void FixedUpdate()
    {
        float distance = sensor.IsFilled ? openDistance : 0f;
        Vector3 targetL = closedL + Vector3.forward * distance;
        Vector3 targetR = closedR - Vector3.forward * distance;
        float step = moveSpeed * Time.fixedDeltaTime;

        doorL.MovePosition(Vector3.MoveTowards(doorL.position, targetL, step));
        doorR.MovePosition(Vector3.MoveTowards(doorR.position, targetR, step));
    }
}