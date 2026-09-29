using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class Stage5SceneContext : SceneContext
{
    private const string CLEAR_GUIDE = @"축하합니다!
    
    준비한 모든 스테이지를 훌륭히 완수하셨습니다!";

    private Action<InputAction.CallbackContext> _confirmHandler;

    protected override void OnInitialize()
    {
        Managers.Gravity.CanFireBlackHole = true;
        Managers.Gravity.CanFireWhiteHole = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        Managers.Input.UIMap.Confirm.performed -= _confirmHandler;
    }

    protected override void OnNextStageEnter(Collider other)
    {
        ShowGuide(CLEAR_GUIDE);
        _confirmHandler = context => base.OnNextStageEnter(other);
        Managers.Input.UIMap.Confirm.performed += _confirmHandler;
    }

    public void RestartAfterDrowning()
    {
        Managers.Clear();
        SceneManager.LoadScene(_currentStage.ToString());
    }
}
