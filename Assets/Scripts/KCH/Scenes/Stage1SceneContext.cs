using System.Collections;
using UnityEngine;

public class Stage1SceneContext : SceneContext
{
    private const string INITIAL_GUIDE = @"반갑습니다! 아래는 기본 조작에 대한 안내입니다.

{Move}: 이동
{Jump}: 점프
{Sprint}: 달리기
{Interact}: 상호작용
{Restart}: 재시작
{Pause}: 메뉴";
    [Header("Hole Position")]
    [SerializeField]
    private Transform _blackHolePosition;
    [SerializeField]
    private Transform _whiteHolePosition;

    protected override void OnInitialize()
    {
        ShowGuide(INITIAL_GUIDE);

        Managers.Gravity.CreateWhiteHole(new Vector3(-10, 5, -70));
        StartCoroutine(CreateBlackHoleRoutine(new Vector3(0, 2, -40)));

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

    protected override SceneType GetCurrentStage() => SceneType.Stage1;

    protected override SceneType GetNextStage() => SceneType.Stage2;
}
