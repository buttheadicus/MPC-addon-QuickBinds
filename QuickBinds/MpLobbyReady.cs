using System;
using System.Reflection;

namespace MultiplayerChat.Core.QuickBinds;

internal static class MpLobbyReady
{
    private static readonly string[] ReadyMethodNames =
    {
        "HandleLobbySetupViewControllerStartGameOrReady"
    };

    private static Type? _lobbyFcType;
    private static bool _reflectionReady;

    internal static bool TryReadyUp()
    {
        try
        {
            EnsureReflection();
            if (_lobbyFcType == null)
            {
                MultiplayerChat.Plugin.Log?.Warn("[MPChat][QuickBinds] Quick Ready Up failed: GameServerLobbyFlowCoordinator type not found.");
                return false;
            }

            var fc = MpUiReflection.FindFirstActiveObject(_lobbyFcType);
            if (fc == null)
            {
                MultiplayerChat.Plugin.Log?.Warn("[MPChat][QuickBinds] Quick Ready Up failed: no active GameServerLobbyFlowCoordinator.");
                return false;
            }

            if (!TryInvokeNamedMethods(fc, _lobbyFcType, ReadyMethodNames))
            {
                MultiplayerChat.Plugin.Log?.Warn("[MPChat][QuickBinds] Quick Ready Up failed: HandleLobbySetupViewControllerStartGameOrReady did not run.");
                return false;
            }

            MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Quick Ready Up invoked HandleLobbySetupViewControllerStartGameOrReady.");
            return true;
        }
        catch (Exception ex)
        {
            MultiplayerChat.Plugin.Log?.Warn("[MPChat][QuickBinds] Quick Ready Up failed: " + ex.Message);
            return false;
        }
    }

    private static void EnsureReflection()
    {
        if (_reflectionReady)
            return;

        _lobbyFcType = MpUiReflection.ResolveType("GameServerLobbyFlowCoordinator");
        _reflectionReady = true;
    }

    private static bool TryInvokeNamedMethods(object target, Type type, string[] names)
    {
        for (var i = 0; i < names.Length; i++)
        {
            var method = MpUiReflection.GetParameterlessInstanceMethod(type, names[i]);
            if (method == null)
                continue;

            try
            {
                method.Invoke(target, null);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }
}
