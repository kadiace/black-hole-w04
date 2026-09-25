
using UnityEngine;

public class GravityManager
{
    public GravityStat GravityStat { get; private set; }
    public BlackHoleController BlackHole { get; set; }
    public BlackHoleController WhiteHole { get; set; }

    public void Init()
    {
        GravityStat = Resources.Load<GravityStat>("KCH/Datas/GravityStat");
    }

    public void Clear()
    {

    }
}
