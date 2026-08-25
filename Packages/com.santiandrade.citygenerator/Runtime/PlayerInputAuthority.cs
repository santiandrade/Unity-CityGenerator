using UnityEngine;
using UnityEngine.InputSystem;

namespace CityGenerator.Runtime
{
    /// <summary>
    /// The single component allowed to call Enable()/Disable() on the Player action map.
    /// PlayerController and ThirdPersonCamera only ever read actions already enabled by this
    /// component, instead of each independently enabling/disabling the shared map — previously,
    /// disabling either one of them (e.g. the player controller alone, to freeze movement while
    /// keeping the camera orbiting) also cut input to the other, since whichever component last
    /// ran its own OnEnable/OnDisable effectively owned the map's on/off state.
    /// </summary>
    public sealed class PlayerInputAuthority : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string actionMapName = "Player";

        private InputActionMap playerMap;

        private void OnEnable()
        {
            if (inputActions == null)
            {
                Debug.LogWarning("PlayerInputAuthority: no InputActionAsset assigned.", this);
                return;
            }

            playerMap = inputActions.FindActionMap(actionMapName, throwIfNotFound: false);
            if (playerMap == null)
            {
                Debug.LogWarning($"PlayerInputAuthority: action map '{actionMapName}' not found in {inputActions.name}.", this);
                return;
            }

            playerMap.Enable();
        }

        private void OnDisable()
        {
            playerMap?.Disable();
        }
    }
}
