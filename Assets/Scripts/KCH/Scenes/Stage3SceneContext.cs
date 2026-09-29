using UnityEngine;

public class Stage3SceneContext : SceneContext
{
    [SerializeField]
    private Transform whiteholePos;

    protected override void OnInitialize()
    {
        Managers.Gravity.CanFireBlackHole = true;
        Managers.Gravity.CanFireWhiteHole = false;


        Managers.Gravity.CreateWhiteHole(whiteholePos.position);
    }
}
