using System;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components.Settings;
using BeatSaberMarkupLanguage.ViewControllers;
using MultiplayerChat.Settings;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerChat.UI;

[ViewDefinition("MultiplayerChat.UI.QuickBindsOptionsSettingsView.bsml")]
public sealed class QuickBindsOptionsSettingsViewController : BSMLAutomaticViewController
{
    public event Action? OptionsSettingsApplied;

    [UIComponent("ComboExpireSeconds")]
    private SliderSetting? _comboExpireSlider;

    [UIComponent("AllowDuringSongToggle")]
    private ToggleSetting? _allowDuringSongToggle;

    private float _comboExpireDraftSeconds = ModSettings.QuickBindComboExpireSeconds;
    private bool _allowDuringSongDraft = ModSettings.AllowQuickBindsDuringSong;

    [UIValue("ComboExpireSeconds")]
    private float ComboExpireSeconds
    {
        get => _comboExpireDraftSeconds;
        set => _comboExpireDraftSeconds = Mathf.Clamp(value, 1f, 60f);
    }

    [UIValue("AllowQuickBindsDuringSong")]
    public bool AllowQuickBindsDuringSong
    {
        get => _allowDuringSongDraft;
        set => _allowDuringSongDraft = value;
    }

    [UIAction("#post-parse")]
    private void PostParse()
    {
        ReloadDraft();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        BsmlLayoutGroups.CompactSliderSetting(_comboExpireSlider);
        _comboExpireSlider?.ReceiveValue();
        _allowDuringSongToggle?.ReceiveValue();
    }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        base.DidActivate(firstActivation, addedToHierarchy, screenSystemEnabling);
        ReloadDraft();
        BsmlDefaultStringCleanup.StripPlaceholderLabels(gameObject);
        BsmlLayoutGroups.CompactSliderSetting(_comboExpireSlider);
        _comboExpireSlider?.ReceiveValue();
        _allowDuringSongToggle?.ReceiveValue();
    }

    private void ReloadDraft()
    {
        _comboExpireDraftSeconds = ModSettings.QuickBindComboExpireSeconds;
        _allowDuringSongDraft = ModSettings.AllowQuickBindsDuringSong;
    }

    [UIAction("ApplyClicked")]
    private void ApplyClicked()
    {
        if (_comboExpireSlider != null)
            _comboExpireDraftSeconds = Mathf.Clamp(_comboExpireSlider.Value, 1f, 60f);

        var allowTgl = _allowDuringSongToggle?.GetComponentInChildren<Toggle>(true);
        if (allowTgl != null)
            _allowDuringSongDraft = allowTgl.isOn;

        ModSettings.QuickBindComboExpireSeconds = Mathf.RoundToInt(_comboExpireDraftSeconds);
        ModSettings.AllowQuickBindsDuringSong = _allowDuringSongDraft;
        OptionsSettingsApplied?.Invoke();
    }
}
