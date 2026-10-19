using System;
using System.Linq;
using Dalamud.Plugin.Services;
using AutoRetainerAPI.Configuration;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using ECommons.Reflection;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HoardFarm.IPC;
using HoardFarm.Tasks;
using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace HoardFarm.Service;

public class RetainerService : IDisposable
{
    public bool Running {get; private set;}
    private long lastUpdate;
    private DateTime startedAt;
    private bool autoRetainerEnabled;
    private readonly AutoRetainerIPC autoRetainerIcp = new();
    private bool retainerSense;

    private DateTime? autoRetainerRunningThreshold;

    // 上游用 System.Timers.Timer，回呼跑在執行緒池上卻去碰 TaskManager、物件表與原生視窗；改掛框架更新。
    public RetainerService()
    {
        Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        if (!Running) return;
        var now = Environment.TickCount64;
        if (now - lastUpdate < 1000) return;
        lastUpdate = now;
        OnTimerUpdate();
    }

    public void StartProcess()
    {
        if (CanRunRetainer())
        {
            Running = true;
            autoRetainerEnabled = false;
            startedAt = DateTime.Now;
            retainerSense = GetRetainerSense();
            if (retainerSense)
            {
                PluginLog.Information("Temporarily disabling RetainerSense.");
                SetRetainerSense(false);
            }
        }
        else
        {
            PluginLog.Information("Not enough inventory space to run retainers.");
        }
    }
    
    public unsafe void FinishProcess()
    {
        PluginLog.Information("Retainer processing finished.");
        if (GetOpenBellBehavior() != OpenBellBehavior.Enable_AutoRetainer)
        {
            PluginLog.Information("Disabling AutoRetainer according to user config.");
            DisableAutoRetainer();
        }

        if (retainerSense)
        {
            PluginLog.Information("Re-enable RetainerSense.");
            SetRetainerSense(true);
        }

        Running = false;

        if (CheckRetainerListOpen() && !AutoRetainerBusyOrUnknown())
        {
            TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon);
            addon->Close(true);
        }
    }
    
    private unsafe void OnTimerUpdate()
    {
        if (DateTime.Now.Subtract(startedAt).TotalMinutes > 5)
        {
            PluginLog.Information("Retainer processing took too long. Stopping.");
            FinishProcess();
            return;
        }
        
        if (!NavmeshIPC.NavIsReady())
        {
            return;
        }

        if (!TaskManager.IsBusy && Running && !AutoRetainerRunning())
        {
            if (Player.Territory != LimsaMapId)
            {
                Enqueue(new TeleportTask(LimsaAetherytId, LimsaMapId), "Teleport to Limsa");
                return;
            }

            if (ObjectTable.TryGetFirst(gameObject => gameObject.BaseId == RetainerBellDataId, out var bell))
            {
                if (bell.Position.Distance(Player.Position) > 3)
                {
                    Enqueue(new PathfindTask(RetainerBellLocation, true, 1f), "Move to bell");
                    EnqueueWait(2000); // SE server are just slow
                    return;
                }
            }

            if (NotBusy() || TargetSystem.Instance()->Target == null || TargetSystem.Instance()->Target->BaseId != RetainerBellDataId)
            {
                Enqueue(new InteractObjectTask(RetainerBellDataId), "Interact Bell");
                if (GetOpenBellBehavior() != OpenBellBehavior.Enable_AutoRetainer)
                {
                    Enqueue(() => { 
                        EnableAutoRetainer();
                        return true;
                    });
                }
                autoRetainerEnabled = true;
                EnqueueWait(1000);
                PluginLog.Information("Passing to AutoRetainer.");
                return;
            }
            
            // We not reach here until autoretainer is done
            if (autoRetainerEnabled && !AutoRetainerRunning())
            {
                FinishProcess();
            }
        }
    }

    // AutoRetainer 還在操作時不替它關僱員清單：它下一步可能正要按這個視窗，而關閉中的視窗被按＝原生崩潰。
    // 問不到（沒載入、IPC 失敗）也當作忙，寧可留著清單。
    private bool AutoRetainerBusyOrUnknown()
    {
        try
        {
            return autoRetainerIcp.IsBusy();
        }
        catch (Exception)
        {
            return true;
        }
    }

    private unsafe bool CheckRetainerListOpen()
    {
        return TryGetAddonByName<AtkUnitBase>("RetainerList", out var addon) && IsAddonReady(addon);
    }
    
    private OpenBellBehavior? GetOpenBellBehavior()
    {
        OpenBellBehavior? behavior = null;
        if (DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true))
        {
            Safe(delegate
            {
                var type = plugin.GetType().Assembly.GetType("AutoRetainer.AutoRetainer", true);
                var result = type?.GetField("config", ReflectionHelper.AllFlags)?.GetValue(plugin);
                behavior = (OpenBellBehavior) result.GetFoP("OpenBellBehaviorWithVentures");
            });
        }

        return behavior;
    }

    private void EnableAutoRetainer()
    {
        if (DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true))
        {
            Safe(delegate
            {
                var type = plugin.GetType().Assembly.GetType("AutoRetainer.Scheduler.SchedulerMain", true);
                var method = type?.GetMethod("EnablePlugin", ReflectionHelper.AllFlags);
                method?.Invoke(plugin, [PluginEnableReason.Manual]);
            });
        }
    }
    
    private void DisableAutoRetainer()
    {
        if (DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true))
        {
            Safe(delegate
            {
                var type = plugin.GetType().Assembly.GetType("AutoRetainer.Scheduler.SchedulerMain", true);
                var method = type?.GetMethod("DisablePlugin", ReflectionHelper.AllFlags);
                method?.Invoke(plugin, null);
            });
        }
    }
    private void SetRetainerSense(bool value)
    {
        if (DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true))
        {
            Safe(delegate
            {
                var type = plugin.GetType().Assembly.GetType("AutoRetainer.AutoRetainer", true);
                var result = type?.GetField("config", ReflectionHelper.AllFlags)?.GetValue(plugin);
                result.SetFoP("RetainerSense", value);
            });
        }
    }
    
    private bool GetRetainerSense()
    {
        var retainerSense = false;
        if (DalamudReflector.TryGetDalamudPlugin("AutoRetainer", out var plugin, false, true))
        {
            Safe(delegate
            {
                var type = plugin.GetType().Assembly.GetType("AutoRetainer.AutoRetainer", true);
                var result = type?.GetField("config", ReflectionHelper.AllFlags)?.GetValue(plugin);
                retainerSense = (bool) result.GetFoP("RetainerSense");
            });
        }

        return retainerSense;
    }

    // AutoRetainer 沒有這個角色的資料或 IPC 失敗時回 false（＝不去跑僱員），不讓例外每秒打斷主迴圈。
    public static bool CheckRetainersDone(bool all = true)
    {
        try
        {
            var data = RetainerApi.GetOfflineCharacterData(Svc.PlayerState.ContentId)?.RetainerData;
            if (data == null || data.Count == 0) return false;
            return all ? data.All(e => CheckIsDone(e.VentureEndsAt)) : data.Any(e => CheckIsDone(e.VentureEndsAt));
        }
        catch (Exception ex)
        {
            PluginLog.Warning($"[HoardFarm] 讀不到 AutoRetainer 的僱員資料，這一輪不跑僱員：{ex.Message}");
            return false;
        }
    }
    
    private static bool CheckIsDone(long time)
    {
        return time+10 <= CSFramework.GetServerTime();
    }

    private bool AutoRetainerRunning()
    {
        if (autoRetainerIcp.IsBusy())
            autoRetainerRunningThreshold = null;
        else
            autoRetainerRunningThreshold ??= DateTime.Now.AddSeconds(10);
        
        return autoRetainerRunningThreshold == null || autoRetainerRunningThreshold > DateTime.Now; 
    }

    public bool CanRunRetainer()
    {
        return autoRetainerIcp.GetInventoryFreeSlotCount() > 4 && 
               Svc.Objects.LocalPlayer != null && 
               Svc.Objects.LocalPlayer.HomeWorld.RowId == Svc.Objects.LocalPlayer.CurrentWorld.RowId;
    }

}
