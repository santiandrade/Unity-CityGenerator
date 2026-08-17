using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace TestAI
{
    /// <summary>
    /// Coordinates an intersection's cycle: alternates right of way between the
    /// east-west axis and the north-south axis, with amber and an all-red phase in between.
    /// </summary>
    public class TrafficLightIntersection : MonoBehaviour
    {
        [Header("Grupos de semáforos")]
        [SerializeField] private List<TrafficLight> eastWest = new List<TrafficLight>();
        [SerializeField] private List<TrafficLight> northSouth = new List<TrafficLight>();

        [Header("Tiempos (segundos)")]
        [SerializeField] private float greenDuration = 8f;
        [SerializeField] private float amberDuration = 2f;
        [SerializeField] private float allRedDuration = 1f;

        [Header("Desfase inicial")]
        [Tooltip("Retardo antes de arrancar el ciclo. Permite desincronizar intersecciones vecinas.")]
        [SerializeField] private float startOffset;

        [Tooltip("Si está activo, el ciclo empieza dando paso al eje norte-sur.")]
        [SerializeField] private bool startWithNorthSouth;

        private void Start()
        {
            StartCoroutine(RunCycle());
        }

        private IEnumerator RunCycle()
        {
            SetGroup(eastWest, TrafficLightState.Red);
            SetGroup(northSouth, TrafficLightState.Red);

            if (startOffset > 0f)
            {
                yield return new WaitForSeconds(startOffset);
            }

            bool northSouthTurn = startWithNorthSouth;

            while (true)
            {
                List<TrafficLight> active = northSouthTurn ? northSouth : eastWest;
                List<TrafficLight> waiting = northSouthTurn ? eastWest : northSouth;

                SetGroup(waiting, TrafficLightState.Red);
                SetGroup(active, TrafficLightState.Green);
                yield return new WaitForSeconds(greenDuration);

                SetGroup(active, TrafficLightState.Amber);
                yield return new WaitForSeconds(amberDuration);

                SetGroup(active, TrafficLightState.Red);
                yield return new WaitForSeconds(allRedDuration);

                northSouthTurn = !northSouthTurn;
            }
        }

        private static void SetGroup(List<TrafficLight> group, TrafficLightState state)
        {
            for (int i = 0; i < group.Count; i++)
            {
                if (group[i] != null)
                {
                    group[i].SetState(state);
                }
            }
        }
    }
}
