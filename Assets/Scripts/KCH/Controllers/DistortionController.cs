using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Volume), typeof(SphereCollider))]
public class DistortionController : MonoBehaviour
{
    [SerializeField, Range(0f, 1f)]
    private float _maxDistortion = 1f;

    [SerializeField]
    private Transform _endRadiusReference;

    private Volume _volume;
    private LensDistortion _distortion;
    private float _radius;
    private float _radiusMin;
    private PlayerController _playerController;

    void Start()
    {
        _volume = GetComponent<Volume>();
        _volume.profile.TryGet(out _distortion);
        _radius = transform.localScale.x / 2;
        _radiusMin = _endRadiusReference.localScale.x / 2;
    }

    void Update()
    {
        if (_playerController == null)
            return;
        float distance = Vector3.Distance(transform.position, _playerController.transform.position);
        _radiusMin = _endRadiusReference.localScale.x / 2;

        float t = Mathf.Clamp01((distance - _radiusMin) / (_radius - _radiusMin));
        float strength = 1f - t;

        Debug.Log($"distance: {distance} " + $"_radius: {_radius} " + $"_radiusMin: {_radiusMin} " + $"t: {t} " + $"value: {strength * _maxDistortion}");

        _distortion.intensity.value = strength * _maxDistortion;
    }

    void OnTriggerEnter(Collider other)
    {
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController == null)
            return;
        _playerController = playerController;
    }

    void OnTriggerExit(Collider other)
    {
        PlayerController playerController = other.GetComponentInParent<PlayerController>();
        if (playerController == null)
            return;
        _playerController = null;
    }
}