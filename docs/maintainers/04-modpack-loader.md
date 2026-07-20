# Menace.ModpackLoader - Maintainer Documentation

## Overview

**Menace.ModpackLoader** is a MelonLoader mod that serves as the runtime injection framework for modpacks. It operates as an IL2CPP plugin that loads modpack definitions, applies template patches, manages asset replacements, and provides SDK features for scripting.

**Location:** `src/Menace.ModpackLoader/`

## Architecture

### Key Integration Points
- **MelonLoader** - Base mod framework (lifecycle hooks)
- **HarmonyLib** - IL2CPP patching (template injection, events, UI)
- **Il2CppInterop** - IL2CPP reflection and memory manipulation
- **Unity Engine** - Direct asset and scene manipulation

### Runtime Flow

```
OnInitializeMelon()
├─ SDK initialization (SdkLogger, OffsetCache, DevConsole)
├─ Load modpacks from Mods/*.json
├─ Load plugin DLLs (IModpackPlugin implementations)
├─ Initialize patches (event hooks, UI, console commands)
├─ Load Lua scripts
└─ Ready for scene load

OnSceneWasLoaded()
├─ Attempt template injection (if not yet successful)
├─ Apply asset replacements
└─ Notify DLLs and Lua scripts
```

## Entry Point

**Location:** `ModpackLoaderMod.cs`

### Assembly Declaration (lines 17-26)
```csharp
[assembly: MelonInfo(typeof(ModpackLoaderMod), "Menace Modpack Loader",
    VersionNumber, "Menace Modkit")]
[assembly: MelonGame(null, null)]  // Works with ANY game
[assembly: MelonOptionalDependencies(...)]  // Optional Roslyn, Newtonsoft.Json
```

### Initialization Sequence (lines 40-114)

1. **SDK Initialization** - SdkLogger, OffsetCache, DevConsole
2. **Modpack Loading** - Discovers `modpack.json` files in `Mods/`
3. **Plugin System** - `DllLoader.InitializeAllPlugins()`
4. **Patching System** - Template injection, tactical/strategy hooks
5. **Lua Engine** - `LuaScriptEngine.Instance.Initialize()`

## Public API Surface

### IModpackPlugin Interface

**Location:** `IModpackPlugin.cs`

```csharp
public interface IModpackPlugin
{
    void OnInitialize(MelonLogger.Instance logger, HarmonyLib.Harmony harmony);
    void OnSceneLoaded(int buildIndex, string sceneName);
    void OnUpdate() { }     // Optional
    void OnGUI() { }        // Optional (IMGUI)
    void OnUnload() { }     // Optional
}
```

### SDK Wrappers (Menace.SDK namespace)
- `GameState` - Scene/state tracking
- `GameQuery` - Entity lookups (cached)
- `Inventory` - Item management
- `Operation` - Campaign missions
- `Roster` - Squad management
- `TileMap`, `Pathfinding`, `LineOfSight` - Tactical gameplay
- `LuaScriptEngine` - Script context management

### HTTP/MCP Server API

**Location:** `Mcp/GameMcpServer.cs:30-145`

Available at `http://localhost:7655/`:
- Toggle-able via ModSettings
- Query endpoints for tooling integration
- REPL evaluation endpoint (requires Roslyn)

## Key Components

### Modpack Structure

```
Mods/
├─ modpack1/
│  ├─ modpack.json        # Manifest
│  ├─ patches/            # Legacy clone definitions (V1)
│  ├─ clones/             # Clone JSON files (per-type)
│  ├─ assets/             # Disk asset files (PNG, WAV)
│  ├─ dlls/               # Plugin DLL files
│  ├─ bundles/            # Asset bundles (loaded loose at runtime)
│  ├─ models/             # GLB model files
│  └─ scripts/            # Lua scripts
```

> [!NOTE]
> **v37 (2026-07):** the old top-level `Mods/compiled/` directory (pre-baked bundles from
> the removed bundler) is gone. Bundles are loaded loose from each modpack's `bundles/`
> folder; the loader no longer reads a `compiled/` path.

### Modpack Manifest (V2)

```json
{
  "manifestVersion": 2,
  "name": "MyMod",
  "version": "1.0.0",
  "author": "Modder",
  "loadOrder": 10,
  "securityStatus": "SourceVerified",

  "patches": {
    "WeaponTemplate": {
      "my_weapon": {
        "Title": "My Custom Weapon",
        "Damage": 50,
        "Properties.BaseMagazineSize": 30
      }
    }
  },

  "clones": {
    "WeaponTemplate": {
      "cloned_weapon": "base_weapon"
    }
  },

  "assets": {
    "Assets/Resources/textures/icon_weapon.png": "assets/icon_weapon.png"
  }
}
```

### Template Injection System

**Location:** `TemplateInjection.cs` (1536 lines)

Core runtime patching engine:
- `ApplyTemplateModifications()` (lines 498-742) - Main entry point
- Uses IL2CPP reflection to find template types
- Modifies field values on game objects
- Handles complex nested types, arrays, localization

**Key Features:**
- IL2CPP Type Resolution via `Il2CppObjectBase.TryCast<T>()`
- Localization handling (creates NEW instances to avoid corruption)
- Collection support (IL2CppStructArray, IL2CppReferenceArray, Il2CppList)
- Nested field support with dot notation

### Template Cloning System

**Location:** `TemplateCloning.cs` (599 lines)

Deep-copy templates to create new ones:
- `ApplyClones()` - Instantiates via UnityEngine.Object.Instantiate()
- `RegisterInLoader()` - Adds to DataTemplateLoader's dictionaries
- `RegisterBundleClones()` - Handles clones from asset bundles

**Note:** Runtime cloning is DISABLED (`DISABLE_RUNTIME_CLONING = true`). Bundles provide native clones.

### Bundle Loading

**Location:** `BundleLoader.cs` (276 lines)

```csharp
// Asset registries
_assetsByName        // Lookup by name only
_assetsByTypeAndName // Precise type+name lookup
_assetSourceModpack  // Track source modpack

// Public API
GetAsset<T>(name)
GetAssetsByType(typeName)
HasAsset(name)
```

**Unity 6 Workaround:** Uses `AssetBundle.LoadFromMemory()` due to IL2CPP binding issues with LoadFromFile.

### Asset Replacement

**Location:** `AssetInjectionPatches.cs` / `AssetReplacer.cs` (1431 lines)

Replacement sources:
1. **Disk Files** - PNG/JPG/TGA/BMP → Textures, WAV → AudioClips, GLB/GLTF → Models
2. **Asset Bundles** - Any type from BundleLoader

Replacement strategies:
- Textures: `Graphics.CopyTexture()` pixel-level replacement
- Audio: `AudioClip.SetData()` sample-level replacement
- Meshes: Full reconstruction
- Prefabs: Recursive component/hierarchy copying

**Note (v37):** Runtime replacement is **enabled** (`DISABLE_RUNTIME_REPLACEMENT = false`) — it is now the only path. Assets are loaded loose from each modpack's `bundles/`/`assets/` folders (via `BundleLoader`) and applied at runtime; there are no pre-compiled bundles.

### GLB/GLTF Model Loading

**Location:** `GlbLoader.cs` (571 lines)

- Parses GLB via SharpGLTF.Schema2
- Creates Unity Mesh/Material/Texture2D objects
- Coordinate conversion: GLTF (Y-up, right-handed) → Unity (Y-up, left-handed)
- Skinned mesh support with bone setup

### Plugin System

**Location:** `DllLoader.cs` (242 lines)

Security:
- `SourceVerified` / `SourceWithWarnings` = trust and load
- Other = blocked unless `AllowUnverifiedDlls=true`

Discovery:
- Reflection-based scan for IModpackPlugin implementations
- Each plugin gets unique Harmony instance

## TODOs and Known Issues

### Runtime paths (now the only paths, v37)

The bake pipeline is gone, so the two flags that used to disable the runtime paths in
favour of pre-compiled bundles are now permanently **off** — these are the live code:

1. **Runtime Asset Replacement** (`AssetReplacer.cs`) — `DISABLE_RUNTIME_REPLACEMENT = false`.
   Loose bundles/assets are applied at runtime by `BundleLoader`.
2. **Runtime Template Cloning** (`TemplateCloning.cs:22`) — `DISABLE_RUNTIME_CLONING = false`.
   Clones are Instantiated, deep-copied (`TemplateCloneDeepCopy`) and registered into
   `DataTemplateLoader` (`TemplateRegistration`) at runtime; there are no native clones in
   pre-built bundles.

### Potential Issues

1. **Bundle Loading Fallback** (BundleLoader.cs:85-135)
   - Primary: `GetAllAssetNames()` may fail on some formats
   - Fallback: `LoadAllAssets()` less efficient but more compatible

2. **Localization Memory Corruption Risk** (TemplateInjection.cs)
   - Old approach modified shared instances, causing corruption
   - Fixed: `CreateLocalizedObject()` creates fresh instances

3. **IL2CPP Type Resolution Fragility** (TemplateCloning / TemplateRegistration)
   - Native fields are surfaced as properties on IL2CPP proxy types; registration works
     through the typed interop dictionaries rather than guessed field names
   - Still version-sensitive if the game renames the underlying members

4. **Black Market Pool** — the former magic `0xAC` max-quantity offset has been removed.
   Clones are mirrored into every ancestor `DataTemplateLoader` slot, so the market's
   `GetAll<BaseItemTemplate>()` enumeration sees them natively (no offset poking).

## Dependencies

### External
- HarmonyLib - IL2CPP patching
- Il2CppInterop.Runtime - IL2CPP proxy types
- MelonLoader - Base mod framework
- Newtonsoft.Json - JSON serialization
- SharpGLTF.Core - GLB/GLTF parsing
- UnityEngine - Game engine APIs
- Microsoft.CodeAnalysis (optional) - Roslyn for REPL

### Game Assembly Dependencies
- `Menace.States.StrategyState`
- `Menace.Strategy.StrategyConfig`, `BlackMarket`
- `Menace.Items.BaseItemTemplate`
- `Menace.Tools.DataTemplateLoader`
- All template types (WeaponTemplate, UnitTemplate, etc.)

## Summary Table

| Component | File | Lines | Purpose |
|-----------|------|-------|---------|
| ModpackLoaderMod | ModpackLoaderMod.cs | 760 | Main entry point |
| TemplateInjection | TemplateInjection.cs | 1536 | Runtime patching |
| TemplateCloning | TemplateCloning.cs | 599 | Template cloning |
| EarlyTemplateInjection | EarlyTemplateInjection.cs | 469 | Pre-pool injection |
| BundleLoader | BundleLoader.cs | 276 | Asset bundles |
| AssetReplacer | AssetInjectionPatches.cs | 1431 | Asset replacement |
| GlbLoader | GlbLoader.cs | 571 | GLB/GLTF import |
| DllLoader | DllLoader.cs | 242 | Plugin discovery |

## Maintainer Notes

1. **IL2CPP Fragility** - All hardcoded offsets will break if game internals change
2. **Load Order Critical** - Patches, clones, assets must apply in correct sequence
3. **Loose-file deploy (v37)** - Modpacks deploy as loose files; the loader applies
   patches/clones and loads loose bundles from `Mods/` at runtime. There is no bake step
   and no pre-compiled-bundle expectation.
4. **Event Hook Integration** - Patches extend game event systems for Lua/C# scripting
5. **Save Compatibility** - ModRegistry tracks which mods were active for saves
