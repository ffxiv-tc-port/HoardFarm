using System;
using System.Diagnostics.CodeAnalysis;
using ECommons.EzHookManager;
using FFXIVClientStructs.FFXIV.Client.Game.UI;

namespace HoardFarm.Service;

// 特徵碼對不上時 EzHook 欄位留 null：只是不顯示成就進度，插件照常載入。
public unsafe class AchievementService
{
    public delegate void ReceiveAchievementProgressDelegate(Achievement* achievement, uint id, uint current, uint max);
    [EzHook("C7 81 ?? ?? ?? ?? ?? ?? ?? ?? 89 91 ?? ?? ?? ?? 44 89 81", autoEnable: false)]
    public EzHook<ReceiveAchievementProgressDelegate>? ReceiveAchievementProgressHook = null;

    public int Progress;

    // 伺服器回報的目標值；0＝還沒收到。
    public int Max;

    public AchievementService()
    {
        try
        {
            EzSignatureHelper.Initialize(this);
            ReceiveAchievementProgressHook?.Enable();
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"[HoardFarm] 成就進度 hook 無法啟用，主視窗不會顯示成就進度：{ex.Message}");
        }

        if (ReceiveAchievementProgressHook == null)
            PluginLog.Warning("[HoardFarm] 成就進度 hook 的特徵碼對不上，主視窗不會顯示成就進度。");
    }

    public void UpdateProgress()
    {
        if (ReceiveAchievementProgressHook == null || Achievement.MemberFunctionPointers.RequestAchievementProgress == null)
            return;
        try
        {
            var achievement = Achievement.Instance();
            if (achievement != null)
                achievement->RequestAchievementProgress(ForTheHoardAchievementId);
        }
        catch (InvalidOperationException)
        {
            // Instance() 解不出位址時擲這個；當作沒有成就進度。
        }
    }

    [SuppressMessage("ReSharper", "UnusedMember.Local")]
    private void ReceiveAchievementProgressDetour(Achievement* achievement, uint id, uint current, uint max)
    {
        if (id == ForTheHoardAchievementId)
        {
            PluginLog.Debug($"Achievement progress: {current}/{max}");
            Progress = (int)current;
            Max = (int)max;
        }
        ReceiveAchievementProgressHook!.Original(achievement, id, current, max);
    }
}
