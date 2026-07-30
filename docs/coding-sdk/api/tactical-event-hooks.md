# TacticalEventHooks

`Menace.SDK.TacticalEventHooks` -- Game lifecycle event system for tactical combat events.

## Overview

TacticalEventHooks provides C# events for entity lifecycle, combat actions, movement, and other tactical gameplay events. Unlike `Intercept` which modifies method return values, TacticalEventHooks lets you observe and react to events as they occur.

Key features:
- **80+ tactical events** covering combat, movement, skills, and entity lifecycle
- **Automatic Lua bridging** -- every C# event fires a corresponding Lua event
- **No return value modification** -- events are for observation and triggering side effects
- **Clean unsubscription** -- remove handlers when your mod unloads

## Quick Start

```csharp
using Menace.SDK;

public class MyPlugin : IModpackPlugin
{
    public void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony)
    {
        // Symmetric lifecycle tracking - actor spawn/death
        TacticalEventHooks.OnActorSpawned += OnActorSpawned;
        TacticalEventHooks.OnActorKilled += OnActorKilled;

        // Track structure destruction
        TacticalEventHooks.OnStructureDeath += OnStructureDeath;

        // Track individual squad member spawning
        TacticalEventHooks.OnElementSpawned += OnElementSpawned;

        // Subscribe to skill usage
        TacticalEventHooks.OnSkillUsed += OnSkillUsed;
    }

    private void OnActorSpawned(IntPtr actorPtr)
    {
        var actor = new GameObj(actorPtr);
        DevConsole.Log($"Actor spawned: {actor.GetName()}");
    }

    private void OnActorKilled(IntPtr actorPtr, IntPtr killerPtr, int faction)
    {
        var actor = new GameObj(actorPtr);
        var killer = new GameObj(killerPtr);
        DevConsole.Log($"{actor.GetName()} killed by {killer.GetName()}");
    }

    private void OnStructureDeath(IntPtr structurePtr)
    {
        var structure = new GameObj(structurePtr);
        DevConsole.Log($"Structure destroyed: {structure.GetName()}");
    }

    private void OnElementSpawned(IntPtr elementPtr, IntPtr parentPtr)
    {
        var element = new GameObj(elementPtr);
        var parent = new GameObj(parentPtr);
        DevConsole.Log($"Element spawned in {parent.GetName()}");
    }

    private void OnSkillUsed(IntPtr userPtr, IntPtr skillPtr, IntPtr targetPtr)
    {
        var user = new GameObj(userPtr);
        var skill = new GameObj(skillPtr);
        DevConsole.Log($"{user.GetName()} used {skill.GetName()}");
    }

    public void OnUnload()
    {
        // Clean up handlers
        TacticalEventHooks.OnActorSpawned -= OnActorSpawned;
        TacticalEventHooks.OnActorKilled -= OnActorKilled;
        TacticalEventHooks.OnStructureDeath -= OnStructureDeath;
        TacticalEventHooks.OnElementSpawned -= OnElementSpawned;
        TacticalEventHooks.OnSkillUsed -= OnSkillUsed;
    }
}
```

## Event Categories

### Entity Lifecycle

Complete symmetric spawn/death coverage for all entity types.

**Entity (Base Class - All Objects)**

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnEntitySpawned` | `(IntPtr entity)` | `entity_spawned` | Any entity spawns (actors, structures, etc.) |
| `OnEntityDeath` | `(IntPtr entity)` | `entity_death` | Any entity dies |

**Actor (Units with AI/Movement)**

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnActorSpawned` | `(IntPtr actor)` | `actor_spawned` | Actor spawns (filtered from OnEntitySpawned) |
| `OnActorKilled` | `(IntPtr actor, IntPtr killer, int faction)` | `actor_killed` | Actor dies (see Combat Events) |

**Structure (Buildings/Walls/Destructibles)**

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnStructureSpawned` | `(IntPtr structure)` | `structure_spawned` | Structure spawns (filtered from OnEntitySpawned) |
| `OnStructureDeath` | `(IntPtr structure)` | `structure_death` | Structure destroyed |

**Element (Squad Members/Vehicle Components)**

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnElementSpawned` | `(IntPtr element, IntPtr parent)` | `element_spawned` | Individual element added to entity |
| `OnElementDeath` | `(IntPtr element)` | `element_destroyed` | Individual element destroyed |
| `OnElementMalfunction` | `(IntPtr element)` | `element_malfunction` | Element damaged but not destroyed |

### Combat Events

React to attacks, damage, kills, and combat state changes.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnActorKilled` | `(IntPtr actor, IntPtr killer, int faction)` | `actor_killed` | Actor died |
| `OnDamageReceived` | `(IntPtr target, IntPtr attacker, IntPtr skill)` | `damage_received` | Actor took damage |
| `OnAttackMissed` | `(IntPtr attacker, IntPtr target)` | `attack_missed` | Attack missed target |
| `OnAttackTileStart` | `(IntPtr attacker, IntPtr tile)` | `attack_start` | Attack started on tile |
| `OnBleedingOut` | `(IntPtr actor)` | `bleeding_out` | Actor is bleeding out |
| `OnStabilized` | `(IntPtr actor)` | `stabilized` | Actor stabilized from bleeding |
| `OnSuppressed` | `(IntPtr actor)` | `suppressed` | Actor became suppressed |
| `OnSuppressionApplied` | `(IntPtr target, IntPtr attacker, float amount)` | `suppression_applied` | Suppression damage applied |

### Actor State Changes

Monitor changes to actor stats and status.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnActorStateChanged` | `(IntPtr actor)` | `actor_state_changed` | Actor state changed (stance, cover, etc.) |
| `OnMoraleStateChanged` | `(IntPtr actor, int newState)` | `morale_changed` | Morale state changed (panicked, shaken, steady) |
| `OnHitpointsChanged` | `(IntPtr entity, float hpPct)` | `hp_changed` | Hit points changed, as a remaining fraction |
| `OnArmorChanged` | `(IntPtr actor)` | `armor_changed` | Armor value changed |

### Visibility & Detection

Track when units discover each other or visibility changes.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnDiscovered` | `(IntPtr discovered, IntPtr discoverer)` | `discovered` | Unit discovered another unit |
| `OnVisibleToPlayer` | `(IntPtr entity)` | `visible_to_player` | Entity became visible to player |
| `OnHiddenToPlayer` | `(IntPtr entity)` | `hidden_from_player` | Entity became hidden from player |

### Movement

Track actor movement start and completion.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnMovementStarted` | `(IntPtr actor, IntPtr fromTile, IntPtr toTile)` | `move_start` | Actor started moving |
| `OnMovementFinished` | `(IntPtr actor, IntPtr tile)` | `move_complete` | Actor finished moving |

### Skills & Abilities

React to skill usage and ability activation.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnSkillUsed` | `(IntPtr user, IntPtr skill, IntPtr target)` | `skill_used` | Skill was used |
| `OnSkillCompleted` | `(IntPtr skill)` | `skill_complete` | Skill execution completed |
| `OnSkillAdded` | `(IntPtr actor, IntPtr skill)` | `skill_added` | Skill added to actor |
| `OnOffmapAbilityUsed` | `(IntPtr ability)` | `offmap_ability_used` | Off-map ability used (artillery, etc.) |
| `OnOffmapAbilityCanceled` | `(IntPtr ability)` | `offmap_ability_canceled` | Off-map ability canceled |

### Turn & Round Management

Track turn order and round progression.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnTurnEnd` | `(IntPtr actor)` | `turn_end` | Actor's turn ended |
| `OnRoundStart` | `(int roundNumber)` | `round_start` | New round started |

### Objectives

Monitor objective state changes.

| Event | Signature | Lua Event | Description |
|-------|-----------|-----------|-------------|
| `OnObjectiveStateChanged` | `(IntPtr objective, int newState)` | `objective_changed` | Objective state changed |

## Usage Patterns

### Symmetric Lifecycle Tracking

The event system provides complete symmetric spawn/death coverage:

```csharp
// Track complete entity lifecycle
private Dictionary<IntPtr, DateTime> _entityLifetimes = new();

TacticalEventHooks.OnEntitySpawned += (entity) =>
{
    _entityLifetimes[entity] = DateTime.Now;
    DevConsole.Log($"Entity spawned: {new GameObj(entity).GetName()}");
};

TacticalEventHooks.OnEntityDeath += (entity) =>
{
    if (_entityLifetimes.TryGetValue(entity, out var spawnTime))
    {
        var lifetime = DateTime.Now - spawnTime;
        DevConsole.Log($"Entity survived {lifetime.TotalSeconds:F1}s");
        _entityLifetimes.Remove(entity);
    }
};
```

### Track Squad Spawning

Elements spawn individually for each squad member:

```csharp
TacticalEventHooks.OnActorSpawned += (actor) =>
{
    var actorObj = new GameObj(actor);
    DevConsole.Log($"Squad spawned: {actorObj.GetName()}");
};

TacticalEventHooks.OnElementSpawned += (element, parent) =>
{
    var parentObj = new GameObj(parent);
    DevConsole.Log($"  - Squad member added to {parentObj.GetName()}");
};
```

Expected output for 4-man marine squad:
```
Squad spawned: Marine Squad
  - Squad member added to Marine Squad
  - Squad member added to Marine Squad
  - Squad member added to Marine Squad
  - Squad member added to Marine Squad
```

### Trigger Custom Effects on Kill

```csharp
TacticalEventHooks.OnActorKilled += (IntPtr actorPtr, IntPtr killerPtr, int faction) =>
{
    var killer = new GameObj(killerPtr);

    // Heal killer on kill
    var props = killer.GetField<IntPtr>("m_Properties");
    if (props != IntPtr.Zero)
    {
        var propsObj = new GameObj(props);
        int currentHp = propsObj.GetField<int>("m_Hitpoints");
        propsObj.SetField("m_Hitpoints", currentHp + 10);
        DevConsole.Log($"{killer.GetName()} healed 10 HP from kill");
    }
};
```

### Track Spawned Reinforcements

```csharp
private int _reinforcementCount = 0;

TacticalEventHooks.OnEntitySpawned += (IntPtr entityPtr) =>
{
    var entity = new GameObj(entityPtr);
    string name = entity.GetName();

    if (name.Contains("Reinforcement"))
    {
        _reinforcementCount++;
        DevConsole.Log($"Reinforcement wave {_reinforcementCount} spawned");

        // Trigger something after 3 waves
        if (_reinforcementCount >= 3)
        {
            DevConsole.Log("All reinforcements arrived!");
        }
    }
};
```

### React to Low Health

```csharp
TacticalEventHooks.OnHitpointsChanged += (IntPtr actorPtr, float hpPct) =>
{
    if (hpPct <= 0f || hpPct > 0.25f)
        return; // Skip dead actors and anyone still comfortably alive

    var actor = new GameObj(actorPtr);
    DevConsole.Log($"{actor.GetName()} is critically wounded! ({hpPct:P0} HP remaining)");
    // Could trigger berserker mode, panic, etc.
};
```

### Count Skill Usage

```csharp
private Dictionary<string, int> _skillUsageCount = new();

TacticalEventHooks.OnSkillUsed += (IntPtr userPtr, IntPtr skillPtr, IntPtr targetPtr) =>
{
    var skill = new GameObj(skillPtr);
    string skillName = skill.GetName();

    if (!_skillUsageCount.ContainsKey(skillName))
        _skillUsageCount[skillName] = 0;

    _skillUsageCount[skillName]++;

    DevConsole.Log($"{skillName} used {_skillUsageCount[skillName]} times this mission");
};
```

## Lua Equivalent

Every C# event fires a corresponding Lua event:

```lua
-- Track entity spawns in Lua
on("entity_spawned", function(data)
    log("Entity spawned: " .. tostring(data.entity))
end)

-- Track actor deaths
on("actor_killed", function(data)
    log("Actor killed by faction " .. data.faction)
end)

-- Track skill usage
on("skill_used", function(data)
    log("Skill used on target")
end)
```

## Architecture & Event Flow

### Entity Class Hierarchy

```
Entity (base)
├── Actor (units with AI/movement)
│   ├── UnitActor (player squads)
│   └── VehicleActor (vehicles)
└── Structure (buildings, walls, destructibles)
```

Each Entity contains:
- **Elements**: List of visual models (squad members, vehicle parts, building sections)
- **EntitySegments**: Damage sections

### Event Flow Examples

**4-Man Squad Spawns:**
1. `OnEntitySpawned` fires (generic)
2. `OnActorSpawned` fires (filtered)
3. `OnElementSpawned` fires 4 times (one per marine)

**Squad Takes Casualties:**
1. First marine dies → `OnElementDeath` fires
2. Second marine dies → `OnElementDeath` fires
3. Third marine dies → `OnElementDeath` fires
4. Fourth marine dies → `OnElementDeath` + `OnActorKilled` fire (squad wiped)
5. `OnEntityDeath` fires (entity destroyed)

**Building Destroyed:**
1. Building spawned → `OnEntitySpawned` + `OnStructureSpawned`
2. Sections destroyed → `OnElementDeath` fires for each section
3. Last section destroyed → `OnStructureDeath` + `OnEntityDeath`

### Event Symmetry

Every spawn event has a corresponding death event:

| Spawn Event | Death Event | Object Type |
|-------------|-------------|-------------|
| `OnEntitySpawned` | `OnEntityDeath` | All entities |
| `OnActorSpawned` | `OnActorKilled` | Actors only |
| `OnStructureSpawned` | `OnStructureDeath` | Structures only |
| `OnElementSpawned` | `OnElementDeath` | Individual elements |

## Differences from Intercept

| TacticalEventHooks | Intercept |
|-------------------|-----------|
| Lifecycle events (spawn, death, movement) | Method interceptions (getters, calculations) |
| Observe and react | Modify return values |
| `Action<>` delegates | Custom delegates with `ref` parameters |
| No return value | Modify `ref result` parameters |

Use **TacticalEventHooks** when you want to know *when something happens*.
Use **Intercept** when you want to change *how something is calculated*.

## Best Practices

### 1. Always Unsubscribe

```csharp
public void OnUnload()
{
    TacticalEventHooks.OnEntitySpawned -= MySpawnHandler;
    TacticalEventHooks.OnActorKilled -= MyKillHandler;
}
```

### 2. Check for Null Pointers

```csharp
TacticalEventHooks.OnDamageReceived += (IntPtr target, IntPtr attacker, IntPtr skill) =>
{
    if (target == IntPtr.Zero || attacker == IntPtr.Zero)
        return;

    var targetObj = new GameObj(target);
    if (targetObj.IsNull)
        return;

    // Safe to proceed
};
```

### 3. Keep Handlers Fast

Events fire during gameplay on the main thread. Slow handlers cause lag:

```csharp
// GOOD - fast lookup
private HashSet<string> _immuneUnits = new() { "Boss", "Champion" };

TacticalEventHooks.OnDamageReceived += (target, attacker, skill) =>
{
    var targetObj = new GameObj(target);
    if (_immuneUnits.Contains(targetObj.GetName()))
    {
        // Apply immunity effect
    }
};

// BAD - expensive operation in hot path
TacticalEventHooks.OnDamageReceived += (target, attacker, skill) =>
{
    var allUnits = GameQuery.FindAll("ActorTemplate"); // Slow!
    // ...
};
```

## See Also

- [Intercept](intercept.md) -- Method interception for modifying calculations
- [GameObj](game-obj.md) -- Working with IL2CPP object pointers
- [Lua Scripting](../../modding-guides/11-lua-scripting.md) -- Lua event system
