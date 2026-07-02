using System.Reflection;
using UnityEngine.UI;

namespace MultiplayerChat.Core.QuickBinds;

internal static class MpUiButtonPress
{
    private static readonly MethodInfo? ButtonPressMethod = typeof(Button).GetMethod(
        "Press",
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    internal static bool TryPressFieldButton(object? target, System.Type? type, string fieldName)
    {
        if (target == null || type == null)
            return false;

        var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field != null && TryPressButton(field.GetValue(target) as Button);
    }

    internal static bool TryPressButton(Button? button)
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
}
