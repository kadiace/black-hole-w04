using System;
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
    private float _expandDuration;
    [SerializeField]
    private float _shrinkDuration;
    [SerializeField]
    private float _existDuration;
    [SerializeField]
    private AnimationCurve _blackHoleShrinkCurve;
    public float OuterScale => _outerScale;
    public float InnerScale => _innerScale;
    public float EventHorizonScale => _eventHorizonScale;
    public float InitScale => _initScale;
    public float ExpandDuration => _expandDuration;
    public float ShrinkDuration => _shrinkDuration;
    public float ExistDuration => _existDuration;
    public Func<float, float> BlackHoleExpandCurve => t => Mathf.Pow(t, 20f);
    public Func<float, float> BlackHoleShrinkCurve => t =>
    {
        float shrinkCurve = _blackHoleShrinkCurve.Evaluate(t);
        if (t >= 0.4f && t <= 0.9f)
        {
            float wave = Mathf.Sin(t * Mathf.PI * 2f / 0.01f) * 0.02f;
            shrinkCurve += wave;
        }
        return shrinkCurve;
    };
    public Func<float, float> WhiteHoleCurve => t => Mathf.Pow(t, 20f);
}
