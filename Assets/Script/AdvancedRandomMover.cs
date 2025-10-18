using UnityEngine;

public class AdvancedRandomMover : MonoBehaviour
{
    [Header("Position Settings")]
    public Vector3 moveRange = new Vector3(3f, 1f, 3f); // 移動範圍
    public float moveSpeed = 2f;                        // 移動速度
    public float changePosInterval = 3f;                // 換位置間隔

    [Header("Rotation Settings")]
    public Vector3 rotationSpeedRange = new Vector3(30f, 60f, 30f); // 每軸最大旋轉速度 (度/秒)
    private Vector3 rotationSpeed;                                 // 當前旋轉速度

    [Header("Scale Settings")]
    public Vector2 scaleRange = new Vector2(0.8f, 1.2f); // 隨機縮放範圍
    public float scaleChangeSpeed = 1f;                  // 縮放平滑速度

    private Vector3 originPos;
    private Vector3 targetPos;
    private float timer;
    private float targetScale;
    private float currentScale;

    void Start()
    {
        originPos = transform.position;
        targetPos = GetRandomPos();
        rotationSpeed = GetRandomRotationSpeed();

        currentScale = transform.localScale.x;
        targetScale = GetRandomScale();
    }

    void Update()
    {
        if (Time.deltaTime == 0)
            return;
        timer += Time.deltaTime;

        // ----- 移動 -----
        transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * moveSpeed);
        if (timer >= changePosInterval || Vector3.Distance(transform.position, targetPos) < 0.05f)
        {
            targetPos = GetRandomPos();
            rotationSpeed = GetRandomRotationSpeed();
            targetScale = GetRandomScale();
            timer = 0f;
        }

        // ----- 旋轉 -----
        transform.Rotate(rotationSpeed * Time.deltaTime, Space.Self);

        // ----- 縮放 -----
        currentScale = Mathf.Lerp(currentScale, targetScale, Time.deltaTime * scaleChangeSpeed);
        transform.localScale = Vector3.one * currentScale;
    }

    Vector3 GetRandomPos()
    {
        return originPos + new Vector3(
            Random.Range(-moveRange.x, moveRange.x),
            Random.Range(-moveRange.y, moveRange.y),
            Random.Range(-moveRange.z, moveRange.z)
        );
    }

    Vector3 GetRandomRotationSpeed()
    {
        return new Vector3(
            Random.Range(-rotationSpeedRange.x, rotationSpeedRange.x),
            Random.Range(-rotationSpeedRange.y, rotationSpeedRange.y),
            Random.Range(-rotationSpeedRange.z, rotationSpeedRange.z)
        );
    }

    float GetRandomScale()
    {
        return Random.Range(scaleRange.x, scaleRange.y);
    }
}
