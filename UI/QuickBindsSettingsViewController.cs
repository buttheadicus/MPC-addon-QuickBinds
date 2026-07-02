using System;

using System.Collections.Generic;

using BeatSaberMarkupLanguage.Attributes;

using BeatSaberMarkupLanguage.Components.Settings;

using BeatSaberMarkupLanguage.ViewControllers;

using MultiplayerChat.Core.QuickBinds;

using MultiplayerChat.Settings;

using TMPro;

using UnityEngine;

using UnityEngine.UI;



namespace MultiplayerChat.UI;



[ViewDefinition("MultiplayerChat.UI.QuickBindsSettingsView.bsml")]

public sealed class QuickBindsSettingsViewController : BSMLAutomaticViewController

{

    private const float RecordIdleSaveSeconds = 3f;

    private const float RecordInputArmDelaySeconds = 0.15f;



    private enum RecordTarget

    {

        None,

        QuickDisconnect,

        QuickReadyUp

    }



    public event Action? QuickBindsSettingsApplied;

    public event Action? OptionsSettingsClicked;

    private const string LabelEnableQuickBinds = "Enable Quick Binds";

    [UIComponent("QuickBindsEnableToggle")] private ToggleSetting? _quickBindsEnableToggle;

    [UIComponent("ApplyButton")] private Button? _applyButton;

    [UIComponent("ControlsSection")] private RectTransform? _controlsSection;

    [UIComponent("RecordButton")] private Button? _recordButton;

    [UIComponent("RecordingStatusText")] private TextMeshProUGUI? _recordingStatusText;



    [UIComponent("ComboPreviewText")] private TextMeshProUGUI? _comboPreviewText;



    [UIComponent("DeleteBindingButton")] private Button? _deleteBindingButton;

    private bool _draftQuickBindsEnabled;

    private RecordTarget _recordTarget = RecordTarget.None;

    private bool _recordingActive;

    private bool _recordingInputArmed;

    private float _recordingInputArmDeadline;

    private readonly List<QuickBindButton> _recordingCombo = new(16);

    private float _recordingIdleDeadline;

    private CanvasGroup? _controlsSectionCanvasGroup;

    private const float DisabledSectionAlpha = 0.45f;

    [UIValue("QuickBindsEnabled")]
    public bool QuickBindsEnabled
    {
        get => _draftQuickBindsEnabled;
        set
        {
            if (_draftQuickBindsEnabled == value)
                return;
            _draftQuickBindsEnabled = value;
            RefreshControlsSectionInteractable();
        }
    }

    [UIAction("#post-parse")]

    private void PostParse()

    {
        ReloadQuickBindsDraft();
        EnsureControlsSectionCanvasGroup();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        _quickBindsEnableToggle?.ReceiveValue();
        ApplyQuickBindsToggleLabel();
        HookEnableToggleListener();
        StabilizeQuickBindsLayout();
        RefreshControlsSectionInteractable();
        RefreshComboPreview();
        RefreshStatusText();
        RefreshDeleteBindingButton();
    }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        ReloadQuickBindsDraft();
        EnsureControlsSectionCanvasGroup();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        _quickBindsEnableToggle?.ReceiveValue();
        ApplyQuickBindsToggleLabel();
        HookEnableToggleListener();
        StabilizeQuickBindsLayout();
        RefreshControlsSectionInteractable();
        RefreshComboPreview();
        RefreshStatusText();
        RefreshDeleteBindingButton();
    }

    private void ReloadQuickBindsDraft() =>
        _draftQuickBindsEnabled = ModSettings.EnableQuickBinds;

    private void ApplyQuickBindsToggleLabel()
    {
        if (_quickBindsEnableToggle != null)
            _quickBindsEnableToggle.Text = LabelEnableQuickBinds;
    }

    private void EnsureControlsSectionCanvasGroup()
    {
        if (_controlsSection == null || _controlsSectionCanvasGroup != null)
            return;

        _controlsSectionCanvasGroup = _controlsSection.gameObject.GetComponent<CanvasGroup>();
        if (_controlsSectionCanvasGroup == null)
            _controlsSectionCanvasGroup = _controlsSection.gameObject.AddComponent<CanvasGroup>();
    }

    private void HookEnableToggleListener()
    {
        if (_quickBindsEnableToggle?.Toggle == null)
            return;

        _quickBindsEnableToggle.Toggle.onValueChanged.RemoveListener(OnEnableToggleUnityChanged);
        _quickBindsEnableToggle.Toggle.onValueChanged.AddListener(OnEnableToggleUnityChanged);
    }

    private void OnEnableToggleUnityChanged(bool isOn) => QuickBindsEnabled = isOn;

    private void RefreshControlsSectionInteractable()
    {
        var enabled = _draftQuickBindsEnabled;
        if (_controlsSectionCanvasGroup != null)
        {
            _controlsSectionCanvasGroup.alpha = enabled ? 1f : DisabledSectionAlpha;
            _controlsSectionCanvasGroup.interactable = enabled;
            _controlsSectionCanvasGroup.blocksRaycasts = enabled;
        }

        SetControlInteractable(_controlsSection?.gameObject, enabled);
        if (_applyButton != null)
            _applyButton.interactable = true;

        if (!enabled && _recordingActive)
            StopRecording(resetCombo: false);
    }

    private static void SetControlInteractable(GameObject? root, bool interactable)
    {
        if (root == null)
            return;

        foreach (var selectable in root.GetComponentsInChildren<Selectable>(true))
            selectable.interactable = interactable;
    }

    private const float QuickBindsHelpTextWidthPx = 400f;

    private const float QuickBindsStatusTextWidthPx = 360f;

    private void StabilizeQuickBindsLayout()
    {
        BsmlLayoutGroups.ConfigureVertical(BsmlUiRefs.FindChildGameObject(transform, "QuickBindsRoot"), 4f);
        BsmlLayoutGroups.ConfigureVertical(BsmlUiRefs.FindChildGameObject(transform, "EnableToggleGroup"), 2f);
        BsmlLayoutGroups.ConfigureHorizontal(BsmlUiRefs.FindChildGameObject(transform, "ApplyRow"), 3f);
        BsmlLayoutGroups.ConfigureVertical(BsmlUiRefs.FindChildGameObject(transform, "ControlsSection"), 2f);
        BsmlLayoutGroups.MirrorSettingRowLayoutFromReference(_recordButton, _quickBindsEnableToggle);
        BsmlLayoutGroups.ConfigureHorizontal(BsmlUiRefs.FindChildGameObject(transform, "QuickBindsActionRow1"), 3f);
        BsmlLayoutGroups.ConfigureHorizontal(BsmlUiRefs.FindChildGameObject(transform, "QuickBindsActionRow2"), 3f);

        BsmlLayoutGroups.SetTextPreferredWidth(_recordingStatusText, QuickBindsStatusTextWidthPx);
        BsmlLayoutGroups.SetTextPreferredWidth(_comboPreviewText, QuickBindsStatusTextWidthPx);

        var help = BsmlUiRefs.FindChildGameObject(transform, "QuickBindsHelpText");
        if (help != null && help.TryGetComponent<TMP_Text>(out var helpTmp))
            BsmlLayoutGroups.SetTextPreferredWidth(helpTmp, QuickBindsHelpTextWidthPx);
    }



    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)

    {

        base.DidDeactivate(removedFromHierarchy, screenSystemDisabling);

        StopRecording(resetCombo: false);

        VrQuickBindInput.SetSettingsRecordingCaptureActive(false);

    }



    private void Update()

    {

        if (!_recordingActive)

            return;



        if (!_recordingInputArmed)

        {

            if (Time.realtimeSinceStartup >= _recordingInputArmDeadline)

            {

                _recordingInputArmed = true;

                VrQuickBindInput.SyncRecordingEdgeStateToHeld();

            }

            else

                return;

        }



        if (VrQuickBindInput.TryConsumeRecordingEdge(out var pressed))

        {

            _recordingCombo.Add(pressed);

            _recordingIdleDeadline = Time.realtimeSinceStartup + RecordIdleSaveSeconds;

            RefreshComboPreview();

            return;

        }



        if (_recordingCombo.Count > 0 && Time.realtimeSinceStartup >= _recordingIdleDeadline)

            FinishRecordingAndSave();

    }



    [UIAction("ConfigureQuickDisconnectClicked")]

    private void OnConfigureQuickDisconnectClicked()

    {

        StopRecording(resetCombo: false);

        _recordTarget = RecordTarget.QuickDisconnect;

        LoadExistingComboIntoPreview(ModSettings.QuickDisconnectCombo);

        RefreshStatusText();

        RefreshDeleteBindingButton();

    }



    [UIAction("ConfigureQuickReadyUpClicked")]

    private void OnConfigureQuickReadyUpClicked()

    {

        StopRecording(resetCombo: false);

        _recordTarget = RecordTarget.QuickReadyUp;

        LoadExistingComboIntoPreview(ModSettings.QuickReadyUpCombo);

        RefreshStatusText();

        RefreshDeleteBindingButton();

    }



    [UIAction("DeleteBindingClicked")]

    private void OnDeleteBindingClicked()

    {

        if (_recordTarget == RecordTarget.None)

        {

            RefreshStatusText("Select an action first.");

            return;

        }



        StopRecording(resetCombo: true);



        switch (_recordTarget)

        {

            case RecordTarget.QuickDisconnect:

                ModSettings.SetQuickDisconnectCombo(Array.Empty<int>());

                break;

            case RecordTarget.QuickReadyUp:

                ModSettings.SetQuickReadyUpCombo(Array.Empty<int>());

                break;

        }



        RefreshStatusText("Binding deleted.");

    }



    [UIAction("RecordClicked")]

    private void OnRecordClicked()

    {

        if (_recordTarget == RecordTarget.None)

        {

            RefreshStatusText("Select an action first.");

            return;

        }



        if (_recordingActive)

        {

            StopRecording(resetCombo: true);

            RefreshStatusText();

            return;

        }



        _recordingActive = true;

        _recordingInputArmed = false;

        _recordingCombo.Clear();

        VrQuickBindInput.ResetRecordingEdgeState();

        VrQuickBindInput.SetSettingsRecordingCaptureActive(true);

        _recordingInputArmDeadline = Time.realtimeSinceStartup + RecordInputArmDelaySeconds;

        _recordingIdleDeadline = Time.realtimeSinceStartup + RecordIdleSaveSeconds;

        RefreshComboPreview();

        RefreshStatusText("Recording... press controller buttons.");

    }



    [UIAction("SettingsClicked")]
    private void OnSettingsClicked() => OptionsSettingsClicked?.Invoke();

    [UIAction("ApplyClicked")]

    private void OnApplyClicked()

    {

        if (_recordingActive)
            FinishRecordingAndSave();

        var enableTgl = _quickBindsEnableToggle?.GetComponentInChildren<Toggle>(true);
        if (enableTgl != null)
            _draftQuickBindsEnabled = enableTgl.isOn;

        ModSettings.EnableQuickBinds = _draftQuickBindsEnabled;
        QuickBindsSettingsApplied?.Invoke();

    }



    private void FinishRecordingAndSave()

    {

        if (_recordTarget == RecordTarget.None || _recordingCombo.Count == 0)

        {

            StopRecording(resetCombo: true);

            RefreshStatusText("No buttons recorded; binding unchanged.");

            return;

        }



        var stored = new List<int>(_recordingCombo.Count);

        foreach (var b in _recordingCombo)

            stored.Add((int)b);



        switch (_recordTarget)

        {

            case RecordTarget.QuickDisconnect:

                ModSettings.SetQuickDisconnectCombo(stored);

                break;

            case RecordTarget.QuickReadyUp:

                ModSettings.SetQuickReadyUpCombo(stored);

                break;

        }



        StopRecording(resetCombo: false);

        RefreshStatusText("Binding saved.");

    }



    private void StopRecording(bool resetCombo)

    {

        _recordingActive = false;

        _recordingInputArmed = false;

        VrQuickBindInput.SetSettingsRecordingCaptureActive(false);

        VrQuickBindInput.ResetRecordingEdgeState();

        if (resetCombo)

            _recordingCombo.Clear();

        RefreshComboPreview();

    }



    private void LoadExistingComboIntoPreview(IReadOnlyList<int> stored)

    {

        _recordingCombo.Clear();

        if (stored == null)

            return;



        foreach (var raw in stored)

            _recordingCombo.Add((QuickBindButton)Mathf.Clamp(raw, 0, 3));

        RefreshComboPreview();

    }



    private void RefreshComboPreview()

    {

        if (_comboPreviewText == null)

            return;



        var label = _recordingCombo.Count == 0

            ? "(none)"

            : VrQuickBindInput.FormatCombo(_recordingCombo);

        _comboPreviewText.text = "Current button combo: " + label;

    }



    private void RefreshStatusText(string? overrideText = null)

    {

        if (_recordingStatusText == null)

            return;



        if (!string.IsNullOrEmpty(overrideText))

        {

            _recordingStatusText.text = overrideText;

            return;

        }



        _recordingStatusText.text = _recordTarget switch

        {

            RecordTarget.QuickDisconnect => "Configuring Quick Disconnect. Tap RECORD to capture a combo.",

            RecordTarget.QuickReadyUp => "Configuring Quick Ready Up. Tap RECORD to capture a combo.",

            _ => "Select an action below to configure a binding."

        };

    }



    private void RefreshDeleteBindingButton()

    {

        if (_deleteBindingButton == null)

            return;



        _deleteBindingButton.interactable = _recordTarget != RecordTarget.None;

    }

}


