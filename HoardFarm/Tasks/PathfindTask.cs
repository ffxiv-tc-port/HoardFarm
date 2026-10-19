using System.Linq;
using System.Numerics;
using ECommons.GameHelpers;
using ECommons.Throttlers;
using FFXIVClientStructs.FFXIV.Client.Game;
using HoardFarm.IPC;
using HoardFarm.Tasks.Base;

namespace HoardFarm.Tasks;

// territory 有值時，人不在那一區就直接略過（座標只在那一區有意義；例如傳送沒成功時不要在別區亂走）。
public class PathfindTask(Vector3 targetPosition, bool sprint = false, float toleranceDistance = 3f, uint? territory = null)
    : BaseTask
{
    public override unsafe bool? Run()
    {
        if (territory.HasValue && Player.Territory != territory.Value)
            return true;

        if (targetPosition.Distance(Player.Position) <= toleranceDistance)
        {
            NavmeshIPC.PathStop();
            return true;
        }
        
        if (sprint && Player.Status.All(e => e.StatusId != 50) && SprintCD == 0 
                   && ActionManager.Instance()->GetActionStatus(ActionType.GeneralAction, 4) == 0)
        {
            if (EzThrottler.Throttle("Sprint"))
            {
                ActionManager.Instance()->UseAction(ActionType.GeneralAction, 4);
            }
        }
        
        if (NavmeshIPC.PathfindInProgress() || NavmeshIPC.PathIsRunning() || IsMoving()) return false;

        NavmeshIPC.PathfindAndMoveTo(targetPosition, false);
        NavmeshIPC.PathSetAlignCamera(true);
        
        return false;
    }
}
