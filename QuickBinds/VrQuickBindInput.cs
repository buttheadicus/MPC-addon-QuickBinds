using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace MultiplayerChat.Core.QuickBinds;

internal static class VrQuickBindInput
{
    private const float TriggerPressThreshold = 0.1f;

    private static readonly List<InputDevice> DeviceBuffer = new(4);

    private static bool _priWas;
    private static bool _secWas;
    private static bool _trigWas;
    private static bool _gripWas;

    private static bool _recPriWas;
    private static bool _recSecWas;
    private static bool _recTrigWas;
    private static bool _recGripWas;

    private static int _inputFrame = -1;
    private static InputDevice _frameLeft;
    private static InputDevice _frameRight;

    internal static bool IsSettingsRecordingCaptureActive { get; private set; }

    internal static void BeginInputFrame()
    {
        var frame = Time.frameCount;
        if (_inputFrame == frame)
            return;

        _inputFrame = frame;
        _frameLeft = PrimaryDeviceAt(XRNode.LeftHand);
        _frameRight = PrimaryDeviceAt(XRNode.RightHand);
    }

    internal static void SetSettingsRecordingCaptureActive(bool active) =>
        IsSettingsRecordingCaptureActive = active;

    internal static void ResetEdgeState()
    {
        _priWas = _secWas = _trigWas = _gripWas = false;
    }

    internal static void ResetRecordingEdgeState()
    {
        _recPriWas = _recSecWas = _recTrigWas = _recGripWas = false;
    }

    // After the arm delay, mark currently held buttons so only new presses count as edges.
    internal static void SyncRecordingEdgeStateToHeld()
    {
        var left = PrimaryDeviceAt(XRNode.LeftHand);
        var right = PrimaryDeviceAt(XRNode.RightHand);
        _recPriWas = Get(left, CommonUsages.primaryButton) || Get(right, CommonUsages.primaryButton);
        _recSecWas = Get(left, CommonUsages.secondaryButton) || Get(right, CommonUsages.secondaryButton);
        _recTrigWas = TriggerHeld(left) || TriggerHeld(right);
        _recGripWas = Get(left, CommonUsages.gripButton) || Get(right, CommonUsages.gripButton);
    }

    internal static bool TryConsumeRecordingEdge(out QuickBindButton button)
    {
        button = default;
        var left = PrimaryDeviceAt(XRNode.LeftHand);
        var right = PrimaryDeviceAt(XRNode.RightHand);

        if (TryConsumeEdge(left, right, QuickBindButton.Primary, ref _recPriWas, CommonUsages.primaryButton))
        {
            button = QuickBindButton.Primary;
            return true;
        }

        if (TryConsumeEdge(left, right, QuickBindButton.Secondary, ref _recSecWas, CommonUsages.secondaryButton))
        {
            button = QuickBindButton.Secondary;
            return true;
        }

        if (TryConsumeRecordingTriggerEdge(left, right))
        {
            button = QuickBindButton.Trigger;
            return true;
        }

        if (TryConsumeEdge(left, right, QuickBindButton.Grip, ref _recGripWas, CommonUsages.gripButton))
        {
            button = QuickBindButton.Grip;
            return true;
        }

        return false;
    }

    internal static bool TryConsumeAnyEdge(out QuickBindButton button)
    {
        button = default;
        var left = _frameLeft;
        var right = _frameRight;

        if (TryConsumeEdge(left, right, QuickBindButton.Primary, ref _priWas, CommonUsages.primaryButton))
        {
            button = QuickBindButton.Primary;
            return true;
        }

        if (TryConsumeEdge(left, right, QuickBindButton.Secondary, ref _secWas, CommonUsages.secondaryButton))
        {
            button = QuickBindButton.Secondary;
            return true;
        }

        if (TryConsumeTriggerEdge(left, right))
        {
            button = QuickBindButton.Trigger;
            return true;
        }

        if (TryConsumeEdge(left, right, QuickBindButton.Grip, ref _gripWas, CommonUsages.gripButton))
        {
            button = QuickBindButton.Grip;
            return true;
        }

        return false;
    }

    internal static bool IsHeld(QuickBindButton button)
    {
        var left = PrimaryDeviceAt(XRNode.LeftHand);
        var right = PrimaryDeviceAt(XRNode.RightHand);
        return button switch
        {
            QuickBindButton.Primary => Get(left, CommonUsages.primaryButton) || Get(right, CommonUsages.primaryButton),
            QuickBindButton.Secondary => Get(left, CommonUsages.secondaryButton) || Get(right, CommonUsages.secondaryButton),
            QuickBindButton.Trigger => TriggerHeld(left) || TriggerHeld(right),
            QuickBindButton.Grip => Get(left, CommonUsages.gripButton) || Get(right, CommonUsages.gripButton),
            _ => false
        };
    }

    internal static string FormatButton(QuickBindButton button) =>
        button switch
        {
            QuickBindButton.Primary => "Primary",
            QuickBindButton.Secondary => "Secondary",
            QuickBindButton.Trigger => "Trigger",
            QuickBindButton.Grip => "Grip",
            _ => button.ToString()
        };

    internal static string FormatCombo(IReadOnlyList<QuickBindButton> combo)
    {
        if (combo == null || combo.Count == 0)
            return "(none)";

        var parts = new string[combo.Count];
        for (var i = 0; i < combo.Count; i++)
            parts[i] = FormatButton(combo[i]);
        return string.Join(", ", parts);
    }

    private static InputDevice PrimaryDeviceAt(XRNode node)
    {
        DeviceBuffer.Clear();
        InputDevices.GetDevicesAtXRNode(node, DeviceBuffer);
        return DeviceBuffer.Count > 0 ? DeviceBuffer[0] : default;
    }

    private static bool TryConsumeEdge(InputDevice left, InputDevice right, QuickBindButton _, ref bool wasHeld,
        InputFeatureUsage<bool> usage)
    {
        var held = Get(left, usage) || Get(right, usage);
        var edge = held && !wasHeld;
        wasHeld = held;
        return edge;
    }

    private static bool TryConsumeTriggerEdge(InputDevice left, InputDevice right)
    {
        var held = TriggerHeld(left) || TriggerHeld(right);
        var edge = held && !_trigWas;
        _trigWas = held;
        return edge;
    }

    private static bool TryConsumeRecordingTriggerEdge(InputDevice left, InputDevice right)
    {
        var held = TriggerHeld(left) || TriggerHeld(right);
        var edge = held && !_recTrigWas;
        _recTrigWas = held;
        return edge;
    }

    private static bool TriggerHeld(InputDevice d)
    {
        if (!d.isValid) return false;
        if (d.TryGetFeatureValue(CommonUsages.triggerButton, out var b) && b)
            return true;
        if (d.TryGetFeatureValue(CommonUsages.trigger, out var t) && t > TriggerPressThreshold)
            return true;
        return false;
    }

    private static bool Get(InputDevice d, InputFeatureUsage<bool> usage) =>
        d.isValid && d.TryGetFeatureValue(usage, out var v) && v;
}
