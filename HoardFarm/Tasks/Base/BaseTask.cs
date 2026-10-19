using System.Diagnostics.CodeAnalysis;

namespace HoardFarm.Tasks.Base;

// 時限由排進佇列的人決定（TaskManagerUtil 的 timeLimitMs，預設 10 秒），任務本身不帶時限。
public abstract class BaseTask
{
    public abstract bool? Run();
}
