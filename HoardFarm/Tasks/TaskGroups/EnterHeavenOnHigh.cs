using System;
using System.Collections;
using Dalamud.Game.ClientState.Conditions;
using ECommons.Automation;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HoardFarm.Tasks.Base;
using HoardFarm.Utils;

#pragma warning disable CS8974 // Converting method group to non-delegate type

namespace HoardFarm.Tasks.TaskGroups;

public class EnterHeavenOnHigh : IBaseTaskGroup
{
    // 「進入任務」確認框只在本組選完存檔後這段時間內按；別的排隊跳出來的同一種框一律不碰。
    private const long ConfirmWindowMs = 30_000;
    private long saveSelectedAt = -1;

    public unsafe ArrayList GetTaskList()
    {
        var result = new ArrayList();
        TryGetAddonByName<AtkUnitBase>("DeepDungeonMenu", out var menu);
        TryGetAddonByName<AtkUnitBase>("DeepDungeonSaveData", out var save);

        result.Add(WaitForYesAlreadyDisabledTask);
        if (menu == null && save == null)
        {
            result.Add(new InteractObjectTask(KyuseiDataId));
        }

        if ((menu == null && save == null) || (menu != null && save == null))
        {
            result.Add(EnterDuty);
        }

        result.Add(SelectSave);
        result.Add(new SelectYesnoTask(ConfirmPartyKoMessageId, KoPromptPassed));
        result.Add(ConfirmDuty);
        result.Add(WaitTillDutyReady);
        return result;
    }
    
    private static unsafe bool? EnterDuty()
    {
        if (TryGetAddonByName<AtkUnitBase>("DeepDungeonMenu", out var menu) && IsAddonReady(menu))
        {
            Callback.Fire(menu, true, 0);
            return true;
        }

        return false;
    }
    
    private unsafe bool? SelectSave()
    {
        if (TryGetAddonByName<AtkUnitBase>("DeepDungeonSaveData", out var menu) && IsAddonReady(menu))
        {
            Callback.Fire(menu, true, Config.HoardModeSave);
            saveSelectedAt = Environment.TickCount64;
            return true;
        }

        return false;
    }

    private unsafe bool? ConfirmDuty()
    {
        if (saveSelectedAt < 0 || Environment.TickCount64 - saveSelectedAt > ConfirmWindowMs)
            return true;

        if (FlowGates.DutyConfirmPassed(Svc.Condition[ConditionFlag.WaitingForDuty], LoadingScreen(), Svc.Condition[ConditionFlag.BoundByDuty]))
            return true;

        if (TryGetAddonByName<AtkUnitBase>("ContentsFinderConfirm", out var menu) && IsAddonReady(menu))
        {
            Callback.Fire(menu, true, 8);
            return true;
        }

        return false;
    }
    
    private static bool KoPromptPassed()
        => FlowGates.PartyKoPromptPassed(Svc.Condition[ConditionFlag.InDutyQueue], Svc.Condition[ConditionFlag.WaitingForDuty],
                                         LoadingScreen(), Svc.Condition[ConditionFlag.BoundByDuty]);

    private static bool LoadingScreen()
        => Svc.Condition[ConditionFlag.BetweenAreas] || Svc.Condition[ConditionFlag.BetweenAreas51];

    private static bool? WaitTillDutyReady()
    {
        if (InHoH && Player.Interactable && NotBusy())
        {
            EnqueueWaitImmediate(1000);
            return true;
        }

        return false;
    }

}
