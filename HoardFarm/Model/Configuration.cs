using System;
using Dalamud.Configuration;

namespace HoardFarm.Model;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    
    public int HoardModeSave;
    public int HoardFarmMode;
    public int StopAfter = 50;
    public int StopAfterMode = 1;
    public int OverallRuns;
    public int OverallFoundHoards;
    public int OverallTime;
    public bool ShowOverlay = true;

    public bool DoRetainers;
    public int RetainerMode = 1;

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
