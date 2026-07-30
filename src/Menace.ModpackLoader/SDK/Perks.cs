using System;
using System.Collections.Generic;
using System.Reflection;
using Il2CppInterop.Runtime.InteropTypes;

namespace Menace.SDK;

/// <summary>
/// SDK wrapper for perk and skill management.
/// Provides safe access to perk trees, perk manipulation, and skill inspection.
///
/// Based on reverse engineering findings:
/// - BaseUnitLeader.m_Perks @ +0x28 (List&lt;PerkTemplate&gt;)
/// - UnitLeaderTemplate.PerkTrees @ array of PerkTreeTemplate
/// - PerkTreeTemplate.Perks @ array of Perk (Tier 1-4)
/// - PerkTemplate extends SkillTemplate
/// </summary>
public static class Perks
{
    // Cached types
    private static GameType _perkTemplateType;
    private static GameType _perkTreeTemplateType;
    private static GameType _perkType;
    private static GameType _skillTemplateType;
    private static GameType _unitLeaderType;

    /// <summary>
    /// Perk information structure.
    /// </summary>
    public class PerkInfo
    {
        public string Name { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int Tier { get; set; }
        public int ActionPointCost { get; set; }
        public bool IsActive { get; set; }
        public IntPtr Pointer { get; set; }
    }

    /// <summary>
    /// Perk tree information structure.
    /// </summary>
    public class PerkTreeInfo
    {
        public string Name { get; set; }
        public int PerkCount { get; set; }
        public List<PerkInfo> Perks { get; set; } = new();
        public IntPtr Pointer { get; set; }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Perk Queries
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Get all perks for a unit leader with detailed info.
    /// </summary>
    public static List<PerkInfo> GetLeaderPerks(GameObj leader)
    {
        var result = new List<PerkInfo>();
        if (leader.IsNull) return result;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return result;

            var proxy = GetManagedProxy(leader, leaderType);
            if (proxy == null) return result;

            var perksField = leaderType.GetField("m_Perks", BindingFlags.Public | BindingFlags.Instance);
            var perks = perksField?.GetValue(proxy);
            if (perks == null) return result;

            var listType = perks.GetType();
            var countProp = listType.GetProperty("Count");
            var indexer = listType.GetMethod("get_Item");

            int count = (int)countProp.GetValue(perks);
            for (int i = 0; i < count; i++)
            {
                var perk = indexer.Invoke(perks, new object[] { i });
                if (perk == null) continue;

                var perkObj = new GameObj(((Il2CppObjectBase)perk).Pointer);
                var info = GetPerkInfo(perkObj);
                if (info != null)
                    result.Add(info);
            }

            return result;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetLeaderPerks", "Failed", ex);
            return result;
        }
    }

    /// <summary>
    /// Get detailed information about a perk template.
    /// </summary>
    public static PerkInfo GetPerkInfo(GameObj perkTemplate)
    {
        if (perkTemplate.IsNull) return null;

        try
        {
            var info = new PerkInfo
            {
                Pointer = perkTemplate.Pointer,
                Name = perkTemplate.GetName()
            };

            // PerkTemplate inherits from SkillTemplate
            // Get Title (LocalizedLine) - read text directly from m_DefaultTranslation at offset 0x38
            var title = perkTemplate.ReadObj("Title");
            if (!title.IsNull)
            {
                info.Title = ReadLocalizedText(title) ?? info.Name;
            }

            // Get Description (LocalizedLine)
            var desc = perkTemplate.ReadObj("Description");
            if (!desc.IsNull)
            {
                info.Description = ReadLocalizedText(desc);
            }

            // Get ActionPointCost
            info.ActionPointCost = perkTemplate.ReadInt("ActionPointCost");

            // Get IsActive
            info.IsActive = perkTemplate.ReadBool("IsActive");

            return info;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetPerkInfo", "Failed", ex);
            return null;
        }
    }

    /// <summary>
    /// Get perk trees available to a unit leader from their template.
    /// </summary>
    public static List<PerkTreeInfo> GetPerkTrees(GameObj leader)
    {
        var result = new List<PerkTreeInfo>();
        if (leader.IsNull) return result;

        try
        {
            EnsureTypesLoaded();

            // Get leader's template using managed reflection (more reliable than IL2CPP field lookup)
            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return result;

            var leaderProxy = GetManagedProxy(leader, leaderType);
            if (leaderProxy == null) return result;

            // Get LeaderTemplate property
            var templateProp = leaderType.GetProperty("LeaderTemplate", BindingFlags.Public | BindingFlags.Instance);
            var templateObj = templateProp?.GetValue(leaderProxy);
            if (templateObj == null)
            {
                SdkLogger.Warning("[Perks.GetPerkTrees] LeaderTemplate property returned null");
                return result;
            }

            // Get PerkTrees from template using managed reflection
            var templateType = templateObj.GetType();
            var perkTreesProp = templateType.GetProperty("PerkTrees", BindingFlags.Public | BindingFlags.Instance);
            if (perkTreesProp == null)
            {
                // Try field instead
                var perkTreesField = templateType.GetField("PerkTrees", BindingFlags.Public | BindingFlags.Instance) ??
                                     templateType.GetField("m_PerkTrees", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (perkTreesField != null)
                {
                    var perkTreesVal = perkTreesField.GetValue(templateObj);
                    return ExtractPerkTrees(perkTreesVal);
                }

                SdkLogger.Warning($"[Perks.GetPerkTrees] PerkTrees not found on {templateType.Name}");
                return result;
            }

            var perkTrees = perkTreesProp.GetValue(templateObj);
            return ExtractPerkTrees(perkTrees);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetPerkTrees", "Failed", ex);
            return result;
        }
    }

    /// <summary>
    /// Extract perk tree info from an IL2CPP array/list of perk tree objects.
    /// Uses Count + get_Item pattern for IL2CPP compatibility.
    /// </summary>
    private static List<PerkTreeInfo> ExtractPerkTrees(object perkTreesObj)
    {
        var result = new List<PerkTreeInfo>();
        if (perkTreesObj == null) return result;

        try
        {
            var collectionType = perkTreesObj.GetType();

            // Try Count property (for Il2CppArrayBase or List-like types)
            var countProp = collectionType.GetProperty("Count") ??
                           collectionType.GetProperty("Length");
            if (countProp == null) return result;

            var indexer = collectionType.GetMethod("get_Item") ??
                         collectionType.GetProperty("Item")?.GetGetMethod();
            if (indexer == null) return result;

            int count = Convert.ToInt32(countProp.GetValue(perkTreesObj));
            for (int i = 0; i < count; i++)
            {
                var item = indexer.Invoke(perkTreesObj, new object[] { i });
                if (item == null) continue;

                var treeObj = new GameObj(((Il2CppObjectBase)item).Pointer);
                var treeInfo = GetPerkTreeInfo(treeObj);
                if (treeInfo != null)
                    result.Add(treeInfo);
            }
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.ExtractPerkTrees", "Failed", ex);
        }

        return result;
    }

    /// <summary>
    /// Get information about a perk tree.
    /// Uses pure reflection for IL2CPP compatibility.
    /// </summary>
    public static PerkTreeInfo GetPerkTreeInfo(GameObj perkTree)
    {
        if (perkTree.IsNull) return null;

        try
        {
            var info = new PerkTreeInfo
            {
                Pointer = perkTree.Pointer,
                Name = perkTree.GetName()
            };

            // Create managed proxy for the perk tree
            var treeType = GameType.Find("Menace.Strategy.PerkTreeTemplate")?.ManagedType;
            if (treeType == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: treeType is null", null, ErrorSeverity.Warning);
                return info;
            }

            var treeCtor = treeType.GetConstructor(new[] { typeof(IntPtr) });
            if (treeCtor == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: treeCtor is null", null, ErrorSeverity.Warning);
                return info;
            }

            var treeProxy = treeCtor.Invoke(new object[] { perkTree.Pointer });
            if (treeProxy == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: treeProxy is null", null, ErrorSeverity.Warning);
                return info;
            }

            // Get Perks property
            var perksProp = treeType.GetProperty("Perks", BindingFlags.Public | BindingFlags.Instance);
            if (perksProp == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: perksProp is null", null, ErrorSeverity.Warning);
                return info;
            }

            var perksArray = perksProp.GetValue(treeProxy);
            if (perksArray == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: perksArray is null", null, ErrorSeverity.Warning);
                return info;
            }

            // Get array length
            var lengthProp = perksArray.GetType().GetProperty("Length");
            if (lengthProp == null)
            {
                ModError.Report("Menace.SDK", $"GetPerkTreeInfo: lengthProp is null for {perksArray.GetType().FullName}", null, ErrorSeverity.Warning);
                return info;
            }

            int count = Convert.ToInt32(lengthProp.GetValue(perksArray) ?? 0);
            ModError.Report("Menace.SDK", $"GetPerkTreeInfo: count={count}", null, ErrorSeverity.Info);
            info.PerkCount = count;

            // Get indexer
            var indexer = perksArray.GetType().GetMethod("get_Item");
            if (indexer == null)
            {
                ModError.Report("Menace.SDK", "GetPerkTreeInfo: indexer is null", null, ErrorSeverity.Warning);
                return info;
            }

            for (int i = 0; i < count; i++)
            {
                var perk = indexer.Invoke(perksArray, new object[] { i });
                if (perk == null) continue;

                var perkType = perk.GetType();

                // Get Skill property
                var skillProp = perkType.GetProperty("Skill", BindingFlags.Public | BindingFlags.Instance);
                var skill = skillProp?.GetValue(perk);
                if (skill == null) continue;

                // Get Pointer from skill
                var pointerProp = skill.GetType().GetProperty("Pointer", BindingFlags.Public | BindingFlags.Instance);
                var ptr = (IntPtr)(pointerProp?.GetValue(skill) ?? IntPtr.Zero);
                if (ptr == IntPtr.Zero) continue;

                var skillObj = new GameObj(ptr);
                var perkInfo = GetPerkInfo(skillObj);
                if (perkInfo != null)
                {
                    // Get Tier property
                    var tierProp = perkType.GetProperty("Tier", BindingFlags.Public | BindingFlags.Instance);
                    perkInfo.Tier = Convert.ToInt32(tierProp?.GetValue(perk) ?? 0);
                    info.Perks.Add(perkInfo);
                }
            }

            return info;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetPerkTreeInfo", "Failed", ex);
            return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Perk Manipulation
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Check if a leader can be promoted (has room for more perks).
    /// </summary>
    public static bool CanBePromoted(GameObj leader)
    {
        if (leader.IsNull) return false;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return false;

            var proxy = GetManagedProxy(leader, leaderType);
            if (proxy == null) return false;

            var method = leaderType.GetMethod("CanBePromoted", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;

            return (bool)method.Invoke(proxy, null);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.CanBePromoted", "Failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Check if a leader can be demoted (has perks to remove).
    /// </summary>
    public static bool CanBeDemoted(GameObj leader)
    {
        if (leader.IsNull) return false;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return false;

            var proxy = GetManagedProxy(leader, leaderType);
            if (proxy == null) return false;

            var method = leaderType.GetMethod("CanBeDemoted", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;

            return (bool)method.Invoke(proxy, null);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.CanBeDemoted", "Failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Add a perk to a unit leader.
    /// </summary>
    /// <param name="leader">The leader to add the perk to</param>
    /// <param name="perkTemplate">The perk template to add</param>
    /// <param name="spendPromotionPoints">Whether to spend promotion points (default true)</param>
    public static bool AddPerk(GameObj leader, GameObj perkTemplate, bool spendPromotionPoints = true)
    {
        if (leader.IsNull || perkTemplate.IsNull) return false;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            var perkType = _perkTemplateType?.ManagedType;
            if (leaderType == null || perkType == null) return false;

            var leaderProxy = GetManagedProxy(leader, leaderType);
            var perkProxy = GetManagedProxy(perkTemplate, perkType);
            if (leaderProxy == null || perkProxy == null) return false;

            var method = leaderType.GetMethod("AddPerk", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;

            method.Invoke(leaderProxy, new object[] { perkProxy, spendPromotionPoints });
            return true;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.AddPerk", "Failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Remove the last perk from a unit leader.
    /// </summary>
    public static bool RemoveLastPerk(GameObj leader)
    {
        if (leader.IsNull) return false;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return false;

            var proxy = GetManagedProxy(leader, leaderType);
            if (proxy == null) return false;

            var method = leaderType.GetMethod("TryRemoveLastPerk", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;

            return (bool)method.Invoke(proxy, null);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.RemoveLastPerk", "Failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Check if a leader has a specific perk.
    /// </summary>
    public static bool HasPerk(GameObj leader, GameObj perkTemplate)
    {
        if (leader.IsNull || perkTemplate.IsNull) return false;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            var perkType = _perkTemplateType?.ManagedType;
            if (leaderType == null || perkType == null) return false;

            var leaderProxy = GetManagedProxy(leader, leaderType);
            var perkProxy = GetManagedProxy(perkTemplate, perkType);
            if (leaderProxy == null || perkProxy == null) return false;

            var method = leaderType.GetMethod("HasPerk", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return false;

            return (bool)method.Invoke(leaderProxy, new object[] { perkProxy });
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.HasPerk", "Failed", ex);
            return false;
        }
    }

    /// <summary>
    /// Get the last perk added to a leader.
    /// </summary>
    public static GameObj GetLastPerk(GameObj leader)
    {
        if (leader.IsNull) return GameObj.Null;

        try
        {
            EnsureTypesLoaded();

            var leaderType = _unitLeaderType?.ManagedType;
            if (leaderType == null) return GameObj.Null;

            var proxy = GetManagedProxy(leader, leaderType);
            if (proxy == null) return GameObj.Null;

            var method = leaderType.GetMethod("GetLastPerk", BindingFlags.Public | BindingFlags.Instance);
            if (method == null) return GameObj.Null;

            var result = method.Invoke(proxy, null);
            if (result == null) return GameObj.Null;

            return new GameObj(((Il2CppObjectBase)result).Pointer);
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetLastPerk", "Failed", ex);
            return GameObj.Null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Perk Finding
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Find a perk template by name from all perk trees of a leader.
    /// </summary>
    public static GameObj FindPerkByName(GameObj leader, string perkName)
    {
        if (leader.IsNull || string.IsNullOrEmpty(perkName)) return GameObj.Null;

        try
        {
            var trees = GetPerkTrees(leader);
            var allPerks = new List<string>();

            foreach (var tree in trees)
            {
                foreach (var perk in tree.Perks)
                {
                    // Collect for diagnostics
                    allPerks.Add($"{perk.Name ?? "?"}/{perk.Title ?? "?"}");

                    if (perk.Name?.Contains(perkName, StringComparison.OrdinalIgnoreCase) == true ||
                        perk.Title?.Contains(perkName, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        return new GameObj(perk.Pointer);
                    }
                }
            }

            // Debug: log available perks when not found
            if (allPerks.Count > 0)
                SdkLogger.Warning($"[Perks.FindPerkByName] '{perkName}' not found. Available: {string.Join(", ", allPerks.Take(10))}...");
            else
                SdkLogger.Warning($"[Perks.FindPerkByName] No perks found in leader's trees");

            return GameObj.Null;
        }
        catch (Exception ex)
        {
            SdkLogger.Warning($"[Perks.FindPerkByName] Exception: {ex.Message}");
            return GameObj.Null;
        }
    }

    /// <summary>
    /// Get available perks (not yet learned) for a leader.
    /// </summary>
    public static List<PerkInfo> GetAvailablePerks(GameObj leader)
    {
        var result = new List<PerkInfo>();
        if (leader.IsNull) return result;

        try
        {
            // Get all learned perks
            var learnedPerks = new HashSet<IntPtr>();
            var learned = GetLeaderPerks(leader);
            foreach (var p in learned)
                learnedPerks.Add(p.Pointer);

            // Get all perks from trees
            var trees = GetPerkTrees(leader);
            foreach (var tree in trees)
            {
                foreach (var perk in tree.Perks)
                {
                    if (!learnedPerks.Contains(perk.Pointer))
                        result.Add(perk);
                }
            }

            return result;
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("Perks.GetAvailablePerks", "Failed", ex);
            return result;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Console Commands
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Register console commands for Perks SDK.
    /// </summary>
    public static void RegisterConsoleCommands()
    {
        // perks <nickname> - Show unit's perks
        DevConsole.RegisterCommand("perks", "<nickname>", "Show a unit's learned perks", args =>
        {
            if (args.Length == 0)
                return "Usage: perks <nickname>";

            var nickname = string.Join(" ", args);
            var leader = Roster.FindByNickname(nickname);
            if (leader.IsNull)
                return $"Leader '{nickname}' not found";

            var perks = GetLeaderPerks(leader);
            if (perks.Count == 0)
                return $"{nickname} has no perks";

            var lines = new List<string> { $"{nickname}'s Perks ({perks.Count}):" };
            foreach (var p in perks)
            {
                var title = !string.IsNullOrEmpty(p.Title) ? p.Title : p.Name;
                var active = p.IsActive ? " [Active]" : "";
                lines.Add($"  {title}{active}");
            }
            return string.Join("\n", lines);
        });

        // perktrees <nickname> - Show available perk trees
        DevConsole.RegisterCommand("perktrees", "<nickname>", "Show a unit's perk trees", args =>
        {
            if (args.Length == 0)
                return "Usage: perktrees <nickname>";

            var nickname = string.Join(" ", args);
            var leader = Roster.FindByNickname(nickname);
            if (leader.IsNull)
                return $"Leader '{nickname}' not found";

            var trees = GetPerkTrees(leader);
            if (trees.Count == 0)
                return $"{nickname} has no perk trees";

            var lines = new List<string> { $"{nickname}'s Perk Trees ({trees.Count}):" };
            foreach (var tree in trees)
            {
                lines.Add($"  {tree.Name} ({tree.PerkCount} perks):");
                foreach (var perk in tree.Perks)
                {
                    var title = !string.IsNullOrEmpty(perk.Title) ? perk.Title : perk.Name;
                    lines.Add($"    T{perk.Tier}: {title}");
                }
            }
            return string.Join("\n", lines);
        });

        // availableperks <nickname> - Show perks available to learn
        DevConsole.RegisterCommand("availableperks", "<nickname>", "Show perks a unit can still learn", args =>
        {
            if (args.Length == 0)
                return "Usage: availableperks <nickname>";

            var nickname = string.Join(" ", args);
            var leader = Roster.FindByNickname(nickname);
            if (leader.IsNull)
                return $"Leader '{nickname}' not found";

            var available = GetAvailablePerks(leader);
            if (available.Count == 0)
                return $"{nickname} has learned all available perks";

            var canPromote = CanBePromoted(leader);
            var lines = new List<string> { $"Available Perks ({available.Count}) - Can Promote: {canPromote}" };

            // Group by tier
            var byTier = new Dictionary<int, List<PerkInfo>>();
            foreach (var p in available)
            {
                if (!byTier.ContainsKey(p.Tier))
                    byTier[p.Tier] = new List<PerkInfo>();
                byTier[p.Tier].Add(p);
            }

            foreach (var tier in byTier.Keys)
            {
                lines.Add($"  Tier {tier}:");
                foreach (var p in byTier[tier])
                {
                    var title = !string.IsNullOrEmpty(p.Title) ? p.Title : p.Name;
                    lines.Add($"    {title}");
                }
            }
            return string.Join("\n", lines);
        });

        // addperk <nickname> <perk> - Add a perk to a unit
        // Sole registration for 'addperk' (Roster no longer registers a duplicate): prefer the
        // leader's own perk trees, fall back to the global perk table for anything outside them.
        DevConsole.RegisterCommand("addperk", "<nickname> <perk>", "Add a perk to a unit (no cost)", args =>
        {
            if (args.Length < 2)
                return "Usage: addperk <nickname> <perk>";

            var nickname = args[0];
            var perkName = string.Join(" ", args, 1, args.Length - 1);

            var leader = Roster.FindByNickname(nickname);
            if (leader.IsNull)
                return $"Leader '{nickname}' not found";

            var perk = FindPerkByName(leader, perkName);
            if (perk.IsNull)
            {
                var globalPerk = Roster.FindPerk(perkName);
                if (globalPerk.IsNull)
                    return $"Perk '{perkName}' not found in {nickname}'s perk trees or the global perk table";
                return Roster.AddPerk(leader, globalPerk)
                    ? $"Added perk '{perkName}' to {nickname} (outside their perk trees)"
                    : "Failed to add perk";
            }

            if (AddPerk(leader, perk, false))
            {
                var info = GetPerkInfo(perk);
                return $"Added perk '{info?.Title ?? perkName}' to {nickname}";
            }
            return "Failed to add perk";
        });

        // removeperk <nickname> - Remove last perk from a unit
        // Sole registration for 'removeperk' (Roster no longer registers a duplicate). Two forms:
        // 'removeperk <nickname>' removes the last-learned perk; 'removeperk <nickname> <perk>'
        // removes that perk by name (the form Roster used to provide).
        DevConsole.RegisterCommand("removeperk", "<nickname> [perk]",
            "Remove a unit's last perk, or a named perk", args =>
        {
            if (args.Length == 0)
                return "Usage: removeperk <nickname>          (removes the last-learned perk)\n" +
                       "       removeperk <nickname> <perk>   (removes that perk by name)";

            // Targeted form: args[0] is the nickname, the rest name the perk.
            if (args.Length >= 2)
            {
                var targetLeader = Roster.FindByNickname(args[0]);
                if (!targetLeader.IsNull)
                {
                    var targetPerk = string.Join(" ", args, 1, args.Length - 1);
                    return Roster.RemovePerk(targetLeader, targetPerk)
                        ? $"Removed perk '{targetPerk}' from {args[0]}"
                        : $"{args[0]} has no perk named '{targetPerk}'";
                }
                // args[0] is not a leader; fall through and treat the whole line as a
                // multi-word nickname for the last-perk form.
            }

            var nickname = string.Join(" ", args);
            var leader = Roster.FindByNickname(nickname);
            if (leader.IsNull)
                return $"Leader '{nickname}' not found";

            if (!CanBeDemoted(leader))
                return $"{nickname} cannot be demoted (no perks to remove)";

            var lastPerk = GetLastPerk(leader);
            var perkName = lastPerk.IsNull ? "unknown" : (GetPerkInfo(lastPerk)?.Title ?? lastPerk.GetName());

            if (RemoveLastPerk(leader))
                return $"Removed perk '{perkName}' from {nickname}";
            return "Failed to remove perk";
        });
    }

    // --- Internal helpers ---

    private static void EnsureTypesLoaded()
    {
        _perkTemplateType ??= GameType.Find("Menace.Strategy.PerkTemplate");
        _perkTreeTemplateType ??= GameType.Find("Menace.Strategy.PerkTreeTemplate");
        _perkType ??= GameType.Find("Menace.Strategy.Perk");
        _skillTemplateType ??= GameType.Find("Menace.Tactical.Skills.SkillTemplate");
        _unitLeaderType ??= GameType.Find("Menace.Strategy.BaseUnitLeader");
    }

    private static object GetManagedProxy(GameObj obj, Type managedType)
        => Il2CppUtils.GetManagedProxy(obj, managedType);

    // Offset for m_DefaultTranslation in LocalizedLine/LocalizedMultiLine
    private const int LOC_DEFAULT_TRANSLATION_OFFSET = 0x38;

    /// <summary>
    /// Read text from a LocalizedLine/LocalizedMultiLine object directly using memory offsets.
    /// </summary>
    private static string ReadLocalizedText(GameObj localizedObj)
    {
        if (localizedObj.IsNull) return null;

        try
        {
            var ptr = localizedObj.Pointer;
            var strPtr = System.Runtime.InteropServices.Marshal.ReadIntPtr(ptr + LOC_DEFAULT_TRANSLATION_OFFSET);
            if (strPtr != IntPtr.Zero)
                return Il2CppInterop.Runtime.IL2CPP.Il2CppStringToManaged(strPtr);
            return null;
        }
        catch
        {
            return null;
        }
    }
}
