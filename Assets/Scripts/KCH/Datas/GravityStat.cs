using UnityEngine;

[CreateAssetMenu(fileName = "GravityStat", menuName = "Scriptable Objects/GravityStat")]
public class GravityStat : ScriptableObject
{
    [Header("Gravity")]
    [SerializeField]
    private float _normalGravity;
    [SerializeField]
    private float _blackHoleGravity;
    public float NormalGravity => _normalGravity;
    public float BlackHoleGravity => _blackHoleGravity;

    [Header("BlackHole")]
    [SerializeField]
    private float _outerScale;
    [SerializeField]
    private float _innerScale;
    [SerializeField]
    private float _eventHorizonScale;
    [SerializeField]
    private float _initScale;
    [SerializeField]
    private float _duration;
    public float OuterScale => _outerScale;
    public float InnerScale => _innerScale;
    public float EventHorizonScale => _eventHorizonScale;
    public float InitScale => _initScale;
    public float Duration => _duration;
}
