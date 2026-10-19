using System;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using HoardFarm.IPC;
using HoardFarm.Tasks.Base;

namespace HoardFarm.Tasks;

// InteractWithObject 的回傳值沒有語意，不拿來判成敗；成敗由後續等視窗出現的那一步判斷。
public class InteractObjectTask(uint dataId) : BaseTask()
{
    public override unsafe bool? Run()
    {
        if (ObjectTable.TryGetFirst(e => e.BaseId == dataId, out var obj))
        {
            var target = (GameObject*)obj.Address;
            if (!InInteractRange(target))
            {
                // 站在 7 米內但互動距離外時，上游會原地互動失敗直到逾時；這裡先走近。
                if (!NavmeshIPC.PathfindInProgress() && !NavmeshIPC.PathIsRunning()
                    && EzThrottler.Throttle("InteractApproach" + dataId, 2000))
                    NavmeshIPC.PathfindAndMoveTo(obj.Position, false);
                return false;
            }

            if (NavmeshIPC.PathIsRunning()) NavmeshIPC.PathStop();

            if (TargetSystem.Instance()->Target == target)
            {
                TargetSystem.Instance()->InteractWithObject(target);
                return true;
            }

            if (EzThrottler.Throttle("Interact" + dataId))
            {
                TargetSystem.Instance()->Target = target;
                return false;
            }
        }

        return false;
    }

    // 函式解析不到時當作在範圍內（＝上游行為）。
    private static unsafe bool InInteractRange(GameObject* target)
    {
        var player = Player.Object;
        if (player == null || EventFramework.MemberFunctionPointers.CheckInteractRange == null)
            return true;
        try
        {
            var framework = EventFramework.Instance();
            return framework == null || framework->CheckInteractRange((GameObject*)player.Address, target, 1, false);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
