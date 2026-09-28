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

    protected override SceneType GetCurrentStage() => SceneType.Stage3;

    protected override SceneType GetNextStage() => SceneType.Stage4;
}
