using UnityEngine;

public class GravityManager
{
    public GravityStat GravityStat { get; private set; }
    public BlackHoleController BlackHole { get; set; }
    public WhiteHoleController WhiteHole { get; set; }

    public GameObject LoadBlackHole => Resources.Load<GameObject>("KCH/Prefabs/BlackHole");
    public GameObject LoadWhiteHole => Resources.Load<GameObject>("KCH/Prefabs/WhiteHole");

    public void Init()
    {
        GravityStat = Resources.Load<GravityStat>("KCH/Datas/GravityStat");
    }

    public void Clear()
    {
        DestroyHole(HoleType.Black);
        DestroyHole(HoleType.White);
    }

    public void CreateHole(HoleType holeType, Vector3 position)
    {
        DestroyHole(holeType);

        GameObject hole = Object.Instantiate(holeType == HoleType.Black ? LoadBlackHole : LoadWhiteHole);
        hole.transform.position = position;
    }

    public void DestroyHole(HoleType holeType)
    {
        switch (holeType)
        {
            case HoleType.Black:
                if (BlackHole == null)
                    return;
                Object.Destroy(BlackHole.gameObject);
                BlackHole = null;
                break;
            case HoleType.White:
                if (WhiteHole == null)
                    return;
                Object.Destroy(WhiteHole.gameObject);
                WhiteHole = null;
                break;
        }
    }
}
