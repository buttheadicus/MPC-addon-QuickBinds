using System;
using HMUI;
using Zenject;

namespace MultiplayerChat.UI;

public sealed class QuickBindsOptionsSettingsFlowCoordinator : FlowCoordinator
{
    [Inject] private readonly QuickBindsOptionsSettingsViewController _optionsView = null!;

    public HMUI.FlowCoordinator? ParentFlow { get; set; }

    protected override void DidActivate(bool firstActivation, bool addedToHierarchy, bool screenSystemEnabling)
    {
        if (firstActivation)
        {
            showBackButton = true;
            SetTitle("Quick Binds Settings");
            ProvideInitialViewControllers(_optionsView);
        }

        if (addedToHierarchy)
            _optionsView.OptionsSettingsApplied += OnApplied;
    }

    protected override void DidDeactivate(bool removedFromHierarchy, bool screenSystemDisabling)
    {
        if (removedFromHierarchy)
            _optionsView.OptionsSettingsApplied -= OnApplied;
    }

    private void OnApplied() => Dismiss();

    protected override void BackButtonWasPressed(ViewController topViewController) => Dismiss();

    private void Dismiss()
    {
        if (ParentFlow != null)
            ParentFlow.DismissFlowCoordinator(this);
        else
            BeatSaberMarkupLanguage.BeatSaberUI.MainFlowCoordinator.DismissFlowCoordinator(this);
    }
}
