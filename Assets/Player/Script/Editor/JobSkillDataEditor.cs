using BattlePvp.Combat;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(JobSkillData))]
[CanEditMultipleObjects]
public sealed class JobSkillDataEditor : Editor
{
    private SerializedProperty _skillKind;
    private SerializedProperty _displayName;
    private SerializedProperty _iconSprite;
    private SerializedProperty _castSeconds;
    private SerializedProperty _durationSeconds;
    private SerializedProperty _cooldownSeconds;
    private SerializedProperty _inputLockFlags;
    private SerializedProperty _inputLockSeconds;
    private SerializedProperty _castAnimationStateName;
    private SerializedProperty _castAnimationLayer;
    private SerializedProperty _useSfx;
    private SerializedProperty _sfxVolume;
    private SerializedProperty _lifestealRatio;
    private SerializedProperty _poisonMaxStacks;
    private SerializedProperty _poisonDamagePerStackPerSecond;
    private SerializedProperty _poisonStackDurationSeconds;
    private SerializedProperty _swordMaterial;
    private SerializedProperty _kickDamageMultiplier;
    private SerializedProperty _kickKnockbackDistance;
    private SerializedProperty _kickSlowMoveMultiplier;
    private SerializedProperty _kickSlowDurationSeconds;
    private SerializedProperty _tauntReadyDurationSeconds;
    private SerializedProperty _tauntDurationSeconds;
    private SerializedProperty _tauntIncomingDamageMultiplier;
    private SerializedProperty _tauntReflectMultiplier;
    private SerializedProperty _tauntReflectHealthCapRatio;
    private SerializedProperty _rollDistance;
    private SerializedProperty _rollDurationSeconds;
    private SerializedProperty _targetPreset;
    private SerializedProperty _maxHealthIncreaseShieldRatio;
    private SerializedProperty _shieldDurationSeconds;
    private SerializedProperty _strategistStrNextAttackMultiplier;
    private SerializedProperty _strategistStrAttackBonusDurationSeconds;
    private SerializedProperty _strategistAgiBonusDurationSeconds;
    private SerializedProperty _strategistAgiMoveMultiplier;
    private SerializedProperty _strategistAgiAttackSpeedMultiplier;
    private SerializedProperty _strategistConTargetMaxHpShieldRatio;
    private SerializedProperty _strategistDefInvulnerableSeconds;
    private SerializedProperty _minimumBowChargeSeconds;
    private SerializedProperty _maximumBowDamageChargeSeconds;
    private SerializedProperty _minimumBowDamageMultiplier;
    private SerializedProperty _maximumBowDamageMultiplier;
    private SerializedProperty _bowChargeMoveMultiplier;
    private SerializedProperty _bowRange;
    private SerializedProperty _weaponSwapMoveBonusDurationSeconds;
    private SerializedProperty _weaponSwapMoveMultiplier;
    private SerializedProperty _weaponSwapNextAttackMultiplier;

    private void OnEnable()
    {
        _skillKind = serializedObject.FindProperty("_skillKind");
        _displayName = serializedObject.FindProperty("_displayName");
        _iconSprite = serializedObject.FindProperty("_iconSprite");
        _castSeconds = serializedObject.FindProperty("_castSeconds");
        _durationSeconds = serializedObject.FindProperty("_durationSeconds");
        _cooldownSeconds = serializedObject.FindProperty("_cooldownSeconds");
        _inputLockFlags = serializedObject.FindProperty("_inputLockFlags");
        _inputLockSeconds = serializedObject.FindProperty("_inputLockSeconds");
        _castAnimationStateName = serializedObject.FindProperty("_castAnimationStateName");
        _castAnimationLayer = serializedObject.FindProperty("_castAnimationLayer");
        _useSfx = serializedObject.FindProperty("_useSfx");
        _sfxVolume = serializedObject.FindProperty("_sfxVolume");
        _lifestealRatio = serializedObject.FindProperty("_lifestealRatio");
        _poisonMaxStacks = serializedObject.FindProperty("_poisonMaxStacks");
        _poisonDamagePerStackPerSecond = serializedObject.FindProperty("_poisonDamagePerStackPerSecond");
        _poisonStackDurationSeconds = serializedObject.FindProperty("_poisonStackDurationSeconds");
        _swordMaterial = serializedObject.FindProperty("_swordMaterial");
        _kickDamageMultiplier = serializedObject.FindProperty("_kickDamageMultiplier");
        _kickKnockbackDistance = serializedObject.FindProperty("_kickKnockbackDistance");
        _kickSlowMoveMultiplier = serializedObject.FindProperty("_kickSlowMoveMultiplier");
        _kickSlowDurationSeconds = serializedObject.FindProperty("_kickSlowDurationSeconds");
        _tauntReadyDurationSeconds = serializedObject.FindProperty("_tauntReadyDurationSeconds");
        _tauntDurationSeconds = serializedObject.FindProperty("_tauntDurationSeconds");
        _tauntIncomingDamageMultiplier = serializedObject.FindProperty("_tauntIncomingDamageMultiplier");
        _tauntReflectMultiplier = serializedObject.FindProperty("_tauntReflectMultiplier");
        _tauntReflectHealthCapRatio = serializedObject.FindProperty("_tauntReflectHealthCapRatio");
        _rollDistance = serializedObject.FindProperty("_rollDistance");
        _rollDurationSeconds = serializedObject.FindProperty("_rollDurationSeconds");
        _targetPreset = serializedObject.FindProperty("_targetPreset");
        _maxHealthIncreaseShieldRatio = serializedObject.FindProperty("_maxHealthIncreaseShieldRatio");
        _shieldDurationSeconds = serializedObject.FindProperty("_shieldDurationSeconds");
        _strategistStrNextAttackMultiplier = serializedObject.FindProperty("_strategistStrNextAttackMultiplier");
        _strategistStrAttackBonusDurationSeconds = serializedObject.FindProperty("_strategistStrAttackBonusDurationSeconds");
        _strategistAgiBonusDurationSeconds = serializedObject.FindProperty("_strategistAgiBonusDurationSeconds");
        _strategistAgiMoveMultiplier = serializedObject.FindProperty("_strategistAgiMoveMultiplier");
        _strategistAgiAttackSpeedMultiplier = serializedObject.FindProperty("_strategistAgiAttackSpeedMultiplier");
        _strategistConTargetMaxHpShieldRatio = serializedObject.FindProperty("_strategistConTargetMaxHpShieldRatio");
        _strategistDefInvulnerableSeconds = serializedObject.FindProperty("_strategistDefInvulnerableSeconds");
        _minimumBowChargeSeconds = serializedObject.FindProperty("_minimumBowChargeSeconds");
        _maximumBowDamageChargeSeconds = serializedObject.FindProperty("_maximumBowDamageChargeSeconds");
        _minimumBowDamageMultiplier = serializedObject.FindProperty("_minimumBowDamageMultiplier");
        _maximumBowDamageMultiplier = serializedObject.FindProperty("_maximumBowDamageMultiplier");
        _bowChargeMoveMultiplier = serializedObject.FindProperty("_bowChargeMoveMultiplier");
        _bowRange = serializedObject.FindProperty("_bowRange");
        _weaponSwapMoveBonusDurationSeconds = serializedObject.FindProperty("_weaponSwapMoveBonusDurationSeconds");
        _weaponSwapMoveMultiplier = serializedObject.FindProperty("_weaponSwapMoveMultiplier");
        _weaponSwapNextAttackMultiplier = serializedObject.FindProperty("_weaponSwapNextAttackMultiplier");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.ObjectField("Script", MonoScript.FromScriptableObject((JobSkillData)target), typeof(JobSkillData), false);

        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(_skillKind);

        DrawSection("Common", _displayName, _iconSprite);
        DrawSection("Timing", _castSeconds, _durationSeconds, _cooldownSeconds);
        DrawSection("Input Lock", _inputLockSeconds);
        DrawInputLockButtons();
        DrawSectionWithLabels(
            "Animation",
            (_castAnimationStateName, "Cast Animation State Name"),
            (_castAnimationLayer, "Cast Animation Layer"));
        DrawSection("Audio", _useSfx, _sfxVolume);

        if (_skillKind.hasMultipleDifferentValues)
        {
            EditorGUILayout.HelpBox("Select assets with the same Skill Kind to edit skill-specific values.", MessageType.Info);
        }
        else
        {
            DrawSkillSpecificFields((JobSkillKind)_skillKind.intValue);
        }

        serializedObject.ApplyModifiedProperties();
    }

    private static void DrawSection(string title, params SerializedProperty[] properties)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        foreach (SerializedProperty property in properties)
            EditorGUILayout.PropertyField(property);
    }

    private static void DrawSectionWithLabels(string title, params (SerializedProperty Property, string Label)[] fields)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

        foreach ((SerializedProperty property, string label) in fields)
            DrawWideProperty(property, label);
    }

    private static void DrawWideProperty(SerializedProperty property, string label)
    {
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        EditorGUIUtility.labelWidth = Mathf.Min(240f, EditorGUIUtility.currentViewWidth * 0.58f);
        EditorGUILayout.PropertyField(property, new GUIContent(label));
        EditorGUIUtility.labelWidth = previousLabelWidth;
    }

    private void DrawSkillSpecificFields(JobSkillKind skillKind)
    {
        switch (skillKind)
        {
            case JobSkillKind.MonostatStrLifesteal:
                DrawSectionWithLabels(
                    "STR Lifesteal",
                    (_lifestealRatio, "Lifesteal Ratio"),
                    (_swordMaterial, "Sword Material"));
                break;

            case JobSkillKind.MonostatAgiPoison:
                DrawSectionWithLabels(
                    "AGI Poison",
                    (_poisonMaxStacks, "Max Stacks"),
                    (_poisonDamagePerStackPerSecond, "Damage Per Stack Per Second"),
                    (_poisonStackDurationSeconds, "Stack Duration Seconds"),
                    (_swordMaterial, "Sword Material"));
                break;

            case JobSkillKind.MonostatConKick:
                DrawSectionWithLabels(
                    "CON Kick",
                    (_kickDamageMultiplier, "Damage Multiplier"),
                    (_kickKnockbackDistance, "Knockback Distance"),
                    (_kickSlowMoveMultiplier, "Slow Move Multiplier"),
                    (_kickSlowDurationSeconds, "Slow Duration Seconds"));
                break;

            case JobSkillKind.MonostatDefTaunt:
                DrawSectionWithLabels(
                    "DEF Taunt",
                    (_tauntReadyDurationSeconds, "Ready Window Seconds"),
                    (_tauntDurationSeconds, "Taunt Duration Seconds"),
                    (_tauntIncomingDamageMultiplier, "Incoming Damage Multiplier"),
                    (_tauntReflectMultiplier, "Reflect Damage Multiplier"),
                    (_tauntReflectHealthCapRatio, "Reflect Max HP Cap Ratio"),
                    (_swordMaterial, "Sword Material"));
                break;

            case JobSkillKind.StrategistRoll:
            case JobSkillKind.PolymathRoll:
                DrawSectionWithLabels(
                    "Roll",
                    (_rollDistance, "Distance"),
                    (_rollDurationSeconds, "Duration Seconds"));
                break;

            case JobSkillKind.StrategistPresetChange:
                DrawSectionWithLabels(
                    "Preset Change",
                    (_targetPreset, "Target Preset"),
                    (_maxHealthIncreaseShieldRatio, "Max HP Increase Shield Ratio"),
                    (_shieldDurationSeconds, "Shield Duration Seconds"),
                    (_strategistStrNextAttackMultiplier, "STR Attack Multiplier"),
                    (_strategistStrAttackBonusDurationSeconds, "STR Attack Bonus Duration Seconds"),
                    (_strategistAgiBonusDurationSeconds, "AGI Bonus Duration Seconds"),
                    (_strategistAgiMoveMultiplier, "AGI Move Multiplier"),
                    (_strategistAgiAttackSpeedMultiplier, "AGI Attack Speed Multiplier"),
                    (_strategistConTargetMaxHpShieldRatio, "CON Target Max HP Shield Ratio"),
                    (_strategistDefInvulnerableSeconds, "DEF Invulnerable Seconds"));
                break;

            case JobSkillKind.PolymathPresetChange:
                DrawSectionWithLabels(
                    "Preset Change",
                    (_targetPreset, "Target Preset"),
                    (_maxHealthIncreaseShieldRatio, "Max HP Increase Shield Ratio"),
                    (_shieldDurationSeconds, "Shield Duration Seconds"));
                break;

            case JobSkillKind.PolymathWeaponSwap:
                DrawSectionWithLabels(
                    "Weapon Swap / Bow",
                    (_minimumBowChargeSeconds, "Minimum Charge Seconds"),
                    (_maximumBowDamageChargeSeconds, "Maximum Damage Charge Seconds"),
                    (_minimumBowDamageMultiplier, "Minimum Damage Multiplier"),
                    (_maximumBowDamageMultiplier, "Maximum Damage Multiplier"),
                    (_bowChargeMoveMultiplier, "Charge Move Multiplier"),
                    (_bowRange, "Range"),
                    (_weaponSwapMoveBonusDurationSeconds, "Swap Move Bonus Duration Seconds"),
                    (_weaponSwapMoveMultiplier, "Swap Move Multiplier"),
                    (_weaponSwapNextAttackMultiplier, "Swap Next Attack Multiplier"));
                break;

            default:
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox("This skill kind does not have additional values yet.", MessageType.Info);
                break;
        }
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
