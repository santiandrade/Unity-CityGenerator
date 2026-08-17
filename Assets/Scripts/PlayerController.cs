using UnityEngine;
using UnityEngine.InputSystem;

namespace TestAI
{
    /// <summary>
    /// Third-person player movement: walk, run and jump,
    /// with direction relative to the camera (Mario 64 style).
    /// Requires a CharacterController and an Animator with the
    /// Speed (float), Grounded (bool), Jump (trigger) and VerticalSpeed (float) parameters.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    [RequireComponent(typeof(Animator))]
    public class PlayerController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string jumpActionName = "Jump";
        [SerializeField] private string sprintActionName = "Sprint";

        [Header("Movimiento")]
        [SerializeField] private float walkSpeed = 2f;
        [SerializeField] private float runSpeed = 5f;
        [SerializeField] private float rotationSmoothTime = 0.1f;
        [SerializeField] private float gravity = -20f;
        [SerializeField] private float jumpHeight = 1.2f;

        [Header("Referencias")]
        [SerializeField] private Transform cameraTransform;

        private CharacterController controller;
        private Animator animator;

        private InputActionMap playerMap;
        private InputAction moveAction;
        private InputAction jumpAction;
        private InputAction sprintAction;

        private float verticalVelocity;
        private float rotationVelocity;

        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int GroundedHash = Animator.StringToHash("Grounded");
        private static readonly int JumpHash = Animator.StringToHash("Jump");
        private static readonly int VerticalSpeedHash = Animator.StringToHash("VerticalSpeed");

        private void Awake()
        {
            controller = GetComponent<CharacterController>();
            animator = GetComponent<Animator>();

            if (cameraTransform == null && Camera.main != null)
                cameraTransform = Camera.main.transform;

            if (inputActions != null)
            {
                playerMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: false);
                if (playerMap != null)
                {
                    moveAction = playerMap.FindAction(moveActionName);
                    jumpAction = playerMap.FindAction(jumpActionName);
                    sprintAction = playerMap.FindAction(sprintActionName);
                }
                else
                {
                    Debug.LogWarning($"PlayerController: no se encontro el action map '{actionMapName}' en {inputActions.name}.", this);
                }
            }
            else
            {
                Debug.LogWarning("PlayerController: no hay InputActionAsset asignado.", this);
            }
        }

        private void OnEnable()
        {
            playerMap?.Enable();
            if (jumpAction != null)
                jumpAction.performed += OnJumpPerformed;
        }

        private void OnDisable()
        {
            if (jumpAction != null)
                jumpAction.performed -= OnJumpPerformed;
            playerMap?.Disable();
        }

        private void OnJumpPerformed(InputAction.CallbackContext ctx)
        {
            if (controller.isGrounded)
            {
                verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
                animator.SetTrigger(JumpHash);
            }
        }

        private void Update()
        {
            Vector2 moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            bool sprinting = sprintAction != null && sprintAction.IsPressed();

            Vector3 moveDir = Vector3.zero;
            float inputMagnitude = moveInput.magnitude;

            if (inputMagnitude > 0.01f)
            {
                Vector3 camForward = cameraTransform != null ? cameraTransform.forward : Vector3.forward;
                Vector3 camRight = cameraTransform != null ? cameraTransform.right : Vector3.right;
                camForward.y = 0f;
                camRight.y = 0f;
                camForward.Normalize();
                camRight.Normalize();

                moveDir = (camForward * moveInput.y + camRight * moveInput.x).normalized;

                float targetAngle = Mathf.Atan2(moveDir.x, moveDir.z) * Mathf.Rad2Deg;
                float smoothedAngle = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetAngle, ref rotationVelocity, rotationSmoothTime);
                transform.rotation = Quaternion.Euler(0f, smoothedAngle, 0f);
            }

            float targetSpeed = (sprinting ? runSpeed : walkSpeed) * Mathf.Clamp01(inputMagnitude);

            // Gravity and jump.
            if (controller.isGrounded && verticalVelocity < 0f)
                verticalVelocity = -2f; // small downward force to keep the grounded check stable
            verticalVelocity += gravity * Time.deltaTime;

            Vector3 horizontalMotion = moveDir * targetSpeed;
            Vector3 motion = new Vector3(horizontalMotion.x, verticalVelocity, horizontalMotion.z);
            controller.Move(motion * Time.deltaTime);

            // Animator: normalized Speed 0 (idle) .. 0.5 (walk) .. 1 (run).
            float normalizedSpeed = targetSpeed <= 0f ? 0f : (sprinting ? Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(inputMagnitude)) : Mathf.Lerp(0f, 0.5f, Mathf.Clamp01(inputMagnitude)));
            animator.SetFloat(SpeedHash, normalizedSpeed);
            animator.SetBool(GroundedHash, controller.isGrounded);
            animator.SetFloat(VerticalSpeedHash, verticalVelocity);
        }
    }
}
