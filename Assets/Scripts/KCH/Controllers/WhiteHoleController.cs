using UnityEngine;

public class WhiteHoleController : MonoBehaviour
{
    [SerializeField]
    private Renderer _renderer;
    [SerializeField]
    private Material _onMaterial;
    [SerializeField]
    private Material _offMaterial;

    void OnEnable()
    {
        SetActive(Managers.Gravity.BlackHole == null ? false : Managers.Gravity.BlackHole.gameObject.activeSelf);
    }

    public void SetActive(bool isActivated)
    {
        _renderer.sharedMaterial = isActivated ? _onMaterial : _offMaterial;
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
        Managers.Gravity.BlackHole.SetActive(false);
    }
}
