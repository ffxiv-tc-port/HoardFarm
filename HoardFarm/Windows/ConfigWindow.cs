using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using Dalamud.Interface;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Dalamud.Game;
using HoardFarm.Data;

namespace HoardFarm.Windows;

public class ConfigWindow() : Window(Strings.ConfigWindow_Title, ImGuiWindowFlags.AlwaysAutoResize)
{
    public override void Draw()
    {
        ImGui.Text(Strings.ConfigWindow_GreetingText);
        ImGui.Text(Strings.ConfigWindow_SupportMe);
        ImGui.PushStyleColor(ImGuiCol.Button, 0xFF000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, 0xDD000000 | 0x005E5BFF);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, 0xAA000000 | 0x005E5BFF);

        if (ImGui.Button(Strings.ConfigWindow_SupportKofi))
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://ko-fi.com/jukkales",
                UseShellExecute = true
            });

        ImGui.PopStyleColor(3);
        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button(Strings.ConfigWindow_ResetStatistics))
        {
            Config.OverallRuns = 0;
            Config.OverallFoundHoards = 0;
            Config.OverallTime = 0;
            Config.Save();
        }
        
        ImGui.Text(Strings.ConfigWindow_Language);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        var languages = GetLanguages();  
        var lang = languages.Find(pair => pair.Key == Config.Language).Value;
        if (ImGui.BeginCombo("##language", lang))
        {
            foreach (var language in languages)
            {
                var selected = language.Key == Config.Language;
                if (ImGui.Selectable(language.Value, selected))
                {
                    Config.Language = language.Key;
                    Config.Save();
                    HoardFarm.ApplyLanguage();
                }
            }
            ImGui.EndCombo();
        }
        
        if (ImGui.Checkbox(Strings.ConfigWindow_OpenHoardfarmOverlay, ref Config.ShowOverlay))
        {
            Config.Save();
        }

        ImGui.Spacing();
        ImGui.Separator();
        DrawLeaveDelay(Strings.ConfigWindow_LeaveDelayAfterIntuition, "##leaveDelayAfterIntuition", ref Config.LeaveDelayAfterIntuition);
        DrawLeaveDelay(Strings.ConfigWindow_LeaveDelayAfterHoard, "##leaveDelayAfterHoard", ref Config.LeaveDelayAfterHoard);
    }

    private static void DrawLeaveDelay(string label, string id, ref int seconds)
    {
        ImGui.Text(label);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.InputInt(id, ref seconds))
        {
            seconds = Math.Clamp(seconds, 0, Model.Configuration.LeaveDelayMax);
            Config.Save();
        }

        if (ImGui.IsItemHovered()) ImGui.SetTooltip(Strings.ConfigWindow_LeaveDelay_Help);
    }
    
    private List<KeyValuePair<String, String>> GetLanguages()
    {
        return new List<KeyValuePair<string, string>>
        {
            new[]
            {
                new KeyValuePair<string, string>("", Strings.ConfigWindow_Language_default),
                new KeyValuePair<string, string>("de", Strings.ConfigWindow_Language_de),
                new KeyValuePair<string, string>("en", Strings.ConfigWindow_Language_en),
                new KeyValuePair<string, string>("fr", Strings.ConfigWindow_Language_fr),
                new KeyValuePair<string, string>("ja", Strings.ConfigWindow_Language_ja),
                new KeyValuePair<string, string>("zh-Hant", Strings.ConfigWindow_Language_zhHant),
                new KeyValuePair<string, string>("zh", Strings.ConfigWindow_Language_zh),
                new KeyValuePair<string, string>("ko", Strings.ConfigWindow_Language_ko),
            }
        };  
    }
}
