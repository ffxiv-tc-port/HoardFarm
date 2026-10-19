using System;
using System.Linq;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.InstanceContent;
using HoardFarm.Model;
using HoardFarm.Tasks.Base;
using HoardFarm.Utils;
using Lumina.Excel.Sheets;

namespace HoardFarm.Tasks;

/// <summary>
/// 找到寶藏後先用魔石。只在框架執行緒、由任務佇列執行；深宮導演每次重查，不跨幀保存指標。
/// 結果一律經 onDone 回報（true＝魔石已確認用掉）；這個任務自己在時限內收尾，不靠佇列逾時。
/// </summary>
public class UseHoardMagiciteTask(Action<bool> onDone, bool concealmentOnFail) : BaseTask
{
    // 送出後多久沒看到數量減少就算這一發沒成功；遊戲拒絕時不會有任何訊息。
    private static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(2.5);
    private const int MaxSends = 2;
    private static readonly TimeSpan ReadyWindow = TimeSpan.FromSeconds(6);
    // 召喚出來的蠻神要幾秒後才打到敵人；在那之前就走等於帶著還活著的怪走。
    private static readonly TimeSpan MinSettle = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan MaxSettle = TimeSpan.FromSeconds(15);

    private enum Phase { WaitReady, Sent, Settling }

    private Phase phase = Phase.WaitReady;
    private DateTime phaseStart = DateTime.MinValue;
    private DateTime sentAt;
    private int sends;
    private int countBefore;
    private byte sentKind;
    private bool finished;

    public override unsafe bool? Run()
    {
        if (finished) return true;
        var now = DateTime.Now;
        if (phaseStart == DateTime.MinValue) phaseStart = now;

        var dd = DeepDungeon();
        if (dd == null) return Finish(false, "讀不到深層迷宮狀態");

        switch (phase)
        {
            case Phase.WaitReady:
            {
                // 上一發晚到才生效時不再送第二發。
                if (sends > 0 && MagicitePicker.HeldCount(dd->Magicite) < countBefore)
                {
                    phase = Phase.Sent;
                    return false;
                }

                var blocked = BlockReason(dd);
                if (blocked != null) return Finish(false, blocked);

                var busy = BusyReason();
                if (busy != null)
                {
                    if (now - phaseStart > ReadyWindow)
                        return Finish(false, $"等了 {ReadyWindow.TotalSeconds:0} 秒仍無法行動（{busy}）");
                    return false;
                }

                var slot = MagicitePicker.PickSlot(dd->Magicite)!.Value;
                countBefore = MagicitePicker.HeldCount(dd->Magicite);
                sentKind = dd->Magicite[slot];
                sends++;
                PluginLog.Information(
                    $"[HoardFarm] 送出魔石：第 {slot + 1} 格「{MagiciteName(sentKind)}」（第 {sends}/{MaxSends} 次；三格＝[{dd->Magicite[0]}, {dd->Magicite[1]}, {dd->Magicite[2]}]）");
                dd->UseStone((uint)slot);
                sentAt = now;
                phase = Phase.Sent;
                return false;
            }

            case Phase.Sent:
            {
                var count = MagicitePicker.HeldCount(dd->Magicite);
                if (count < countBefore)
                {
                    PluginLog.Information(
                        $"[HoardFarm] 找到寶藏，用了「{MagiciteName(sentKind)}」（持有 {countBefore}→{count}）；等召喚結束再走");
                    phase = Phase.Settling;
                    return false;
                }

                if (now - sentAt < ConfirmWindow) return false;
                if (sends < MaxSends)
                {
                    phase = Phase.WaitReady;
                    phaseStart = now;
                    return false;
                }

                return Finish(false, $"送出 {MaxSends} 次數量都沒變");
            }

            case Phase.Settling:
            {
                var waited = now - sentAt;
                if (waited < MinSettle) return false;
                var busy = BusyReason();
                if (busy == null)
                    return Finish(true, $"送出後 {waited.TotalSeconds:0.0} 秒，角色可行動，開始走");
                if (waited >= MaxSettle)
                    return Finish(true, $"送出後 {waited.TotalSeconds:0.0} 秒仍{busy}，不再等，照樣開始走");
                return false;
            }
        }

        return Finish(false, "未知狀態");
    }

    private bool Finish(bool used, string detail)
    {
        finished = true;
        if (used)
        {
            PluginLog.Information($"[HoardFarm] 魔石完成：{detail}");
        }
        else
        {
            PluginLog.Information($"[HoardFarm] 找到寶藏，魔石沒用出去（{detail}），改用隱形");
            if (concealmentOnFail)
                EnqueueImmediate(new UsePomanderTask(Pomander.Concealment), "Use Concealment");
        }

        onDone(used);
        return true;
    }

    /// <summary>每次重查；解不出位址或不在深宮時回 null。</summary>
    public static unsafe InstanceContentDeepDungeon* DeepDungeon()
    {
        if (EventFramework.MemberFunctionPointers.GetInstanceContentDirector == null) return null;
        try
        {
            var ef = EventFramework.Instance();
            return ef == null ? null : ef->GetInstanceContentDeepDungeon();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>這一層不能或不該用魔石的理由；可以用時回 null。不含「暫時忙碌」，那個看 BusyReason。</summary>
    public static unsafe string? BlockReason(InstanceContentDeepDungeon* dd)
    {
        if (dd == null) return "讀不到深層迷宮狀態";
        if (InstanceContentDeepDungeon.MemberFunctionPointers.UseStone == null) return "遊戲函式對不上";
        if (dd->Floor % 10 == 0) return $"第 {dd->Floor} 層是頭目層";
        if (HasStatus(MagicitePenaltyStatusId)) return "本層禁用魔石";
        if (HasStatus(ItemPenaltyStatusId)) return "禁止使用道具";
        if (MagicitePicker.PickSlot(dd->Magicite) == null) return "身上沒有魔石";
        return null;
    }

    /// <summary>暫時不能行動的理由（動畫鎖、詠唱、過場、暈眩／睡眠）；可以行動時回 null。</summary>
    public static unsafe string? BusyReason()
    {
        var am = ActionManager.Instance();
        if (am != null && am->AnimationLock > 0) return "動畫鎖中";
        if (HasStatus(StunStatusId) || HasStatus(SleepStatusId)) return "暈眩或睡眠中";
        if (!NotBusy()) return "詠唱或忙碌中";
        return null;
    }

    public static string MagiciteName(byte kind)
    {
        var row = DataManager.GetExcelSheet<DeepDungeonMagicStone>().GetRowOrDefault(kind);
        var name = row?.Name.ToString();
        return string.IsNullOrEmpty(name) ? $"#{kind}" : name;
    }

    private static bool HasStatus(uint id) => Player.Status.Any(s => s.StatusId == id);
}
