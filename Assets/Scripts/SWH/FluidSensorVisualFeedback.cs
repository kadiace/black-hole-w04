using UnityEngine;

[DisallowMultipleComponent]
public sealed class FluidSensorVisualFeedback : MonoBehaviour
{
    // 연결
    [SerializeField] private FluidLevelSensor sensor;
    [SerializeField] private MeshRenderer[] cords;
    [SerializeField] private Material poweredMaterial;
    [SerializeField] private TextMesh signText;

    // 표시 설정
    [SerializeField] private string poweredText = "Power On";
    [SerializeField] private Color poweredColor = Color.green;

    private Material[] originalMaterials;
    private string originalText;
    private Color originalColor;
    private int activeCordCount;
    private bool signPowered;

    // 원상태 저장
    private void Awake()
    {
        originalMaterials = new Material[cords.Length];

        for (int i = 0; i < cords.Length; i++)
        {
            originalMaterials[i] = cords[i].sharedMaterial;
        }

        originalText = signText.text;
        originalColor = signText.color;
    }

    // 연출 갱신
    private void Update()
    {
        if (sensor.IsFilled)
        {
            SetCordCount(cords.Length);
            SetSign(true);
            return;
        }

        if (sensor.IsOnDelayCounting)
        {
            int count = cords.Length > 1 ? Mathf.Clamp(1 + Mathf.FloorToInt(sensor.OnDelayProgress * (cords.Length - 1)), 1, cords.Length) : 0;
            SetCordCount(count);
            SetSign(false);
            return;
        }

        SetCordCount(0);
        SetSign(false);
    }

    // 전선 재질
    private void SetCordCount(int count)
    {
        if (activeCordCount == count)
        {
            return;
        }

        for (int i = 0; i < cords.Length; i++)
        {
            cords[i].sharedMaterial = i < count ? poweredMaterial : originalMaterials[i];
        }

        activeCordCount = count;
    }

    // 안내판 표시
    private void SetSign(bool powered)
    {
        if (signPowered == powered)
        {
            return;
        }

        signText.text = powered ? poweredText : originalText;
        signText.color = powered ? poweredColor : originalColor;
        signPowered = powered;
    }

    // 원상태 복구
    private void OnDisable()
    {
        if (originalMaterials == null)
        {
            return;
        }

        SetCordCount(0);
        SetSign(false);
    }
}
