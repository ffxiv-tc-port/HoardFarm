using System;
using System.Linq;
using ECommons.DalamudServices;
using HoardFarm.IPC;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace HoardFarm.Service;

/// <summary>只讀 AutoRetainer 的僱員資料與背包空格，判斷「僱員回來了」；停下與提示由主迴圈做。</summary>
public class RetainerService
{
    private readonly AutoRetainerIPC autoRetainerIcp = new();

    // AutoRetainer 沒有這個角色的資料或 IPC 失敗時回 false（＝不停下），不讓例外每秒打斷主迴圈。
    public static bool CheckRetainersDone(bool all = true)
    {
        try
        {
            var data = RetainerApi.GetOfflineCharacterData(Svc.PlayerState.ContentId)?.RetainerData;
            if (data == null || data.Count == 0) return false;
            return all ? data.All(e => CheckIsDone(e.VentureEndsAt)) : data.Any(e => CheckIsDone(e.VentureEndsAt));
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"[HoardFarm] 讀不到 AutoRetainer 的僱員資料，這次當作僱員還沒回來：{ex.Message}");
            return false;
        }
    }

    private static bool CheckIsDone(long time)
    {
        return time+10 <= CSFramework.GetServerTime();
    }

    public bool CanRunRetainer()
    {
        return autoRetainerIcp.TryGetInventoryFreeSlotCount() is > 4 &&
               Svc.Objects.LocalPlayer != null &&
               Svc.Objects.LocalPlayer.HomeWorld.RowId == Svc.Objects.LocalPlayer.CurrentWorld.RowId;
    }

    private long uiStatusAt;
    private bool uiReady;
    private bool uiCanRun;

    /// <summary>給主視窗每幀讀；AutoRetainer 狀態每秒最多查一次（它沒載入時每次查詢都是一次例外）。框架執行緒呼叫。</summary>
    public (bool Ready, bool CanRun) GetUiStatus()
    {
        var now = Environment.TickCount64;
        if (uiStatusAt == 0 || now - uiStatusAt >= 1000)
        {
            uiStatusAt = now;
            uiReady = AutoRetainerIPC.Loaded() && RetainerApi.Ready;
            uiCanRun = uiReady && CanRunRetainer();
        }

        return (uiReady, uiCanRun);
    }

}
