using System;
using ECommons.Automation;
using ECommons.Throttlers;
using HoardFarm.Tasks.Base;

namespace HoardFarm.Tasks;

// passedWhen 回 true＝遊戲已經走過這一步、這個框這次不會出現，直接放行（不按任何東西）。
public class SelectYesnoTask(uint messageId, Func<bool>? passedWhen = null) : BaseTask
{
    public override unsafe bool? Run()
    {
        if (passedWhen?.Invoke() == true) return true;

        var addon = FindSelectYesNo(messageId);
        if (addon != null && IsAddonReady(addon) && EzThrottler.Throttle("SelectYesnoTask" + messageId))
        {
            Callback.Fire(addon, true, 0);
            return true;
        }

        return false;
    }
}
