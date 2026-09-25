using UnityEngine;

public class WhiteHoleController : MonoBehaviour
{
    void Awake()
    {
        Managers.Gravity.WhiteHole = this;
    }
}
