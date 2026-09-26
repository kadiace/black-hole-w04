using UnityEngine;

public class WhiteHoleController : MonoBehaviour
{
    [SerializeField]
    private Renderer _renderer;
    [SerializeField]
    private Material _onMaterial;
    [SerializeField]
    private Material _offMaterial;
    [SerializeField]
    private GameObject _emission;

    void OnEnable()
    {
        SetActive(Managers.Gravity.BlackHole == null ? false : Managers.Gravity.IsBlackHoleEnabled);
    }

    public void SetActive(bool isActivated)
    {
        _renderer.sharedMaterial = isActivated ? _onMaterial : _offMaterial;
        _emission.SetActive(isActivated);
    }

    public void Deactivate()
    {
        gameObject.SetActive(false);
        Managers.Gravity.BlackHole.SetActive(false);
    }
}
