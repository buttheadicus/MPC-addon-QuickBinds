using System;
using System.Reflection;
using MultiplayerChat.Core;
using UnityEngine;

namespace MultiplayerChat.Core.QuickBinds;

internal static class MpLobbySessionExit
{
    private static readonly string[] LobbyLeaveMethodNames =
    {
        "LeaveLobby",
        "DismissViewControllersAndCoordinators",
        "HandleLobbyGameStateControllerLobbyDisconnected"
    };

    private static readonly string[] LeaveLobbyOnly = { "LeaveLobby" };

    private static Type? _modeSelectionFcType;
    private static Type? _lobbyFcType;
    private static Type? _sessionManagerType;
    private static FieldInfo? _connectionField;
    private static MethodInfo? _disconnectMethod;
    private static bool _reflectionReady;

    internal static bool IsInCustomServerLobby()
    {
        try
        {
            EnsureReflection();

            var lobbySetupType = MpUiReflection.ResolveType("LobbySetupViewController");
            if (MpUiReflection.FindBestActiveObject(lobbySetupType) != null)
                return true;

            var mpResultsType = MpUiReflection.ResolveType("MultiplayerResultsViewController");
            return MpUiReflection.FindBestActiveObject(mpResultsType) != null && IsSessionConnected();
        }
        catch
        {
            return false;
        }
    }

    internal static bool IsSessionConnected()
    {
        try
        {
            EnsureReflection();
            var session = FindActiveSessionManager();
            return session != null && SessionLooksConnected(session);
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryLeaveLobbyImmediately()
    {
        try
        {
            EnsureReflection();
            if (TryLeaveViaConnectionController())
                return true;
            if (TryLeaveViaLobbyFlowCoordinator())
                return true;
            return TryLeaveViaSessionManager();
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureReflection()
    {
        if (_reflectionReady)
            return;

        _modeSelectionFcType = MpUiReflection.ResolveType("MultiplayerModeSelectionFlowCoordinator");
        _lobbyFcType = MpUiReflection.ResolveType("GameServerLobbyFlowCoordinator");
        _sessionManagerType = MpUiReflection.ResolveType("MultiplayerSessionManager");

        if (_modeSelectionFcType != null)
        {
            _connectionField = _modeSelectionFcType.GetField(
                "_multiplayerLobbyConnectionController",
                BindingFlags.Instance | BindingFlags.NonPublic);
        }

        if (_sessionManagerType != null)
            _disconnectMethod = MpUiReflection.GetParameterlessInstanceMethod(_sessionManagerType, "Disconnect");

        _reflectionReady = true;
    }

    private static bool TryLeaveViaConnectionController()
    {
        if (_modeSelectionFcType == null || _connectionField == null)
            return false;

        foreach (var fc in Resources.FindObjectsOfTypeAll(_modeSelectionFcType))
        {
            var conn = _connectionField.GetValue(fc);
            if (conn == null)
                continue;

            if (TryInvokeNamedMethods(conn, conn.GetType(), LeaveLobbyOnly))
                return true;
        }

        return false;
    }

    private static bool TryLeaveViaLobbyFlowCoordinator()
    {
        if (_lobbyFcType == null)
            return false;

        var fc = MpUiReflection.GetBestFlowCoordinator(_lobbyFcType, "_lobbySetupViewController");
        if (fc == null)
            return false;

        if (TryInvokeNamedMethods(fc, _lobbyFcType, LobbyLeaveMethodNames))
            return true;

        return TryLeaveViaConnectionFieldOnFlowCoordinator(fc, _lobbyFcType);
    }

    private static bool TryLeaveViaConnectionFieldOnFlowCoordinator(object flowCoordinator, Type fcType)
    {
        foreach (var field in fcType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.Name.IndexOf("Connection", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            var value = field.GetValue(flowCoordinator);
            if (value == null)
                continue;

            if (TryInvokeNamedMethods(value, value.GetType(), LeaveLobbyOnly))
                return true;
        }

        return false;
    }

    private static bool TryLeaveViaSessionManager()
    {
        var session = FindActiveSessionManager();
        if (session == null || _disconnectMethod == null)
            return false;

        try
        {
            _disconnectMethod.Invoke(session, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? FindActiveSessionManager()
    {
        EnsureReflection();
        return _sessionManagerType == null
            ? null
            : MpUiReflection.FindBestActiveObject(_sessionManagerType);
    }

    private static bool SessionLooksConnected(object session)
    {
        var t = session.GetType();
        var prop = t.GetProperty("connected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.GetValue(session, null) is bool b)
            return b;

        prop = t.GetProperty("isConnected", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (prop?.GetValue(session, null) is bool b2)
            return b2;

        return true;
    }

    private static bool TryInvokeNamedMethods(object target, Type type, string[] names)
    {
        foreach (var name in names)
        {
            var method = MpUiReflection.GetParameterlessInstanceMethod(type, name);
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
