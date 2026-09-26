using UnityEngine;
public enum ButtonType
{
    Hold,       // 누르는 동안만 활성
    Timed,      // 누르면 일정 시간 활성
    Toggle      // 누를 때마다 ON/OFF
}
public class Interactive_Button : MonoBehaviour, IInteractable
{
    [SerializeField]
    private ButtonType buttonType;

    [SerializeField]
    private float timerDuration = 3f;

    [SerializeField]
    private LogicBase logicTarget;

    private float ticker;
    private bool isActive;

    private void Update()
    {
        if (buttonType != ButtonType.Timed || !isActive)
            return;

        ticker += Time.deltaTime;

        if (ticker >= timerDuration)
        {
            ticker = timerDuration;

            SetState(false);
        }
    }

    public void Interact()
    {
        switch (buttonType)
        {
            case ButtonType.Hold:
                SetState(true);
                break;

            case ButtonType.Timed:
                ticker = 0f;
                SetState(true);
                break;

            case ButtonType.Toggle:
                SetState(!isActive);
                break;
        }
    }

    public void Release()
    {
        if (buttonType != ButtonType.Hold)
            return;

        SetState(false);
    }

    private void SetState(bool active)
    {
        if (isActive == active)
            return;

        isActive = active;

        logicTarget.SetActive(active);
    }
}
