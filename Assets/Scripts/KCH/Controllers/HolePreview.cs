using UnityEngine;

public class HolePreview : MonoBehaviour
{
    public enum HoleType
    {
        Black,
        White
    }

    [SerializeField]
    private GameObject _eventHorizonObj;
    [SerializeField]
    private GameObject _InnerObj;
    [SerializeField]
    private HoleType _type;

    public void Show(HoleType type, float horizonScale, float innerScale)
    {
        gameObject.SetActive(true);
        _type = type;
        _eventHorizonObj.transform.localScale = Vector3.one;
        _InnerObj.transform.localScale = Mathf.Clamp((innerScale - horizonScale), 0, innerScale) * Vector3.one;
    }

    public void Hide()
    {
        gameObject.SetActive(false);
        _eventHorizonObj.transform.localScale = Vector3.zero;
        _InnerObj.transform.localScale = Vector3.zero;
    }
}
