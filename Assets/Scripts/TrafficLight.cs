using UnityEngine;

namespace TestAI
{
    /// <summary>
    /// States of a three-light traffic light.
    /// </summary>
    public enum TrafficLightState
    {
        Red,
        Amber,
        Green
    }

    /// <summary>
    /// A single traffic light: swaps its three lamp materials
    /// to reflect the current state. The cycle is driven by <see cref="TrafficLightIntersection"/>.
    /// </summary>
    public class TrafficLight : MonoBehaviour
    {
        [Header("Lámparas")]
        [SerializeField] private MeshRenderer redLamp;
        [SerializeField] private MeshRenderer amberLamp;
        [SerializeField] private MeshRenderer greenLamp;

        [Header("Materiales")]
        [SerializeField] private Material redOnMaterial;
        [SerializeField] private Material amberOnMaterial;
        [SerializeField] private Material greenOnMaterial;
        [SerializeField] private Material offMaterial;

        [Header("Estado")]
        [SerializeField] private TrafficLightState state = TrafficLightState.Red;

        public TrafficLightState State => state;

        private void Start()
        {
            Apply();
        }

        public void SetState(TrafficLightState newState)
        {
            state = newState;
            Apply();
        }

        private void Apply()
        {
            Set(redLamp, state == TrafficLightState.Red, redOnMaterial);
            Set(amberLamp, state == TrafficLightState.Amber, amberOnMaterial);
            Set(greenLamp, state == TrafficLightState.Green, greenOnMaterial);
        }

        private void Set(MeshRenderer lamp, bool on, Material onMaterial)
        {
            if (lamp == null)
            {
                return;
            }

            lamp.sharedMaterial = on ? onMaterial : offMaterial;
        }
    }
}
