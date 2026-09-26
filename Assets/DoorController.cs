using UnityEngine;

public class DoorController : LogicBase
{
    private enum ForceState
    {
        None,
        Open,
        Closed
    }

    [SerializeField]
    GameObject door_l;
    [SerializeField]
    GameObject door_r;

    [SerializeField]
    Vector3 closed_pos_l, closed_pos_r;

    [SerializeField]
    Vector3 opened_pos_l, opened_pos_r;
    [SerializeField]
    AnimationCurve doorAnim;

    [SerializeField]
    float timer;

    public float Timer => timer;

    [SerializeField]
    bool isOpen;
    public bool IsOpen => isOpen;

    private ForceState forceState = ForceState.None;

    public void ToggleOpen()
    {
        if (forceState != ForceState.None)
            return;

        if (isOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (forceState != ForceState.None)
            return;

        isOpen = true;
    }

    public void Close()
    {
        if (forceState != ForceState.None)
            return;

        isOpen = false;
    }

    private void Update()
    {
        timer += Time.deltaTime * (isOpen ? 1 : -1);
        timer = Mathf.Clamp(timer, 0, 1);
        door_l.transform.localPosition = Vector3.Lerp(closed_pos_l, opened_pos_l, timer);
        door_r.transform.localPosition = Vector3.Lerp(closed_pos_r, opened_pos_r, timer);
    }

    public override void SetActive(bool active)
    {
        if (forceState != ForceState.None)
            return;

        isOpen = active;
    }

    public override void SetForceActive(bool active)
    {
        if (forceState != ForceState.None)
        {
            forceState = active ? ForceState.Open : ForceState.Closed;
        }

        isOpen = active;
    }

    public void ForceOpen()
    {
        forceState = ForceState.Open;
        isOpen = true;
    }

    public void ForceClose()
    {
        forceState = ForceState.Closed;
        isOpen = false;
    }

    public void ReleaseForce()
    {
        forceState = ForceState.None;
    }
}