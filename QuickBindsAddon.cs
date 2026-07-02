using System;
using MultiplayerChat.Contracts;
using MultiplayerChat.Core.QuickBinds;
using UnityEngine;

namespace MultiplayerChat.Addon.QuickBinds;

[MpChatAddon(AddonIds.QuickBinds)]
public sealed class QuickBindsAddon : IMpChatAddon, IMpChatSettingsPage
{
    private IMpChatHost? _host;
    private object? _runtimeHost;

    public string Id => AddonIds.QuickBinds;

    public string DisplayName => "Quick Binds";

    public Version Version => new(1, 0, 0);

    string IMpChatSettingsPage.AddonId => Id;

    public string PageTitle => "Quick Binds";

    public string SettingsCategory => "Addons";

    public void OnLoad(IMpChatHost host)
    {
        _host = host;
        _runtimeHost = host.CreatePersistentHost("MPChatQuickBindsHost");
        if (_runtimeHost is GameObject go)
            go.AddComponent<QuickBindsRuntimeManager>();
        host.RegisterSettingsPage(this);
        host.RegisterSettingsPresenter(
            Id,
            typeof(MultiplayerChat.UI.QuickBindsSettingsFlowCoordinator),
            "Quick Binds setup");
        host.SetCapability(AddonCapability.QuickBinds, true);
    }

    public void OnUnload()
    {
        if (_host != null && _runtimeHost != null)
            _host.DestroyPersistentHost(_runtimeHost);
        _runtimeHost = null;
        _host?.UnregisterSettingsPresenter(Id);
        _host?.UnregisterSettingsPage(this);
        _host?.SetCapability(AddonCapability.QuickBinds, false);
        _host = null;
    }
}
