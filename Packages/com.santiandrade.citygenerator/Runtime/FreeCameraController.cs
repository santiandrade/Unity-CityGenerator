using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// First-person free-flying camera ("Free View"), toggled with the Toggle action shared
    /// between the Player and Free View action maps. The single authority allowed to call
    /// Enable()/Disable() on the Free View action map — mirrors the pattern
    /// <see cref="PlayerInputAuthority"/> already applies to the Player map, never touching it.
    /// Reads the Player map's Toggle action read-only while the Player is active, and its own
    /// map's Toggle read-only while Free View is active, purely to know when to switch.
    /// Lives on the same Main Camera GameObject as <see cref="ThirdPersonCamera"/>, taking over
    /// that same Camera while active instead of spawning a second one.
    /// </summary>
    public sealed class FreeCameraController : MonoBehaviour
    {
        // Fixed in code, same values ThirdPersonCamera already uses, to keep the Free Camera
        // card down to its minimal field set (moveSpeed/sprintMultiplier/rotationSmoothTime).
        private const float CollisionRadius = 0.3f;
        private const int CollisionMask = ~0;
        private const float MoveSmoothTime = 0.15f;
        private const float LookSensitivity = 0.12f;
        private const float MinPitch = -89f;
        private const float MaxPitch = 89f;

        [Header("Cross-Read (Player)")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string playerActionMapName = "Player";
        [SerializeField] private string playerToggleActionName = "Toggle";

        [Header("Free View Input Actions")]
        [SerializeField] private string actionMapName = "Free View";
        [SerializeField] private string moveActionName = "Move";
        [SerializeField] private string verticalActionName = "Vertical";
        [SerializeField] private string sprintActionName = "Sprint";
        [SerializeField] private string lookActionName = "Look";
        [SerializeField] private string toggleActionName = "Toggle";

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 8f;
        [SerializeField] private float sprintMultiplier = 2.5f;

        [Header("Rotation")]
        [SerializeField] private float rotationSmoothTime = 0.08f;

        [Header("References")]
        [SerializeField] private GameObject player;
        [SerializeField] private ThirdPersonCamera thirdPersonCamera;

        private InputActionMap playerMap;
        private InputAction playerToggleAction;

        private InputActionMap freeViewMap;
        private InputAction moveAction;
        private InputAction verticalAction;
        private InputAction sprintAction;
        private InputAction lookAction;
        private InputAction toggleAction;

        private bool active;

        /// <summary>Whether Free View is currently active (read-only; toggled only by the Toggle input action).</summary>
        public bool IsActive => active;

        private float targetYaw;
        private float targetPitch;
        private float currentYaw;
        private float currentPitch;
        private float yawVelocity;
        private float pitchVelocity;

        private Vector3 currentVelocity;
        private Vector3 velocityDamp;

        private void Awake()
        {
            if (inputActions == null)
            {
                Debug.LogWarning("FreeCameraController: no InputActionAsset assigned.", this);
                return;
            }

            playerMap = inputActions.FindActionMap(playerActionMapName, throwIfNotFound: false);
            if (playerMap != null)
                playerToggleAction = playerMap.FindAction(playerToggleActionName);
            else
                Debug.LogWarning($"FreeCameraController: action map '{playerActionMapName}' not found in {inputActions.name}.", this);

            freeViewMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: false);
            if (freeViewMap != null)
            {
                moveAction = freeViewMap.FindAction(moveActionName);
                verticalAction = freeViewMap.FindAction(verticalActionName);
                sprintAction = freeViewMap.FindAction(sprintActionName);
                lookAction = freeViewMap.FindAction(lookActionName);
                toggleAction = freeViewMap.FindAction(toggleActionName);
            }
            else
            {
                Debug.LogWarning($"FreeCameraController: action map '{actionMapName}' not found in {inputActions.name}.", this);
            }
        }

        // Never Enable()s freeViewMap on its own: it starts disabled and only turns on in
        // response to the first Toggle press, read from the Player map below.
        private void OnDisable()
        {
            freeViewMap?.Disable();
        }

        private void Update()
        {
            if (!active)
            {
                if (playerToggleAction != null && playerToggleAction.WasPressedThisFrame())
                    EnterFreeView();
                return;
            }

            if (toggleAction != null && toggleAction.WasPressedThisFrame())
            {
                ExitFreeView();
                return;
            }

            UpdateRotation();
            UpdateMovement();
        }

        private void EnterFreeView()
        {
            active = true;

            // Read the Main Camera's current orientation (wherever ThirdPersonCamera left it) so
            // Free View picks up from there instead of snapping to a different angle.
            Vector3 euler = transform.eulerAngles;
            currentPitch = targetPitch = NormalizePitch(euler.x);
            currentYaw = targetYaw = euler.y;
            yawVelocity = 0f;
            pitchVelocity = 0f;
            currentVelocity = Vector3.zero;
            velocityDamp = Vector3.zero;

            freeViewMap?.Enable();

            if (player != null)
                player.SetActive(false); // also disables PlayerInputAuthority, and with it the Player map's Toggle.
            if (thirdPersonCamera != null)
                thirdPersonCamera.enabled = false;

            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void ExitFreeView()
        {
            active = false;

            freeViewMap?.Disable();

            if (player != null)
                player.SetActive(true);
            if (thirdPersonCamera != null)
                thirdPersonCamera.enabled = true; // re-locks the cursor itself via its own OnEnable.
        }

        // Yaw/pitch smoothed with SmoothDampAngle: unlike ThirdPersonCamera, Free Camera smooths
        // the rotation itself (looking back takes a moment to reach the target angle), not just
        // the position tracking.
        private void UpdateRotation()
        {
            Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

            targetYaw += lookInput.x * LookSensitivity;
            targetPitch -= lookInput.y * LookSensitivity;
            targetPitch = Mathf.Clamp(targetPitch, MinPitch, MaxPitch);

            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, rotationSmoothTime);
            currentPitch = Mathf.SmoothDampAngle(currentPitch, targetPitch, ref pitchVelocity, rotationSmoothTime);

            transform.rotation = Quaternion.Euler(currentPitch, currentYaw, 0f);
        }

        // WASD moves along the camera's full current facing (including pitch) + Q/E moves along
        // world up/down, same as Unity's own Scene view fly camera — the explicit reference this
        // feature replicates, rather than flattening WASD to the horizontal plane.
        private void UpdateMovement()
        {
            Vector2 moveInput = moveAction != null ? moveAction.ReadValue<Vector2>() : Vector2.zero;
            float verticalInput = verticalAction != null ? verticalAction.ReadValue<float>() : 0f;
            bool sprinting = sprintAction != null && sprintAction.IsPressed();

            Vector3 wishDir = transform.forward * moveInput.y + transform.right * moveInput.x + Vector3.up * verticalInput;
            if (wishDir.sqrMagnitude > 1f)
                wishDir.Normalize();

            float speed = moveSpeed * (sprinting ? sprintMultiplier : 1f);
            Vector3 targetVelocity = wishDir * speed;
            currentVelocity = Vector3.SmoothDamp(currentVelocity, targetVelocity, ref velocityDamp, MoveSmoothTime);

            transform.position = MoveWithCollision(transform.position, currentVelocity * Time.deltaTime);
        }

        // Slides along whichever axes aren't blocked, by resolving the full displacement first
        // and, only if that overlaps a collider, retrying each axis independently.
        private Vector3 MoveWithCollision(Vector3 position, Vector3 delta)
        {
            if (delta == Vector3.zero)
                return position;

            Vector3 full = position + delta;
            if (!Physics.CheckSphere(full, CollisionRadius, CollisionMask, QueryTriggerInteraction.Ignore))
                return full;

            Vector3 result = position;

            Vector3 xDelta = new Vector3(delta.x, 0f, 0f);
            if (xDelta != Vector3.zero && !Physics.CheckSphere(result + xDelta, CollisionRadius, CollisionMask, QueryTriggerInteraction.Ignore))
                result += xDelta;

            Vector3 yDelta = new Vector3(0f, delta.y, 0f);
            if (yDelta != Vector3.zero && !Physics.CheckSphere(result + yDelta, CollisionRadius, CollisionMask, QueryTriggerInteraction.Ignore))
                result += yDelta;

            Vector3 zDelta = new Vector3(0f, 0f, delta.z);
            if (zDelta != Vector3.zero && !Physics.CheckSphere(result + zDelta, CollisionRadius, CollisionMask, QueryTriggerInteraction.Ignore))
                result += zDelta;

            return result;
        }

        // Euler angles report pitch as 0..360 (e.g. 350 instead of -10); fold it back to -180..180
        // so it lands inside the -89..89 clamp instead of getting stuck at the wrap-around.
        private static float NormalizePitch(float pitch)
        {
            return pitch > 180f ? pitch - 360f : pitch;
        }
    }
}
