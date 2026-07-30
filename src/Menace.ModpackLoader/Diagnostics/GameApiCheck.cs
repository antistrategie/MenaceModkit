using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Menace.SDK;

namespace Menace.ModpackLoader.Diagnostics;

/// <summary>
/// Probes the game members the SDK reaches for and reports the ones that have moved.
///
/// Without this, a game update that renames or re-signatures a member shows up as one console
/// command quietly returning an error, months later, when somebody happens to try it. The check
/// runs at load and puts the whole picture in one place: either a single line saying everything
/// resolved, or the exact list of what to go and re-derive.
/// </summary>
public static class GameApiCheck
{
    /// <summary>What kind of member an entry expects to find.</summary>
    public enum MemberKind
    {
        Type,
        Method,
        Property,
        Field
    }

    /// <summary>One thing the SDK depends on, and where it is used.</summary>
    public sealed class Entry
    {
        public string TypeName;
        public MemberKind Kind;
        public string MemberName;
        /// <summary>Least number of parameters the call site can work with. 0 for non-methods.</summary>
        public int MinParameters;
        /// <summary>
        /// Game type name of the method's first parameter, when the call site binds on it. Set this
        /// for overloaded methods: without it, a long unrelated overload satisfies the parameter
        /// count on its own and the check passes while the overload actually called is gone.
        /// </summary>
        public string FirstParameterTypeName;
        /// <summary>
        /// Exact parameter count the call site binds with, when it uses exact-arity lookup.
        /// -1 (the default) means arity is not probed.
        /// </summary>
        public int ExactArity = -1;
        /// <summary>True when the call site binds the member as static (BindingFlags.Static).</summary>
        public bool RequireStatic;
        /// <summary>Who breaks when this is missing, so a failure names the affected feature.</summary>
        public string UsedBy;
    }

    public sealed class Result
    {
        public Entry Entry;
        public bool Found;
        public string Detail;
    }

    /// <summary>
    /// The dependencies worth checking: members the SDK calls by reflection, where a rename or a
    /// changed signature fails silently. Members the loader references through the generated
    /// proxies are left out, since those break the build instead.
    /// </summary>
    private static readonly Entry[] Manifest =
    {
        // Item spawning and giving (spawn / give console commands)
        new() { TypeName = "Menace.States.StrategyState", Kind = MemberKind.Method, MemberName = "Get", RequireStatic = true, UsedBy = "spawn, give, strategy SDK" },
        new() { TypeName = "Menace.States.StrategyState", Kind = MemberKind.Property, MemberName = "OwnedItems", UsedBy = "spawn" },
        new() { TypeName = "Menace.Strategy.OwnedItems", Kind = MemberKind.Method, MemberName = "AddItem", MinParameters = 2, FirstParameterTypeName = "Menace.Items.BaseItemTemplate", UsedBy = "spawn" },
        new() { TypeName = "Menace.Items.BaseItemTemplate", Kind = MemberKind.Method, MemberName = "CreateItem", ExactArity = 1, UsedBy = "give" },
        new() { TypeName = "Menace.Items.ItemContainer", Kind = MemberKind.Method, MemberName = "Add", MinParameters = 2, FirstParameterTypeName = "Menace.Items.Item", UsedBy = "give" },
        new() { TypeName = "Menace.Tactical.Entity", Kind = MemberKind.Method, MemberName = "GetItems", UsedBy = "give, items, itemvalue, hastag" },

        // Event hooks (Lua events, plugin events). The Invoke methods below stand in for the whole
        // set: they are hooked positionally, so a changed parameter list is the thing to catch.
        new() { TypeName = "Menace.Tactical.TacticalManager", Kind = MemberKind.Type, UsedBy = "tactical event hooks" },
        new() { TypeName = "Menace.Tactical.TacticalManager", Kind = MemberKind.Method, MemberName = "InvokeOnDeath", MinParameters = 3, UsedBy = "actor_killed event" },
        new() { TypeName = "Menace.Tactical.TacticalManager", Kind = MemberKind.Method, MemberName = "SetActiveActor", MinParameters = 2, UsedBy = "turn_start event" },
        new() { TypeName = "Menace.Strategy.Roster", Kind = MemberKind.Method, MemberName = "HireLeader", MinParameters = 1, UsedBy = "leader_hired event" },
        new() { TypeName = "Menace.Strategy.Squaddies", Kind = MemberKind.Method, MemberName = "TryAddAlive", MinParameters = 6, UsedBy = "squaddie_added event" },
        new() { TypeName = "Menace.Strategy.BlackMarket", Kind = MemberKind.Method, MemberName = "Restock", MinParameters = 2, UsedBy = "blackmarket_restocked event" },
        new() { TypeName = "Menace.Tactical.Actor", Kind = MemberKind.Type, UsedBy = "tactical event hooks, Lua actor API" },
        new() { TypeName = "Menace.Tactical.Entity", Kind = MemberKind.Field, MemberName = "m_FactionID", UsedBy = "faction lookups in event hooks and Lua" },
        new() { TypeName = "Menace.Strategy.Roster", Kind = MemberKind.Type, UsedBy = "strategy event hooks, roster SDK" },
        new() { TypeName = "Menace.Strategy.BlackMarket", Kind = MemberKind.Type, UsedBy = "strategy event hooks, black market SDK" },
        new() { TypeName = "Menace.Strategy.EventManager", Kind = MemberKind.Type, UsedBy = "strategy event hooks" },

        // Combat interception
        new() { TypeName = "Menace.Tactical.EntityProperties", Kind = MemberKind.Type, UsedBy = "Intercept (damage, accuracy, armour)" },
        new() { TypeName = "Menace.Tactical.AI.Agent", Kind = MemberKind.Method, MemberName = "Evaluate", UsedBy = "Intercept (AI evaluation)" },

        // Fields read positionally by the strategy SDK
        new() { TypeName = "Menace.Strategy.Roster", Kind = MemberKind.Field, MemberName = "m_HiredLeaders", UsedBy = "roster listing" },
        new() { TypeName = "Menace.Strategy.Operation", Kind = MemberKind.Field, MemberName = "m_Missions", UsedBy = "operation SDK" },
        new() { TypeName = "Menace.Strategy.OperationsManager", Kind = MemberKind.Field, MemberName = "m_AvailableOperations", UsedBy = "operation SDK" },
        new() { TypeName = "Menace.Strategy.BaseUnitLeader", Kind = MemberKind.Field, MemberName = "m_Perks", UsedBy = "perks SDK" }
    };

    private static List<Result> _lastRun;

    /// <summary>
    /// Probe every entry. Returns one result per entry, in manifest order.
    /// </summary>
    public static List<Result> Run()
    {
        var results = new List<Result>(Manifest.Length);

        foreach (var entry in Manifest)
        {
            var type = GameType.FindManagedProxy(entry.TypeName);
            if (type == null)
            {
                results.Add(new Result { Entry = entry, Found = false, Detail = "type not found" });
                continue;
            }

            if (entry.Kind == MemberKind.Type)
            {
                results.Add(new Result { Entry = entry, Found = true, Detail = type.FullName });
                continue;
            }

            // One throwing probe (an AmbiguousMatchException from a shadowed property, say) must
            // not abort the whole manifest: the run matters most right after a game update, which
            // is exactly when probes are most likely to throw.
            try
            {
                results.Add(Probe(entry, type));
            }
            catch (Exception ex)
            {
                results.Add(new Result { Entry = entry, Found = false, Detail = $"probe threw: {ex.GetType().Name}" });
            }
        }

        _lastRun = results;
        return results;
    }

    /// <summary>
    /// Run the check and log a one-line summary, plus a warning per missing member. Called during
    /// loader startup.
    /// </summary>
    public static void RunAndLog()
    {
        List<Result> results;
        try
        {
            results = Run();
        }
        catch (Exception ex)
        {
            ModError.ReportInternal("GameApiCheck", "API check failed to run", ex);
            return;
        }

        var missing = results.Where(r => !r.Found).ToList();
        if (missing.Count == 0)
        {
            SdkLogger.Msg($"[GameApiCheck] {results.Count}/{results.Count} game members resolved");
            return;
        }

        SdkLogger.Warning(
            $"[GameApiCheck] {results.Count - missing.Count}/{results.Count} game members resolved, " +
            $"{missing.Count} missing (run 'debug.apicheck' for the list)");

        foreach (var r in missing)
            SdkLogger.Warning($"[GameApiCheck]   {Describe(r.Entry)}: {r.Detail} - affects {r.Entry.UsedBy}");
    }

    public static void RegisterConsoleCommands()
    {
        DevConsole.RegisterCommand("debug.apicheck", "",
            "Check the game members the SDK depends on and report any that moved", _ =>
        {
            // Always probe fresh: the startup snapshot goes stale the moment anything loads.
            var results = Run();
            var missing = results.Count(r => !r.Found);
            var lines = new List<string>
            {
                $"Game API check: {results.Count - missing}/{results.Count} resolved"
            };

            foreach (var r in results.OrderBy(r => r.Found))
            {
                lines.Add($"  [{(r.Found ? "ok" : "MISSING")}] {Describe(r.Entry)}" +
                          (r.Found ? "" : $" - {r.Detail}, affects {r.Entry.UsedBy}"));
            }

            return string.Join("\n", lines);
        });

        DevConsole.RegisterCommand("debug.apicheck_rerun", "",
            "Re-probe the game members (use after a scene load or game update)", _ =>
        {
            var results = Run();
            var missing = results.Count(r => !r.Found);
            return $"Re-probed {results.Count} members, {missing} missing";
        });
    }

    private static Result Probe(Entry entry, Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                                   | BindingFlags.Instance | BindingFlags.Static;

        switch (entry.Kind)
        {
            case MemberKind.Method:
                var overloads = type.GetMethods(flags).Where(m => m.Name == entry.MemberName).ToList();
                if (overloads.Count == 0)
                    return new Result { Entry = entry, Found = false, Detail = "method not found" };

                // When the entry names a first parameter, bind exactly the way the call site does,
                // so a surviving unrelated overload cannot mask the one that went missing.
                if (!string.IsNullOrEmpty(entry.FirstParameterTypeName))
                {
                    var firstParam = GameType.FindManagedProxy(entry.FirstParameterTypeName);
                    if (firstParam == null)
                        return new Result
                        {
                            Entry = entry,
                            Found = false,
                            Detail = $"parameter type {entry.FirstParameterTypeName} not found"
                        };

                    return GameMember.FindMethod(type, entry.MemberName, firstParam) != null
                        ? new Result { Entry = entry, Found = true }
                        : new Result
                        {
                            Entry = entry,
                            Found = false,
                            Detail = $"no overload taking {entry.FirstParameterTypeName} first"
                        };
                }

                if (entry.RequireStatic && !overloads.Any(m => m.IsStatic))
                    return new Result { Entry = entry, Found = false, Detail = "no static overload (call site binds static)" };

                if (entry.ExactArity >= 0)
                    return overloads.Any(m => m.GetParameters().Length == entry.ExactArity)
                        ? new Result { Entry = entry, Found = true }
                        : new Result
                        {
                            Entry = entry,
                            Found = false,
                            Detail = $"no overload with exactly {entry.ExactArity} parameter(s)"
                        };

                var best = overloads.Max(m => m.GetParameters().Length);
                if (best < entry.MinParameters)
                    return new Result
                    {
                        Entry = entry,
                        Found = false,
                        Detail = $"longest overload takes {best} parameter(s), call site needs {entry.MinParameters}"
                    };

                return new Result { Entry = entry, Found = true };

            case MemberKind.Property:
                // GetProperty(name) throws AmbiguousMatchException when a proxy hierarchy shadows
                // the name; a shadowed property still exists, so enumerate instead.
                return type.GetProperties(flags).Any(pr => pr.Name == entry.MemberName)
                    ? new Result { Entry = entry, Found = true }
                    : new Result { Entry = entry, Found = false, Detail = "property not found" };

            case MemberKind.Field:
                // IL2CppInterop exposes game fields as get_/set_ accessor pairs, so a public field
                // shows up through reflection as a property. Check both, and the native field
                // metadata pointer it generates, before calling it missing.
                var hasField = type.GetFields(flags).Any(f => f.Name == entry.MemberName
                                                              || f.Name == $"NativeFieldInfoPtr_{entry.MemberName}")
                               || type.GetProperties(flags).Any(pr => pr.Name == entry.MemberName);
                return hasField
                    ? new Result { Entry = entry, Found = true }
                    : new Result { Entry = entry, Found = false, Detail = "field not found" };

            default:
                return new Result { Entry = entry, Found = true };
        }
    }

    private static string Describe(Entry entry)
        => entry.Kind == MemberKind.Type
            ? entry.TypeName
            : $"{entry.TypeName}.{entry.MemberName}";
}
