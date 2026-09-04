using System.Reflection;
using CityGenerator.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CityGenerator.Tests.PlayMode
{
    /// <summary>
    /// Characterization coverage for FreeCameraController (SPEC 15, closing a P1 gap the technical
    /// review flagged): describes what the component already does, without changing its behaviour.
    /// EnterFreeView/ExitFreeView are invoked directly via reflection instead of driving the Input
    /// System's Toggle action end to end -- what is under test is the state transition and its side
    /// effects on IsActive/the player GameObject/ThirdPersonCamera, not input wiring.
    /// </summary>
    internal class FreeCameraControllerTests
    {
        private static void Invoke(FreeCameraController controller, string method)
        {
            MethodInfo info = typeof(FreeCameraController).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Method '{method}' not found on FreeCameraController");
            info.Invoke(controller, null);
        }

        private static (FreeCameraController free, GameObject player, ThirdPersonCamera thirdPerson) BuildRig()
        {
            var cameraGo = new GameObject("Main Camera", typeof(Camera));
            ThirdPersonCamera thirdPerson = cameraGo.AddComponent<ThirdPersonCamera>();
            FreeCameraController free = cameraGo.AddComponent<FreeCameraController>();

            var playerGo = new GameObject("Player");

            SetField(free, "player", playerGo);
            SetField(free, "thirdPersonCamera", thirdPerson);

            return (free, playerGo, thirdPerson);
        }

        private static void SetField(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(info, $"Field '{field}' not found on {target.GetType()}");
            info.SetValue(target, value);
        }

        [Test]
        public void IsActive_IsFalse_ByDefault()
        {
            (FreeCameraController free, GameObject player, _) = BuildRig();

            Assert.IsFalse(free.IsActive);

            Object.DestroyImmediate(free.gameObject);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void EnterFreeView_ActivatesFreeView_DisablesThirdPersonCamera_AndDeactivatesPlayer()
        {
            (FreeCameraController free, GameObject player, ThirdPersonCamera thirdPerson) = BuildRig();

            Invoke(free, "EnterFreeView");

            Assert.IsTrue(free.IsActive);
            Assert.IsFalse(thirdPerson.enabled, "ThirdPersonCamera must be disabled while Free View is active -- they take turns on the same camera.");
            Assert.IsFalse(player.activeSelf, "The player must be deactivated while Free View is active.");

            Object.DestroyImmediate(free.gameObject);
            Object.DestroyImmediate(player);
        }

        [Test]
        public void ExitFreeView_RestoresThirdPersonCamera_AndReactivatesPlayer()
        {
            (FreeCameraController free, GameObject player, ThirdPersonCamera thirdPerson) = BuildRig();

            Invoke(free, "EnterFreeView");
            Invoke(free, "ExitFreeView");

            Assert.IsFalse(free.IsActive);
            Assert.IsTrue(thirdPerson.enabled);
            Assert.IsTrue(player.activeSelf);

            Object.DestroyImmediate(free.gameObject);
            Object.DestroyImmediate(player);
        }
    }
}
