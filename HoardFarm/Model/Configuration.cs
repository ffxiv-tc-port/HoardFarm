using System;
using Dalamud.Configuration;

namespace HoardFarm.Model;

[Serializable]
public class Configuration : IPluginConfiguration
{
    // 下列模式值＝存檔裡的數字＝主視窗選項的索引；不可改值、不可重排。
    public const int HoardFarmModeEfficiency = 0;
    public const int HoardFarmModeSafety = 1;
    public const int StopAfterRuns = 0;
    public const int StopAfterHoards = 1;
    public const int StopAfterMinutes = 2;
    public const int RetainerModeAnyDone = 0;
    public const int RetainerModeAllDone = 1;

    public int Version { get; set; } = 1;

    public int HoardModeSave;
    public int HoardFarmMode = HoardFarmModeEfficiency;
    public int StopAfter = 50;
    public int StopAfterMode = StopAfterHoards;
    public int OverallRuns;
    public int OverallFoundHoards;
    public int OverallTime;
    public bool ShowOverlay = true;

    public bool DoRetainers;
    public int RetainerMode = RetainerModeAllDone;

    // 退出前固定等待秒數，0＝不等；刻意不加隨機（不移植原作者躲偵測用的隨機等待）。
    public int LeaveDelayAfterIntuition = 5;
    public int LeaveDelayAfterHoard = 3;
    public const int LeaveDelayMax = 60;

    public string Language { get; set; } = "";

    public void Save()
    {
        PluginInterface.SavePluginConfig(this);
    }
}
