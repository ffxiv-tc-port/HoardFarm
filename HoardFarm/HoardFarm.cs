using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using AutoRetainerAPI;
using Dalamud.Game;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using Dalamud.Plugin.Services;
using ECommons;
using ECommons.Automation.LegacyTaskManager;
using HoardFarm.Data;
using HoardFarm.IPC;
using HoardFarm.Model;
using HoardFarm.Service;
using HoardFarm.Windows;

namespace HoardFarm;

[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
public sealed class HoardFarm : IDalamudPlugin
{
    private readonly AchievementService achievementService;
    private readonly AutoRetainerApi autoRetainerApi;
    private readonly ConfigWindow configWindow;
    private readonly DeepDungeonMenuOverlay deepDungeonMenuOverlay;

    private readonly HoardFarmService hoardFarmService;
    private readonly MainWindow mainWindow;
    private readonly RetainerService retainerService;
    public readonly WindowSystem WindowSystem = new("HoardFarm");

    // 急停端點：Action<string>，參數是呼叫端署名（寫進 log 與狀態列）。刻意只給停止、不給開始。
    private const string StopAllIpcName = "HoardFarm.StopAll";
    private readonly ICallGateProvider<string, object>? stopAllProvider;

    // 狀態端點：Func<bool>，自動流程在跑＝true（含進出副本之間）。任何執行緒都可能呼叫，只讀 volatile 欄位。
    private const string IsRunningIpcName = "HoardFarm.IsRunning";
    private readonly ICallGateProvider<bool>? isRunningProvider;

    public HoardFarm(IDalamudPluginInterface? pluginInterface)
    {
        pluginInterface?.Create<PluginService>();
        P = this;

        ECommonsMain.Init(pluginInterface, this, Module.DalamudReflector);

        // 先於第一個訂閱建立（建構子不在框架執行緒，訂閱一掛上處理器就可能讀它）；刻意維持「逾時只丟掉那一步」：叫不出坐騎、進場時沒跳團滅確認框都靠它繼續往下走。
        PluginService.TaskManager = new TaskManager();

        NavmeshIPC.Init();

        Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ApplyLanguage();

        mainWindow = new MainWindow();
        configWindow = new ConfigWindow();
        deepDungeonMenuOverlay = new DeepDungeonMenuOverlay();

        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);
        WindowSystem.AddWindow(deepDungeonMenuOverlay);

        hoardFarmService = new HoardFarmService();
        HoardService = hoardFarmService;

        achievementService = new AchievementService();
        Achievements = achievementService;

        autoRetainerApi = new AutoRetainerApi();
        RetainerApi = autoRetainerApi;

        retainerService = new RetainerService();
        RetainerScv = retainerService;

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += () => OnCommand();
        PluginInterface.UiBuilder.OpenConfigUi += ShowConfigWindow;
        Framework.Update += FrameworkUpdate;

        stopAllProvider = PluginInterface.GetIpcProvider<string, object>(StopAllIpcName);
        stopAllProvider.RegisterAction(source => HoardService.RequestStopAll(source));

        var service = hoardFarmService;
        isRunningProvider = PluginInterface.GetIpcProvider<bool>(IsRunningIpcName);
        isRunningProvider.RegisterFunc(() => service.IsRunning);

        EzCmd.Add("/hoardfarm", (_, args) => OnCommand(args), Strings.HoardFarm_CommandHelp);
    }

    public void Dispose()
    {
        stopAllProvider?.UnregisterAction();
        isRunningProvider?.UnregisterFunc();
        WindowSystem.RemoveAllWindows();
        hoardFarmService?.Dispose();

        autoRetainerApi?.Dispose();
        retainerService?.Dispose();

        Framework.Update -= FrameworkUpdate;
        YesAlreadyManager.Release();
        ECommonsMain.Dispose();
    }


    private void FrameworkUpdate(IFramework framework)
    {
        Tick();
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
    }

    public void OnCommand(string? args = null)
    {
        args = args?.Trim().ToLower() ?? "";

        switch (args)
        {
            case "c":
            case "config":
                ShowConfigWindow();
                return;
            case "e":
            case "enable":
                HoardService.HoardMode = true;
                return;
            case "d":
            case "disable":
                if (HoardService.HoardMode) HoardService.FinishRun = true;
                return;
            case "s":
            case "stop":
                HoardService.StopAll(Strings.HoardFarm_StopReason_Command);
                return;
            case "t":
            case "toggle":
                HoardService.HoardMode = !HoardService.HoardMode;
                return;
            default:
                ShowMainWindow();
                break;
        }
    }

    // 只設本插件的資源文化，不動整個行程的 DefaultThreadCurrentUICulture（那會改到別的插件）。
    // 台服客戶端回報 ClientLanguage 7；4／5 一併歸繁中（艦隊慣例，用數值不用列舉名）。
    public static void ApplyLanguage()
    {
        Strings.Culture = Config.Language != ""
            ? CultureInfo.GetCultureInfo(Config.Language)
            : ClientState.ClientLanguage switch
            {
                ClientLanguage.French => CultureInfo.GetCultureInfo("fr"),
                ClientLanguage.German => CultureInfo.GetCultureInfo("de"),
                ClientLanguage.Japanese => CultureInfo.GetCultureInfo("ja"),
                (ClientLanguage)4 or (ClientLanguage)5 or (ClientLanguage)7 => CultureInfo.GetCultureInfo("zh-Hant"),
                _ => CultureInfo.GetCultureInfo("en")
            };
    }

    public void ShowConfigWindow()
    {
        configWindow.IsOpen = true;
    }

    public void ShowMainWindow()
    {
        if (!mainWindow.IsOpen)
        {
            Achievements.UpdateProgress();
            mainWindow.IsOpen = true;
        }
    }
}
