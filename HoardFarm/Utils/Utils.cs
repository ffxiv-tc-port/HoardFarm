using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Memory;
using Dalamud.Utility;
using ECommons.DalamudServices;
using ECommons.GameHelpers;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using HoardFarm.Model;
using Lumina.Excel.Sheets;

namespace HoardFarm.Utils;

public static class Utils
{
    public static float SprintCD => Player.Status.FirstOrDefault(s => s.StatusId == 50)?.RemainingTime ?? 0;

    public static bool Concealment =>
        (Player.Status.FirstOrDefault(s => s.StatusId == VanishStatusId)?.RemainingTime ?? 0) > 0;

    public static bool InHoH => Player.Territory == HoHMapId11 || Player.Territory == HoHMapId21;
    public static bool InRubySea => Player.Territory == RubySeaMapId;

    // 含 1～10 層：「退出任務」只准在天之御柱裡送。
    public static bool InHoHDuty => InHoH || Player.Territory == HoHMapId1;

    public static unsafe bool IsMoving() => AgentMap.Instance()->IsPlayerMoving;

    public static float Distance(this Vector3 v, Vector3 v2)
    {
        return new Vector2(v.X - v2.X, v.Z - v2.Z).Length();
    }

    private static readonly Dictionary<string, (bool Loaded, long At)> PluginLoadedCache = new();

    // 只在主執行緒呼叫（框架更新與繪製）；每個名字一秒內重用上次的答案，避免每幀列舉插件清單。
    public static bool PluginInstalled(string name)
    {
        var now = Environment.TickCount64;
        if (PluginLoadedCache.TryGetValue(name, out var cached) && now - cached.At < 1000)
            return cached.Loaded;

        var loaded = false;
        foreach (var plugin in PluginInterface.InstalledPlugins)
        {
            if (plugin.IsLoaded && plugin.InternalName == name)
            {
                loaded = true;
                break;
            }
        }

        PluginLoadedCache[name] = (loaded, now);
        return loaded;
    }

    public static unsafe bool NotBusy()
    {
        var occupied = IsOccupied();
        var target = TargetSystem.Instance()->Target;

        if (occupied && Svc.Condition[ConditionFlag.OccupiedInQuestEvent] && target != null &&
            target->BaseId == KyuseiDataId) occupied = false;

        return Player.Available
               && Player.Object?.CastActionId == 0
               && !occupied
               && !Svc.Condition[ConditionFlag.Jumping]
               && Player.Object.IsTargetable;
    }


    public static unsafe AtkResNode* GetNodeByIDChain(AtkResNode* node, params uint[] ids)
    {
        if (node == null || ids.Length <= 0) return null;

        if (node->NodeId == ids[0])
        {
            if (ids.Length == 1) return node;

            var newList = new List<uint>(ids);
            newList.RemoveAt(0);

            var childNode = node->ChildNode;
            if (childNode != null) return GetNodeByIDChain(childNode, newList.ToArray());

            if ((int)node->Type >= 1000)
            {
                var componentNode = node->GetAsAtkComponentNode();
                var component = componentNode->Component;
                if (component == null || component->UldManager.NodeListCount == 0 || component->UldManager.NodeList == null)
                    return null;
                childNode = component->UldManager.NodeList[0];
                return childNode == null ? null : GetNodeByIDChain(childNode, newList.ToArray());
            }

            return null;
        }

        //check siblings
        var sibNode = node->PrevSiblingNode;
        return sibNode != null ? GetNodeByIDChain(sibNode, ids) : null;
    }

    private static readonly Dictionary<uint, string> YesnoPromptCache = new();

    // 空白（含換行與全形空白）不算：畫面在 NewLine 巨集處是字面 LF，表字串剝成空字串；其餘逐字比，不做子字串比對。
    public static string NormalizeGameText(string s) =>
        string.Concat(s.Where(c => !char.IsWhiteSpace(c)));

    /// <summary>目前開著、而且提示文字正好是 Addon 表第 rowId 列的確認框；不是就回 null。</summary>
    public static unsafe AtkUnitBase* FindSelectYesNo(uint rowId)
    {
        if (!YesnoPromptCache.TryGetValue(rowId, out var expected))
        {
            var row = Svc.Data.GetExcelSheet<Addon>().GetRowOrDefault(rowId);
            expected = row == null ? "" : NormalizeGameText(row.Value.Text.ToDalamudString().GetText());
            YesnoPromptCache[rowId] = expected;
        }

        if (expected.Length == 0) return null;
        if (!TryGetAddonByName<AddonSelectYesno>("SelectYesno", out var addon) || !IsAddonReady(&addon->AtkUnitBase))
            return null;
        if (addon->PromptText == null) return null;

        var actual = NormalizeGameText(MemoryHelper.ReadSeString(&addon->PromptText->NodeText).GetText());
        return actual == expected ? &addon->AtkUnitBase : null;
    }


    public static unsafe bool KyuseiInteractable()
    {
        if (ObjectTable.TryGetFirst(e => e.BaseId == KyuseiDataId, out var npc))
            return npc.Position.Distance(Player.Position) < 7f;
        return false;
    }

    public static unsafe bool CanUsePomander(Pomander pomander)
    {
        if (TryGetAddonByName<AtkUnitBase>("DeepDungeonStatus", out var addon) && IsAddonReady(addon))
        {
            var chain = pomander switch
            {
                Pomander.Intuition => IntuitionChain,
                Pomander.Concealment => ConcealmentChain,
                Pomander.Safety => SafetyChain,
                _ => []
            };
            if (chain.Length == 0) return false;
            var node = GetNodeByIDChain(addon->GetRootNode(), chain);
            return node != null && node->IsVisible();
        }

        return false;
    }

    public static unsafe bool CanUseMagicite()
    {
        if (TryGetAddonByName<AtkUnitBase>("DeepDungeonStatus", out var addon) && IsAddonReady(addon))
        {
            var node = GetNodeByIDChain(addon->GetRootNode(), MagiciteChain);
            return node != null && node->IsVisible();
        }

        return false;
    }

}
