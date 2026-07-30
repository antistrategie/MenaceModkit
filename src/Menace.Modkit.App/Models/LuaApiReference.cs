using System.Collections.Generic;

namespace Menace.Modkit.App.Models;

/// <summary>
/// Represents a Lua API function or event for the reference panel.
/// </summary>
public class LuaApiItem
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Signature { get; set; } = string.Empty;
    public string InsertText { get; set; } = string.Empty;
    public string? DocLink { get; set; }
    public LuaApiItemType ItemType { get; set; }
    public List<LuaApiItem> Children { get; set; } = new();

    /// <summary>
    /// For tree expansion state.
    /// </summary>
    public bool IsExpanded { get; set; } = true;

    /// <summary>
    /// True if this is a category node (not insertable).
    /// </summary>
    public bool IsCategory => Children.Count > 0;

    /// <summary>
    /// True if this item is an interceptor (for icon styling).
    /// </summary>
    public bool IsInterceptor { get; set; }
}

public enum LuaApiItemType
{
    Category,
    Function,
    Event
}

/// <summary>
/// Provides the Lua API reference data for the Code tab.
/// </summary>
public static class LuaApiReference
{
    public static List<LuaApiItem> GetApiTree()
    {
        return new List<LuaApiItem>
        {
            GetCoreFunctions(),
            GetActorQueryApi(),
            GetMovementApi(),
            GetCombatApi(),
            GetTacticalStateApi(),
            GetTileMapApi(),
            GetSpawnApi(),
            GetTileEffectsApi(),
            GetInventoryApi(),
            GetBlackMarketApi(),
            GetTacticalEvents(),
            GetStrategyEvents(),
            GetGeneralEvents(),
            // Interceptors - modify game values in real-time
            GetPropertyInterceptors(),
            GetSkillInterceptors(),
            GetTileInterceptors(),
            GetMovementInterceptors(),
            GetStrategyInterceptors(),
            GetAIInterceptors()
        };
    }

    /// <summary>
    /// Get the C# API reference tree with C# syntax - mirrors Lua structure.
    /// </summary>
    public static List<LuaApiItem> GetCSharpApiTree()
    {
        return new List<LuaApiItem>
        {
            // Core SDK - equivalent to Lua core functions
            GetCSharpSdkClasses(),
            GetCSharpActorQuery(),
            GetCSharpMovement(),
            GetCSharpCombat(),
            GetCSharpTacticalState(),
            GetCSharpTileMap(),
            GetCSharpSpawn(),
            GetCSharpTileEffects(),
            GetCSharpInventory(),
            // Action APIs (v35.0)
            GetCSharpEntityState(),
            GetCSharpEntitySkills(),
            GetCSharpEntityAI(),
            GetCSharpEntityVisibility(),
            GetCSharpTileManipulation(),
            // Events
            GetCSharpTacticalEvents(),
            GetCSharpStrategyEvents(),
            // Interceptors
            GetCSharpPropertyInterceptors(),
            GetCSharpSkillInterceptors(),
            GetCSharpCombatInterceptors(),
            GetCSharpTileInterceptors(),
            GetCSharpMovementInterceptors(),
            GetCSharpStrategyInterceptors(),
            GetCSharpAIInterceptors(),
            // Utilities
            GetCSharpPatchSet(),
            GetCSharpPointerCache()
        };
    }

    private static LuaApiItem GetCSharpActorQuery()
    {
        return new LuaApiItem
        {
            Name = "Actor Query",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "GameQuery.FindAll", Description = "Find all instances of a type", Signature = "GameQuery.FindAll(typeName)", InsertText = "var actors = GameQuery.FindAll(\"TacticalActor\");", ItemType = LuaApiItemType.Function },
                new() { Name = "GameQuery.FindByName", Description = "Find object by name", Signature = "GameQuery.FindByName(typeName, name)", InsertText = "var actor = GameQuery.FindByName(\"TacticalActor\", \"Leader1\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.GetActiveActor", Description = "Get currently selected actor", Signature = "EntityCombat.GetActiveActor()", InsertText = "var actor = EntityCombat.GetActiveActor();", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpMovement()
    {
        return new LuaApiItem
        {
            Name = "Movement",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntityMovement.MoveTo", Description = "Move actor to tile", Signature = "EntityMovement.MoveTo(actor, x, y)", InsertText = "EntityMovement.MoveTo(actor, x, y);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityMovement.Teleport", Description = "Teleport actor instantly", Signature = "EntityMovement.Teleport(actor, x, y)", InsertText = "EntityMovement.Teleport(actor, x, y);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityMovement.GetPosition", Description = "Get actor position", Signature = "EntityMovement.GetPosition(actor)", InsertText = "var (x, y) = EntityMovement.GetPosition(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityMovement.GetActionPoints", Description = "Get remaining AP", Signature = "EntityMovement.GetActionPoints(actor)", InsertText = "int ap = EntityMovement.GetActionPoints(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityMovement.SetActionPoints", Description = "Set action points", Signature = "EntityMovement.SetActionPoints(actor, ap)", InsertText = "EntityMovement.SetActionPoints(actor, 100);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpCombat()
    {
        return new LuaApiItem
        {
            Name = "Combat",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntityCombat.Attack", Description = "Attack a target", Signature = "EntityCombat.Attack(attacker, target)", InsertText = "EntityCombat.Attack(attacker, target);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.UseAbility", Description = "Use a skill on target", Signature = "EntityCombat.UseAbility(actor, skill, target)", InsertText = "EntityCombat.UseAbility(actor, \"Overwatch\", target);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.GetSkills", Description = "Get all actor skills", Signature = "EntityCombat.GetSkills(actor)", InsertText = "var skills = EntityCombat.GetSkills(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.GetHealth", Description = "Get actor HP info", Signature = "EntityCombat.GetHealth(actor)", InsertText = "var (current, max) = EntityCombat.GetHealth(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.SetHealth", Description = "Set actor HP", Signature = "EntityCombat.SetHealth(actor, hp)", InsertText = "EntityCombat.SetHealth(actor, 100);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.ApplyDamage", Description = "Apply damage", Signature = "EntityCombat.ApplyDamage(actor, amount)", InsertText = "EntityCombat.ApplyDamage(actor, 25);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityCombat.Heal", Description = "Heal actor", Signature = "EntityCombat.Heal(actor, amount)", InsertText = "EntityCombat.Heal(actor, 50);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpTacticalState()
    {
        return new LuaApiItem
        {
            Name = "Tactical State",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "TacticalController.GetCurrentRound", Description = "Get current round", Signature = "TacticalController.GetCurrentRound()", InsertText = "int round = TacticalController.GetCurrentRound();", ItemType = LuaApiItemType.Function },
                new() { Name = "TacticalController.GetCurrentFaction", Description = "Get active faction", Signature = "TacticalController.GetCurrentFaction()", InsertText = "int faction = TacticalController.GetCurrentFaction();", ItemType = LuaApiItemType.Function },
                new() { Name = "TacticalController.IsPlayerTurn", Description = "Check if player's turn", Signature = "TacticalController.IsPlayerTurn()", InsertText = "if (TacticalController.IsPlayerTurn())\n{\n    // Player's turn\n}", ItemType = LuaApiItemType.Function },
                new() { Name = "TacticalController.EndTurn", Description = "End current turn", Signature = "TacticalController.EndTurn()", InsertText = "TacticalController.EndTurn();", ItemType = LuaApiItemType.Function },
                new() { Name = "TacticalController.SetPaused", Description = "Pause/unpause", Signature = "TacticalController.SetPaused(paused)", InsertText = "TacticalController.SetPaused(true);", ItemType = LuaApiItemType.Function },
                new() { Name = "TacticalController.SetTimeScale", Description = "Set game speed", Signature = "TacticalController.SetTimeScale(scale)", InsertText = "TacticalController.SetTimeScale(2.0f);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpTileMap()
    {
        return new LuaApiItem
        {
            Name = "TileMap",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "TileMap.GetTile", Description = "Get tile at position", Signature = "TileMap.GetTile(x, z)", InsertText = "var tile = TileMap.GetTile(x, z);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileMap.GetCover", Description = "Get cover in direction", Signature = "TileMap.GetCover(x, z, direction)", InsertText = "int cover = TileMap.GetCover(x, z, 0);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileMap.IsBlocked", Description = "Check if impassable", Signature = "TileMap.IsBlocked(x, z)", InsertText = "if (!TileMap.IsBlocked(x, z))\n{\n    // Can move here\n}", ItemType = LuaApiItemType.Function },
                new() { Name = "TileMap.GetMapInfo", Description = "Get map dimensions", Signature = "TileMap.GetMapInfo()", InsertText = "var info = TileMap.GetMapInfo();", ItemType = LuaApiItemType.Function },
                new() { Name = "LineOfSight.HasLOS", Description = "Check line of sight", Signature = "LineOfSight.HasLOS(from, to)", InsertText = "bool hasLos = LineOfSight.HasLOS(fromTile, toTile);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpSpawn()
    {
        return new LuaApiItem
        {
            Name = "Spawn",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntitySpawner.SpawnUnit", Description = "Spawn unit at position", Signature = "EntitySpawner.SpawnUnit(template, x, y, faction)", InsertText = "var entity = EntitySpawner.SpawnUnit(\"Grunt\", x, y, Faction.Enemy);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySpawner.DestroyEntity", Description = "Kill an entity", Signature = "EntitySpawner.DestroyEntity(actor, immediate)", InsertText = "EntitySpawner.DestroyEntity(actor, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySpawner.ClearEnemies", Description = "Remove all enemies", Signature = "EntitySpawner.ClearEnemies(immediate)", InsertText = "int cleared = EntitySpawner.ClearEnemies(true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySpawner.ListEntities", Description = "List entities by faction", Signature = "EntitySpawner.ListEntities(faction)", InsertText = "var enemies = EntitySpawner.ListEntities(Faction.Enemy);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpTileEffects()
    {
        return new LuaApiItem
        {
            Name = "Tile Effects",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "TileEffects.GetEffects", Description = "Get effects on tile", Signature = "TileEffects.GetEffects(x, z)", InsertText = "var effects = TileEffects.GetEffects(x, z);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileEffects.SpawnFire", Description = "Spawn fire effect", Signature = "TileEffects.SpawnFire(x, z)", InsertText = "TileEffects.SpawnFire(x, z);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileEffects.SpawnSmoke", Description = "Spawn smoke effect", Signature = "TileEffects.SpawnSmoke(x, z)", InsertText = "TileEffects.SpawnSmoke(x, z);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileEffects.ClearEffects", Description = "Clear all effects", Signature = "TileEffects.ClearEffects(x, z)", InsertText = "TileEffects.ClearEffects(x, z);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpInventory()
    {
        return new LuaApiItem
        {
            Name = "Inventory",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Inventory.GetContainer", Description = "Get actor's inventory", Signature = "Inventory.GetContainer(actor)", InsertText = "var container = Inventory.GetContainer(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.GetAllItems", Description = "Get all items", Signature = "Inventory.GetAllItems(container)", InsertText = "var items = Inventory.GetAllItems(container);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.GetEquippedWeapons", Description = "Get equipped weapons", Signature = "Inventory.GetEquippedWeapons(actor)", InsertText = "var weapons = Inventory.GetEquippedWeapons(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.RemoveItem", Description = "Remove specific item", Signature = "Inventory.RemoveItem(container, item)", InsertText = "Inventory.RemoveItem(container, item);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.RemoveItemAt", Description = "Remove item at slot index", Signature = "Inventory.RemoveItemAt(container, slotType, index)", InsertText = "Inventory.RemoveItemAt(container, 0, 0);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.TransferItem", Description = "Transfer item between containers", Signature = "Inventory.TransferItem(from, to, item)", InsertText = "Inventory.TransferItem(fromContainer, toContainer, item);", ItemType = LuaApiItemType.Function },
                new() { Name = "Inventory.ClearInventory", Description = "Remove all items from container", Signature = "Inventory.ClearInventory(container, slotType = -1)", InsertText = "int removed = Inventory.ClearInventory(container);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpEntityState()
    {
        return new LuaApiItem
        {
            Name = "Entity State",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntityState.SetHeavyWeaponDeployed", Description = "Set heavy weapon deployment", Signature = "EntityState.SetHeavyWeaponDeployed(actor, deployed)", InsertText = "EntityState.SetHeavyWeaponDeployed(actor, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.ToggleHeavyWeapon", Description = "Toggle deployment on/off", Signature = "EntityState.ToggleHeavyWeapon(actor)", InsertText = "EntityState.ToggleHeavyWeapon(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.SetDetectedByFaction", Description = "Set faction detection", Signature = "EntityState.SetDetectedByFaction(actor, faction, detected)", InsertText = "EntityState.SetDetectedByFaction(actor, 1, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.RevealToAll", Description = "Reveal to all factions", Signature = "EntityState.RevealToAll(actor)", InsertText = "EntityState.RevealToAll(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.ConcealFromAll", Description = "Conceal from all factions", Signature = "EntityState.ConcealFromAll(actor)", InsertText = "EntityState.ConcealFromAll(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.SetHiddenToAI", Description = "Hide/reveal from AI", Signature = "EntityState.SetHiddenToAI(actor, hidden)", InsertText = "EntityState.SetHiddenToAI(actor, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.SetDying", Description = "Set dying state flag", Signature = "EntityState.SetDying(actor, dying)", InsertText = "EntityState.SetDying(actor, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.SetLeavingMap", Description = "Set leaving map state", Signature = "EntityState.SetLeavingMap(actor, leaving)", InsertText = "EntityState.SetLeavingMap(actor, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityState.GetStateFlags", Description = "Query all state flags", Signature = "EntityState.GetStateFlags(actor)", InsertText = "var flags = EntityState.GetStateFlags(actor);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpEntitySkills()
    {
        return new LuaApiItem
        {
            Name = "Entity Skills",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntitySkills.AddSkill", Description = "Add skill to actor", Signature = "EntitySkills.AddSkill(actor, skillTemplateID)", InsertText = "EntitySkills.AddSkill(actor, \"skill.overwatch\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.RemoveSkill", Description = "Remove skill from actor", Signature = "EntitySkills.RemoveSkill(actor, skillID)", InsertText = "EntitySkills.RemoveSkill(actor, \"skill.overwatch\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.HasSkill", Description = "Check if actor has skill", Signature = "EntitySkills.HasSkill(actor, skillID)", InsertText = "if (EntitySkills.HasSkill(actor, \"skill.overwatch\"))\n{\n    // Has skill\n}", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.GetSkillIDs", Description = "List all skill IDs", Signature = "EntitySkills.GetSkillIDs(actor)", InsertText = "var skills = EntitySkills.GetSkillIDs(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.SetCooldown", Description = "Set skill cooldown", Signature = "EntitySkills.SetCooldown(actor, skillID, turns)", InsertText = "EntitySkills.SetCooldown(actor, \"skill.ultimate\", 3);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.ResetCooldown", Description = "Clear skill cooldown", Signature = "EntitySkills.ResetCooldown(actor, skillID)", InsertText = "EntitySkills.ResetCooldown(actor, \"skill.ultimate\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.ModifyCooldown", Description = "Modify cooldown by delta", Signature = "EntitySkills.ModifyCooldown(actor, skillID, delta)", InsertText = "EntitySkills.ModifyCooldown(actor, \"skill.ultimate\", -1);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.GetRemainingCooldown", Description = "Query current cooldown", Signature = "EntitySkills.GetRemainingCooldown(actor, skillID)", InsertText = "int cd = EntitySkills.GetRemainingCooldown(actor, \"skill.ultimate\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.ModifySkillRange", Description = "Change skill range", Signature = "EntitySkills.ModifySkillRange(actor, skillID, newRange)", InsertText = "EntitySkills.ModifySkillRange(actor, \"skill.overwatch\", 15);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.ModifySkillAPCost", Description = "Change skill AP cost", Signature = "EntitySkills.ModifySkillAPCost(actor, skillID, newCost)", InsertText = "EntitySkills.ModifySkillAPCost(actor, \"skill.overwatch\", 25);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.EnableSkill", Description = "Enable skill usage", Signature = "EntitySkills.EnableSkill(actor, skillID)", InsertText = "EntitySkills.EnableSkill(actor, \"skill.overwatch\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.DisableSkill", Description = "Disable skill usage", Signature = "EntitySkills.DisableSkill(actor, skillID)", InsertText = "EntitySkills.DisableSkill(actor, \"skill.overwatch\");", ItemType = LuaApiItemType.Function },
                new() { Name = "EntitySkills.GetSkillState", Description = "Get comprehensive skill info", Signature = "EntitySkills.GetSkillState(actor, skillID)", InsertText = "var state = EntitySkills.GetSkillState(actor, \"skill.overwatch\");", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpEntityAI()
    {
        return new LuaApiItem
        {
            Name = "Entity AI",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntityAI.PauseAI", Description = "Pause AI for actor", Signature = "EntityAI.PauseAI(actor)", InsertText = "EntityAI.PauseAI(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.ResumeAI", Description = "Resume AI for actor", Signature = "EntityAI.ResumeAI(actor)", InsertText = "EntityAI.ResumeAI(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.IsAIPaused", Description = "Check if AI is paused", Signature = "EntityAI.IsAIPaused(actor)", InsertText = "if (EntityAI.IsAIPaused(actor))\n{\n    // AI paused\n}", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.ForceFleeDecision", Description = "Force actor to flee (morale 0)", Signature = "EntityAI.ForceFleeDecision(actor)", InsertText = "EntityAI.ForceFleeDecision(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.BlockFleeDecision", Description = "Prevent fleeing (morale 100)", Signature = "EntityAI.BlockFleeDecision(actor)", InsertText = "EntityAI.BlockFleeDecision(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.SetThreatValueOverride", Description = "Override threat value", Signature = "EntityAI.SetThreatValueOverride(actor, target, threat)", InsertText = "EntityAI.SetThreatValueOverride(actor, target, 100.0f);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityAI.ClearThreatOverrides", Description = "Clear threat overrides", Signature = "EntityAI.ClearThreatOverrides(actor)", InsertText = "EntityAI.ClearThreatOverrides(actor);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpEntityVisibility()
    {
        return new LuaApiItem
        {
            Name = "Entity Visibility",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "EntityVisibility.RevealToFaction", Description = "Reveal to specific faction", Signature = "EntityVisibility.RevealToFaction(actor, factionIndex)", InsertText = "EntityVisibility.RevealToFaction(actor, 1);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityVisibility.ConcealFromFaction", Description = "Conceal from specific faction", Signature = "EntityVisibility.ConcealFromFaction(actor, factionIndex)", InsertText = "EntityVisibility.ConcealFromFaction(actor, 1);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityVisibility.SetDetectionMask", Description = "Set entire detection bitmask", Signature = "EntityVisibility.SetDetectionMask(actor, bitmask)", InsertText = "EntityVisibility.SetDetectionMask(actor, 0b0110);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityVisibility.GetDetectionMask", Description = "Get detection bitmask", Signature = "EntityVisibility.GetDetectionMask(actor)", InsertText = "int mask = EntityVisibility.GetDetectionMask(actor);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityVisibility.ForceVisibleTo", Description = "Temporary visibility (N turns)", Signature = "EntityVisibility.ForceVisibleTo(actor, viewer, turns = 1)", InsertText = "EntityVisibility.ForceVisibleTo(actor, viewer, 3);", ItemType = LuaApiItemType.Function },
                new() { Name = "EntityVisibility.ForceConcealedFrom", Description = "Temporary concealment (N turns)", Signature = "EntityVisibility.ForceConcealedFrom(actor, viewer, turns = 1)", InsertText = "EntityVisibility.ForceConcealedFrom(actor, viewer, 3);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpTileManipulation()
    {
        return new LuaApiItem
        {
            Name = "Tile Manipulation",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "TileManipulation.SetTraversableOverride", Description = "Set tile traversability", Signature = "TileManipulation.SetTraversableOverride(tile, traversable, turns = -1)", InsertText = "TileManipulation.SetTraversableOverride(tile, false, 3);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileManipulation.ClearTraversableOverride", Description = "Clear traversability override", Signature = "TileManipulation.ClearTraversableOverride(tile)", InsertText = "TileManipulation.ClearTraversableOverride(tile);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileManipulation.SetCoverOverride", Description = "Set cover value", Signature = "TileManipulation.SetCoverOverride(tile, direction, cover, turns = -1)", InsertText = "TileManipulation.SetCoverOverride(tile, 0, 3.0f, 5);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileManipulation.ClearCoverOverrides", Description = "Clear all cover overrides", Signature = "TileManipulation.ClearCoverOverrides(tile)", InsertText = "TileManipulation.ClearCoverOverrides(tile);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileManipulation.SetBlocksLOS", Description = "Block/unblock line of sight", Signature = "TileManipulation.SetBlocksLOS(tile, blocks, turns = -1)", InsertText = "TileManipulation.SetBlocksLOS(tile, true);", ItemType = LuaApiItemType.Function },
                new() { Name = "TileManipulation.SetBlocksMovement", Description = "Block/unblock movement", Signature = "TileManipulation.SetBlocksMovement(tile, blocks, turns = -1)", InsertText = "TileManipulation.SetBlocksMovement(tile, true);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpTacticalEvents()
    {
        return new LuaApiItem
        {
            Name = "Tactical Events",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "GameState.TacticalReady", Description = "Subscribe to tactical ready", Signature = "GameState.TacticalReady += handler", InsertText = "GameState.TacticalReady += () =>\n{\n    // Mission started\n};", ItemType = LuaApiItemType.Event },
                new() { Name = "GameState.SceneLoaded", Description = "Subscribe to scene loads", Signature = "GameState.SceneLoaded += handler", InsertText = "GameState.SceneLoaded += (buildIndex, sceneName) =>\n{\n    DevConsole.Log($\"Scene: {sceneName}\");\n};", ItemType = LuaApiItemType.Event },
                new() { Name = "GameState.RunDelayed", Description = "Run callback after N frames", Signature = "GameState.RunDelayed(frames, callback)", InsertText = "GameState.RunDelayed(30, () =>\n{\n    // Run after 30 frames\n});", ItemType = LuaApiItemType.Function },
                new() { Name = "GameState.RunWhen", Description = "Run when condition is true", Signature = "GameState.RunWhen(predicate, callback)", InsertText = "GameState.RunWhen(\n    () => TacticalController.IsReady(),\n    () => { /* Ready */ }\n);", ItemType = LuaApiItemType.Function }
            }
        };
    }

    private static LuaApiItem GetCSharpStrategyEvents()
    {
        return new LuaApiItem
        {
            Name = "Strategy Events",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Roster.OnLeaderHired", Description = "Leader joins roster", Signature = "Roster.OnLeaderHired += handler", InsertText = "Roster.OnLeaderHired += (leader) =>\n{\n    DevConsole.Log($\"Hired: {leader.Name}\");\n};", ItemType = LuaApiItemType.Event },
                new() { Name = "Roster.OnLeaderDismissed", Description = "Leader dismissed", Signature = "Roster.OnLeaderDismissed += handler", InsertText = "Roster.OnLeaderDismissed += (leader) =>\n{\n    DevConsole.Log($\"Dismissed: {leader.Name}\");\n};", ItemType = LuaApiItemType.Event }
            }
        };
    }

    private static LuaApiItem GetCSharpPropertyInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Property Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnGetDamage", Description = "Intercept damage calculations", Signature = "Intercept.OnGetDamage += (props, owner, ref result) => { }", InsertText = "Intercept.OnGetDamage += (GameObj props, GameObj owner, ref float result) =>\n{\n    result *= 1.5f;  // +50% damage\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetAccuracy", Description = "Intercept accuracy calculations", Signature = "Intercept.OnGetAccuracy += (props, owner, ref result) => { }", InsertText = "Intercept.OnGetAccuracy += (GameObj props, GameObj owner, ref float result) =>\n{\n    result += 10f;  // +10 accuracy\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetArmor", Description = "Intercept armor calculations", Signature = "Intercept.OnGetArmor += (props, owner, ref result) => { }", InsertText = "Intercept.OnGetArmor += (GameObj props, GameObj owner, ref int result) =>\n{\n    result += 2;  // +2 armor\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetMaxHitpoints", Description = "Intercept max HP", Signature = "Intercept.OnGetMaxHitpoints += (props, owner, ref result) => { }", InsertText = "Intercept.OnGetMaxHitpoints += (GameObj props, GameObj owner, ref int result) =>\n{\n    result = (int)(result * 1.25f);  // +25% HP\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnPropertyUpdate", Description = "Intercept additive stat bonuses", Signature = "Intercept.OnPropertyUpdate += (props, propertyType, ref amount) => { }", InsertText = "Intercept.OnPropertyUpdate += (GameObj props, int propertyType, ref int amount) =>\n{\n    amount *= 2;  // Double all additive bonuses\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnPropertyUpdateMult", Description = "Intercept multiplicative stat modifiers", Signature = "Intercept.OnPropertyUpdateMult += (props, propertyType, ref multiplier) => { }", InsertText = "Intercept.OnPropertyUpdateMult += (GameObj props, int propertyType, ref float multiplier) =>\n{\n    multiplier *= 1.5f;  // Boost multipliers by 50%\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpSkillInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Skill Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnGetHitChance", Description = "Intercept hit chance", Signature = "Intercept.OnGetHitChance += (skill, attacker, target, ref result) => { }", InsertText = "Intercept.OnGetHitChance += (GameObj skill, GameObj attacker, GameObj target, ref HitChanceResult result) =>\n{\n    result.FinalChance = Math.Min(result.FinalChance + 0.1f, 1.0f);  // +10% hit\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetActionPointCost", Description = "Intercept AP cost", Signature = "Intercept.OnGetActionPointCost += (skill, ref result) => { }", InsertText = "Intercept.OnGetActionPointCost += (GameObj skill, ref int result) =>\n{\n    result = Math.Max(1, result - 1);  // -1 AP (min 1)\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetExpectedDamage", Description = "Intercept damage preview", Signature = "Intercept.OnGetExpectedDamage += (skill, attacker, target, ref result) => { }", InsertText = "Intercept.OnGetExpectedDamage += (GameObj skill, GameObj attacker, GameObj target, ref ExpectedDamageResult result) =>\n{\n    result.Damage *= 1.5f;\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnSkillIsUsable", Description = "Intercept skill availability check", Signature = "Intercept.OnSkillIsUsable += (skill, actor, ref result) => { }", InsertText = "Intercept.OnSkillIsUsable += (GameObj skill, GameObj actor, ref bool result) =>\n{\n    // result = true;  // Force skill available\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpTileInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Tile Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnGetCover", Description = "Intercept cover values", Signature = "Intercept.OnGetCover += (tile, dir, ref result) => { }", InsertText = "Intercept.OnGetCover += (GameObj tile, int dir, ref int result) =>\n{\n    result = 3;  // Force full cover\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnHasLineOfSight", Description = "Intercept LOS checks", Signature = "Intercept.OnHasLineOfSight += (from, to, ref result) => { }", InsertText = "Intercept.OnHasLineOfSight += (GameObj from, GameObj to, ref bool result) =>\n{\n    // result = true;  // Grant LoS\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpMovementInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Movement Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnGetMaxSpeed", Description = "Intercept movement speed", Signature = "Intercept.OnGetMaxSpeed += (instance, mode, ref result) => { }", InsertText = "Intercept.OnGetMaxSpeed += (GameObj instance, int mode, ref float result) =>\n{\n    result *= 1.5f;  // +50% speed\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnGetPathCost", Description = "Intercept path AP cost", Signature = "Intercept.OnGetPathCost += (actor, path, ref result) => { }", InsertText = "Intercept.OnGetPathCost += (GameObj actor, GameObj path, ref int result) =>\n{\n    result = (int)(result * 0.75f);  // -25% move cost\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnTileTraversable", Description = "Intercept tile traversability (⚠️ high-frequency)", Signature = "Intercept.OnTileTraversable += (process, tile, ref result) => { }", InsertText = "Intercept.OnTileTraversable += (GameObj process, GameObj tile, ref bool result) =>\n{\n    // result = false;  // Block tile\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnPathfinding", Description = "Intercept pathfinding calculation", Signature = "Intercept.OnPathfinding += (process, start, end, ref pathResult, ref cancel) => { }", InsertText = "Intercept.OnPathfinding += (GameObj process, GameObj start, GameObj end, ref IntPtr pathResult, ref bool cancel) =>\n{\n    // cancel = true;  // Prevent pathfinding\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnMoveTo", Description = "Intercept actor movement (master hook)", Signature = "Intercept.OnMoveTo += (actor, tile, flags, ref cancel) => { }", InsertText = "Intercept.OnMoveTo += (GameObj actor, GameObj tile, int flags, ref bool cancel) =>\n{\n    // cancel = true;  // Prevent movement\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpStrategyInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Strategy Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnStrategyGetActionPoints", Description = "Intercept strategy AP", Signature = "Intercept.OnStrategyGetActionPoints += (instance, ref result) => { }", InsertText = "Intercept.OnStrategyGetActionPoints += (GameObj instance, ref int result) =>\n{\n    result += 2;  // +2 strategy AP\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnCanBePromoted", Description = "Intercept promotion checks", Signature = "Intercept.OnCanBePromoted += (leader, ref result) => { }", InsertText = "Intercept.OnCanBePromoted += (GameObj leader, ref bool result) =>\n{\n    result = true;  // Always allow promotion\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpAIInterceptors()
    {
        return new LuaApiItem
        {
            Name = "AI Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnAIGetAttackScore", Description = "Intercept AI target scoring", Signature = "Intercept.OnAIGetAttackScore += (ai, target, ref result) => { }", InsertText = "Intercept.OnAIGetAttackScore += (GameObj ai, GameObj target, ref float result) =>\n{\n    result *= 0.5f;  // Make targets less attractive\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnAIShouldFlee", Description = "Intercept flee decisions", Signature = "Intercept.OnAIShouldFlee += (ai, context, ref result) => { }", InsertText = "Intercept.OnAIShouldFlee += (GameObj ai, GameObj context, ref bool result) =>\n{\n    result = false;  // Never flee\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnAIEvaluate", Description = "Intercept AI evaluation start (⚠️ thread-safe required)", Signature = "Intercept.OnAIEvaluate += (agent, ref cancel) => { }", InsertText = "Intercept.OnAIEvaluate += (GameObj agent, ref bool cancel) =>\n{\n    // cancel = true;  // Skip AI evaluation\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnPositionScore", Description = "Intercept AI position scoring (⚠️ parallel, thread-safe required)", Signature = "Intercept.OnPositionScore += (criterion, tile, ref score) => { }", InsertText = "Intercept.OnPositionScore += (GameObj criterion, GameObj tile, ref float score) =>\n{\n    // Modify position score (thread-safe only!)\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpCombatInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Combat Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new() { Name = "Intercept.OnDamageApplied", Description = "Intercept damage application", Signature = "Intercept.OnDamageApplied += (handler, target, attacker, skill, ref damage, ref cancel) => { }", InsertText = "Intercept.OnDamageApplied += (GameObj handler, GameObj target, GameObj attacker, GameObj skill, ref float damage, ref bool cancel) =>\n{\n    damage *= 1.5f;  // +50% damage\n    // cancel = true;  // Prevent damage\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnSuppressionApplied", Description = "Intercept suppression application", Signature = "Intercept.OnSuppressionApplied += (actor, attacker, ref amount, ref isFriendlyFire, ref cancel) => { }", InsertText = "Intercept.OnSuppressionApplied += (GameObj actor, GameObj attacker, ref float amount, ref bool isFriendlyFire, ref bool cancel) =>\n{\n    amount *= 0.5f;  // -50% suppression\n    // cancel = true;  // Prevent suppression\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnMoraleApplied", Description = "Intercept morale changes", Signature = "Intercept.OnMoraleApplied += (actor, eventType, ref amount, ref cancel) => { }", InsertText = "Intercept.OnMoraleApplied += (GameObj actor, int eventType, ref float amount, ref bool cancel) =>\n{\n    amount *= 1.5f;  // Amplify morale changes\n    // cancel = true;  // Prevent morale change\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true },
                new() { Name = "Intercept.OnItemAdd", Description = "Intercept item added to container", Signature = "Intercept.OnItemAdd += (container, item, ref expandSlots, ref cancel) => { }", InsertText = "Intercept.OnItemAdd += (GameObj container, GameObj item, ref bool expandSlots, ref bool cancel) =>\n{\n    // cancel = true;  // Prevent item from being added\n};", ItemType = LuaApiItemType.Event, IsInterceptor = true }
            }
        };
    }

    private static LuaApiItem GetCSharpSdkClasses()
    {
        return new LuaApiItem
        {
            Name = "SDK Classes",
            ItemType = LuaApiItemType.Category,
            IsExpanded = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "GameObj",
                    Description = "Wrapper for IL2CPP game objects with safe pointer handling.",
                    Signature = "new GameObj(IntPtr pointer)",
                    InsertText = "var obj = new GameObj(pointer);\nvar name = obj.GetName();\nvar typeName = obj.GetTypeName();",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "GameType.Find",
                    Description = "Find a game type by full name.",
                    Signature = "GameType.Find(string typeName)",
                    InsertText = "var actorType = GameType.Find(\"Menace.Tactical.Actor\");",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "Templates.Find",
                    Description = "Find a game template by type and name.",
                    Signature = "Templates.Find(string typeName, string templateName)",
                    InsertText = "var template = Templates.Find(\"Menace.Tactical.EntityTemplate\", \"Grunt\");",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "TileMap.GetTile",
                    Description = "Get a tile at coordinates.",
                    Signature = "TileMap.GetTile(int x, int z)",
                    InsertText = "var tile = TileMap.GetTile(5, 10);",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "DevConsole.RegisterCommand",
                    Description = "Register a console command.",
                    Signature = "DevConsole.RegisterCommand(name, args, description, callback)",
                    InsertText = "DevConsole.RegisterCommand(\"mycommand\", \"<arg>\", \"Description\", args =>\n{\n    return \"Result\";\n});",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "ModError.Info/Warn/Report",
                    Description = "Log messages with mod attribution.",
                    Signature = "ModError.Info(modName, message)",
                    InsertText = "ModError.Info(\"MyMod\", \"Something happened\");\nModError.Warn(\"MyMod\", \"Warning!\");\nModError.Report(\"MyMod\", \"Error details\", exception);",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetCSharpPatchSet()
    {
        return new LuaApiItem
        {
            Name = "PatchSet (Fluent Harmony)",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "PatchSet basics",
                    Description = "Fluent builder for Harmony patches. Reduces boilerplate.",
                    Signature = "new PatchSet(harmony, modName).Postfix<T>(method, patch).Apply()",
                    InsertText = "var result = new PatchSet(harmony, \"MyMod\")\n    .Postfix<EntityProperties>(\"GetDamage\", GetDamage_Postfix)\n    .Postfix<EntityProperties>(\"GetAccuracy\", GetAccuracy_Postfix)\n    .Apply();\n\nif (!result.AllSucceeded)\n    ModError.Warn(\"MyMod\", $\"Failed patches: {string.Join(\", \", result.FailedPatches)}\");",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PatchSet.Prefix",
                    Description = "Add a prefix patch (runs before original method).",
                    Signature = ".Prefix<T>(methodName, patchDelegate)",
                    InsertText = ".Prefix<Actor>(\"TakeDamage\", TakeDamage_Prefix)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PatchSet.Postfix",
                    Description = "Add a postfix patch (runs after original method).",
                    Signature = ".Postfix<T>(methodName, patchDelegate)",
                    InsertText = ".Postfix<EntityProperties>(\"GetDamage\", GetDamage_Postfix)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PatchSet.PrefixPostfix",
                    Description = "Add both prefix and postfix to the same method.",
                    Signature = ".PrefixPostfix<T>(methodName, paramTypes, prefix, postfix)",
                    InsertText = ".PrefixPostfix<UnitPanel>(\"Update\",\n    new[] { typeof(BaseUnitLeader) },\n    MyPrefix, MyPostfix,\n    optional: true)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetCSharpPointerCache()
    {
        return new LuaApiItem
        {
            Name = "PointerCache (IL2CPP Tracking)",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "PointerCache<T>",
                    Description = "Dictionary mapping IL2CPP pointers to values. Safe null handling.",
                    Signature = "new PointerCache<T>(concurrent: false)",
                    InsertText = "private static readonly PointerCache<float> s_BaseMult = new();\n\n// Usage:\ns_BaseMult.Set(props.Pointer, 1.5f);\nvar val = s_BaseMult.Get(props.Pointer, defaultValue: 1.0f);",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PointerCache.Set",
                    Description = "Store a value for a pointer. Safe for IntPtr.Zero.",
                    Signature = "cache.Set(IntPtr pointer, TValue value)",
                    InsertText = "cache.Set(actor.Pointer, myValue);",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PointerCache.Get",
                    Description = "Get a value for a pointer with default fallback.",
                    Signature = "cache.Get(IntPtr pointer, TValue defaultValue = default)",
                    InsertText = "var value = cache.Get(actor.Pointer, defaultValue: 1.0f);",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PointerCache.TryGet",
                    Description = "Try to get a value, returns false if not found.",
                    Signature = "cache.TryGet(IntPtr pointer, out TValue value)",
                    InsertText = "if (cache.TryGet(actor.Pointer, out var value))\n{\n    // Use value\n}",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "PointerCache.GetOrSet",
                    Description = "Get existing or create new value atomically.",
                    Signature = "cache.GetOrSet(IntPtr pointer, Func<TValue> factory)",
                    InsertText = "var value = cache.GetOrSet(actor.Pointer, () => ComputeDefault());",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "GameObjCache<T>",
                    Description = "Specialized cache that works with GameObj directly.",
                    Signature = "new GameObjCache<T>()",
                    InsertText = "private static readonly GameObjCache<MyData> s_Data = new();\n\ns_Data.Set(gameObj, myData);\nvar data = s_Data.Get(gameObj);",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetCoreFunctions()
    {
        return new LuaApiItem
        {
            Name = "Core Functions",
            ItemType = LuaApiItemType.Category,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "log",
                    Description = "Print a message to the game log",
                    Signature = "log(message)",
                    InsertText = "log(\"\")",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "on",
                    Description = "Register an event handler",
                    Signature = "on(eventName, callback)",
                    InsertText = "on(\"event_name\", function(data)\n    \nend)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "cmd",
                    Description = "Register a console command",
                    Signature = "cmd(name, callback)",
                    InsertText = "cmd(\"my_command\", function(args)\n    log(\"Command executed\")\n    return \"Done\"\nend)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetActorQueryApi()
    {
        return new LuaApiItem
        {
            Name = "Actor Query",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "get_actors",
                    Description = "Get all actors in the mission",
                    Signature = "get_actors() -> table",
                    InsertText = "local actors = get_actors()\nfor i, actor in ipairs(actors) do\n    log(actor.name)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_player_actors",
                    Description = "Get player-controlled actors",
                    Signature = "get_player_actors() -> table",
                    InsertText = "local players = get_player_actors()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_enemy_actors",
                    Description = "Get enemy actors",
                    Signature = "get_enemy_actors() -> table",
                    InsertText = "local enemies = get_enemy_actors()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "find_actor",
                    Description = "Find actor by name",
                    Signature = "find_actor(name) -> actor",
                    InsertText = "local actor = find_actor(\"ActorName\")",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_active_actor",
                    Description = "Get currently selected actor",
                    Signature = "get_active_actor() -> actor",
                    InsertText = "local actor = get_active_actor()",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetMovementApi()
    {
        return new LuaApiItem
        {
            Name = "Movement",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "move_to",
                    Description = "Move actor to tile position",
                    Signature = "move_to(actor, x, y) -> {success, error}",
                    InsertText = "local result = move_to(actor, x, y)\nif result.success then log(\"Moving\") end",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "teleport",
                    Description = "Teleport actor instantly",
                    Signature = "teleport(actor, x, y) -> {success, error}",
                    InsertText = "teleport(actor, x, y)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_position",
                    Description = "Get actor's tile position",
                    Signature = "get_position(actor) -> {x, y}",
                    InsertText = "local pos = get_position(actor)\nlog(\"Position: \" .. pos.x .. \", \" .. pos.y)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_ap",
                    Description = "Get remaining action points",
                    Signature = "get_ap(actor) -> number",
                    InsertText = "local ap = get_ap(actor)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_ap",
                    Description = "Set action points",
                    Signature = "set_ap(actor, ap) -> boolean",
                    InsertText = "set_ap(actor, 10)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_facing",
                    Description = "Get facing direction (0-7)",
                    Signature = "get_facing(actor) -> number",
                    InsertText = "local dir = get_facing(actor)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_facing",
                    Description = "Set facing direction (0-7)",
                    Signature = "set_facing(actor, dir) -> boolean",
                    InsertText = "set_facing(actor, 0)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_moving",
                    Description = "Check if actor is currently moving",
                    Signature = "is_moving(actor) -> boolean",
                    InsertText = "if not is_moving(actor) then\n    -- actor is stationary\nend",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetCombatApi()
    {
        return new LuaApiItem
        {
            Name = "Combat",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "attack",
                    Description = "Attack a target",
                    Signature = "attack(attacker, target) -> {success, error, skill, damage}",
                    InsertText = "local result = attack(attacker, target)\nif result.success then\n    log(\"Dealt \" .. result.damage .. \" damage\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "use_ability",
                    Description = "Use a skill/ability on target",
                    Signature = "use_ability(actor, skillName, target?) -> {success, error, skill}",
                    InsertText = "use_ability(actor, \"Overwatch\")",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_skills",
                    Description = "Get all actor skills",
                    Signature = "get_skills(actor) -> table",
                    InsertText = "local skills = get_skills(actor)\nfor i, skill in ipairs(skills) do\n    log(skill.name .. \" - AP: \" .. skill.ap_cost)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_hp",
                    Description = "Get actor HP info",
                    Signature = "get_hp(actor) -> {current, max, percent}",
                    InsertText = "local hp = get_hp(actor)\nlog(\"HP: \" .. hp.current .. \"/\" .. hp.max)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_hp",
                    Description = "Set actor HP value",
                    Signature = "set_hp(actor, hp) -> boolean",
                    InsertText = "set_hp(actor, 100)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "damage",
                    Description = "Apply damage to actor",
                    Signature = "damage(actor, amount) -> boolean",
                    InsertText = "damage(actor, 25)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "heal",
                    Description = "Heal actor",
                    Signature = "heal(actor, amount) -> boolean",
                    InsertText = "heal(actor, 50)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_suppression",
                    Description = "Get suppression level (0-100)",
                    Signature = "get_suppression(actor) -> number",
                    InsertText = "local supp = get_suppression(actor)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_suppression",
                    Description = "Set suppression level",
                    Signature = "set_suppression(actor, value) -> boolean",
                    InsertText = "set_suppression(actor, 0)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_morale",
                    Description = "Get actor morale",
                    Signature = "get_morale(actor) -> number",
                    InsertText = "local morale = get_morale(actor)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_morale",
                    Description = "Set actor morale",
                    Signature = "set_morale(actor, value) -> boolean",
                    InsertText = "set_morale(actor, 100)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_stunned",
                    Description = "Set stunned state",
                    Signature = "set_stunned(actor, bool) -> boolean",
                    InsertText = "set_stunned(actor, true)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_combat_info",
                    Description = "Get full combat state",
                    Signature = "get_combat_info(actor) -> table",
                    InsertText = "local info = get_combat_info(actor)\nlog(\"HP: \" .. info.hp .. \" Morale: \" .. info.morale)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetTacticalStateApi()
    {
        return new LuaApiItem
        {
            Name = "Tactical State",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "is_mission_running",
                    Description = "Check if mission is active",
                    Signature = "is_mission_running() -> boolean",
                    InsertText = "if is_mission_running() then\n    -- in tactical\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_round",
                    Description = "Get current round number",
                    Signature = "get_round() -> number",
                    InsertText = "local round = get_round()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_faction",
                    Description = "Get current faction ID",
                    Signature = "get_faction() -> number",
                    InsertText = "local faction = get_faction()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_faction_name",
                    Description = "Get faction name from ID",
                    Signature = "get_faction_name(id) -> string",
                    InsertText = "local name = get_faction_name(faction)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_player_turn",
                    Description = "Check if it's the player's turn",
                    Signature = "is_player_turn() -> boolean",
                    InsertText = "if is_player_turn() then\n    log(\"Your turn!\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_paused",
                    Description = "Check if game is paused",
                    Signature = "is_paused() -> boolean",
                    InsertText = "if is_paused() then log(\"Game paused\") end",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "pause",
                    Description = "Pause the game",
                    Signature = "pause(bool?) -> boolean",
                    InsertText = "pause()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "unpause",
                    Description = "Unpause the game",
                    Signature = "unpause() -> boolean",
                    InsertText = "unpause()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "toggle_pause",
                    Description = "Toggle pause state",
                    Signature = "toggle_pause() -> boolean",
                    InsertText = "toggle_pause()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "end_turn",
                    Description = "End current turn",
                    Signature = "end_turn() -> boolean",
                    InsertText = "end_turn()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_time_scale",
                    Description = "Get game speed multiplier",
                    Signature = "get_time_scale() -> number",
                    InsertText = "local speed = get_time_scale()",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "set_time_scale",
                    Description = "Set game speed (1.0 = normal)",
                    Signature = "set_time_scale(scale) -> boolean",
                    InsertText = "set_time_scale(2.0)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_tactical_state",
                    Description = "Get full tactical state table",
                    Signature = "get_tactical_state() -> table",
                    InsertText = "local state = get_tactical_state()\nlog(\"Round: \" .. state.round .. \" Enemies: \" .. state.alive_enemies)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetTileMapApi()
    {
        return new LuaApiItem
        {
            Name = "TileMap",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "get_tile_info",
                    Description = "Get info about a tile",
                    Signature = "get_tile_info(x, z) -> table",
                    InsertText = "local tile = get_tile_info(x, z)\nlog(\"Elevation: \" .. tile.elevation)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_cover",
                    Description = "Get cover value in direction (0-3)",
                    Signature = "get_cover(x, z, dir) -> number",
                    InsertText = "local cover = get_cover(x, z, 0)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_all_cover",
                    Description = "Get cover in all 8 directions",
                    Signature = "get_all_cover(x, z) -> table",
                    InsertText = "local cover = get_all_cover(x, z)\nlog(\"North cover: \" .. cover.north)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_blocked",
                    Description = "Check if tile is impassable",
                    Signature = "is_blocked(x, z) -> boolean",
                    InsertText = "if not is_blocked(x, z) then\n    -- can move here\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "has_actor_at",
                    Description = "Check if tile has an actor",
                    Signature = "has_actor_at(x, z) -> boolean",
                    InsertText = "if has_actor_at(x, z) then\n    local actor = get_actor_at(x, z)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_visible",
                    Description = "Check if tile is visible to player",
                    Signature = "is_visible(x, z) -> boolean",
                    InsertText = "if is_visible(x, z) then\n    -- tile is revealed\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_map_info",
                    Description = "Get map dimensions",
                    Signature = "get_map_info() -> {width, height, fog_of_war}",
                    InsertText = "local map = get_map_info()\nlog(\"Map size: \" .. map.width .. \"x\" .. map.height)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_actor_at",
                    Description = "Get actor on a tile",
                    Signature = "get_actor_at(x, z) -> actor",
                    InsertText = "local actor = get_actor_at(x, z)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_distance",
                    Description = "Get distance between tiles",
                    Signature = "get_distance(x1, z1, x2, z2) -> number",
                    InsertText = "local dist = get_distance(x1, z1, x2, z2)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetSpawnApi()
    {
        return new LuaApiItem
        {
            Name = "Spawn (Experimental)",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "spawn_unit",
                    Description = "Spawn a unit at position",
                    Signature = "spawn_unit(template, x, y, faction?) -> {success, error, entity}",
                    InsertText = "local result = spawn_unit(\"EnemySoldier\", x, y, 4)\nif result.success then\n    log(\"Spawned: \" .. result.entity.name)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "destroy_entity",
                    Description = "Kill an entity",
                    Signature = "destroy_entity(actor, immediate?) -> boolean",
                    InsertText = "destroy_entity(actor, true)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "clear_enemies",
                    Description = "Remove all enemies",
                    Signature = "clear_enemies(immediate?) -> number",
                    InsertText = "local count = clear_enemies()\nlog(\"Cleared \" .. count .. \" enemies\")",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "list_entities",
                    Description = "List entities by faction (-1 for all)",
                    Signature = "list_entities(faction?) -> table",
                    InsertText = "local entities = list_entities(-1)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_entity_info",
                    Description = "Get entity details",
                    Signature = "get_entity_info(actor) -> table",
                    InsertText = "local info = get_entity_info(actor)\nlog(\"Type: \" .. info.type_name)",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetTileEffectsApi()
    {
        return new LuaApiItem
        {
            Name = "Tile Effects",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "get_tile_effects",
                    Description = "Get all effects on tile",
                    Signature = "get_tile_effects(x, z) -> table",
                    InsertText = "local effects = get_tile_effects(x, z)\nfor i, effect in ipairs(effects) do\n    log(effect.template)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "has_effects",
                    Description = "Check if tile has any effects",
                    Signature = "has_effects(x, z) -> boolean",
                    InsertText = "if has_effects(x, z) then\n    -- tile has effects\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "is_on_fire",
                    Description = "Check if tile is burning",
                    Signature = "is_on_fire(x, z) -> boolean",
                    InsertText = "if is_on_fire(x, z) then\n    log(\"Tile is on fire!\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "has_smoke",
                    Description = "Check if tile has smoke",
                    Signature = "has_smoke(x, z) -> boolean",
                    InsertText = "if has_smoke(x, z) then\n    log(\"Smoke cover available\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "spawn_effect",
                    Description = "Spawn effect on tile",
                    Signature = "spawn_effect(x, z, template, delay?) -> boolean",
                    InsertText = "spawn_effect(x, z, \"FireEffect\", 0)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "clear_tile_effects",
                    Description = "Remove all effects from tile",
                    Signature = "clear_tile_effects(x, z) -> number",
                    InsertText = "local cleared = clear_tile_effects(x, z)",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_effect_templates",
                    Description = "List available effect templates",
                    Signature = "get_effect_templates() -> table",
                    InsertText = "local templates = get_effect_templates()\nfor i, name in ipairs(templates) do\n    log(name)\nend",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetInventoryApi()
    {
        return new LuaApiItem
        {
            Name = "Inventory",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "give_item",
                    Description = "Give item to actor (nil = active actor)",
                    Signature = "give_item(actor?, template) -> {success, message}",
                    InsertText = "give_item(nil, \"WeaponAssaultRifle\")",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_inventory",
                    Description = "Get all items in actor's inventory",
                    Signature = "get_inventory(actor?) -> table",
                    InsertText = "local items = get_inventory()\nfor i, item in ipairs(items) do\n    log(item.name .. \" (\" .. item.rarity .. \")\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_equipped_weapons",
                    Description = "Get equipped weapons",
                    Signature = "get_equipped_weapons(actor?) -> table",
                    InsertText = "local weapons = get_equipped_weapons()\nfor i, w in ipairs(weapons) do\n    log(\"Weapon: \" .. w.name)\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_equipped_armor",
                    Description = "Get equipped armor",
                    Signature = "get_equipped_armor(actor?) -> table",
                    InsertText = "local armor = get_equipped_armor()\nif armor then log(\"Armor: \" .. armor.name) end",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "get_item_templates",
                    Description = "List item template names",
                    Signature = "get_item_templates(filter?) -> table",
                    InsertText = "local items = get_item_templates(\"Weapon\")\nfor i, name in ipairs(items) do\n    log(name)\nend",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetBlackMarketApi()
    {
        return new LuaApiItem
        {
            Name = "Black Market",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "blackmarket_stock",
                    Description = "Add item to black market",
                    Signature = "blackmarket_stock(template) -> {success, message}",
                    InsertText = "local result = blackmarket_stock(\"WeaponSniper\")\nif result.success then\n    log(\"Added to black market!\")\nend",
                    ItemType = LuaApiItemType.Function
                },
                new()
                {
                    Name = "blackmarket_has",
                    Description = "Check if item exists in black market",
                    Signature = "blackmarket_has(template) -> boolean",
                    InsertText = "if blackmarket_has(\"WeaponSniper\") then\n    log(\"Item available\")\nend",
                    ItemType = LuaApiItemType.Function
                }
            }
        };
    }

    private static LuaApiItem GetTacticalEvents()
    {
        return new LuaApiItem
        {
            Name = "Tactical Events",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                // Mission lifecycle
                new() { Name = "tactical_ready", Description = "Fired when tactical mission is initialized", InsertText = "on(\"tactical_ready\", function()\n    log(\"Mission ready\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "mission_start", Description = "Fired at mission start with mission info", InsertText = "on(\"mission_start\", function(data)\n    log(\"Mission: \" .. tostring(data.name))\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "mission_ended", Description = "Fired when mission ends. Data: { success }", InsertText = "on(\"mission_ended\", function(data)\n    if data.success then log(\"Victory!\") end\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "objective_changed", Description = "Fired when objectives update", InsertText = "on(\"objective_changed\", function(data)\n    log(\"Objective updated\")\nend)", ItemType = LuaApiItemType.Event },

                // Turn/round events
                new() { Name = "turn_start", Description = "Fired when a turn begins. Data: { faction, factionName }", InsertText = "on(\"turn_start\", function(data)\n    log(data.factionName .. \" turn\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "turn_end", Description = "Fired when a turn ends. Data: { faction, factionName }", InsertText = "on(\"turn_end\", function(data)\n    log(\"Turn ended\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "round_start", Description = "Fired when a round begins", InsertText = "on(\"round_start\", function(data)\n    log(\"New round\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "round_end", Description = "Fired when a round ends", InsertText = "on(\"round_end\", function(data)\n    log(\"Round ended\")\nend)", ItemType = LuaApiItemType.Event },

                // Combat events
                new() { Name = "actor_killed", Description = "Fired when an actor dies", InsertText = "on(\"actor_killed\", function(data)\n    log(\"Actor killed\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "damage_received", Description = "Fired when damage is dealt", InsertText = "on(\"damage_received\", function(data)\n    log(\"Damage received\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "attack_start", Description = "Fired when an attack begins", InsertText = "on(\"attack_start\", function(data)\n    log(\"Attack started\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "attack_missed", Description = "Fired when an attack misses", InsertText = "on(\"attack_missed\", function(data)\n    log(\"Missed!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "critical_hit", Description = "Fired on a critical hit", InsertText = "on(\"critical_hit\", function(data)\n    log(\"Critical hit!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "overwatch_triggered", Description = "Fired when overwatch activates", InsertText = "on(\"overwatch_triggered\", function(data)\n    log(\"Overwatch!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "grenade_thrown", Description = "Fired when a grenade is thrown", InsertText = "on(\"grenade_thrown\", function(data)\n    log(\"Grenade!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "bleeding_out", Description = "Fired when actor starts bleeding out", InsertText = "on(\"bleeding_out\", function(data)\n    log(\"Bleeding out!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "stabilized", Description = "Fired when actor is stabilized", InsertText = "on(\"stabilized\", function(data)\n    log(\"Stabilized\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "suppressed", Description = "Fired when actor becomes suppressed", InsertText = "on(\"suppressed\", function(data)\n    log(\"Suppressed!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "suppression_applied", Description = "Fired when suppression is applied", InsertText = "on(\"suppression_applied\", function(data)\n    log(\"Suppression applied\")\nend)", ItemType = LuaApiItemType.Event },

                // State changes
                new() { Name = "actor_state_changed", Description = "Fired when actor state changes", InsertText = "on(\"actor_state_changed\", function(data)\n    log(\"State changed\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "hp_changed", Description = "Fired when HP changes (data.hp_pct is the remaining fraction)", InsertText = "on(\"hp_changed\", function(data)\n    log(\"HP changed\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "armor_changed", Description = "Fired when armor changes", InsertText = "on(\"armor_changed\", function(data)\n    log(\"Armor changed\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "morale_changed", Description = "Fired when morale changes", InsertText = "on(\"morale_changed\", function(data)\n    log(\"Morale changed\")\nend)", ItemType = LuaApiItemType.Event },

                // Visibility events
                new() { Name = "discovered", Description = "Fired when actor is discovered", InsertText = "on(\"discovered\", function(data)\n    log(\"Enemy discovered!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "visible_to_player", Description = "Fired when actor becomes visible", InsertText = "on(\"visible_to_player\", function(data)\n    log(\"Now visible\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "hidden_from_player", Description = "Fired when actor becomes hidden", InsertText = "on(\"hidden_from_player\", function(data)\n    log(\"Now hidden\")\nend)", ItemType = LuaApiItemType.Event },

                // Movement
                new() { Name = "move_start", Description = "Fired when movement begins", InsertText = "on(\"move_start\", function(data)\n    log(\"Moving...\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "move_complete", Description = "Fired when movement ends", InsertText = "on(\"move_complete\", function(data)\n    log(\"Move complete\")\nend)", ItemType = LuaApiItemType.Event },

                // Skills
                new() { Name = "skill_used", Description = "Fired when a skill is activated", InsertText = "on(\"skill_used\", function(data)\n    log(\"Skill used\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "skill_complete", Description = "Fired when a skill finishes", InsertText = "on(\"skill_complete\", function(data)\n    log(\"Skill complete\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "skill_added", Description = "Fired when a skill is added", InsertText = "on(\"skill_added\", function(data)\n    log(\"Skill added\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "offmap_ability_used", Description = "Fired when offmap ability is used", InsertText = "on(\"offmap_ability_used\", function(data)\n    log(\"Offmap ability!\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "offmap_ability_canceled", Description = "Fired when offmap ability is canceled", InsertText = "on(\"offmap_ability_canceled\", function(data)\n    log(\"Ability canceled\")\nend)", ItemType = LuaApiItemType.Event },

                // Entity events
                new() { Name = "entity_spawned", Description = "Fired when an entity spawns", InsertText = "on(\"entity_spawned\", function(data)\n    log(\"Entity spawned\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "element_destroyed", Description = "Fired when a map element is destroyed", InsertText = "on(\"element_destroyed\", function(data)\n    log(\"Element destroyed\")\nend)", ItemType = LuaApiItemType.Event },
                new() { Name = "element_malfunction", Description = "Fired when equipment malfunctions", InsertText = "on(\"element_malfunction\", function(data)\n    log(\"Malfunction!\")\nend)", ItemType = LuaApiItemType.Event }
            }
        };
    }

    private static LuaApiItem GetStrategyEvents()
    {
        return new LuaApiItem
        {
            Name = "Strategy Events",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                // Campaign
                new()
                {
                    Name = "campaign_start",
                    Description = "Fired when a new campaign starts",
                    Signature = "on(\"campaign_start\", function() ... end)",
                    InsertText = "on(\"campaign_start\", function()\n    log(\"Campaign started!\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                // Leaders
                new()
                {
                    Name = "leader_hired",
                    Description = "Fired when a leader is recruited. Data: { leader }",
                    Signature = "on(\"leader_hired\", function(data) ... end)",
                    InsertText = "on(\"leader_hired\", function(data)\n    log(\"Leader hired: \" .. tostring(data.leader))\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "leader_dismissed",
                    Description = "Fired when a leader is dismissed. Data: { leader }",
                    Signature = "on(\"leader_dismissed\", function(data) ... end)",
                    InsertText = "on(\"leader_dismissed\", function(data)\n    log(\"Leader dismissed\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "leader_permadeath",
                    Description = "Fired when a leader dies permanently. Data: { leader }",
                    Signature = "on(\"leader_permadeath\", function(data) ... end)",
                    InsertText = "on(\"leader_permadeath\", function(data)\n    log(\"Leader lost forever: \" .. tostring(data.leader))\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "leader_levelup",
                    Description = "Fired when a leader gains a perk. Data: { leader }",
                    Signature = "on(\"leader_levelup\", function(data) ... end)",
                    InsertText = "on(\"leader_levelup\", function(data)\n    log(\"Leader leveled up!\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                // Factions
                new()
                {
                    Name = "faction_trust_changed",
                    Description = "Fired when faction trust changes. Data: { faction, delta }",
                    Signature = "on(\"faction_trust_changed\", function(data) ... end)",
                    InsertText = "on(\"faction_trust_changed\", function(data)\n    if data.delta > 0 then\n        log(\"Trust increased with \" .. tostring(data.faction))\n    else\n        log(\"Trust decreased with \" .. tostring(data.faction))\n    end\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "faction_status_changed",
                    Description = "Fired when faction status changes. Data: { faction, status }",
                    Signature = "on(\"faction_status_changed\", function(data) ... end)",
                    InsertText = "on(\"faction_status_changed\", function(data)\n    log(\"Faction status: \" .. tostring(data.status))\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "faction_upgrade_unlocked",
                    Description = "Fired when a faction upgrade unlocks. Data: { faction, upgrade }",
                    Signature = "on(\"faction_upgrade_unlocked\", function(data) ... end)",
                    InsertText = "on(\"faction_upgrade_unlocked\", function(data)\n    log(\"Upgrade unlocked: \" .. tostring(data.upgrade))\nend)",
                    ItemType = LuaApiItemType.Event
                },
                // Squaddies
                new()
                {
                    Name = "squaddie_killed",
                    Description = "Fired when a squaddie dies. Data: { id }",
                    Signature = "on(\"squaddie_killed\", function(data) ... end)",
                    InsertText = "on(\"squaddie_killed\", function(data)\n    log(\"Squaddie lost\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                // Economy
                new()
                {
                    Name = "blackmarket_item_added",
                    Description = "Fired when an item is added to the black market. Data: { item }",
                    Signature = "on(\"blackmarket_item_added\", function(data) ... end)",
                    InsertText = "on(\"blackmarket_item_added\", function(data)\n    log(\"New black market item\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "blackmarket_restocked",
                    Description = "Fired when the black market restocks",
                    Signature = "on(\"blackmarket_restocked\", function() ... end)",
                    InsertText = "on(\"blackmarket_restocked\", function()\n    log(\"Black market restocked!\")\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "operation_finished",
                    Description = "Fired when an operation completes",
                    Signature = "on(\"operation_finished\", function() ... end)",
                    InsertText = "on(\"operation_finished\", function()\n    log(\"Operation complete\")\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetGeneralEvents()
    {
        return new LuaApiItem
        {
            Name = "General Events",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "scene_loaded",
                    Description = "Fired when a scene loads. Data: sceneName (string)",
                    Signature = "on(\"scene_loaded\", function(sceneName) ... end)",
                    InsertText = "on(\"scene_loaded\", function(sceneName)\n    log(\"Scene loaded: \" .. tostring(sceneName))\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    //  INTERCEPTORS - Modify game values in real-time
    // ═══════════════════════════════════════════════════════════════════════════════

    private static LuaApiItem GetPropertyInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Property Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "property_damage",
                    Description = "Intercept damage calculations. Modify data.result to change damage.",
                    Signature = "on(\"property_damage\", function(data) data.result = data.result * 1.5 end)",
                    InsertText = "on(\"property_damage\", function(data)\n    -- data.props_ptr, data.owner_ptr, data.result\n    data.result = data.result * 1.5  -- +50% damage\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_accuracy",
                    Description = "Intercept accuracy calculations.",
                    Signature = "on(\"property_accuracy\", function(data) ... end)",
                    InsertText = "on(\"property_accuracy\", function(data)\n    data.result = data.result + 10  -- +10 accuracy\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_armor",
                    Description = "Intercept armor value calculations.",
                    Signature = "on(\"property_armor\", function(data) ... end)",
                    InsertText = "on(\"property_armor\", function(data)\n    data.result = data.result + 2  -- +2 armor\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_concealment",
                    Description = "Intercept concealment calculations.",
                    Signature = "on(\"property_concealment\", function(data) ... end)",
                    InsertText = "on(\"property_concealment\", function(data)\n    data.result = data.result + 5  -- +5 concealment\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_detection",
                    Description = "Intercept detection range calculations.",
                    Signature = "on(\"property_detection\", function(data) ... end)",
                    InsertText = "on(\"property_detection\", function(data)\n    data.result = data.result * 1.2  -- +20% detection\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_vision",
                    Description = "Intercept vision range calculations.",
                    Signature = "on(\"property_vision\", function(data) ... end)",
                    InsertText = "on(\"property_vision\", function(data)\n    data.result = data.result + 2  -- +2 vision tiles\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_armor_penetration",
                    Description = "Intercept armor penetration calculations.",
                    Signature = "on(\"property_armor_penetration\", function(data) ... end)",
                    InsertText = "on(\"property_armor_penetration\", function(data)\n    data.result = data.result + 1  -- +1 AP\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_suppression",
                    Description = "Intercept suppression value calculations.",
                    Signature = "on(\"property_suppression\", function(data) ... end)",
                    InsertText = "on(\"property_suppression\", function(data)\n    data.result = data.result * 0.5  -- -50% suppression taken\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_action_points",
                    Description = "Intercept action point calculations.",
                    Signature = "on(\"property_action_points\", function(data) ... end)",
                    InsertText = "on(\"property_action_points\", function(data)\n    data.result = data.result + 2  -- +2 AP\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "property_max_hitpoints",
                    Description = "Intercept max hitpoints calculations.",
                    Signature = "on(\"property_max_hitpoints\", function(data) ... end)",
                    InsertText = "on(\"property_max_hitpoints\", function(data)\n    data.result = data.result * 1.25  -- +25% HP\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetSkillInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Skill Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "skill_hitchance",
                    Description = "Intercept hit chance calculations. Modify final_chance, base_accuracy, cover_mult.",
                    Signature = "on(\"skill_hitchance\", function(data) ... end)",
                    InsertText = "on(\"skill_hitchance\", function(data)\n    -- data.skill_ptr, data.attacker_ptr, data.target_ptr\n    -- data.final_chance, data.base_accuracy, data.cover_mult\n    data.final_chance = math.min(data.final_chance + 0.1, 1.0)  -- +10% hit\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_expected_damage",
                    Description = "Intercept expected damage preview calculations.",
                    Signature = "on(\"skill_expected_damage\", function(data) ... end)",
                    InsertText = "on(\"skill_expected_damage\", function(data)\n    data.damage = data.damage * 1.5  -- +50% preview damage\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_covermult",
                    Description = "Intercept cover effectiveness calculations.",
                    Signature = "on(\"skill_covermult\", function(data) ... end)",
                    InsertText = "on(\"skill_covermult\", function(data)\n    data.result = data.result * 0.5  -- Halve cover effectiveness\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_ap_cost",
                    Description = "Intercept skill AP cost calculations.",
                    Signature = "on(\"skill_ap_cost\", function(data) ... end)",
                    InsertText = "on(\"skill_ap_cost\", function(data)\n    data.result = math.max(1, data.result - 1)  -- -1 AP cost (min 1)\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_max_range",
                    Description = "Intercept maximum range calculations.",
                    Signature = "on(\"skill_max_range\", function(data) ... end)",
                    InsertText = "on(\"skill_max_range\", function(data)\n    data.result = data.result + 3  -- +3 range\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_min_range",
                    Description = "Intercept minimum range calculations.",
                    Signature = "on(\"skill_min_range\", function(data) ... end)",
                    InsertText = "on(\"skill_min_range\", function(data)\n    data.result = math.max(0, data.result - 1)  -- -1 min range\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "skill_is_in_range",
                    Description = "Intercept range validity checks. Set result to true/false.",
                    Signature = "on(\"skill_is_in_range\", function(data) ... end)",
                    InsertText = "on(\"skill_is_in_range\", function(data)\n    -- data.result = true  -- Force always in range\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetTileInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Tile Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "actor_los",
                    Description = "Intercept line of sight checks between actors.",
                    Signature = "on(\"actor_los\", function(data) ... end)",
                    InsertText = "on(\"actor_los\", function(data)\n    -- data.observer_ptr, data.target_ptr, data.result (bool)\n    -- data.result = true  -- Grant LoS\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "tile_has_los",
                    Description = "Intercept tile-to-tile LoS checks.",
                    Signature = "on(\"tile_has_los\", function(data) ... end)",
                    InsertText = "on(\"tile_has_los\", function(data)\n    -- data.from_ptr, data.to_ptr, data.result\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "tile_get_cover",
                    Description = "Intercept cover value queries (0-3).",
                    Signature = "on(\"tile_get_cover\", function(data) ... end)",
                    InsertText = "on(\"tile_get_cover\", function(data)\n    -- data.tile_ptr, data.direction (0-7), data.result (0-3)\n    data.result = 3  -- Force full cover\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "tile_can_enter",
                    Description = "Intercept tile entry permission checks.",
                    Signature = "on(\"tile_can_enter\", function(data) ... end)",
                    InsertText = "on(\"tile_can_enter\", function(data)\n    -- data.tile_ptr, data.result (bool)\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "los_raytrace",
                    Description = "Intercept core LoS raycast algorithm.",
                    Signature = "on(\"los_raytrace\", function(data) ... end)",
                    InsertText = "on(\"los_raytrace\", function(data)\n    -- data.from_ptr, data.to_ptr, data.flags, data.result\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetMovementInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Movement Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "movement_max_speed",
                    Description = "Intercept movement speed calculations.",
                    Signature = "on(\"movement_max_speed\", function(data) ... end)",
                    InsertText = "on(\"movement_max_speed\", function(data)\n    -- data.instance_ptr, data.movement_mode (0=Walk,1=Run,2=Sprint)\n    data.result = data.result * 1.5  -- +50% speed\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "movement_path_cost",
                    Description = "Intercept path AP cost calculations.",
                    Signature = "on(\"movement_path_cost\", function(data) ... end)",
                    InsertText = "on(\"movement_path_cost\", function(data)\n    data.result = math.floor(data.result * 0.75)  -- -25% movement cost\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "movement_turn_speed",
                    Description = "Intercept turn animation speed.",
                    Signature = "on(\"movement_turn_speed\", function(data) ... end)",
                    InsertText = "on(\"movement_turn_speed\", function(data)\n    data.result = data.result * 2  -- 2x turn speed\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "movement_clip_path",
                    Description = "Intercept path clipping by AP budget.",
                    Signature = "on(\"movement_clip_path\", function(data) ... end)",
                    InsertText = "on(\"movement_clip_path\", function(data)\n    -- data.actual_cost, data.max_cost, data.clip_index\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetStrategyInterceptors()
    {
        return new LuaApiItem
        {
            Name = "Strategy Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "strategy_action_points",
                    Description = "Intercept strategy layer AP calculations.",
                    Signature = "on(\"strategy_action_points\", function(data) ... end)",
                    InsertText = "on(\"strategy_action_points\", function(data)\n    data.result = data.result + 2  -- +2 strategy AP\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "strategy_hitpoints_per_element",
                    Description = "Intercept HP per squad element.",
                    Signature = "on(\"strategy_hitpoints_per_element\", function(data) ... end)",
                    InsertText = "on(\"strategy_hitpoints_per_element\", function(data)\n    data.result = data.result + 5  -- +5 HP per element\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "strategy_damage_sustained_mult",
                    Description = "Intercept damage resistance multiplier.",
                    Signature = "on(\"strategy_damage_sustained_mult\", function(data) ... end)",
                    InsertText = "on(\"strategy_damage_sustained_mult\", function(data)\n    data.result = data.result * 0.8  -- -20% damage taken\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "strategy_can_be_promoted",
                    Description = "Intercept promotion eligibility checks.",
                    Signature = "on(\"strategy_can_be_promoted\", function(data) ... end)",
                    InsertText = "on(\"strategy_can_be_promoted\", function(data)\n    data.result = true  -- Always allow promotion\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "strategy_can_be_demoted",
                    Description = "Intercept demotion eligibility checks.",
                    Signature = "on(\"strategy_can_be_demoted\", function(data) ... end)",
                    InsertText = "on(\"strategy_can_be_demoted\", function(data)\n    data.result = false  -- Prevent demotion\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "strategy_vehicle_armor",
                    Description = "Intercept vehicle armor calculations.",
                    Signature = "on(\"strategy_vehicle_armor\", function(data) ... end)",
                    InsertText = "on(\"strategy_vehicle_armor\", function(data)\n    data.result = data.result + 3  -- +3 vehicle armor\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    private static LuaApiItem GetAIInterceptors()
    {
        return new LuaApiItem
        {
            Name = "AI Interceptors",
            ItemType = LuaApiItemType.Category,
            IsExpanded = false,
            IsInterceptor = true,
            Children = new List<LuaApiItem>
            {
                new()
                {
                    Name = "ai_attack_score",
                    Description = "Intercept AI target scoring. Higher = more attractive target.",
                    Signature = "on(\"ai_attack_score\", function(data) ... end)",
                    InsertText = "on(\"ai_attack_score\", function(data)\n    -- data.ai_ptr, data.target_ptr, data.result\n    data.result = data.result * 0.5  -- Make targets less attractive\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "ai_threat_value",
                    Description = "Intercept AI threat evaluation. Higher = more dangerous.",
                    Signature = "on(\"ai_threat_value\", function(data) ... end)",
                    InsertText = "on(\"ai_threat_value\", function(data)\n    data.result = data.result * 2  -- Double perceived threat\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "ai_action_priority",
                    Description = "Intercept AI action priority scoring.",
                    Signature = "on(\"ai_action_priority\", function(data) ... end)",
                    InsertText = "on(\"ai_action_priority\", function(data)\n    -- Modify action priority\nend)",
                    ItemType = LuaApiItemType.Event
                },
                new()
                {
                    Name = "ai_should_flee",
                    Description = "Intercept AI flee decision. Set result to true/false.",
                    Signature = "on(\"ai_should_flee\", function(data) ... end)",
                    InsertText = "on(\"ai_should_flee\", function(data)\n    data.result = false  -- Never flee\nend)",
                    ItemType = LuaApiItemType.Event
                }
            }
        };
    }

    /// <summary>
    /// Get script templates for new file creation.
    /// </summary>
    public static List<(string Name, string Description, string Content)> GetScriptTemplates()
    {
        return new List<(string, string, string)>
        {
            ("Basic Script", "Simple script with logging and scene event",
@"-- Basic Lua Script
log(""[MyMod] Loading..."")

on(""scene_loaded"", function(sceneName)
    log(""[MyMod] Scene: "" .. tostring(sceneName))
end)

log(""[MyMod] Loaded!"")
"),
            ("Tactical Helper", "Track combat events in tactical missions",
@"-- Tactical Helper Script
log(""[TacticalMod] Loading..."")

local killCount = 0

on(""tactical_ready"", function()
    log(""[TacticalMod] Mission started"")
    killCount = 0
end)

on(""turn_start"", function(data)
    if data and data.faction == 0 then
        log(""[TacticalMod] Your turn!"")
    end
end)

on(""actor_killed"", function(data)
    killCount = killCount + 1
    log(""[TacticalMod] Kill #"" .. killCount)
end)

log(""[TacticalMod] Loaded!"")
"),
            ("Strategy Helper", "Track campaign events in strategy layer",
@"-- Strategy Helper Script
log(""[StrategyMod] Loading..."")

on(""campaign_start"", function()
    log(""[StrategyMod] Campaign started!"")
end)

on(""leader_hired"", function(data)
    log(""[StrategyMod] New leader: "" .. tostring(data.leader))
end)

on(""faction_trust_changed"", function(data)
    local direction = data.delta > 0 and ""up"" or ""down""
    log(""[StrategyMod] Trust "" .. direction .. "" with "" .. tostring(data.faction))
end)

log(""[StrategyMod] Loaded!"")
"),
            ("Console Commands", "Add custom console commands",
@"-- Console Commands Script
log(""[Commands] Loading..."")

cmd(""hello"", function(args)
    log(""Hello, "" .. (args or ""world"") .. ""!"")
    return ""Greeting sent""
end)

cmd(""status"", function()
    log(""=== STATUS ==="")
    if type(is_mission_running) == ""function"" then
        log(""Mission running: "" .. tostring(is_mission_running()))
    end
    return ""Status printed""
end)

log(""[Commands] Loaded!"")
log(""[Commands] Available: /hello, /status"")
")
        };
    }
}
