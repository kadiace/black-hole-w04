using UnityEngine;

public sealed class SceneWhiteHolePlacement : MonoBehaviour
{
    private WhiteHoleController _whiteHole;

    // 초기 배치
    private void OnEnable()
    {
        _whiteHole = Managers.Gravity.WhiteHole;

        if (_whiteHole == null)
        {            
            return;
        }

        _whiteHole.gameObject.SetActive(false);
        _whiteHole.transform.SetPositionAndRotation(transform.position, transform.rotation);
        _whiteHole.gameObject.SetActive(true);
    }

    // 씬 정리
    private void OnDisable()
    {
        if (_whiteHole != null)
        {
            _whiteHole.gameObject.SetActive(false);
            _whiteHole = null;
        }
    }
}