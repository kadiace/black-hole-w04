using UnityEngine;

[RequireComponent(typeof(Renderer))]
public class CableLogic : LogicBase
{
    [SerializeField]
    private Material poweredMaterial;
    [SerializeField]
    private float delayTimer;
    [SerializeField]
    private LogicBase nextLogic;
    [SerializeField]
    private bool IsConnect = false;

    private Renderer rd;
    private Material origin_mat;


    private float timer = 0f;
    private void Awake()
    {
        rd = GetComponent<Renderer>();
        origin_mat = rd.sharedMaterial;
    }
    private void Update()
    {
        if (!IsConnect)
        {
            timer = 0;
            return;
        }

        timer = Mathf.Clamp(timer + Time.deltaTime, 0f, delayTimer);
        if (timer == delayTimer)
        {
            rd.sharedMaterial = poweredMaterial;
            nextLogic?.SetActive(true);
        }
        else
        {
            rd.sharedMaterial = origin_mat;
            nextLogic?.SetActive(false);
        }

    }

    public override void SetActive(bool active)
    {
        IsConnect = active;
    }

    public override void SetForceActive(bool active)
    {
        IsConnect = active;
    }
}
