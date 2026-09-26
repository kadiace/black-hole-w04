using System.Collections.Generic;
using UnityEngine;

// 압력판 상호작용 가능 객체
public interface IPressable
{
}

public class PressurePlate : MonoBehaviour
{
    [Header("References")]
    [SerializeField]
    private Transform buttonTop;

    [SerializeField]
    private LogicBase logicTarget;

    [Header("Button Position")]
    [SerializeField]
    private Vector3 releasedPosition;

    [SerializeField]
    private Vector3 pressedPosition;

    [Header("Press Effect")]
    [SerializeField]
    private float pressDuration = 0.2f;

    [SerializeField]
    private AnimationCurve pressCurve = AnimationCurve.EaseInOut(0, 0, 1, 1);

    [SerializeField]
    private TriggerChecker triggerChecker;


    private readonly Dictionary<Collider, IPressable> pressingColliders = new();

    private float pressProgress;
    private bool wasPressed;

    private void Awake()
    {
        triggerChecker.OnTriggerEntered += OnEnter;
        triggerChecker.OnTriggerExited += OnExit;
    }
    private void OnTriggerEnter(Collider other)
    {
        OnEnter(other);
    }


    private void OnTriggerExit(Collider other)
    {
        OnExit(other);
    }


    private void Update()
    {
        UpdateButtonState();
        UpdateButtonPosition();
    }


    private void UpdateButtonState()
    {
        bool isPressed = pressingColliders.Count > 0;

        if (isPressed == wasPressed)
            return;

        wasPressed = isPressed;

        if (logicTarget != null)
        {
            logicTarget.SetActive(isPressed);
        }
    }


    private void UpdateButtonPosition()
    {
        bool isPressed = pressingColliders.Count > 0;

        float targetProgress = isPressed ? 1f : 0f;

        if (pressDuration <= 0f)
        {
            pressProgress = targetProgress;
        }
        else
        {
            pressProgress = Mathf.MoveTowards(
                pressProgress,
                targetProgress,
                Time.deltaTime / pressDuration
            );
        }

        float curvedProgress = pressCurve.Evaluate(pressProgress);

        buttonTop.localPosition = Vector3.Lerp(
            releasedPosition,
            pressedPosition,
            curvedProgress
        );
    }


    private IPressable FindPressable(Collider other)
    {
        if (other.TryGetComponent<IPressable>(out var pressable))
            return pressable;

        pressable = other.GetComponentInParent<IPressable>();

        if (pressable != null)
            return pressable;

        return other.GetComponentInChildren<IPressable>();
    }

    public void OnEnter(Collider other)
    {
        IPressable pressable = FindPressable(other);

        if (pressable == null)
            return;

        pressingColliders[other] = pressable;
    }

    public void OnExit(Collider other)
    {
        pressingColliders.Remove(other);
    }
}
