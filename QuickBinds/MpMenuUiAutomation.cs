using System;
using System.Collections.Generic;
using System.Reflection;
using MultiplayerChat.Core;
using MultiplayerChat.Core.Addons;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MultiplayerChat.Core.QuickBinds;

// Drives menu button presses for Quick Disconnect.
internal static class MpMenuUiAutomation
{
    private sealed class ScheduledStep
    {
        public float ExecuteAt;
        public string Name = "";
        public Action? Action;
    }

    private static readonly List<ScheduledStep> Pending = new(16);
    private static readonly MethodInfo? ButtonPressMethod = typeof(Button).GetMethod(
        "Press",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static Type? _mainFcType;
    private static Type? _mpFcType;
    private static Type? _joiningVcType;
    private static Type? _simpleDialogType;
    private static Type? _disconnectPromptType;
    private static Type? _lobbyFcType;
    private static Type? _resultsVcType;
    private static Type? _missionResultsVcType;
    private static Type? _mpResultsVcType;
    private static Type? _activeInGameMenuVcType;
    private static Type? _inactiveInGameMenuVcType;
    private static Type? _inGameMenuControllerType;

    private static int _disconnectFlowId;
    private static bool _arenaDisconnectTriggered;
    private static bool _lobbyDisconnectTriggered;

    private const int MaxDisconnectPollAttempts = 40;
    private const float DisconnectPollIntervalSeconds = 0.2f;
    private const string JoiningLobbyCancelClickHandler = "<DidActivate>b__8_0";

    private static readonly string[] ContinueHandlerMethodNames =
    {
        "ContinueButtonPressed",
        "BackToMenuPressed",
        "BackToLobbyPressed"
    };

    private static readonly string[] DialogDismissLabelNeedles =
    {
        "no",
        "close",
        "ok",
        "dismiss"
    };

    private static readonly string[] LeaveConfirmLabelNeedles =
    {
        "yes",
        "leave",
        "disconnect",
        "confirm"
    };

    internal static bool HasPending => Pending.Count > 0;

    internal static void Tick()
    {
        if (Pending.Count == 0)
            return;

        var now = Time.realtimeSinceStartup;
        for (var i = Pending.Count - 1; i >= 0; i--)
        {
            if (now < Pending[i].ExecuteAt)
                continue;

            var item = Pending[i];
            Pending.RemoveAt(i);
            try
            {
                item.Action?.Invoke();
            }
            catch
            {
            }
        }
    }

    internal static void ScheduleQuickDisconnectFlow()
    {
        Pending.Clear();
        _disconnectFlowId++;
        _arenaDisconnectTriggered = false;
        _lobbyDisconnectTriggered = false;
        var flowId = _disconnectFlowId;
        MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Quick Disconnect flow scheduled.");
        RunDisconnectPollStep(flowId, 0);
    }

    private static void Schedule(float delaySeconds, string name, Action action)
    {
        Pending.Add(new ScheduledStep
        {
            ExecuteAt = Time.realtimeSinceStartup + delaySeconds,
            Name = name,
            Action = action
        });
    }

    private static bool RunStep(Func<bool> action)
    {
        try
        {
            return action();
        }
        catch
        {
            return false;
        }
    }

    private static void RunDisconnectPollStep(int flowId, int attempt)
    {
        if (flowId != _disconnectFlowId)
            return;

        if (IsDisconnectComplete())
        {
            FinishDisconnectFlow();
            return;
        }

        RunStep(() => TryClearBlockingDialogs(includeDisconnectPrompt: false));
        RunStep(TryPressContinueIfOnResults);

        if (IsDisconnectPromptVisible())
            RunStep(TryPressDisconnectPromptOk);
        else if (IsInArenaContext() && !_arenaDisconnectTriggered)
            RunStep(TryTriggerInArenaDisconnect);
        else if ((MpLobbySessionExit.IsSessionConnected() || MpLobbySessionExit.IsInCustomServerLobby())
                 && !_lobbyDisconnectTriggered)
            RunStep(TryTriggerLobbyDisconnect);

        if (IsDisconnectPromptVisible())
            RunStep(TryPressDisconnectPromptOk);

        RunStep(TryConfirmLeaveLobbyDialog);

        if (IsDisconnectComplete())
        {
            FinishDisconnectFlow();
            return;
        }

        if (attempt + 1 >= MaxDisconnectPollAttempts)
        {
            MultiplayerChat.Plugin.Log?.Warn("[MPChat][QuickBinds] Quick Disconnect gave up after polling.");
            return;
        }

        Schedule(
            DisconnectPollIntervalSeconds,
            "DisconnectPoll#" + (attempt + 1),
            () => RunDisconnectPollStep(flowId, attempt + 1));
    }

    private static void FinishDisconnectFlow()
    {
        AddonCustomAvatarsBridge.FlushLobbyCustomAvatarsOnServerLeave();
        MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Quick Disconnect complete.");
    }

    private static bool IsDisconnectComplete() => !MpLobbySessionExit.IsSessionConnected();

    private static bool IsInArenaContext() =>
        MpChatLobbyDiagnostics.SongGameplayLikelyActive()
        || MpChatLobbyDiagnostics.IsSpectatingInActiveMultiplayerSong();

    private static bool TryTriggerInArenaDisconnect()
    {
        if (!IsInArenaContext() || _arenaDisconnectTriggered)
            return false;

        if (!TryFindBestInGameMenuView(out var menuView, out var menuViewType))
            return false;

        _arenaDisconnectTriggered = true;

        var controller = MpUiReflection.FindBestActiveObject(InGameMenuControllerType);
        if (controller != null)
            MpUiReflection.TryInvokeParameterless(controller, "ShowInGameMenu");
        else
            MpUiReflection.TryInvokeParameterless(menuView, "ShowMenu");

        if (MpUiReflection.TryInvokeParameterless(menuView, "DisconnectButtonPressed"))
        {
            MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Triggered in-arena DisconnectButtonPressed (once).");
            return true;
        }

        if (TryPressFieldButton(menuView, menuViewType, "_disconnectButton"))
        {
            MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Pressed in-arena disconnect button (once).");
            return true;
        }

        if (controller != null
            && MpUiReflection.TryInvokeParameterless(controller, "HandleInGameMenuViewControllerDidPressDisconnectButton"))
        {
            MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Triggered in-arena menu controller disconnect handler (once).");
            return true;
        }

        _arenaDisconnectTriggered = false;
        return false;
    }

    private static bool TryFindBestInGameMenuView(out object? menuView, out Type? menuViewType)
    {
        menuView = null;
        menuViewType = null;

        var types = MpChatLobbyDiagnostics.IsSpectatingInActiveMultiplayerSong()
            ? new[] { InactiveInGameMenuVcType, ActiveInGameMenuVcType }
            : new[] { ActiveInGameMenuVcType, InactiveInGameMenuVcType };

        foreach (var type in types)
        {
            if (type == null)
                continue;

            foreach (var vc in MpUiReflection.FindAllInLoadedScenes(type, requireHierarchy: true))
            {
                menuView = vc;
                menuViewType = type;
                return true;
            }
        }

        return false;
    }

    private static bool TryTriggerLobbyDisconnect()
    {
        if (_lobbyDisconnectTriggered)
            return false;

        _lobbyDisconnectTriggered = true;

        var lobbyFc = MpUiReflection.GetBestFlowCoordinator(LobbyFcType, "_lobbySetupViewController");
        if (lobbyFc != null)
        {
            var userInitiated = ResolveEnumValue("DisconnectedReason", "UserInitiated");
            if (userInitiated != null
                && MpUiReflection.TryInvoke(lobbyFc, "ShowDisconnectDialogAndFinish", userInitiated))
            {
                MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Triggered lobby ShowDisconnectDialogAndFinish.");
                return true;
            }
        }

        if (MpLobbySessionExit.TryLeaveLobbyImmediately())
        {
            MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Triggered lobby leave fallback.");
            return true;
        }

        return false;
    }

    private static object? ResolveEnumValue(string typeName, string valueName)
    {
        var type = MpUiReflection.ResolveType(typeName);
        if (type == null || !type.IsEnum)
            return null;

        try
        {
            return Enum.Parse(type, valueName);
        }
        catch
        {
            return null;
        }
    }

    private static bool TryClearBlockingDialogs(bool includeDisconnectPrompt = true)
    {
        var acted = false;
        if (TryCancelJoiningLobby())
            acted = true;
        if (TryDismissBlockingErrorDialog())
            acted = true;
        if (includeDisconnectPrompt && TryCancelDisconnectPrompt())
            acted = true;
        return acted;
    }

    private static bool TryPressContinueIfOnResults()
    {
        if (!IsOnAnyResultsScreen())
            return true;
        return TryPressContinue();
    }

    private static bool TryPressContinue()
    {
        if (TryInvokeContinueHandlers())
            return true;
        if (TryFinishSimpleDialogContinue())
            return true;
        if (TryPressDisconnectPromptOk())
            return true;
        return IsOnAnyResultsScreen() && TryPressButtonByLabelNeedles(new[] { "continue" });
    }

    private static bool TryLeaveSessionForMenuFlow()
    {
        if (TryPressMultiplayerResultsBackToMenu())
            return !MpLobbySessionExit.IsInCustomServerLobby();

        if (!MpLobbySessionExit.IsInCustomServerLobby() && !MpLobbySessionExit.IsSessionConnected())
            return true;

        return MpLobbySessionExit.TryLeaveLobbyImmediately();
    }

    private static bool IsJoiningLobbyScreenActive()
    {
        if (MpUiReflection.FindBestActiveObject(JoiningVcType) != null)
            return true;

        foreach (var vc in MpUiReflection.FindAllInLoadedScenes(JoiningVcType))
        {
            if (IsFieldButtonVisible(vc, JoiningVcType, "_cancelJoiningButton"))
                return true;
        }

        return false;
    }

    private static bool IsOnAnyResultsScreen()
    {
        return MpUiReflection.FindBestActiveObject(ResultsVcType) != null
            || MpUiReflection.FindBestActiveObject(MissionResultsVcType) != null
            || MpUiReflection.FindBestActiveObject(MpResultsVcType) != null;
    }

    private static bool TryCancelJoiningLobby()
    {
        if (!IsJoiningLobbyScreenActive())
            return false;

        var mpFc = MpUiReflection.GetBestFlowCoordinator(MpFcType, "_joiningLobbyViewController");
        if (mpFc != null && MpUiReflection.TryInvokeParameterless(mpFc, "HandleJoiningLobbyViewControllerDidCancel"))
            return true;

        foreach (var joiningVc in MpUiReflection.FindAllInLoadedScenes(JoiningVcType))
        {
            if (TryPressFieldButton(joiningVc, JoiningVcType, "_cancelJoiningButton"))
                return true;
            if (MpUiReflection.TryInvokeParameterless(joiningVc, JoiningLobbyCancelClickHandler))
                return true;
        }

        return false;
    }

    private static bool TryDismissBlockingErrorDialog()
    {
        if (!GetVisibleSimpleDialog(out var viewController, out var viewControllerType))
            return false;

        if (viewController == null || viewControllerType == null)
            return false;

        if (IsDisclaimerDialog(viewController, viewControllerType))
            return false;

        var buttonsField = viewControllerType.GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        var textsField = viewControllerType.GetField("_buttonTexts", BindingFlags.Instance | BindingFlags.NonPublic);
        var finishField = viewControllerType.GetField("_didFinishAction", BindingFlags.Instance | BindingFlags.NonPublic);

        if (buttonsField?.GetValue(viewController) is not Button[] buttons || buttons.Length == 0)
            return false;

        var texts = textsField?.GetValue(viewController) as TextMeshProUGUI[];
        var dismissIndex = FindLabelButtonIndexOrNegative(buttons, texts, DialogDismissLabelNeedles);
        if (dismissIndex < 0)
            return false;

        if (TryPressButton(buttons[dismissIndex]))
            return true;

        if (finishField?.GetValue(viewController) is Action<int> finishAction)
        {
            try
            {
                finishAction.Invoke(dismissIndex);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryConfirmLeaveLobbyDialog()
    {
        if (!GetVisibleSimpleDialog(out var viewController, out var viewControllerType))
            return false;

        if (viewController == null || viewControllerType == null)
            return false;

        if (IsDisclaimerDialog(viewController, viewControllerType))
            return false;

        var buttonsField = viewControllerType.GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        var textsField = viewControllerType.GetField("_buttonTexts", BindingFlags.Instance | BindingFlags.NonPublic);
        var finishField = viewControllerType.GetField("_didFinishAction", BindingFlags.Instance | BindingFlags.NonPublic);

        if (buttonsField?.GetValue(viewController) is not Button[] buttons || buttons.Length == 0)
            return false;

        var texts = textsField?.GetValue(viewController) as TextMeshProUGUI[];
        var confirmIndex = FindLabelButtonIndexOrNegative(buttons, texts, LeaveConfirmLabelNeedles);
        if (confirmIndex < 0)
            return false;

        if (TryPressButton(buttons[confirmIndex]))
            return true;

        if (finishField?.GetValue(viewController) is Action<int> finishAction)
        {
            try
            {
                finishAction.Invoke(confirmIndex);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryCancelDisconnectPrompt()
    {
        if (!IsDisconnectPromptVisible())
            return false;

        var view = MpUiReflection.FindBestActiveObject(DisconnectPromptType);
        if (view == null)
            return false;

        if (TryPressFieldButton(view, DisconnectPromptType, "_cancelButton"))
            return true;

        return TryPressFieldButton(view, DisconnectPromptType, "_okButton");
    }

    private static bool TryPressMultiplayerResultsBackToMenu()
    {
        var mpResults = MpUiReflection.FindBestActiveObject(MpResultsVcType);
        if (mpResults == null)
            return false;

        if (MpUiReflection.TryInvokeParameterless(mpResults, "BackToMenuPressed"))
            return true;

        var lobbyFc = MpUiReflection.GetBestFlowCoordinator(LobbyFcType, "_lobbySetupViewController");
        if (lobbyFc != null
            && MpUiReflection.TryInvoke(lobbyFc, "HandleMultiplayerResultsViewControllerBackToMenuPressed", mpResults))
            return true;

        return TryPressFieldButton(mpResults, MpResultsVcType, "_backToMenuButton");
    }

    private static bool GetVisibleSimpleDialog(out object? viewController, out Type? viewControllerType)
    {
        viewController = null;
        viewControllerType = SimpleDialogType;
        if (viewControllerType == null)
            return false;

        foreach (var vc in MpUiReflection.FindAllInLoadedScenes(viewControllerType, requireHierarchy: true))
        {
            if (!HasVisibleSimpleDialogButton(vc, viewControllerType))
                continue;
            viewController = vc;
            return true;
        }

        return false;
    }

    private static bool HasVisibleSimpleDialogButton(object viewController, Type viewControllerType)
    {
        var buttonsField = viewControllerType.GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        if (buttonsField?.GetValue(viewController) is not Button[] buttons)
            return false;

        foreach (var button in buttons)
        {
            if (button != null && button.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static bool IsDisclaimerDialog(object viewController, Type viewControllerType)
    {
        var buttonsField = viewControllerType.GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        var textsField = viewControllerType.GetField("_buttonTexts", BindingFlags.Instance | BindingFlags.NonPublic);
        if (buttonsField?.GetValue(viewController) is not Button[] buttons)
            return false;

        var texts = textsField?.GetValue(viewController) as TextMeshProUGUI[];
        return FindLabelButtonIndexOrNegative(buttons, texts, new[] { "agree", "accept" }) >= 0;
    }

    private static bool IsDisconnectPromptVisible()
    {
        if (IsDisconnectPromptInstanceVisible(MpUiReflection.FindBestActiveObject(DisconnectPromptType)))
            return true;

        foreach (var type in new[] { ActiveInGameMenuVcType, InactiveInGameMenuVcType })
        {
            if (type == null)
                continue;

            foreach (var vc in MpUiReflection.FindAllInLoadedScenes(type, requireHierarchy: true))
            {
                var prompt = MpUiReflection.GetInstanceField(vc, "_disconnectPromptView");
                if (IsDisconnectPromptInstanceVisible(prompt))
                    return true;
            }
        }

        return false;
    }

    private static bool IsDisconnectPromptInstanceVisible(object? promptView)
    {
        if (promptView == null || DisconnectPromptType == null)
            return false;

        var promptField = DisconnectPromptType.GetField("_promptGameObject", BindingFlags.Instance | BindingFlags.NonPublic);
        if (promptField?.GetValue(promptView) is GameObject prompt && prompt != null)
            return prompt.activeInHierarchy;

        return promptView is Component component && component.gameObject.activeInHierarchy;
    }

    private static bool TryInvokeContinueHandlers()
    {
        Type?[] types = { ResultsVcType, MissionResultsVcType, MpResultsVcType };
        foreach (var type in types)
        {
            if (type == null)
                continue;

            foreach (var vc in MpUiReflection.FindAllInLoadedScenes(type, requireHierarchy: true))
            {
                foreach (var methodName in ContinueHandlerMethodNames)
                {
                    if (MpUiReflection.TryInvokeParameterless(vc, methodName))
                        return true;
                }
            }
        }

        return false;
    }

    private static bool TryFinishSimpleDialogContinue()
    {
        var type = SimpleDialogType;
        var vc = MpUiReflection.GetFlowCoordinatorField(MpFcType, "_simpleDialogPromptViewController")
            ?? MpUiReflection.GetFlowCoordinatorField(LobbyFcType, "_simpleDialogPromptViewController")
            ?? MpUiReflection.GetFlowCoordinatorField(MainFcType, "_simpleDialogPromptViewController")
            ?? MpUiReflection.FindBestActiveObject(type);
        if (vc == null || type == null)
            return false;

        var buttonsField = type.GetField("_buttons", BindingFlags.Instance | BindingFlags.NonPublic);
        var textsField = type.GetField("_buttonTexts", BindingFlags.Instance | BindingFlags.NonPublic);
        var finishField = type.GetField("_didFinishAction", BindingFlags.Instance | BindingFlags.NonPublic);

        if (buttonsField?.GetValue(vc) is not Button[] buttons || buttons.Length == 0)
            return false;

        var texts = textsField?.GetValue(vc) as TextMeshProUGUI[];
        var continueIndex = FindLabelButtonIndex(buttons, texts, new[] { "continue" });

        if (TryPressButton(buttons[continueIndex]))
            return true;

        if (finishField?.GetValue(vc) is Action<int> finishAction)
        {
            try
            {
                finishAction.Invoke(continueIndex);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryPressDisconnectPromptOk()
    {
        if (TryPressDisconnectPromptInstanceOk(MpUiReflection.FindBestActiveObject(DisconnectPromptType)))
            return true;

        foreach (var type in new[] { ActiveInGameMenuVcType, InactiveInGameMenuVcType })
        {
            if (type == null)
                continue;

            foreach (var vc in MpUiReflection.FindAllInLoadedScenes(type, requireHierarchy: true))
            {
                var prompt = MpUiReflection.GetInstanceField(vc, "_disconnectPromptView");
                if (!IsDisconnectPromptInstanceVisible(prompt))
                    continue;

                if (TryPressDisconnectPromptInstanceOk(prompt))
                {
                    MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Confirmed in-arena disconnect prompt (OK).");
                    return true;
                }

                if (MpUiReflection.TryInvoke(vc, "HandleDisconnectPromptViewDidViewFinish", true))
                {
                    MultiplayerChat.Plugin.Log?.Info("[MPChat][QuickBinds] Confirmed in-arena disconnect prompt (finish handler).");
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryPressDisconnectPromptInstanceOk(object? promptView)
    {
        if (!IsDisconnectPromptInstanceVisible(promptView) || DisconnectPromptType == null)
            return false;

        return TryPressFieldButton(promptView, DisconnectPromptType, "_okButton");
    }

    private static bool TryPressButtonByLabelNeedles(string[] needles)
    {
        foreach (var button in Resources.FindObjectsOfTypeAll<Button>())
        {
            if (!IsSceneButton(button))
                continue;

            var labelText = GetButtonLabelText(button);
            if (string.IsNullOrEmpty(labelText))
                continue;

            foreach (var needle in needles)
            {
                if (string.IsNullOrEmpty(needle))
                    continue;
                if (labelText!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (TryPressButton(button))
                    return true;
            }
        }

        return false;
    }

    private static bool IsFieldButtonVisible(object? target, Type? type, string fieldName)
    {
        if (target == null || type == null)
            return false;

        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(target) is not Button button)
            return false;

        return button.gameObject.activeInHierarchy;
    }

    private static bool TryPressFieldButton(object? target, Type? type, string fieldName)
    {
        if (target == null || type == null)
            return false;

        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        return field != null && TryPressButton(field.GetValue(target) as Button);
    }

    private static bool TryPressButton(Button? button)
    {
        if (button == null || !button.gameObject.activeInHierarchy)
            return false;

        if (!button.interactable)
            button.interactable = true;

        if (ButtonPressMethod != null)
        {
            try
            {
                ButtonPressMethod.Invoke(button, null);
                return true;
            }
            catch
            {
            }
        }

        try
        {
            button.onClick?.Invoke();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsSceneButton(Button? button)
    {
        if (button == null)
            return false;

        var go = button.gameObject;
        if (!go.activeInHierarchy)
            return false;

        var scene = go.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    private static string? GetButtonLabelText(Button button)
    {
        foreach (var label in button.GetComponentsInChildren<TextMeshProUGUI>(true))
        {
            if (label != null && !string.IsNullOrEmpty(label.text))
                return label.text;
        }

        return null;
    }

    private static int FindLabelButtonIndex(Button[] buttons, TextMeshProUGUI[]? texts, string[] needles)
    {
        var index = FindLabelButtonIndexOrNegative(buttons, texts, needles);
        return index >= 0 ? index : 0;
    }

    private static int FindLabelButtonIndexOrNegative(Button[] buttons, TextMeshProUGUI[]? texts, string[] needles)
    {
        for (var i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null || !buttons[i].gameObject.activeInHierarchy)
                continue;

            var label = texts != null && i < texts.Length && texts[i] != null
                ? texts[i].text
                : GetButtonLabelText(buttons[i]);

            if (string.IsNullOrEmpty(label))
                continue;

            foreach (var needle in needles)
            {
                if (string.IsNullOrEmpty(needle))
                    continue;
                if (label!.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
        }

        return -1;
    }

    private static Type? MainFcType =>
        _mainFcType ??= MpUiReflection.ResolveType("MainFlowCoordinator");

    private static Type? MpFcType =>
        _mpFcType ??= MpUiReflection.ResolveType("MultiplayerModeSelectionFlowCoordinator");

    private static Type? JoiningVcType =>
        _joiningVcType ??= MpUiReflection.ResolveType("JoiningLobbyViewController");

    private static Type? SimpleDialogType =>
        _simpleDialogType ??= MpUiReflection.ResolveType("SimpleDialogPromptViewController");

    private static Type? DisconnectPromptType =>
        _disconnectPromptType ??= MpUiReflection.ResolveType("DisconnectPromptView");

    private static Type? LobbyFcType =>
        _lobbyFcType ??= MpUiReflection.ResolveType("GameServerLobbyFlowCoordinator");

    private static Type? ResultsVcType =>
        _resultsVcType ??= MpUiReflection.ResolveType("ResultsViewController");

    private static Type? MissionResultsVcType =>
        _missionResultsVcType ??= MpUiReflection.ResolveType("MissionResultsViewController");

    private static Type? MpResultsVcType =>
        _mpResultsVcType ??= MpUiReflection.ResolveType("MultiplayerResultsViewController");

    private static Type? ActiveInGameMenuVcType =>
        _activeInGameMenuVcType ??= MpUiReflection.ResolveType("MultiplayerLocalActivePlayerInGameMenuViewController");

    private static Type? InactiveInGameMenuVcType =>
        _inactiveInGameMenuVcType ??= MpUiReflection.ResolveType("MultiplayerLocalInactivePlayerInGameMenuViewController");

    private static Type? InGameMenuControllerType =>
        _inGameMenuControllerType ??= MpUiReflection.ResolveType("MultiplayerLocalActivePlayerInGameMenuController");
}
