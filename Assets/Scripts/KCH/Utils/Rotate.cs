using UnityEngine;

public class Rotate : MonoBehaviour
{
    [SerializeField] private float _rotateSpeed = 90f;

    void Update()
    {
        float angle = _rotateSpeed * Time.time;
        transform.rotation = Quaternion.Euler(0f, angle, 0f);
    }
}
