using UnityEditor;
using UnityEngine;

namespace PartyGame.EditorTools
{
    /// <summary>
    /// Shows the identity fields always, but only the input fields that belong to the
    /// player's <see cref="PlayerInputData.CurrentTemplate"/>. Repaints continuously so
    /// live input values are visible while in Play Mode.
    /// </summary>
    [CustomEditor(typeof(PlayerInputData))]
    public class PlayerInputDataEditor : Editor
    {
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Identity", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("ClientId"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Nickname"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("JoinOrder"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("Score"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("CurrentTemplate"));

            EditorGUILayout.Space();

            var data = (PlayerInputData)target;
            switch (data.CurrentTemplate)
            {
                case MobileTemplate.JoystickAB:
                    EditorGUILayout.LabelField("JoystickAB Input", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Joystick"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("JoystickA"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("JoystickB"));
                    break;
                case MobileTemplate.DPadFour:
                    EditorGUILayout.LabelField("DPadFour Input", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DpadUp"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DpadDown"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DpadLeft"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("DpadRight"));
                    break;
                case MobileTemplate.SingleButton:
                    EditorGUILayout.LabelField("SingleButton Input", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("SingleA"));
                    break;
                case MobileTemplate.FourChoice:
                    EditorGUILayout.LabelField("FourChoice Input", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice1"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice2"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice3"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice4"));
                    break;
                case MobileTemplate.ThreeChoice:
                    EditorGUILayout.LabelField("ThreeChoice Input (1=Scissors, 2=Rock, 3=Paper)", EditorStyles.boldLabel);
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice1"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice2"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("Choice3"));
                    break;
                default:
                    EditorGUILayout.HelpBox("No template active (None). Waiting for host to assign a controller.", MessageType.Info);
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
