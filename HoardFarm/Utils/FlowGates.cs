namespace HoardFarm.Utils;

// 純判斷：不碰遊戲狀態，呼叫端把旗標讀好傳進來（離線探針直接編入本檔）。
public static class FlowGates
{
    /// <summary>
    /// 團滅確認框（Addon 10483）只在選完存檔、排進任務搜尋器之前出現；
    /// 已經排進去或已在進場＝這一次不會再跳，不必等滿時限。
    /// </summary>
    public static bool PartyKoPromptPassed(bool inDutyQueue, bool waitingForDuty, bool loadingScreen, bool boundByDuty)
        => inDutyQueue || DutyConfirmPassed(waitingForDuty, loadingScreen, boundByDuty);

    /// <summary>進入確認已經按過（不論誰按的）或已在進場。只排進佇列、確認框還在等的時候不算。</summary>
    public static bool DutyConfirmPassed(bool waitingForDuty, bool loadingScreen, bool boundByDuty)
        => waitingForDuty || loadingScreen || boundByDuty;

    /// <summary>YesAlready 看門狗會移除連續掛超過上限（預設 120 分鐘）的登記；壓滿這麼久就找讀取畫面放開一次讓它重新計時。</summary>
    public const long YesAlreadyRefreshAfterMs = 90L * 60_000;

    /// <summary>
    /// 這一幀要不要壓住 YesAlready。flowActive＝自動流程在跑；holding／heldMs＝目前是否已壓住、壓了多久。
    /// refreshing 由呼叫端跨幀保存：放開後整段讀取畫面都維持放開，看門狗才看得到這一筆不見了。
    /// </summary>
    public static bool ShouldSuppressYesAlready(bool flowActive, bool holding, long heldMs, bool loadingScreen, ref bool refreshing)
    {
        if (flowActive && loadingScreen && (refreshing || (holding && heldMs >= YesAlreadyRefreshAfterMs)))
        {
            refreshing = true;
            return false;
        }

        refreshing = false;
        return flowActive;
    }
}
