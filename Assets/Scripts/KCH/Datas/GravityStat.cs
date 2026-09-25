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
    private float _duration;
    public float OuterScale => _outerScale;
    public float InnerScale => _innerScale;
    public float Duration => _duration;
}
