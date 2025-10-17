using UnityEngine;

public class RandomMover : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 2f;         // 移動速度
    public float changeTargetInterval = 3f; // 每幾秒換一個新目標
    public Vector3 moveRange = new Vector3(5f, 0f, 5f); // 隨機範圍（相對於初始位置）

    private Vector3 origin;
    private Vector3 targetPosition;
    private float timer;

    void Start()
    {
        origin = transform.position;
        PickNewTarget();
    }

    void Update()
    {
        // 移動到目標位置
        transform.position = Vector3.MoveTowards(transform.position, targetPosition, moveSpeed * Time.deltaTime);

        // 到達目標或時間到時換新目標
        timer += Time.deltaTime;
        if (timer >= changeTargetInterval || Vector3.Distance(transform.position, targetPosition) < 0.1f)
        {
            PickNewTarget();
            timer = 0f;
        }
    }

    void PickNewTarget()
    {
        // 在範圍內隨機挑一個位置
        Vector3 offset = new Vector3(
            Random.Range(-moveRange.x, moveRange.x),
            Random.Range(-moveRange.y, moveRange.y),
            Random.Range(-moveRange.z, moveRange.z)
        );

        targetPosition = origin + offset;
    }
}
