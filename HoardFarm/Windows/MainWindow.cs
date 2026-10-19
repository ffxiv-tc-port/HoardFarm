using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using HoardFarm.Data;
using HoardFarm.IPC;
using HoardFarm.Model;

namespace HoardFarm.Windows;

public class MainWindow : Window
{
    private readonly Configuration conf = Config;

    public MainWindow()
        : base(string.Format(Strings.MainWindow_Title, P.GetType().Assembly.GetName().Version) + "###HoardFarm")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(330, 380),
            MaximumSize = new Vector2(360, 450)
        };
        RespectCloseHotkey = false;
        
        TitleBarButtons =
        [
            new TitleBarButton
            {
                Icon = FontAwesomeIcon.Cog,
                IconOffset = new Vector2(1.5f, 1),
                ShowTooltip = () =>
                {
                    using (_ = ImRaii.Tooltip())
                    {
                        ImGui.Text(Strings.MainWindow_OpenConfig);
                    }
                },
                Click = _ => P.ShowConfigWindow()
            },

            new TitleBarButton
            {
                Icon = FontAwesomeIcon.QuestionCircle,
                IconOffset = new Vector2(1.5f, 1),
                ShowTooltip = () =>
                {
                    using (_ = ImRaii.Tooltip())
                    {
                        ImGui.Text(Strings.MainWindow_OpenHelp);
                    }
                },
                Click = _ =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://github.com/Jukkales/HoardFarm/wiki/How-to-run",
                        UseShellExecute = true
                    });
                }
            },

            new TitleBarButton
            {
                Icon = FontAwesomeIcon.Heart,
                IconOffset = new Vector2(1.5f, 1),
                ShowTooltip = () =>
                {
                    using (_ = ImRaii.Tooltip())
                    {
                        ImGui.Text(Strings.MainWindow_SupportMe);
                    }
                },
                Click = _ =>
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "https://ko-fi.com/jukkales",
                        UseShellExecute = true
                    });
                }
            }
        ];
    }

    public override void Draw()
    {
        using (_ = ImRaii.Disabled(!PluginInstalled(NavmeshIPC.Name)))
        {
            var enabled = HoardService.HoardMode;
            if (ImGui.Checkbox(Strings.MainWindow_EnableHoardFarmMode, ref enabled))
            {
                if (enabled)
                    HoardService.HoardMode = true;
                else
                    HoardService.StopAll(Strings.HoardFarm_StopReason_Manual);
            }
        }

        if (!PluginInstalled(NavmeshIPC.Name))
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(Strings.MainWindow_vnavmeshRequired);
        }

        ImGui.SameLine(230);
        ImGui.Text(HoardService.HoardModeStatus);
        ImGui.Text(Strings.MainWindow_StopAfter);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt("##stopAfter", ref Config.StopAfter))
            Config.Save();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);

        if (ImGui.Combo("##stopAfterMode", ref Config.StopAfterMode, [
                Strings.MainWindow_StopAfter_Runs,
                Strings.MainWindow_StopAfter_Hoards,
                Strings.MainWindow_StopAfter_Minutes
            ], 3))
            Config.Save();

        DrawRetainerSettings();

        ImGui.Separator();

        ImGui.BeginGroup();
        ImGui.Text(Strings.MainWindow_Savegame);
        ImGui.Indent(15);

        if (ImGui.RadioButton(Strings.MainWindow_Savegame_1, ref conf.HoardModeSave, 0))
            Config.Save();

        if (ImGui.RadioButton(Strings.MainWindow_Savegame_2, ref conf.HoardModeSave, 1))
            Config.Save();

        ImGui.Unindent(15);
        ImGui.EndGroup();

        ImGui.SameLine(170);

        ImGui.BeginGroup();
        ImGui.Text(Strings.MainWindow_FarmMode);
        ImGui.Indent(15);
        if (ImGui.RadioButton(Strings.MainWindow_FarmMode_Efficiency, ref conf.HoardFarmMode, 0))
            Config.Save();

        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.MainWindow_FarmMode_Efficiency_Help);

        if (ImGui.RadioButton(Strings.MainWindow_FarmMode_Safety, ref conf.HoardFarmMode, 1))
            Config.Save();


        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.QuestionCircle.ToIconString());
        ImGui.PopFont();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.MainWindow_FarmMode_Safety_Help);

        ImGui.Unindent(15);
        ImGui.EndGroup();

        ImGui.Separator();
        ImGui.Text(Strings.MainWindow_Statistics);

        ImGui.BeginGroup();

        ImGui.Text(Strings.MainWindow_Statistics_CurrentSession);
        ImGui.Text(string.Format(Strings.MainWindow_Statistics_Runs, HoardService.SessionRuns));
        var sessionPercent = HoardService.SessionFoundHoards == 0
                                 ? 0
                                 : HoardService.SessionFoundHoards / (double)HoardService.SessionRuns * 100;
        ImGui.Text(
            string.Format(Strings.MainWindow_Statistics_Found, HoardService.SessionFoundHoards, sessionPercent));

        var sessionTimeAverage = HoardService.SessionFoundHoards == 0
                                     ? 0
                                     : HoardService.SessionTime / HoardService.SessionFoundHoards;
        if (sessionTimeAverage > 0)
            ImGui.Text(string.Format(Strings.MainWindow_TimeWithAverage, FormatTime(HoardService.SessionTime),
                                     FormatTime(sessionTimeAverage, false)));
        else
            ImGui.Text(string.Format(Strings.MainWindow_Time, FormatTime(HoardService.SessionTime)));

        ImGui.EndGroup();
        ImGui.SameLine(170);
        ImGui.BeginGroup();

        ImGui.Text(Strings.MainWindow_Overall);
        ImGui.Text(string.Format(Strings.MainWindow_Statistics_Runs, Config.OverallRuns));
        var overallPercent = Config.OverallRuns == 0 ? 0 : Config.OverallFoundHoards / (double)Config.OverallRuns * 100;
        ImGui.Text(
            string.Format(Strings.MainWindow_Statistics_Found, Config.OverallFoundHoards, overallPercent));

        var overallTimeAverage = Config.OverallFoundHoards == 0 ? 0 : Config.OverallTime / Config.OverallFoundHoards;
        if (overallTimeAverage > 0)
            ImGui.Text(string.Format(Strings.MainWindow_TimeWithAverage, FormatTime(Config.OverallTime),
                                     FormatTime(overallTimeAverage, false)));
        else
            ImGui.Text(string.Format(Strings.MainWindow_Time, FormatTime(Config.OverallTime)));

        ImGui.EndGroup();
        ImGui.Separator();

        var targetProgress = Achievements.Max > 0 ? Achievements.Max : ForTheHoardAchievementFallbackTarget;
        if (Achievements.Max == 0)
        {
            ImGui.TextDisabled(string.Format(Strings.MainWindow_ProgressUnknown, targetProgress));
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.MainWindow_ProgressUnknown_Help);
        }
        else
            ImGui.Text(string.Format(Strings.MainWindow_Progress, Achievements.Progress, targetProgress));

        if (Achievements.Max == 0)
        {
            // 進度不明時不估算剩餘時間。
        }
        else if (Achievements.Progress == 0)
            ImGui.Text(Strings.MainWindow_ProgressZero);
        else if (overallTimeAverage == 0)
            ImGui.Text(Strings.MainWindow_ProgressNoTime);
        else
        {
            ImGui.TextWrapped(FormatRemaining(Math.Max(0, targetProgress - Achievements.Progress) * overallTimeAverage));
        }

        if (HoardService.HoardModeError != string.Empty)
        {
            ImGui.Separator();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(new Vector4(1, 0, 0, 1), FontAwesomeIcon.ExclamationTriangle.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1, 0, 0, 1), Strings.MainWindow_UnableToRun + "\n");
            ImGui.Text(HoardService.HoardModeError);
        }
    }

    private void DrawRetainerSettings()
    {
        var autoRetainer = RetainerApi.Ready;
        using (_ = ImRaii.Disabled(!autoRetainer))
        {
            if (ImGui.Checkbox(Strings.MainWindow_DoRetainers, ref Config.DoRetainers))
                Config.Save();
        }

        var hoverText = Strings.MainWindow_DoRetainers_Port;

        if (autoRetainer && !RetainerScv.CanRunRetainer())
            hoverText = Strings.MainWindow_DoRetainers_WrongWorld;
        if (!autoRetainer)
            hoverText = Strings.MainWindow_DoRetainers_AutoretainerNeeded;

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(hoverText);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(200);
        if (ImGui.Combo("##retainerMode", ref Config.RetainerMode,
                        [Strings.MainWindow_DoRetainers_AnyDone, Strings.MainWindow_DoRetainers_AllDone], 2))
            Config.Save();
    }

    private static string FormatTime(int seconds, bool withHours = true)
    {
        var timespan = TimeSpan.FromSeconds(seconds);
        return timespan.ToString(withHours ? timespan.Days >= 1 ? @"d\:hh\:mm\:ss" : @"hh\:mm\:ss" : @"mm\:ss");
    }

    private static string FormatRemaining(int seconds)
    {
        var timespan = TimeSpan.FromSeconds(seconds);
        if (timespan.Days >= 1)
            return string.Format(Strings.MainWindow_RemainingTimeDays, timespan.Days, timespan.ToString(@"hh\:mm\:ss"));

        return string.Format(Strings.MainWindow_RemainingTime, timespan.ToString(@"hh\:mm\:ss"));
    }
}
