    using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// Mario-64-style third-person orbit camera: orbits around a
    /// pivot point above the target, with smoothing and collision against the environment.
    /// Only reads the Look action — never calls Enable()/Disable() on the action map itself,
    /// since <see cref="PlayerInputAuthority"/> is the map's single owner.
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private float verticalOffset = 1f;
        [SerializeField] private float horizontalOffset = 0f;

        [Header("Input")]
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Player";
        [SerializeField] private string lookActionName = "Look";

        [Header("Orbit")]
        [SerializeField] private float distance = 5f;
        [SerializeField] private float minDistance = 1f;
        [SerializeField] private float sensitivity = 0.12f;
        [SerializeField] private float minPitch = -20f;
        [SerializeField] private float maxPitch = 60f;
        [Tooltip("Smooths only the tracking of the player's position (e.g. while walking), never the camera's rotation.")]
        [SerializeField] private float followSmoothTime = 0.08f;

        [Header("Collision")]
        [SerializeField] private LayerMask collisionMask = ~0;
        [SerializeField] private float collisionRadius = 0.3f;

        [Header("Cursor")]
        [SerializeField] private bool lockCursor = true;

        private InputActionMap playerMap;
        private InputAction lookAction;

        private float yaw;
        private float pitch = 15f;
        private Vector3 smoothedTargetPos;
        private Vector3 followVelocity;

        // Collision hits are filtered by identity (anything under the target's
        // hierarchy is skipped), never by distance: while jumping backwards the
        // player's own collider sits between the pivot and the camera, and letting
        // it through snapped the camera onto the character's face.
        private readonly RaycastHit[] collisionHits = new RaycastHit[16];
        private Transform targetRoot;

        private void Awake()
        {
            if (inputActions != null)
            {
                playerMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: false);
                if (playerMap != null)
                    lookAction = playerMap.FindAction(lookActionName);
                else
                    Debug.LogWarning($"ThirdPersonCamera: action map '{actionMapName}' not found in {inputActions.name}.", this);
            }
            else
            {
                Debug.LogWarning("ThirdPersonCamera: no InputActionAsset assigned.", this);
            }

            if (target != null)
            {
                yaw = target.eulerAngles.y;
                smoothedTargetPos = target.position;
                targetRoot = target.root;
            }
        }

        // Does not Enable()/Disable() playerMap itself: PlayerInputAuthority is the single owner
        // of that action map's lifecycle, so PlayerController keeps receiving Move/Sprint/Jump
        // even if this component alone is disabled, and vice versa.
        private void OnEnable()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // Previously absent: leaving Play (or disabling this component by hand) left the cursor
        // locked/hidden, stranding it over whatever UI or other window regained focus next.
        private void OnDisable()
        {
            if (lockCursor)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            // Rotation (yaw/pitch) responds instantly to mouse input: smoothing it is
            // what caused the mismatch between "where it's looking" and
            // "where the camera is", which was nauseating.
            Vector2 lookInput = lookAction != null ? lookAction.ReadValue<Vector2>() : Vector2.zero;

            yaw += lookInput.x * sensitivity;
            pitch -= lookInput.y * sensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);

            // Only the player's position follow is smoothed (e.g. while walking),
            // never the rotation: this way the camera always orbits around the player and looks
            // at it, without the "look first, then reframe" of the previous approach.
            smoothedTargetPos = Vector3.SmoothDamp(smoothedTargetPos, target.position, ref followVelocity, followSmoothTime);

            Vector3 pivot = smoothedTargetPos + Vector3.up * verticalOffset + orbitRotation * Vector3.right * horizontalOffset;
            Vector3 desiredPosition = pivot - orbitRotation * Vector3.forward * distance;

            float finalDistance = distance;
            Vector3 castDirection = (desiredPosition - pivot).normalized;
            int hitCount = Physics.SphereCastNonAlloc(pivot, collisionRadius, castDirection, collisionHits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Transform hitTransform = collisionHits[i].transform;
                if (targetRoot != null && hitTransform.IsChildOf(targetRoot))
                    continue;

                finalDistance = Mathf.Min(finalDistance, collisionHits[i].distance);
            }
            finalDistance = Mathf.Clamp(finalDistance, minDistance, distance);

            transform.position = pivot - orbitRotation * Vector3.forward * finalDistance;
            transform.rotation = Quaternion.LookRotation((pivot - transform.position).normalized, Vector3.up);
        }
    }
}
