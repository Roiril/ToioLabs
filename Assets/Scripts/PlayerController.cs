using UnityEngine;

public class PlayerController : MonoBehaviour
{
    public float moveSpeed = 2.0f;
    public float rotateSpeed = 180.0f; // 1秒間に180度
    private Rigidbody rb;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) { rb = gameObject.AddComponent<Rigidbody>(); }
        
        // 念のため、ここでも回転を固定
        rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        rb.useGravity = true;
    }

    void Update()
    {
        // 回転の入力 (A, D, ←, →)
        float rotateInput = Input.GetAxisRaw("Horizontal");
        // 移動の入力 (W, S, ↑, ↓)
        float moveInput = Input.GetAxisRaw("Vertical");

        // Y軸回転
        transform.Rotate(0, rotateInput * rotateSpeed * Time.deltaTime, 0);

        // 前方への移動
        Vector3 moveVelocity = transform.forward * moveInput * moveSpeed;
        moveVelocity.y = rb.velocity.y; // 現在のY軸（重力）の速度は維持する
        
        rb.velocity = moveVelocity;
    }
}