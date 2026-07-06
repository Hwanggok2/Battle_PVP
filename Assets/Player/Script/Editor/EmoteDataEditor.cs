using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EmoteData))]
[CanEditMultipleObjects]
public sealed class EmoteDataEditor : Editor
{
    private SerializedProperty _displayName;
    private SerializedProperty _animationClip;
    private SerializedProperty _animationStateName;
    private SerializedProperty _animationLayer;
    private SerializedProperty _fallbackStateName;
    private SerializedProperty _lockSeconds;
    private SerializedProperty _lockMovement;
    private SerializedProperty _lockAttack;
    private SerializedProperty _lockJump;
    private SerializedProperty _useSfx;
    private SerializedProperty _sfxVolume;

    private void OnEnable()
    {
        _displayName = serializedObject.FindProperty("_displayName");
        _animationClip = serializedObject.FindProperty("_animationClip");
        _animationStateName = serializedObject.FindProperty("_animationStateName");
        _animationLayer = serializedObject.FindProperty("_animationLayer");
        _fallbackStateName = serializedObject.FindProperty("_fallbackStateName");
        _lockSeconds = serializedObject.FindProperty("_lockSeconds");
        _lockMovement = serializedObject.FindProperty("_lockMovement");
        _lockAttack = serializedObject.FindProperty("_lockAttack");
        _lockJump = serializedObject.FindProperty("_lockJump");
        _useSfx = serializedObject.FindProperty("_useSfx");
        _sfxVolume = serializedObject.FindProperty("_sfxVolume");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Script", MonoScript.FromScriptableObject((EmoteData)target), typeof(EmoteData), false);

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(_displayName);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Animation", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_animationClip);
        EditorGUILayout.PropertyField(_animationStateName, new GUIContent("State Name"));
        EditorGUILayout.PropertyField(_animationLayer, new GUIContent("Layer"));
        EditorGUILayout.PropertyField(_fallbackStateName, new GUIContent("Fallback State"));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Lock", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_lockSeconds, new GUIContent("Lock Seconds"));
        EditorGUILayout.PropertyField(_lockMovement);
        EditorGUILayout.PropertyField(_lockAttack);
        EditorGUILayout.PropertyField(_lockJump);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_useSfx);
        EditorGUILayout.PropertyField(_sfxVolume);

        serializedObject.ApplyModifiedProperties();
    }
}
