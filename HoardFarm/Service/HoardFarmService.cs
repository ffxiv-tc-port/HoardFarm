using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using HoardFarm.Data;
using HoardFarm.IPC;
using HoardFarm.Model;
using HoardFarm.Tasks;
using HoardFarm.Tasks.TaskGroups;
using HoardFarm.Utils;
using Lumina.Excel.Sheets;

namespace HoardFarm.Service;

public class HoardFarmService : IDisposable
{
    private readonly string hoardFoundMessage;
    private readonly string noHoardMessage;
    private readonly Dictionary<uint, MapObject> objectPositions = new();
    private readonly string senseHoardMessage;
    private readonly List<uint> visitedObjectIds = [];
    public bool FinishRun;
    private bool hoardAvailable;

    private bool hoardFound;
    // volatile：IsRunning 會從 IPC（任何執行緒）讀；寫入仍只在 framework 執行緒。
    private volatile bool hoardModeActive;
    public string HoardModeError = "";

    public string HoardModeStatus = "";
    private Vector3 hoardPosition = Vector3.Zero;
    private bool intuitionUsed;
    // 每層只決定一次「找到寶藏後用魔石還是隱形」；魔石確認用掉後這一層移動時不再補隱形。
    private bool hoardCoverChosen;
    private bool magiciteCoverUsed;
    private DateTime magiciteTaskUntil = DateTime.MinValue;
    // 已排了退出（含退出前的固定等待）；之後到換層為止，戰鬥中不再自動用魔石。
    private bool leaveDecided;
    private DateTime lastTick = DateTime.Now;
    private bool movingToHoard;

    private volatile bool running;
    // 按開始時人已經在天之御柱裡＝那一趟是使用者自己進的；離開天之御柱之前，出錯也不替他放棄任務。
    private bool startedInUserRun;
    private DateTime runStarted;
    private bool safetyUsed;
    private bool searchMode;
    public int SessionFoundHoards;
    public int SessionRuns;
    public int SessionTime;

    // 由 IPC（任何執行緒）寫入、framework 執行緒在下一幀取走；null＝沒有待處理的外部停止。
    private string? pendingExternalStop;

    public HoardFarmService()
    {
        hoardFoundMessage = LogMessageText(HoardFoundLogMessageId);
        senseHoardMessage = LogMessageText(SenseHoardLogMessageId);
        noHoardMessage = LogMessageText(NoHoardLogMessageId);

        ClientState.TerritoryChanged += OnMapChange;
        ClientState.Logout += OnLogout;

        Framework.Update += OnTick;
    }

    // 列不存在時回空字串；空字串永遠不會被當成命中（見 OnChatMessage）。
    private static string LogMessageText(uint rowId)
    {
        var row = DataManager.GetExcelSheet<LogMessage>().GetRowOrDefault(rowId);
        return row == null ? "" : NormalizeGameText(row.Value.Text.ToDalamudString().GetText());
    }

    public bool HoardMode
    {
        get => hoardModeActive;
        set
        {
            // 設成原本的值不重來：重複開始會把聊天處理器掛兩次，停止時只拆得掉一個。
            if (value == hoardModeActive) return;
            hoardModeActive = value;
            if (hoardModeActive)
                EnableFarm();
            else
                DisableFarm();
        }
    }

    /// <summary>
    /// 自動流程是否在跑（按開始後到停止／跑完為止，含進出天之御柱之間的空檔）。
    /// 任何執行緒都可以讀：只讀兩個 volatile 欄位，不碰任務佇列與遊戲物件。
    /// </summary>
    public bool IsRunning => hoardModeActive || running;

    public void Dispose()
    {
        if (hoardModeActive || running)
            StopAll(Strings.HoardFarm_StopReason_Unload);

        ChatGui.ChatMessage -= OnChatMessage;
        ClientState.TerritoryChanged -= OnMapChange;
        ClientState.Logout -= OnLogout;
        Framework.Update -= OnTick;
    }

    /// <summary>任何執行緒都可以呼叫；在 framework 執行緒上當場停，否則排到下一幀。</summary>
    public void RequestStopAll(string? source)
    {
        var label = string.IsNullOrWhiteSpace(source) ? "?" : source.Trim();
        if (Framework.IsInFrameworkUpdateThread)
        {
            StopAll(label);
            return;
        }

        Interlocked.Exchange(ref pendingExternalStop, label);
    }

    /// <summary>立刻停下所有動作（不等這一趟跑完）。只在 framework 執行緒呼叫。</summary>
    public void StopAll(string reason)
    {
        var wasRunning = hoardModeActive || running;
        hoardModeActive = false;
        DisableFarm();
        HoardModeStatus = wasRunning ? string.Format(Strings.HoardFarm_Status_Stopped, reason) : HoardModeStatus;
        PluginLog.Information($"[HoardFarm] 全部停止（{reason}）：{(wasRunning ? "原本在跑，已中止任務佇列與導航" : "原本就沒在跑")}");
    }

    private void DisableFarm()
    {
        var wasRunning = running;
        running = false;
        TaskManager.Abort();
        HoardModeStatus = "";
        ChatGui.ChatMessage -= OnChatMessage;
        Reset();

        // 沒在跑時不碰 vnavmesh：那時的導航是別的插件或使用者自己的。
        if (wasRunning)
        {
            NavmeshIPC.PathfindCancelAll();
            NavmeshIPC.PathStop();
        }

        if (RetainerScv.Running) RetainerScv.FinishProcess();
        EnableIfNeeded();
    }

    private void OnLogout(int type, int code)
    {
        if (hoardModeActive || running)
            StopAll(Strings.HoardFarm_StopReason_Logout);
    }

    private void EnableFarm()
    {
        Reset();
        SessionTime = 0;
        SessionRuns = 0;
        SessionFoundHoards = 0;
        startedInUserRun = InHoHDuty;
        running = true;
        HoardModeStatus = Strings.HoardFarm_Status_Running;
        HoardModeError = "";
        ChatGui.ChatMessage -= OnChatMessage;
        ChatGui.ChatMessage += OnChatMessage;
    }

    private void Reset()
    {
        Config.Save();
        intuitionUsed = false;
        hoardCoverChosen = false;
        magiciteCoverUsed = false;
        magiciteTaskUntil = DateTime.MinValue;
        leaveDecided = false;
        hoardFound = false;
        hoardPosition = Vector3.Zero;
        movingToHoard = false;
        hoardAvailable = false;
        searchMode = false;
        visitedObjectIds.Clear();
        FinishRun = false;
        safetyUsed = false;
        objectPositions.Clear();
        runStarted = DateTime.Now;
    }

    private bool SearchLogic()
    {
        HoardModeStatus = Strings.HoardFarm_Status_Searching;

        if (!TaskManager.IsBusy)
        {
            if (!objectPositions.Where(e => !visitedObjectIds.Contains(e.Value.ObjectId))
                                .Where(e => ChestIDs.Contains(e.Value.DataId))
                                .OrderBy(e => e.Value.Position.Distance(Player.Position))
                                .Select(e => e.Value)
                                .TryGetFirst(out var next))
            {
                if (!objectPositions.Where(e => !visitedObjectIds.Contains(e.Value.ObjectId))
                                    .OrderBy(e => e.Value.Position.Distance(Player.Position))
                                    .Select(e => e.Value)
                                    .TryGetFirst(out next))
                {
                    // We should never reach here normally .. but "never" is still a chance > 0% ;)
                    LeaveDuty(Strings.HoardFarm_Status_Unreachable);
                    return true;
                }
            }

            visitedObjectIds.Add(next!.ObjectId);
            Enqueue(new PathfindTask(next.Position, true), 60 * 1000,
                    string.Format(Strings.HoardFarm_Status_SearchingObject, next.ObjectId));
        }

        FindHoardPosition();
        if (hoardPosition != Vector3.Zero)
        {
            NavmeshIPC.PathStop();
            TaskManager.Abort();
            return true;
        }

        return false;
    }

    private void OnTick(IFramework framework)
    {
        var external = Interlocked.Exchange(ref pendingExternalStop, null);
        if (external != null)
            StopAll(external);

        if (!running || DateTime.Now - lastTick < TimeSpan.FromSeconds(1))
            return;

        lastTick = DateTime.Now;

        // Retainer do not increase runtime
        if (RetainerScv.Running)
        {
            HoardModeStatus = Strings.HoardFarm_Status_RetainerRunning;
            return;
        }

        SessionTime++;
        Config.OverallTime++;

        if (!NavmeshIPC.NavIsReady())
        {
            HoardModeStatus = Strings.HoardFarm_Status_WaitingNavmesh;
            return;
        }

        UpdateObjectPositions();
        SafetyChecks();

        // searchMode 只在進天之御柱時重置；逾時或死亡退出後回到紅玉海時它還是 true。
        if (searchMode && InHoH && hoardPosition == Vector3.Zero)
        {
            if (!SearchLogic())
                return;
        }

        if (!TaskManager.IsBusy && hoardModeActive)
        {
            if (CheckDone() && !FinishRun)
            {
                FinishRun = true;
                return;
            }

            if (Player.Territory == HoHMapId1)
            {
                Error(Strings.HoardFarm_Status_Error_Unprepared);
                return;
            }

            // 人在紅玉海但離九星太遠時也要走過去，否則沒有任何分支會接手。
            if (!InHoH && NotBusy() && !KyuseiInteractable())
            {
                HoardModeStatus = Strings.HoardFarm_Status_MoveToHoH;
                Enqueue(new MoveToHoHTask());
                EnqueueWait(1000);
            }

            if (InRubySea && NotBusy() && KyuseiInteractable())
            {
                if (FinishRun)
                {
                    HoardMode = false;
                    HoardModeStatus = Strings.HoardFarm_Status_Finished;
                    return;
                }

                if (CheckRetainer())
                {
                    // Do retainers first
                    return;
                }

                // 已經在任務搜尋器排別的任務時不進場，等它排完或取消。
                if (Svc.Condition[ConditionFlag.InDutyQueue])
                {
                    HoardModeStatus = Strings.HoardFarm_Status_InDutyQueue;
                    return;
                }

                HoardModeStatus = Strings.HoardFarm_Status_EnteringHoH;
                Enqueue(new EnterHeavenOnHigh());
            }

            if (InHoH && NotBusy())
            {
                if (!intuitionUsed)
                {
                    if (!CheckMinimalSetup())
                    {
                        Error(
                            Strings.HoardFarm_Status_Error_MinimalSetup);
                        return;
                    }

                    if (CanUsePomander(Pomander.Intuition))
                    {
                        Enqueue(new UsePomanderTask(Pomander.Intuition), "Use Intuition");
                        intuitionUsed = true;
                    }
                }
                else
                {
                    if (hoardAvailable)
                    {
                        FindHoardPosition();

                        if (hoardPosition != Vector3.Zero)
                        {
                            if (!movingToHoard)
                            {
                                // if (!Concealment)
                                // {
                                //     Enqueue(new UsePomanderTask(Pomander.Concealment, false), "Use Concealment");
                                // }
                                // 魔石失敗時不必另外排隱形：移動中的 SafetyChecks 會照舊補。
                                if (TryStartHoardMagicite(false))
                                    return;
                                Enqueue(new PathfindTask(hoardPosition, true, 1.5f), 60 * 1000, "Move to Hoard");
                                movingToHoard = true;
                                HoardModeStatus = Strings.HoardFarm_Status_MoveToHoard;
                            }
                        }
                        else
                        {
                            if (Config.HoardFarmMode == 1)
                            {
                                LeaveDuty(Strings.HoardFarm_Status_Leaving, Config.LeaveDelayAfterIntuition, "安全模式：看不到寶藏位置、不搜尋");
                                return;
                            }

                            if (!hoardFound)
                            {
                                if (!TryStartHoardMagicite(true))
                                    Enqueue(new UsePomanderTask(Pomander.Concealment), "Use Concealment");
                                searchMode = true;
                                return;
                            }
                        }

                        if (hoardFound)
                        {
                            LeaveDuty(Strings.HoardFarm_Status_Leaving, Config.LeaveDelayAfterHoard, "挖到寶藏");
                        }
                    }
                    else
                        LeaveDuty(Strings.HoardFarm_Status_Leaving, Config.LeaveDelayAfterIntuition, "這一層沒有寶藏");
                }
            }
        }
    }

    private void SafetyChecks()
    {
        if (InHoH && intuitionUsed)
        {
            // 退出那一串還在佇列裡就不再中止重排；跑完仍在層內（退出被擋）時才再送一次。
            if (DateTime.Now.Subtract(runStarted).TotalSeconds > 130 && !Svc.Condition[ConditionFlag.InCombat]
                && !(leaveDecided && TaskManager.IsBusy))
            {
                TaskManager.Abort();
                NavmeshIPC.PathStop();
                LeaveDuty(Strings.HoardFarm_Status_Timeout);
                return;
            }

            if (IsMoving())
            {
                if (!Concealment)
                {
                    // 魔石掩護過的這一層不補隱形；咒印解除仍只在「沒有隱形魔陶器」時才用。
                    if (CanUsePomander(Pomander.Concealment))
                    {
                        if (!magiciteCoverUsed && EzThrottler.Check("Concealment"))
                        {
                            EzThrottler.Throttle("Concealment", 2000);
                            new UsePomanderTask(Pomander.Concealment, false).Run();
                            return; // start next iteration
                        }
                    }
                    else if (CanUsePomander(Pomander.Safety) && !safetyUsed && EzThrottler.Check("Concealment"))
                    {
                        if (EzThrottler.Check("Safety"))
                        {
                            EzThrottler.Throttle("Safety", 2000);
                            new UsePomanderTask(Pomander.Safety, false).Run();
                            safetyUsed = true;
                            return; // start next iteration
                        }
                    }
                }
            }

            if (Svc.Condition[ConditionFlag.InCombat])
            {
                if (DateTime.Now >= magiciteTaskUntil && CanUseMagicite() && EzThrottler.Check("Magicite"))
                {
                    EzThrottler.Throttle("Magicite", 6000);
                    if (leaveDecided)
                        PluginLog.Information("[HoardFarm] 已決定退出，戰鬥中不自動用魔石");
                    else
                        new UseMagiciteTask().Run();
                }
            }

            // 同逾時分支：倒地期間每秒都會走到這裡，佇列裡已有退出就不再疊一組。
            if (Svc.Condition[ConditionFlag.Unconscious] && !(leaveDecided && TaskManager.IsBusy))
                LeaveDuty(Strings.HoardFarm_Status_PlayerDied);
        }
    }

    /// <summary>
    /// 找到寶藏後、要用隱形之前呼叫；每層只決定一次。回 true＝已排魔石任務，這一幀不要再排別的。
    /// concealmentOnFail＝魔石沒用出去時由任務自己補排隱形（搜尋模式用；直接走向寶藏時交給 SafetyChecks）。
    /// </summary>
    private unsafe bool TryStartHoardMagicite(bool concealmentOnFail)
    {
        if (hoardCoverChosen) return false;
        hoardCoverChosen = true;

        var dd = UseHoardMagiciteTask.DeepDungeon();
        var blocked = UseHoardMagiciteTask.BlockReason(dd);
        if (blocked != null)
        {
            PluginLog.Information($"[HoardFarm] 找到寶藏，不用魔石（{blocked}），用隱形");
            return false;
        }

        var slot = MagicitePicker.PickSlot(dd->Magicite)!.Value;
        PluginLog.Information(
            $"[HoardFarm] 找到寶藏，先用魔石「{UseHoardMagiciteTask.MagiciteName(dd->Magicite[slot])}」而不用隱形（三格＝[{dd->Magicite[0]}, {dd->Magicite[1]}, {dd->Magicite[2]}]）");
        HoardModeStatus = Strings.HoardFarm_Status_UsingMagicite;
        magiciteTaskUntil = DateTime.Now.AddSeconds(45);
        var floorRun = runStarted;
        Enqueue(new UseHoardMagiciteTask(used =>
        {
            if (runStarted != floorRun) return;
            magiciteCoverUsed = used;
            magiciteTaskUntil = DateTime.MinValue;
        }, concealmentOnFail), 45 * 1000, "Use Magicite");
        return true;
    }

    private void UpdateObjectPositions()
    {
        foreach (var gameObject in ObjectTable)
            objectPositions.TryAdd(gameObject.EntityId,
                                   new MapObject(gameObject.EntityId, gameObject.BaseId, gameObject.Position));
    }

    private bool CheckRetainer()
    {
        if (Config.DoRetainers
            && RetainerApi.Ready
            && RetainerService.CheckRetainersDone(Config.RetainerMode == 1)
            && RetainerScv.CanRunRetainer())
        {
            RetainerScv.StartProcess();
            return true;
        }

        return false;
    }

    private bool CheckMinimalSetup()
    {
        if (!CanUsePomander(Pomander.Intuition)) return false;
        if (CanUsePomander(Pomander.Concealment)) return true;

        return CanUsePomander(Pomander.Safety) && CanUseMagicite();
    }

    /// <param name="delaySeconds">退出前固定等待秒數，0＝立刻。等待是佇列裡的一個任務，停止／急停的 Abort 會連它一起清掉。</param>
    /// <param name="why">只用於等待那一行 log。</param>
    private void LeaveDuty(string message, int delaySeconds = 0, string? why = null)
    {
        HoardModeStatus = message;
        if (!InHoHDuty) return;
        // 趟數只在這一層第一次決定退出時算；退出被擋（例如戰鬥中）而重送時不再加。
        if (!leaveDecided)
        {
            SessionRuns++;
            Config.OverallRuns++;
        }

        leaveDecided = true;

        delaySeconds = Math.Clamp(delaySeconds, 0, Configuration.LeaveDelayMax);
        if (delaySeconds > 0)
        {
            PluginLog.Information($"[HoardFarm] {why ?? message}：退出前固定等 {delaySeconds} 秒");
            var until = DateTime.Now.AddSeconds(delaySeconds);
            Enqueue(() =>
            {
                var left = until - DateTime.Now;
                if (left <= TimeSpan.Zero) return true;
                HoardModeStatus = string.Format(Strings.HoardFarm_Status_LeavingIn, message, (int)Math.Ceiling(left.TotalSeconds));
                return false;
            }, (delaySeconds + 10) * 1000, "Wait before leaving");
        }

        Enqueue(new LeaveDutyTask());
    }

    private void Error(string message)
    {
        HoardModeStatus = Strings.HoardFarm_Status_Error;
        HoardModeError = message;
        if (startedInUserRun)
        {
            StopInUserRun(message);
            return;
        }

        FinishRun = true;
        leaveDecided = true;
        Enqueue(new LeaveDutyTask());
    }

    // 只停下並說明，不放棄任務：要不要退出由使用者自己決定。
    private void StopInUserRun(string message)
    {
        HoardModeError = message + "\n" + Strings.HoardFarm_Status_Error_NotLeaving;
        PluginLog.Information($"[HoardFarm] 按開始時人已在天之御柱（地區 {Player.Territory}），無法執行，停止且不退出這一趟：{message.ReplaceLineEndings(" ")}");
        // 先清 running：這一趟裡插件還沒送過導航，停止時不要取消使用者自己的 vnavmesh 路徑。
        running = false;
        HoardMode = false;
        HoardModeStatus = Strings.HoardFarm_Status_Error;
        ChatGui.Print($"[HoardFarm] {Strings.MainWindow_UnableToRun}{message.ReplaceLineEndings(" ")} {Strings.HoardFarm_Status_Error_NotLeaving}");
    }

    private void FindHoardPosition()
    {
        if (hoardPosition == Vector3.Zero &&
            ObjectTable.TryGetFirst(gameObject => gameObject.BaseId == AccursedHoardId, out var hoard))
            hoardPosition = hoard.Position;
    }

    private bool CheckDone()
    {
        switch (Config.StopAfterMode)
        {
            case 0 when SessionRuns >= Config.StopAfter:
            case 1 when SessionFoundHoards >= Config.StopAfter:
            case 2 when SessionTime >= Config.StopAfter * 60:
                return true;
            default:
                return false;
        }
    }

    private void OnMapChange(ushort territoryType)
    {
        if (territoryType is not (HoHMapId1 or HoHMapId11 or HoHMapId21))
            startedInUserRun = false;

        if (territoryType is HoHMapId11 or HoHMapId21)
        {
            Reset();
            HoardModeStatus = Strings.HoardFarm_Status_Waiting;
        }
    }

    private void OnChatMessage(
        XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // 沒在跑時一律不理：這裡會中止任務佇列並排「退出任務」，停止後殘留的處理器會把使用者自己的那一趟退掉。
        if (!running) return;
        var text = NormalizeGameText(message.TextValue);
        if (text.Length == 0) return;

        if (senseHoardMessage.Equals(text))
        {
            intuitionUsed = true;
            hoardAvailable = true;
            HoardModeStatus = Strings.HoardFarm_Status_HoardFound;
        }

        if (noHoardMessage.Equals(text))
        {
            intuitionUsed = true;
            hoardAvailable = false;
            TaskManager.Abort();
            LeaveDuty(Strings.HoardFarm_Status_NoHoard, Config.LeaveDelayAfterIntuition, "這一層沒有寶藏");
        }

        if (hoardFoundMessage.Equals(text))
        {
            hoardFound = true;
            SessionFoundHoards++;
            Config.OverallFoundHoards++;
            Achievements.Progress++;
            TaskManager.Abort();
            LeaveDuty(Strings.HoardFarm_Status_Done, Config.LeaveDelayAfterHoard, "挖到寶藏");
        }
    }

    private record MapObject(uint ObjectId, uint DataId, Vector3 Position);
}
