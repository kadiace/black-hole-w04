using System.Collections;
using UnityEngine;

public class TitleSceneContext : MonoBehaviour
{
    [Header("Hole Position")]
    [SerializeField]
    private Transform _blackHolePosition;
    [SerializeField]
    private Transform _whiteHolePosition;

    void Start()
    {
        Managers.Gravity.CreateWhiteHole(_whiteHolePosition.position);
        StartCoroutine(CreateBlackHoleRoutine(_blackHolePosition.position));

        Managers.Gravity.CanFireBlackHole = false;
        Managers.Gravity.CanFireWhiteHole = false;
    }

    private IEnumerator CreateBlackHoleRoutine(Vector3 position)
    {
        while (true)
        {
            Managers.Gravity.CreateBlackHole(position);

            yield return new WaitForSeconds(12f);
        }
    }
}

