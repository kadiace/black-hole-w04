using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class Stage5SceneContext : SceneContext
{
    private const string CLEAR_GUIDE = @"축하합니다!
    
    준비한 모든 스테이지를 훌륭히 완수하셨습니다!";

    private bool _isNextStageEntering;

    protected override void OnInitialize()
    {
        Managers.Gravity.CanFireBlackHole = true;
        Managers.Gravity.CanFireWhiteHole = true;
    }

    protected override void OnDestroy()
    {
        Managers.Input.UIMap.Confirm.performed -= OnConfirmNextStage;

        base.OnDestroy();
    }

    protected override void OnNextStageEnter(Collider other)
    {
        if (_isNextStageEntering)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        _isNextStageEntering = true;

        Managers.Input.UIMap.Confirm.performed += OnConfirmNextStage;
        _guideCanvas.Button.onClick.AddListener(OnClickNextStage);

        ShowGuide(CLEAR_GUIDE);
    }

    public void RestartAfterDrowning()
    {
        Managers.Clear();
        SceneManager.LoadScene(_currentStage.ToString());
    }

    private void OnConfirmNextStage(InputAction.CallbackContext context)
    {
        Managers.Input.UIMap.Confirm.performed -= OnConfirmNextStage;

        Managers.Clear();
        SceneManager.LoadScene(_nextStage.ToString());
    }

    private void OnClickNextStage()
    {
        _guideCanvas.Button.onClick.RemoveListener(OnClickNextStage);

        Managers.Clear();
        SceneManager.LoadScene(_nextStage.ToString());
    }
}
