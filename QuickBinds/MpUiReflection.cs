using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MultiplayerChat.Core.QuickBinds;

internal static class MpUiReflection
{
    private static readonly HashSet<string> AssemblyNames = new(StringComparer.Ordinal)
    {
        "Main", "BGNetCore", "HMUI", "HMLib"
    };

    private static readonly Dictionary<string, Type?> TypeCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo?> MethodCache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, FieldInfo?> FieldCache = new(StringComparer.Ordinal);
    private static readonly UnityEngine.Object[] EmptyObjects = Array.Empty<UnityEngine.Object>();
    private static int _loadedSceneObjectCacheFrame = -1;
    private static readonly Dictionary<string, UnityEngine.Object[]> LoadedSceneObjectCache = new(StringComparer.Ordinal);

    internal static Type? ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            return null;

        if (TypeCache.TryGetValue(typeName, out var cached))
            return cached;

        var found = typeof(CutScoreBuffer).Assembly.GetType(typeName, throwOnError: false);
        if (found == null)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (!AssemblyNames.Contains(asm.GetName().Name ?? ""))
                    continue;
                found = asm.GetType(typeName, throwOnError: false);
                if (found != null)
                    break;
            }
        }

        TypeCache[typeName] = found;
        return found;
    }

    internal static MethodInfo? GetParameterlessInstanceMethod(Type? type, string methodName)
    {
        if (type == null || string.IsNullOrEmpty(methodName))
            return null;

        var key = type.AssemblyQualifiedName + "|" + methodName;
        if (MethodCache.TryGetValue(key, out var cached))
            return cached;

        var method = type.GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        MethodCache[key] = method;
        return method;
    }

    internal static object? FindFirstActiveObject(Type? type) =>
        FindBestActiveObject(type, requireEnabled: true);

    internal static object? FindBestActiveObject(Type? type, bool requireEnabled = false)
    {
        if (type == null || !typeof(UnityEngine.Object).IsAssignableFrom(type))
            return null;

        var all = Resources.FindObjectsOfTypeAll(type);
        if (all == null || all.Length == 0)
            return null;

        object? best = null;
        var bestScore = -1;

        foreach (var obj in all)
        {
            if (obj == null)
                continue;

            var go = obj is Component component ? component.gameObject : obj as GameObject;
            if (go == null || !go.activeInHierarchy)
                continue;

            if (!go.scene.IsValid() || !go.scene.isLoaded)
                continue;

            var score = 1;
            if (obj is Behaviour behaviour && behaviour.isActiveAndEnabled)
                score += 4;

            if (score > bestScore)
            {
                bestScore = score;
                best = obj;
            }
        }

        if (best == null)
            return null;

        if (requireEnabled && best is Behaviour required && !required.isActiveAndEnabled)
            return null;

        return best;
    }

    internal static UnityEngine.Object[] FindAllInLoadedScenes(Type? type, bool requireHierarchy = false)
    {
        if (type == null || !typeof(UnityEngine.Object).IsAssignableFrom(type))
            return EmptyObjects;

        var frame = Time.frameCount;
        var cacheKey = (type.AssemblyQualifiedName ?? type.FullName ?? type.Name) + "|" + requireHierarchy;
        if (_loadedSceneObjectCacheFrame == frame
            && LoadedSceneObjectCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var all = Resources.FindObjectsOfTypeAll(type);
        if (all == null || all.Length == 0)
            return EmptyObjects;

        var count = 0;
        foreach (var obj in all)
        {
            if (IsLoadedSceneObject(obj, requireHierarchy))
                count++;
        }

        if (count == 0)
        {
            LoadedSceneObjectCache[cacheKey] = EmptyObjects;
            _loadedSceneObjectCacheFrame = frame;
            return EmptyObjects;
        }

        var filtered = new UnityEngine.Object[count];
        var j = 0;
        foreach (var obj in all)
        {
            if (IsLoadedSceneObject(obj, requireHierarchy))
                filtered[j++] = obj;
        }

        LoadedSceneObjectCache[cacheKey] = filtered;
        _loadedSceneObjectCacheFrame = frame;
        return filtered;
    }

    private static bool IsLoadedSceneObject(UnityEngine.Object? obj, bool requireHierarchy)
    {
        if (obj == null)
            return false;

        var go = obj is Component component ? component.gameObject : obj as GameObject;
        if (go == null)
            return false;

        if (requireHierarchy && !go.activeInHierarchy)
            return false;

        var scene = go.scene;
        return scene.IsValid() && scene.isLoaded;
    }

    internal static object? GetFlowCoordinatorField(Type? flowCoordinatorType, string fieldName)
    {
        var fc = GetBestFlowCoordinator(flowCoordinatorType, fieldName);
        return fc == null ? null : GetInstanceField(fc, fieldName);
    }

    internal static object? GetBestFlowCoordinator(Type? flowCoordinatorType, string? linkedViewFieldName = null)
    {
        var all = FindAllInLoadedScenes(flowCoordinatorType);
        if (all.Length == 0)
            return null;

        object? best = null;
        var bestScore = -1;

        foreach (var obj in all)
        {
            var score = ScoreFlowCoordinator(obj, linkedViewFieldName);
            if (score > bestScore)
            {
                bestScore = score;
                best = obj;
            }
        }

        return bestScore >= 0 ? best : null;
    }

    private static int ScoreFlowCoordinator(UnityEngine.Object? flowCoordinator, string? linkedViewFieldName)
    {
        if (flowCoordinator == null)
            return -1;

        var score = 0;
        if (flowCoordinator is Behaviour behaviour && behaviour.isActiveAndEnabled)
            score += 4;

        if (!string.IsNullOrEmpty(linkedViewFieldName))
        {
            var linked = GetInstanceField(flowCoordinator, linkedViewFieldName!);
            if (linked is Component component && component.gameObject.activeInHierarchy)
                score += 16;
        }

        if (flowCoordinator is Component fcComponent && fcComponent.gameObject.activeInHierarchy)
            score += 2;

        return score;
    }

    internal static bool TryInvokeParameterless(object? target, string methodName)
    {
        if (target == null)
            return false;

        var method = GetParameterlessInstanceMethod(target.GetType(), methodName);
        if (method == null)
            return false;

        try
        {
            method.Invoke(target, null);
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static bool TryInvoke(object? target, string methodName, params object?[] args)
    {
        if (target == null)
            return false;
        return TryInvokeOnType(target.GetType(), target, methodName, args);
    }

    internal static bool TryInvokeOnType(Type? type, object? target, string methodName, params object?[] args)
    {
        if (type == null || target == null)
            return false;

        foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length != args.Length)
                continue;

            var invokeArgs = (object?[])args.Clone();
            var match = true;
            for (var p = 0; p < parameters.Length; p++)
            {
                if (invokeArgs[p] == null)
                    continue;

                var paramType = parameters[p].ParameterType;
                var argType = invokeArgs[p]!.GetType();
                if (paramType.IsEnum)
                {
                    if (!paramType.IsInstanceOfType(invokeArgs[p]))
                        invokeArgs[p] = Enum.ToObject(paramType, invokeArgs[p]!);
                    continue;
                }

                if (!paramType.IsAssignableFrom(argType))
                {
                    match = false;
                    break;
                }
            }

            if (!match)
                continue;

            try
            {
                method.Invoke(target, invokeArgs);
                return true;
            }
            catch
            {
            }
        }

        return false;
    }

    internal static Type? ResolveNestedEnum(string declaringTypeName, string nestedTypeName)
    {
        return ResolveType(declaringTypeName)?.GetNestedType(nestedTypeName, BindingFlags.Public | BindingFlags.NonPublic);
    }

    internal static object? GetInstanceField(object? target, string fieldName)
    {
        if (target == null || string.IsNullOrEmpty(fieldName))
            return null;

        var type = target.GetType();
        var key = type.AssemblyQualifiedName + "|" + fieldName;
        if (!FieldCache.TryGetValue(key, out var field))
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            FieldCache[key] = field;
        }

        return field?.GetValue(target);
    }
}
