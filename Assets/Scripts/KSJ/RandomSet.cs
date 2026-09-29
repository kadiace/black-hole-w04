using System.Collections.Generic;
using UnityEngine;

public class RandomSet : MonoBehaviour
{
    [SerializeField]
    private Transform[] _points;
    [SerializeField]
    private GameObject _cubeObject1;
    [SerializeField]
    private GameObject _cubeObject2;
    [SerializeField]
    private GameObject _switchObject;

    private void Start()
    {
        List<int> index = new List<int>();

        for (int i = 0; i < _points.Length; i++)
        {
            index.Add(i);
        }

        _cubeObject1.transform.position = PopRandom(index);
        _cubeObject2.transform.position = PopRandom(index);
        _switchObject.transform.position = PopRandom(index);
    }


    private Vector3 PopRandom(List<int> templist)
    {
        int pick = Random.Range(0, templist.Count);
        int pointIndex = templist[pick];
        templist.RemoveAt(pick);
        return _points[pointIndex].position;


    }
}
