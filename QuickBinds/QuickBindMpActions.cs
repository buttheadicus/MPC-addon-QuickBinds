namespace MultiplayerChat.Core.QuickBinds;

internal static class QuickBindMpActions
{
    internal static void TryQuickDisconnect() =>
        MpMenuUiAutomation.ScheduleQuickDisconnectFlow();

    internal static void TryQuickReadyUp()
    {
        MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Quick Ready Up combo matched.");
        MpLobbyReady.TryReadyUp();
    }
}
