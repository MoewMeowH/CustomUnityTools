using EString.Script;
using UnityEngine;
using UnityEngine.UI;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

[E2String(options: E2StringOptions.IntConversion)]
public enum TestEnum
{
    Value1,
    Value2,
    Value3
}

/// <summary>
/// 유저의 WASD 입력을 받아 오브젝트를 이동시키며,
/// Vector3.Lerp를 활용해 카메라가 플레이어를 부드럽게 추적하도록 제어하는 컴포넌트입니다.
/// 최신 Unity Input System(6/7) 및 레거시 Input Manager 환경 모두를 지원합니다.
/// </summary>
public class NewMonoBehaviourScript : MonoBehaviour
{
    // [NeedSlotNullCheck] 테스트: 인스펙터 슬롯 미할당 시 검증기 및 Test Runner에서 감지
    [NeedSlotNullCheck] 
    [SerializeField] private Image m_Image;
    
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

    private void Awake()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
        }
    }

    private void Start()
    {
        HLog.Common.Log("[Player] Player object initialized in scene.");
        HLog.Common.Warning("[Player] In-Game Console UI active! Click top-right 'Logs' button to view.");

        TestEnum t = TestEnum.Value1;
        HLog.Common.Log($"[E2String Test] TestEnum Fast String: {t.ToStringFast()}");

    }

    private void Update()
    {
        ProcessInput();
        MovePlayer();
        
        // 예시 1: 비정상 속도 감지 시 즉시 에디터 Pause
        HLog.Common.BreakIf(moveSpeed > 50f, $"Abnormal move speed detected: {moveSpeed}");

        // 예시 2: 조건 만족 시 현재 Watch 변수 전체 + 링크한 추가 변수들을 텍스트 파일(Report)로 프로젝트 경로에 덤프하고 에디터 Pause
        HLog.Common.ReportIf(moveSpeed > 30f, "High Speed Anomaly",
            ("CurrentInput", currentInputVector),
            ("RotationSpeed", rotationSpeed),
            ("Is3DMovement", is3DMovement)
        );
    }

    private void LateUpdate()
    {
        if (enableCameraFollow && targetCamera != null)
        {
            FollowCamera();
        }
    }

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
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) input.x -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) input.x += 1f;
#else
        float h = Input.GetAxisRaw("Horizontal");
        float v = Input.GetAxisRaw("Vertical");
        input = new Vector2(h, v);
#endif

        currentInputVector = input.sqrMagnitude > 1f ? input.normalized : input;
    }

    private void MovePlayer()
    {
        if (currentInputVector.sqrMagnitude <= 0.0001f) return;

        Vector3 moveDirection;
        if (is3DMovement)
        {
            moveDirection = new Vector3(currentInputVector.x, 0f, currentInputVector.y);
        }
        else
        {
            moveDirection = new Vector3(currentInputVector.x, currentInputVector.y, 0f);
        }

        transform.position += moveDirection * (moveSpeed * Time.deltaTime);

        if (is3DMovement && rotateTowardsMovement && moveDirection != Vector3.zero)
        {
            Quaternion targetRot = Quaternion.LookRotation(moveDirection, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationSpeed * Time.deltaTime);
        }
    }

    private void FollowCamera()
    {
        Vector3 targetCamPos = transform.position + cameraOffset;
        targetCamera.transform.position = Vector3.Lerp(targetCamera.transform.position, targetCamPos, cameraSmoothSpeed * Time.deltaTime);

        if (cameraLookAtPlayer)
        {
            targetCamera.transform.LookAt(transform.position);
        }
    }
}
