using UnityEngine;

public class Stage3SceneContext : SceneContext
{

    protected override void OnInitialize()
    {
        Managers.Gravity.CanFireBlackHole = true;
        Managers.Gravity.CanFireWhiteHole = false;


        Managers.Gravity.CreateWhiteHole(new Vector3(-3f, 7.5f, -10));
    }

    protected override SceneType GetCurrentStage() => SceneType.Stage3;

    protected override SceneType GetNextStage() => SceneType.Stage4;
}
