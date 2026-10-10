using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// 유저의 WASD 입력을 받아 오브젝트를 이동시키며,
/// Vector3.Lerp를 활용해 카메라가 플레이어를 부드럽게 추적하도록 제어하는 컴포넌트입니다.
/// 최신 Unity Input System(6/7) 및 레거시 Input Manager 환경 모두를 지원합니다.
/// 커스텀 HB_Log 클래스와 연동되어 이동 및 상태 로그를 인게임 콘솔에 출력합니다.
/// </summary>
public class NewMonoBehaviourScript : MonoBehaviour
{
    [Header("=== 플레이어 이동 설정 (Movement) ===")]
    [Tooltip("오브젝트의 이동 속도")]
    [SerializeField] private float moveSpeed = 5f;

    [Tooltip("3D(X-Z 평면) 이동 여부. 체크 해제 시 2D(X-Y 평면)로 이동합니다.")]
    [SerializeField] private bool is3DMovement = true;

    [Tooltip("이동 방향으로 오브젝트를 회전시킬지 여부 (3D 모드 전용)")]
    [SerializeField] private bool rotateTowardsMovement = true;

    [Tooltip("오브젝트 회전 속도")]
    [SerializeField] private float rotationSpeed = 10f;

    [Header("=== 카메라 추적 설정 (Camera Follow - Lerp) ===")]
    [Tooltip("플레이어를 추적할 대상 카메라 (비워둘 경우 Main Camera 자동 감지)")]
    [SerializeField] private Camera targetCamera;

    [Tooltip("카메라 추적 기능 활성화 여부")]
    [SerializeField] private bool enableCameraFollow = true;

    [Tooltip("플레이어 위치 기준 카메라 상대 오프셋")]
    [SerializeField] private Vector3 cameraOffset = new Vector3(0f, 5f, -10f);

    [Tooltip("카메라 추적 부드러움 계수 (Lerp t 배수값)")]
    [Range(0.1f, 30f)]
    [SerializeField] private float cameraSmoothSpeed = 5f;

    [Tooltip("카메라가 항상 플레이어를 바라보게 할지 여부")]
    [SerializeField] private bool cameraLookAtPlayer = true;

    private Vector2 currentInputVector = Vector2.zero;
    private float logTimer = 0f;

    private void Awake()
    {
        // 타깃 카메라가 미할당된 경우 Scene의 메인 카메라 자동 감지
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Start()
    {
        // 인게임 콘솔 테스트용 로그 출력
        HB_Log.Log("[Player] Player object initialized in scene.");
        HB_Log.LogWarning("[Player] In-Game Console UI active! Click top-right 'Logs' button to view.");
    }

    private void Update()
    {
        ProcessInput();
        MovePlayer();
    }

    private void LateUpdate()
    {
        if (enableCameraFollow && targetCamera != null)
        {
            FollowCamera();
        }
    }

    /// <summary>
    /// New Input System과 Legacy Input을 모두 지원하는 크로스 플랫폼 입력 수신
    /// </summary>
    private void ProcessInput()
    {
        Vector2 input = Vector2.zero;

#if ENABLE_INPUT_SYSTEM
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) input.y += 1f;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) input.y -= 1f;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) input.x -= 1f;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) input.x += 1f;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) input.y += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) input.y -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.x += 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x += 1f;
#else
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        input = new Vector2(h, v);
#endif

        // 대각선 이동 시 정규화
        currentInputVector = input.sqrMagnitude > 1f ? input.normalized : input;
    }

    /// <summary>
    /// 입력값에 따른 플레이어 위치 이동 및 회전
    /// </summary>
    private void MovePlayer()
    {
        if (currentInputVector.sqrMagnitude <= 0.0001f) return;

        Vector3 moveDirection;
        if (is3DMovement)
        {
            // X-Z 평면 이동 (3D 탑다운/쿼터뷰)
            moveDirection = new Vector3(currentInputVector.x, 0f, currentInputVector.y);
        }
        else
        {
            // X-Y 평면 이동 (2D)
            moveDirection = new Vector3(currentInputVector.x, currentInputVector.y, 0f);
        }

        transform.position += moveDirection * (moveSpeed * Time.deltaTime);

        // 주기적 이동 로그 (2초 간격)
        logTimer += Time.deltaTime;
        if (logTimer >= 2f)
        {
            logTimer = 0f;
            HB_Log.Log($"[Player Movement] Current Position: {transform.position}");
        }

        // 이동 방향으로 부드럽게 회전
        if (is3DMovement && rotateTowardsMovement && moveDirection != Vector3.zero)
        {
            Quaternion targetRotation = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
        }
    }

    /// <summary>
    /// Vector3.Lerp를 활용한 부드러운 카메라 추적
    /// </summary>
    private void FollowCamera()
    {
        Vector3 targetPosition = transform.position + cameraOffset;

        // Lerp를 사용하여 부드러운 카메라 이동
        targetCamera.transform.position = Vector3.Lerp(
            targetCamera.transform.position,
            targetPosition,
            cameraSmoothSpeed * Time.deltaTime
        );

        if (cameraLookAtPlayer)
        {
            targetCamera.transform.LookAt(transform.position);
        }
    }
}
