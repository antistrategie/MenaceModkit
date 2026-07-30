using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes;

namespace Menace.SDK;

/// <summary>
/// Harmony hooks for strategy layer events that fire Lua callbacks.
///
/// Hooks various strategy classes (Roster, StoryFaction, Squaddies, Operation, etc.)
/// to provide a comprehensive event system for both C# plugins and Lua scripts.
///
/// C# Usage:
///   StrategyEventHooks.OnLeaderHired += (leader) => { ... };
///   StrategyEventHooks.OnFactionTrustChanged += (faction, delta) => { ... };
///
/// Lua Usage:
///   on("leader_hired", function(data) log(data.leader .. " joined the roster") end)
///   on("faction_trust_changed", function(data) log(data.faction .. " trust: " .. data.delta) end)
/// </summary>
public static class StrategyEventHooks
{
    private static bool _initialized;
    private static Assembly _gameAssembly;

    // Cached types
    private static Type _rosterType;
    private static Type _storyFactionType;
    private static Type _squaddiesType;
    private static Type _operationType;
    private static Type _blackMarketType;
    private static Type _eventManagerType;
    private static Type _baseUnitLeaderType;

    // ═══════════════════════════════════════════════════════════════════
    //  C# Events - Subscribe from plugins
    // ═══════════════════════════════════════════════════════════════════

    // Roster Events
    public static event Action<IntPtr> OnLeaderHired;                    // leader
    public static event Action<IntPtr> OnLeaderDismissed;                // leader
    public static event Action<IntPtr> OnLeaderPermadeath;               // leader
    public static event Action<IntPtr> OnLeaderLevelUp;                  // leader

    // Faction Events
    public static event Action<IntPtr, int> OnFactionTrustChanged;       // faction, delta
    public static event Action<IntPtr, int> OnFactionStatusChanged;      // faction, newStatus
    public static event Action<IntPtr, IntPtr> OnFactionUpgradeUnlocked; // faction, upgrade

    // Squaddie Events
    public static event Action<int> OnSquaddieKilled;                    // squaddieId

    // Operation/Mission Events
    public static event Action<IntPtr> OnMissionEnded;                   // mission
    public static event Action OnOperationFinished;

    // Black Market Events
    public static event Action<IntPtr> OnBlackMarketItemAdded;           // item
    public static event Action OnBlackMarketRestocked;

    // ═══════════════════════════════════════════════════════════════════
    //  Initialization
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Initialize strategy event hooks. Call from ModpackLoaderMod after game assembly is loaded.
    /// </summary>
    public static void Initialize(HarmonyLib.Harmony harmony)
    {
        if (_initialized) return;

        try
        {
            _gameAssembly = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == "Assembly-CSharp");

            if (_gameAssembly == null)
            {
                SdkLogger.Warning("[StrategyEventHooks] Assembly-CSharp not found");
                return;
            }

            // Cache types via FindManagedProxy: IL2CppInterop prefixes the namespace, so looking
            // up the game's own name returns null and every patch below is skipped in silence.
            _rosterType = GameType.FindManagedProxy("Menace.Strategy.Roster");
            _storyFactionType = GameType.FindManagedProxy("Menace.Strategy.StoryFaction");
            _squaddiesType = GameType.FindManagedProxy("Menace.Strategy.Squaddies");
            _operationType = GameType.FindManagedProxy("Menace.Strategy.Operation");
            _blackMarketType = GameType.FindManagedProxy("Menace.Strategy.BlackMarket");
            _eventManagerType = GameType.FindManagedProxy("Menace.Strategy.EventManager");
            _baseUnitLeaderType = GameType.FindManagedProxy("Menace.Strategy.BaseUnitLeader");

            int patchCount = 0;

            // Roster patches
            if (_rosterType != null)
            {
                patchCount += PatchMethod(harmony, _rosterType, "HireLeader", nameof(HireLeader_Postfix));
                patchCount += PatchMethod(harmony, _rosterType, "TryDismissLeader", nameof(DismissLeader_Postfix));
                patchCount += PatchMethod(harmony, _rosterType, "OnPermanentDeath", nameof(OnPermanentDeath_Postfix));
            }

            // Leader patches
            if (_baseUnitLeaderType != null)
            {
                patchCount += PatchMethod(harmony, _baseUnitLeaderType, "AddPerk", nameof(AddPerk_Postfix));
            }

            // Faction patches
            if (_storyFactionType != null)
            {
                patchCount += PatchMethod(harmony, _storyFactionType, "ChangeTrust", nameof(ChangeTrust_Postfix));
                patchCount += PatchMethod(harmony, _storyFactionType, "SetStatus", nameof(SetStatus_Postfix));
                patchCount += PatchMethod(harmony, _storyFactionType, "UnlockUpgrade", nameof(UnlockUpgrade_Postfix));
            }

            // Squaddie patches. TryAddAlive is deliberately not hooked: its Il2CppSystem.Nullable
            // parameters NRE in the interop trampoline when the game passes null, which skips the
            // original and empties the campaign-start roster. See BACKLOG for the safe alternative.
            if (_squaddiesType != null)
            {
                patchCount += PatchMethod(harmony, _squaddiesType, "Kill", nameof(SquaddieKill_Postfix));
            }

            // Operation patches
            if (_operationType != null)
            {
                patchCount += PatchMethod(harmony, _operationType, "EndMission", nameof(EndMission_Postfix));
            }

            // EventManager patches
            if (_eventManagerType != null)
            {
                patchCount += PatchMethod(harmony, _eventManagerType, "OnOperationFinished", nameof(OnOperationFinished_Postfix));
            }

            // BlackMarket patches
            if (_blackMarketType != null)
            {
                patchCount += PatchMethod(harmony, _blackMarketType, "AddItem", nameof(BlackMarketAddItem_Postfix));
                // Restocking is Restock(bool, bool) in the shipped game, not FillUp.
                patchCount += PatchMethod(harmony, _blackMarketType, "Restock", nameof(BlackMarketFillUp_Postfix));
            }

            _initialized = true;
            SdkLogger.Msg($"[StrategyEventHooks] Initialized with {patchCount} event hooks");
        }
        catch (Exception ex)
        {
            SdkLogger.Error($"[StrategyEventHooks] Failed to initialize: {ex.Message}");
        }
    }

    private static int PatchMethod(HarmonyLib.Harmony harmony, Type targetType, string methodName, string patchMethodName)
    {
        try
        {
            var targetMethod = targetType.GetMethod(methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (targetMethod == null)
            {
                SdkLogger.Warning($"[StrategyEventHooks] Method not found: {targetType.Name}.{methodName}");
                return 0;
            }

            var unmarshallable = GamePatch.FindUnmarshallableParameter(targetMethod);
            if (unmarshallable != null)
            {
                SdkLogger.Warning($"[StrategyEventHooks] Refusing to patch {targetType.Name}.{methodName}: {unmarshallable}");
                return 0;
            }

            var patchMethod = typeof(StrategyEventHooks).GetMethod(patchMethodName,
                BindingFlags.Static | BindingFlags.NonPublic);

            if (patchMethod == null)
            {
                SdkLogger.Warning($"[StrategyEventHooks] Patch method not found: {patchMethodName}");
                return 0;
            }

            harmony.Patch(targetMethod, postfix: new HarmonyMethod(patchMethod));
            return 1;
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"[StrategyEventHooks] Failed to patch {targetType?.Name}.{methodName}: {ex.Message}");
            return 0;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helper Methods
    // ═══════════════════════════════════════════════════════════════════

    private static IntPtr GetPointer(object obj)
    {
        if (obj == null) return IntPtr.Zero;
        if (obj is Il2CppObjectBase il2cppObj)
            return il2cppObj.Pointer;
        return IntPtr.Zero;
    }

    // The postfixes read their arguments positionally out of Harmony's __args, because Harmony
    // binds named patch parameters against the game's own names ("_leader", "_change") and the
    // mismatch is silent. The comment above each postfix records the signature it maps.
    private static object Arg(object[] args, int index) => GameMember.Arg(args, index);
    private static int ArgInt(object[] args, int index) => GameMember.ArgInt(args, index);

    private static string GetName(object obj)
    {
        if (obj == null) return "<null>";
        try
        {
            var ptr = GetPointer(obj);
            if (ptr == IntPtr.Zero) return "<null>";

            // Try to get template ID or name
            var gameObj = new GameObj(ptr);

            // For leaders, try to get nickname
            var nicknameMethod = obj.GetType().GetMethod("GetNickname",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (nicknameMethod != null)
            {
                var result = nicknameMethod.Invoke(obj, null);
                if (result != null) return result.ToString();
            }

            // Try GetID for templates
            var idMethod = obj.GetType().GetMethod("GetID",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (idMethod != null)
            {
                var result = idMethod.Invoke(obj, null);
                if (result != null) return result.ToString();
            }

            return gameObj.GetName() ?? "<unnamed>";
        }
        catch
        {
            return "<unknown>";
        }
    }

    private static string GetFactionName(object faction)
    {
        if (faction == null) return "<null>";
        try
        {
            // Try to get the template and its name
            var templateProp = faction.GetType().GetProperty("Template",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (templateProp != null)
            {
                var template = templateProp.GetValue(faction);
                if (template != null)
                {
                    return GetName(template);
                }
            }
            return GetName(faction);
        }
        catch
        {
            return "<unknown>";
        }
    }

    private static void FireLuaEvent(string eventName, Dictionary<string, object> data)
    {
        try
        {
            LuaScriptEngine.Instance?.FireEventWithTable(eventName, data);
        }
        catch (Exception ex)
        {
            ModError.WarnInternal("StrategyEventHooks", $"Lua event '{eventName}' failed: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Harmony Postfix Patches
    // ═══════════════════════════════════════════════════════════════════

    // --- Roster Events ---

    // Roster.HireLeader(UnitLeaderTemplate _leaderTemplate)
    private static void HireLeader_Postfix(object __instance, object __result, object[] __args)
    {
        var template = Arg(__args, 0);

        if (__result == null) return; // Hire failed

        var leaderPtr = GetPointer(__result);
        OnLeaderHired?.Invoke(leaderPtr);

        FireLuaEvent("leader_hired", new Dictionary<string, object>
        {
            ["leader"] = GetName(__result),
            ["leader_ptr"] = leaderPtr.ToInt64(),
            ["template"] = GetName(template)
        });
    }

    // Roster.TryDismissLeader(BaseUnitLeader _leader)
    private static void DismissLeader_Postfix(object __instance, bool __result, object[] __args)
    {
        var leader = Arg(__args, 0);

        if (!__result) return; // Dismiss failed

        var leaderPtr = GetPointer(leader);
        OnLeaderDismissed?.Invoke(leaderPtr);

        FireLuaEvent("leader_dismissed", new Dictionary<string, object>
        {
            ["leader"] = GetName(leader),
            ["leader_ptr"] = leaderPtr.ToInt64()
        });
    }

    // Roster.OnPermanentDeath(BaseUnitLeader _leader)
    private static void OnPermanentDeath_Postfix(object __instance, object[] __args)
    {
        var leader = Arg(__args, 0);

        var leaderPtr = GetPointer(leader);
        OnLeaderPermadeath?.Invoke(leaderPtr);

        FireLuaEvent("leader_permadeath", new Dictionary<string, object>
        {
            ["leader"] = GetName(leader),
            ["leader_ptr"] = leaderPtr.ToInt64()
        });
    }

    // BaseUnitLeader.AddPerk(PerkTemplate _perk, bool _spendPromotionPoints)
    private static void AddPerk_Postfix(object __instance, object[] __args)
    {
        var perk = Arg(__args, 0);

        var leaderPtr = GetPointer(__instance);
        OnLeaderLevelUp?.Invoke(leaderPtr);

        FireLuaEvent("leader_levelup", new Dictionary<string, object>
        {
            ["leader"] = GetName(__instance),
            ["leader_ptr"] = leaderPtr.ToInt64(),
            ["perk"] = GetName(perk)
        });
    }

    // --- Faction Events ---

    // StoryFaction.ChangeTrust(int _change)
    private static void ChangeTrust_Postfix(object __instance, object[] __args)
    {
        var delta = ArgInt(__args, 0);

        if (delta == 0) return;

        var factionPtr = GetPointer(__instance);
        OnFactionTrustChanged?.Invoke(factionPtr, delta);

        FireLuaEvent("faction_trust_changed", new Dictionary<string, object>
        {
            ["faction"] = GetFactionName(__instance),
            ["faction_ptr"] = factionPtr.ToInt64(),
            ["delta"] = delta
        });
    }

    // StoryFaction.SetStatus(StoryFactionStatus _status)
    private static void SetStatus_Postfix(object __instance, object[] __args)
    {
        var status = ArgInt(__args, 0);

        var factionPtr = GetPointer(__instance);
        OnFactionStatusChanged?.Invoke(factionPtr, status);

        FireLuaEvent("faction_status_changed", new Dictionary<string, object>
        {
            ["faction"] = GetFactionName(__instance),
            ["faction_ptr"] = factionPtr.ToInt64(),
            ["status"] = status
        });
    }

    // StoryFaction.UnlockUpgrade(ShipUpgradeTemplate _upgrade)
    private static void UnlockUpgrade_Postfix(object __instance, object[] __args)
    {
        var upgrade = Arg(__args, 0);

        var factionPtr = GetPointer(__instance);
        var upgradePtr = GetPointer(upgrade);

        OnFactionUpgradeUnlocked?.Invoke(factionPtr, upgradePtr);

        FireLuaEvent("faction_upgrade_unlocked", new Dictionary<string, object>
        {
            ["faction"] = GetFactionName(__instance),
            ["faction_ptr"] = factionPtr.ToInt64(),
            ["upgrade"] = GetName(upgrade),
            ["upgrade_ptr"] = upgradePtr.ToInt64()
        });
    }

    // --- Squaddie Events ---

    // Squaddies.Kill(int _squaddieId)
    private static void SquaddieKill_Postfix(object __instance, bool __result, object[] __args)
    {
        var squaddieId = ArgInt(__args, 0);

        if (!__result) return; // Kill failed

        OnSquaddieKilled?.Invoke(squaddieId);

        FireLuaEvent("squaddie_killed", new Dictionary<string, object>
        {
            ["squaddie_id"] = squaddieId
        });
    }

    // --- Operation/Mission Events ---

    // Operation.EndMission(Mission _mission, MissionResultStats _resultStats,
    //                      List<BaseItemTemplate> _loot, bool _abortedMission)
    private static void EndMission_Postfix(object __instance, object[] __args)
    {
        var mission = Arg(__args, 0);

        var missionPtr = GetPointer(mission);
        OnMissionEnded?.Invoke(missionPtr);

        // Get mission result info if available
        string status = "unknown";
        try
        {
            // mission is MissionState, get result
            var statusProp = mission?.GetType().GetProperty("Status",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (statusProp != null)
            {
                var statusVal = statusProp.GetValue(mission);
                status = statusVal?.ToString() ?? "unknown";
            }
        }
        catch { }

        FireLuaEvent("mission_ended", new Dictionary<string, object>
        {
            ["mission_ptr"] = missionPtr.ToInt64(),
            ["status"] = status
        });
    }

    private static void OnOperationFinished_Postfix(object __instance)
    {
        OnOperationFinished?.Invoke();

        FireLuaEvent("operation_finished", new Dictionary<string, object>());
    }

    // --- Black Market Events ---

    // BlackMarket.AddItem(BaseItem _item, int _remainingTimeout)
    private static void BlackMarketAddItem_Postfix(object __instance, object[] __args)
    {
        var item = Arg(__args, 0);

        var itemPtr = GetPointer(item);
        OnBlackMarketItemAdded?.Invoke(itemPtr);

        FireLuaEvent("blackmarket_item_added", new Dictionary<string, object>
        {
            ["item"] = GetName(item),
            ["item_ptr"] = itemPtr.ToInt64()
        });
    }

    private static void BlackMarketFillUp_Postfix(object __instance)
    {
        OnBlackMarketRestocked?.Invoke();

        FireLuaEvent("blackmarket_restocked", new Dictionary<string, object>());
    }
}
