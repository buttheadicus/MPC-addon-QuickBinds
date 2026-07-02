using System.Collections.Generic;
using MultiplayerChat.Core;
using MultiplayerChat.Settings;
using UnityEngine;

namespace MultiplayerChat.Core.QuickBinds;

public sealed class QuickBindsRuntimeManager : MonoBehaviour
{
    public static QuickBindsRuntimeManager? Instance { get; private set; }

    private readonly List<QuickBindButton> _quickDisconnectProgress = new(16);
    private readonly List<QuickBindButton> _quickReadyUpProgress = new(16);

    private float _quickDisconnectComboExpiry = -999f;
    private float _quickReadyUpComboExpiry = -999f;


    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (MpMenuUiAutomation.HasPending)
            MpMenuUiAutomation.Tick();

        if (!ModSettings.EnableQuickBinds)
            return;

        if (!MpChatLobbyDiagnostics.QuickBindsAllowedDuringGameplay())
            return;

        if (VrQuickBindInput.IsSettingsRecordingCaptureActive)
            return;

        var disconnectCombo = ModSettings.QuickDisconnectCombo;
        var readyCombo = ModSettings.QuickReadyUpCombo;
        var trackDisconnect = disconnectCombo.Count > 0;
        var trackReady = readyCombo.Count > 0;
        if (!trackDisconnect && !trackReady)
            return;

        if (trackDisconnect)
            ExpireComboIfNeeded(_quickDisconnectProgress, ref _quickDisconnectComboExpiry);
        if (trackReady)
            ExpireComboIfNeeded(_quickReadyUpProgress, ref _quickReadyUpComboExpiry);

        // One edge stream shared by all binds. Do not drain it in the first PollCombo only.
        VrQuickBindInput.BeginInputFrame();
        while (VrQuickBindInput.TryConsumeAnyEdge(out var pressed))
        {
            if (trackDisconnect)
            {
                ProcessComboPress(disconnectCombo, _quickDisconnectProgress, ref _quickDisconnectComboExpiry,
                    pressed, QuickBindMpActions.TryQuickDisconnect);
            }

            if (trackReady)
            {
                ProcessComboPress(readyCombo, _quickReadyUpProgress, ref _quickReadyUpComboExpiry,
                    pressed, QuickBindMpActions.TryQuickReadyUp);
            }
        }
    }

    private static void ExpireComboIfNeeded(List<QuickBindButton> progress, ref float comboExpiry)
    {
        if (progress.Count == 0)
            return;

        if (Time.realtimeSinceStartup > comboExpiry)
            progress.Clear();
    }

    private static void ProcessComboPress(
        IReadOnlyList<int> storedCombo,
        List<QuickBindButton> progress,
        ref float comboExpiry,
        QuickBindButton pressed,
        System.Action onMatch)
    {
        var expected = (QuickBindButton)Mathf.Clamp(storedCombo[progress.Count], 0, 3);
        if (pressed == expected)
        {
            progress.Add(pressed);
            comboExpiry = Time.realtimeSinceStartup + ModSettings.QuickBindComboExpireSeconds;
            if (progress.Count >= storedCombo.Count)
            {
                progress.Clear();
                comboExpiry = -999f;
                onMatch();
            }

            return;
        }

        progress.Clear();
        comboExpiry = -999f;
        if (pressed == (QuickBindButton)Mathf.Clamp(storedCombo[0], 0, 3))
        {
            progress.Add(pressed);
            comboExpiry = Time.realtimeSinceStartup + ModSettings.QuickBindComboExpireSeconds;
        }
    }
}
