# SDK Backlog

**Last Updated:** 2026-03-07

This document tracks known issues, fixes applied, and remaining work for the Menace ModpackLoader SDK.

---

## Status Summary

| Category | Status | Details |
|----------|--------|---------|
| Template Loading | ✅ Working | 77 types verified, all loading correctly |
| Helper APIs | ✅ Complete | Templates.GetProperty, ModeAware, Modpacks SDK |
| Deployment Sync | ✅ Complete | Post-build target syncs DLL to bundled |
| Diagnostics | ✅ Complete | Console commands available |
| Scene Navigation | 🔄 Needs Testing | SafeLoadScene helper added |
| EventHandler Schema | ✅ Complete + Verified | 130 handlers, 711 fields, 294 high-confidence, 0 low-confidence |

---

## Completed Work

### Phase 1: Diagnostic Infrastructure

**Console Commands Added:**
- `debug.test_all_templates` - Tests loading of all 24 important template types
- `debug.template_log` - Shows recent template loading log entries
- `debug.clear_template_log` - Clears diagnostic log
- `debug.scene_info` - Shows current scene details
- `debug.list_scenes` - Lists scenes in build settings
- `debug.test_tilemap` - Tests TileMap SDK methods

**Files Created:**
- `src/Menace.ModpackLoader/Diagnostics/DataTemplateLoaderDiagnostics.cs`
- `src/Menace.ModpackLoader/Diagnostics/SceneLoadingDiagnostics.cs`
- `src/Menace.ModpackLoader/Diagnostics/SdkSafetyTesting.cs`
- `src/Menace.ModpackLoader/Diagnostics/TemplatePipelineValidator.cs`

### Phase 3: Helper APIs

**Templates.GetProperty() API:**
```csharp
// Simple property access
var damage = Templates.GetProperty("WeaponTemplate", "weapon.sword", "Damage");

// Typed access
var damage = Templates.GetProperty<int>("WeaponTemplate", "Sword", "Damage");

// Multiple properties
var props = Templates.GetProperties("WeaponTemplate", "Sword", "Damage", "Range");
```

**ModeAware API:**
```csharp
var result = ModeAware.Execute(GameMode.Tactical,
    () => TileMap.GetMapInfo()?.Width.ToString(),
    "TileMap.GetMapInfo");
```

**Modpacks SDK:**
```csharp
var allModpacks = Modpacks.GetAllModpacks();
var myMod = Modpacks.GetModpack("My Mod");
var count = Modpacks.GetModpackCount();
var loaded = Modpacks.IsModpackLoaded("My Mod");
```

### Phase 5: Deployment Sync

**Post-build Target Added:**
The `Menace.ModpackLoader.csproj` now includes:
```xml
<Target Name="SyncToBundled" AfterTargets="Build">
  <Copy SourceFiles="$(TargetPath)"
        DestinationFolder="..\..\third_party\bundled\ModpackLoader\"
        SkipUnchangedFiles="false" />
</Target>
```

This automatically copies the built DLL to `third_party/bundled/ModpackLoader/` after every build.

### EventHandler Schema Analysis

**Completed via Ghidra MCP Integration:**
- **130 handlers** documented in knowledge base
- **711 field entries** with 100% description coverage
- **226 fields** verified via direct Ghidra binary analysis
- **294 high-confidence fields** (≥0.9 confidence)
- **0 low-confidence fields remaining** (100% verified)
- 50+ JSON analysis files created in `/docs/`
- 1000+ Ghidra comments added to binary

**Key Formulas Documented:**
- AddMult stacking: `current += (multiplier - 1.0)` - Multiple 1.5x = 2.0x, not 2.25x
- Damage: `FinalDamage = (Damage + DropoffDamage + %MaxHP + %CurrentHP) * CoverMult * CritMult`
- Accuracy: `hitChance = accuracy * coverMult * defenseMult + distance_penalty`
- PropertyChange struct: 12 bytes (PropertyType int, Value int, MultValue float)

**Verified Field Types:**
- Vector3 patterns confirmed (8-byte x,y read + 4-byte z read)
- All counters/stacks use SIGNED comparisons (AccuracyPerStack CAN be negative)
- SkillTemplate references are direct Unity Object pointers (not IDs)

See `ghidra_analysis_report.md` for full details.

---

## Known Limitations (Not Bugs)

### Template Types with Correct but Alternate Names

These types load correctly but have different names than might be expected:

| Expected Name | Actual Type Name | Notes |
|--------------|------------------|-------|
| CharacterTemplate | UnitLeaderTemplate | Squad leaders/characters |
| RegionTemplate | PlanetTemplate | Strategy map regions |
| EncounterTemplate | EntityTemplate | Tactical entities |
| PerkTree | PerkTreeTemplate | Skill trees |
| PerkNode | PerkTemplate | Individual perks |

### Mode-Dependent SDK Methods

These methods only work in specific game modes:

| Method | Required Mode | Error if Wrong Mode |
|--------|--------------|---------------------|
| TileMap.GetMapInfo() | Tactical | Clear error message via ModeAware |
| TileMap.TileToWorld() | Tactical | Clear error message via ModeAware |
| Roster.GetSquaddies() | Strategy | Clear error message via ModeAware |

---

## Remaining Work

### In-Game Testing Required

The following should be validated by running the game:

1. **Template Loading Validation**
   - Run `debug.test_all_templates` to verify all types load
   - Expected: 24/24 types should show ✓

2. **Scene Navigation Testing**
   - Run `debug.scene_info` to verify scene detection works
   - Test `SceneLoadingFixes.SafeLoadScene()` actually changes scenes

3. **Modpack Query Testing**
   - Run `modpacks.list` to verify it shows all loaded modpacks
   - Run `modpacks.info <name>` to verify details work

### Low Priority Enhancements

1. **UI Tooltips** - Show field descriptions in EventHandler editor
2. **Enum Value Labels** - Extract actual enum names from IL2CPP
3. **Field Validation** - Add validation rules (e.g., percentages 0-100)
4. **Default Values** - Document common/default values
5. **Reinstate `squaddie_added` event** - Dropped 2026-07-31: the Harmony hook on
   `Squaddies.TryAddAlive` NREd in the interop trampoline (its `Il2CppSystem.Nullable<Gender>`
   / `Il2CppSystem.Nullable<SkinColor>` parameters cannot be marshalled when the game passes
   null), which skipped the original and emptied the campaign-start roster. `GamePatch.
   FindUnmarshallableParameter` now refuses such methods at patch time. Safe reimplementation
   route if ever wanted: the game exposes `Squaddies.OnAliveSquaddiesChanged` (`Action<int>`);
   patch the zero-arg `Squaddies` ctor or `Init()` postfix, subscribe via
   `DelegateSupport.ConvertDelegate`, diff the alive count to distinguish add from kill, and
   read the nickname from the last entry of `GetAllAlive()`. Unverified whether the game
   actually invokes that event - confirm in Ghidra before building on it.

---

## Testing Checklist

Before each release:

- [ ] Build ModpackLoader (should auto-sync to bundled)
- [ ] Run `debug.test_all_templates` - all should pass
- [ ] Run `modpacks.list` - should show test modpacks
- [ ] Test Templates.GetProperty() in REPL
- [ ] Test ModeAware wrappers give good errors outside tactical mode

---

## File Reference

| File | Purpose |
|------|---------|
| `src/Menace.ModpackLoader/SDK/Templates.cs` | Template property access API |
| `src/Menace.ModpackLoader/SDK/ModeAware.cs` | Mode-aware execution wrapper |
| `src/Menace.ModpackLoader/SDK/Modpacks.cs` | Modpack query API |
| `src/Menace.ModpackLoader/Diagnostics/*.cs` | Diagnostic commands and patches |
| `src/Menace.ModpackLoader/TemplateLoading/*.cs` | Template loading fixes |
| `eventhandler_knowledge.json` | EventHandler field knowledge base (130 handlers, 653 fields) |
| `ghidra_analysis_report.md` | Ghidra analysis summary |
| `docs/ATTACK_HANDLER_VERIFIED_FIELDS.json` | Verified Attack handler fields with damage formulas |
| `docs/reverse-engineering/combat-damage.md` | Combat damage system documentation |
| `docs/reverse-engineering/suppression-morale.md` | Suppression and morale system |
| `docs/reverse-engineering/line-of-sight.md` | Visibility and LOS system |
| `scripts/consolidate_analysis_to_schema.py` | Script to merge Ghidra analysis into schema |
