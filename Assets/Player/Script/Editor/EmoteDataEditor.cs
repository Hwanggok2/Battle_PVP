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
    private SerializedProperty _inputLockFlags;
    private SerializedProperty _lockSeconds;
    private SerializedProperty _useSfx;
    private SerializedProperty _sfxVolume;

    private void OnEnable()
    {
        _displayName = serializedObject.FindProperty("_displayName");
        _animationClip = serializedObject.FindProperty("_animationClip");
        _animationStateName = serializedObject.FindProperty("_animationStateName");
        _animationLayer = serializedObject.FindProperty("_animationLayer");
        _fallbackStateName = serializedObject.FindProperty("_fallbackStateName");
        _inputLockFlags = serializedObject.FindProperty("_inputLockFlags");
        _lockSeconds = serializedObject.FindProperty("_lockSeconds");
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
        DrawInputLockButtons();

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Audio", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(_useSfx);
        EditorGUILayout.PropertyField(_sfxVolume);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawInputLockButtons()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Input Lock Flags", EditorStyles.boldLabel);

        if (_inputLockFlags == null)
            return;

        SkillInputLockFlags flags = (SkillInputLockFlags)_inputLockFlags.intValue;
        SkillInputLockFlags next = DrawFlagCheckboxes(flags);
        if (next != flags)
            _inputLockFlags.intValue = (int)next;
    }

    private static SkillInputLockFlags DrawFlagCheckboxes(SkillInputLockFlags flags)
    {
        flags = DrawFlagCheckbox(flags, SkillInputLockFlags.Move, "Move");
        flags = DrawFlagCheckbox(flags, SkillInputLockFlags.Attack, "Attack");
        flags = DrawFlagCheckbox(flags, SkillInputLockFlags.Jump, "Jump");
        flags = DrawFlagCheckbox(flags, SkillInputLockFlags.Crouch, "Crouch");
        return flags;
    }

    private static SkillInputLockFlags DrawFlagCheckbox(SkillInputLockFlags flags, SkillInputLockFlags flag, string label)
    {
        bool enabled = (flags & flag) != 0;
        bool nextEnabled = EditorGUILayout.ToggleLeft(label, enabled);
        if (nextEnabled == enabled)
            return flags;

        return nextEnabled ? flags | flag : flags & ~flag;
    }
}
