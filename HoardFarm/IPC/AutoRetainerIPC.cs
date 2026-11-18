using System;
using System.Diagnostics.CodeAnalysis;
using ECommons.EzIpcManager;
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace HoardFarm.IPC;

[SuppressMessage("ReSharper", "InconsistentNaming")]
[SuppressMessage("ReSharper", "UnassignedReadonlyField")]
public class AutoRetainerIPC
{
    private const string InternalName = "AutoRetainer";

    public AutoRetainerIPC() => EzIPC.Init(this, "AutoRetainer.PluginState");

    // 刻意不掛 SafeWrapper：失敗時它回 default，分不出「問不到」與真值。一律經下面的 Try* 呼叫。
    [EzIPC] public readonly Func<int> GetInventoryFreeSlotCount;

    private bool failureLogged;

    /// <summary>AutoRetainer 已載入。每次現讀 IsLoaded，不靠卸載事件快取；框架執行緒呼叫。</summary>
    public static bool Loaded()
    {
        foreach (var plugin in PluginInterface.InstalledPlugins)
        {
            if (plugin.IsLoaded && plugin.InternalName == InternalName)
                return true;
        }

        return false;
    }

    /// <summary>null＝問不到（沒載入或 IPC 失敗）；不擲例外。</summary>
    public int? TryGetInventoryFreeSlotCount() => TryInvoke(GetInventoryFreeSlotCount);

    private T? TryInvoke<T>(Func<T>? func) where T : struct
    {
        if (func == null || !Loaded()) return null;
        try
        {
            return func();
        }
        catch (Exception ex)
        {
            if (!failureLogged)
            {
                failureLogged = true;
                PluginLog.Information($"[HoardFarm] 呼叫 AutoRetainer 失敗，當作問不到：{ex.GetType().Name}: {ex.Message}");
            }

            return null;
        }
    }
}
