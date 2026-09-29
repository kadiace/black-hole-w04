using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public abstract class SceneContext : MonoBehaviour
{
    [Header("Guide")]
    [SerializeField]
    protected GuideCanvas _guideCanvas;

    [Header("Stage")]
    [SerializeField]
    protected SceneType _currentStage;
    [SerializeField]
    private SceneType _nextStage;
    [SerializeField]
    private TriggerChecker _nextStageTrigger;

    protected virtual void Awake()
    {
        Managers.Input.SetInputMode(InputMode.Player);
        if (_nextStageTrigger != null && _nextStage != SceneType.Unknown)
            _nextStageTrigger.OnTriggerEntered += OnNextStageEnter;
        OnInitialize();
        Managers.Input.PlayerMap.Restart.performed += RestartStage;
    }

    protected virtual void OnDestroy()
    {
        Managers.Input.PlayerMap.Restart.performed -= RestartStage;
    }

    protected abstract void OnInitialize();

    protected void ShowGuide(string message)
    {
        Managers.Input.SetInputMode(InputMode.UI);

        _guideCanvas.Text = message;
        _guideCanvas.gameObject.SetActive(true);
    }

    private void RestartStage(InputAction.CallbackContext context)
    {
        Managers.Clear();
        SceneManager.LoadScene(_currentStage.ToString());
    }

    protected virtual void OnNextStageEnter(Collider other)
    {
        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null)
            return;

        Managers.Clear();
        SceneManager.LoadScene(_nextStage.ToString());
    }
}
