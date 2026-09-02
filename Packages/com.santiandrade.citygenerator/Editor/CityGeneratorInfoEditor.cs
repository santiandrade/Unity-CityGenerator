using UnityEditor;
using UnityEngine;
using CityGenerator.Runtime;

namespace CityGenerator.Editor
{
    /// <summary>
    /// CityGeneratorInfo's fields are public so CityGeneratorSceneBuilder/CityGeneratorContentAssembler
    /// can write them at build time and CityGeneratorAPI can read them at runtime, but hand-editing
    /// them in the Inspector has no effect on the generated city — it's a snapshot, not a live
    /// control. This editor draws the default Inspector disabled so that's clear at a glance instead
    /// of inviting the user to edit values that silently do nothing.
    /// </summary>
    [CustomEditor(typeof(CityGeneratorInfo))]
    public sealed class CityGeneratorInfoEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Read-only snapshot from the last Build/Re-Build. Editing these values here has no effect on the generated city — use CityGeneratorAPI to query them at runtime.",
                MessageType.Info);

            using (new EditorGUI.DisabledScope(true))
            {
                DrawDefaultInspector();
            }
        }
    }
}
