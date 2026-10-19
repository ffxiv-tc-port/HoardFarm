using System;
using System.Collections.Generic;
using Dalamud.Game.ClientState.Conditions;
using ECommons.DalamudServices;
using HoardFarm.Utils;

namespace HoardFarm;

public static class YesAlreadyManager
{
    
    private static bool Reenable = false;
    private static HashSet<string>? Data = null;
    private static long disabledAt;
    private static bool refreshing;

    public static void GetData()
    {
        if (Data != null) return;
        if (Svc.PluginInterface.TryGetData<HashSet<string>>("YesAlready.StopRequests", out var data))
        {
            Data = data;
        }
    }

    public static void DisableIfNeeded()
    {
        GetData();
        if (Data != null)
        {
            PluginLog.Information("Disabling Yes Already (new)");
            Data.Add(Svc.PluginInterface.InternalName);
            Reenable = true;
            disabledAt = Environment.TickCount64;
        }
    }

    public static void EnableIfNeeded()
    {
        if (Reenable)
        {
            GetData();
            if (Data != null)
            {
                PluginLog.Information("Enabling Yes Already (new)");
                Data.Remove(Svc.PluginInterface.InternalName);
                Reenable = false;
            }
        }
    }

    /// <summary>卸載時呼叫：拿掉自己的阻擋登記並放掉共享資料的參考。</summary>
    public static void Release()
    {
        EnableIfNeeded();
        if (Data != null)
        {
            Svc.PluginInterface.RelinquishData("YesAlready.StopRequests");
            Data = null;
        }
    }

    public static bool IsEnabled()
    {
        GetData();
        if (Data != null)
        {
            return !Data.Contains(Svc.PluginInterface.InternalName);
        }
        return false;
    }

    // 範圍＝整段自動流程（按開始到停止／跑完），不是任務佇列忙碌期；流程結束的那一幀起就放開。
    public static void Tick()
    {
        var flowActive = TaskManager.IsBusy || HoardService.IsRunning;
        var loading = flowActive && (Reenable || refreshing)
                      && (Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51]);
        var heldMs = Reenable ? Environment.TickCount64 - disabledAt : 0;
        var wasRefreshing = refreshing;
        if (FlowGates.ShouldSuppressYesAlready(flowActive, Reenable, heldMs, loading, ref refreshing))
        {
            if (IsEnabled())
            {
                DisableIfNeeded();
            }
        }
        else
        {
            if (refreshing && !wasRefreshing)
                PluginLog.Information($"[HoardFarm] 已連續壓住 YesAlready {heldMs / 60_000} 分鐘，趁讀取畫面放開一次讓它的看門狗重新計時");
            if (Reenable)
            {
                EnableIfNeeded();
            }
        }
    }

    public static bool? WaitForYesAlreadyDisabledTask()
    {
        return !IsEnabled();
    }
}
