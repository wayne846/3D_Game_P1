// 這是 GPT 生的相機控制器
// 1. 在 Unity 中建立一個空物件或相機物件，例如 Main Camera。
// 2. 把這個腳本 FreeCameraController.cs 掛上去。
//      進入 Play 模式後：
//      右鍵拖曳滑鼠 → 旋轉視角
//      W / A / S / D → 前後左右移動
//      Q / E → 下／上移動
//      Shift → 加速
//      滑鼠滾輪 → 調整移動速度
// 3. 這樣就能完全模擬 Scene 視窗那種自由相機移動。

using UnityEngine;

[RequireComponent(typeof(Camera))]
public class FreeCameraController : MonoBehaviour
{
    [Header("Movement Settings")]
    public float moveSpeed = 5f;
    public float fastMoveMultiplier = 3f;
    public float lookSensitivity = 2f;
    public float scrollSpeed = 2f;

    private float rotationX;
    private float rotationY;
    private bool isMovedOrRotated;

    void Start()
    {
        // 初始化相機旋轉
        Vector3 euler = transform.eulerAngles;
        rotationX = euler.y;
        rotationY = euler.x;
    }

    void Update()
    {
        isMovedOrRotated = false;
        HandleMouseLook();
        HandleMovement();

        if (isMovedOrRotated)
        {
            var tracer = GetComponent<RayTracer_ShaderVer>();
            if (tracer != null && tracer.OnlyRenderOneTime)
            {
                tracer.DestroyQuad();
            }
        }
    }

    void HandleMouseLook()
    {
        // 按住右鍵才可旋轉視角（模仿 Scene View）
        if (Input.GetMouseButton(1))
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;

            isMovedOrRotated |= (Input.GetAxis("Mouse X") != 0 || Input.GetAxis("Mouse Y") != 0);
            rotationX += Input.GetAxis("Mouse X") * lookSensitivity;
            rotationY -= Input.GetAxis("Mouse Y") * lookSensitivity;
            rotationY = Mathf.Clamp(rotationY, -89f, 89f); // 防止上下翻轉

            transform.rotation = Quaternion.Euler(rotationY, rotationX, 0);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void HandleMovement()
    {
        Vector3 moveDir = Vector3.zero;

        if (Input.GetKey(KeyCode.W)) { isMovedOrRotated = true; moveDir += transform.forward; }  
        if (Input.GetKey(KeyCode.S)) { isMovedOrRotated = true; moveDir -= transform.forward; }  
        if (Input.GetKey(KeyCode.A)) { isMovedOrRotated = true; moveDir -= transform.right;   }
        if (Input.GetKey(KeyCode.D)) { isMovedOrRotated = true; moveDir += transform.right;   }
        if (Input.GetKey(KeyCode.E)) { isMovedOrRotated = true; moveDir += transform.up;      }
        if (Input.GetKey(KeyCode.Q)) { isMovedOrRotated = true; moveDir -= transform.up;      }

        // 滾輪控制移動速度（可選）
        moveSpeed += Input.mouseScrollDelta.y * scrollSpeed;
        moveSpeed = Mathf.Clamp(moveSpeed, 0.1f, 50f);

        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastMoveMultiplier : 1f);
        transform.position += moveDir.normalized * speed * Time.deltaTime;
    }
}

