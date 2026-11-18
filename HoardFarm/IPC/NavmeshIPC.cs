﻿using Dalamud.Plugin.Ipc;
using ECommons;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace HoardFarm.IPC;

[SuppressMessage("ReSharper", "InconsistentNaming")]
public static class NavmeshIPC
{
    internal static readonly string Name = "vnavmesh";
    private static ICallGateSubscriber<bool>? _navIsReady;
    private static ICallGateSubscriber<object>? _navPathfindCancelAll;

    private static ICallGateSubscriber<object>? _pathStop;
    private static ICallGateSubscriber<bool>? _pathIsRunning;
    private static ICallGateSubscriber<bool>? _pathGetAlignCamera;
    private static ICallGateSubscriber<bool, object>? _pathSetAlignCamera;

    private static ICallGateSubscriber<Vector3, bool, bool>? _pathfindAndMoveTo;
    private static ICallGateSubscriber<bool>? _pathfindInProgress;

    // 只建訂閱端、不需要 vnavmesh 已載入；呼叫時才由 Execute 判斷它在不在。
    internal static void Init()
    {
        try
        {
            _navIsReady = PluginInterface.GetIpcSubscriber<bool>($"{Name}.Nav.IsReady");
            _navPathfindCancelAll = PluginInterface.GetIpcSubscriber<object>($"{Name}.Nav.PathfindCancelAll");

            _pathStop = PluginInterface.GetIpcSubscriber<object>($"{Name}.Path.Stop");
            _pathIsRunning = PluginInterface.GetIpcSubscriber<bool>($"{Name}.Path.IsRunning");
            _pathGetAlignCamera = PluginInterface.GetIpcSubscriber<bool>($"{Name}.Path.GetAlignCamera");
            _pathSetAlignCamera = PluginInterface.GetIpcSubscriber<bool, object>($"{Name}.Path.SetAlignCamera");

            _pathfindAndMoveTo = PluginInterface.GetIpcSubscriber<Vector3, bool, bool>($"{Name}.SimpleMove.PathfindAndMoveTo");
            _pathfindInProgress = PluginInterface.GetIpcSubscriber<bool>($"{Name}.SimpleMove.PathfindInProgress");
        }
        catch (Exception ex) { ex.Log(); }
    }

    internal static T? Execute<T>(Func<T> func)
    {
        if (PluginInstalled(Name))
        {
            try
            {
                if (func != null)
                    return func();
            }
            catch (Exception ex) { ex.Log(); }
        }

        return default;
    }

    internal static void Execute(Action action)
    {
        if (PluginInstalled(Name))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex) { ex.Log(); }
        }
    }

    internal static void Execute<T>(Action<T> action, T param)
    {
        if (PluginInstalled(Name))
        {
            try
            {
                action?.Invoke(param);
            }
            catch (Exception ex) { ex.Log(); }
        }
    }

    internal static bool NavIsReady() => Execute(() => _navIsReady!.InvokeFunc());
    internal static void PathfindCancelAll() => Execute(_navPathfindCancelAll!.InvokeAction);

    internal static void PathStop() => Execute(_pathStop!.InvokeAction);
    internal static bool PathIsRunning() => Execute(() => _pathIsRunning!.InvokeFunc());
    internal static bool PathGetAlignCamera() => Execute(() => _pathGetAlignCamera!.InvokeFunc());
    internal static void PathSetAlignCamera(bool value) => Execute(_pathSetAlignCamera!.InvokeAction, value);
    // 只記「關→開是我們切的」；還原時已被別人關掉就不動。只在框架執行緒呼叫。
    private static bool alignCameraEnabledByUs;

    internal static void EnsureAlignCamera()
    {
        if (!PluginInstalled(Name) || PathGetAlignCamera()) return;
        PathSetAlignCamera(true);
        if (!alignCameraEnabledByUs)
            PluginLog.Information("[HoardFarm] vnavmesh 對齊鏡頭原本是關的，自動流程期間打開，停止時關回去");
        alignCameraEnabledByUs = true;
    }

    internal static void RestoreAlignCamera()
    {
        if (!alignCameraEnabledByUs) return;
        alignCameraEnabledByUs = false;
        if (!PluginInstalled(Name) || !PathGetAlignCamera()) return;
        PathSetAlignCamera(false);
        PluginLog.Information("[HoardFarm] 已把 vnavmesh 對齊鏡頭關回開始前的狀態");
    }

    internal static void PathfindAndMoveTo(Vector3 pos, bool fly) => Execute(() => _pathfindAndMoveTo!.InvokeFunc(pos, fly));
    internal static bool PathfindInProgress() => Execute(() => _pathfindInProgress!.InvokeFunc());
}
