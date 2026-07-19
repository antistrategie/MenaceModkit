using System.Collections.ObjectModel;
using System.Linq;
using Menace.Modkit.ModManagement;
using ReactiveUI;

namespace Menace.ModManager.ViewModels;

/// <summary>
/// Backs the main window: scans <c>Mods/</c> via <see cref="ModCatalog"/> and exposes the
/// unified mod list. Stateless — <see cref="Refresh"/> re-reads the filesystem each call.
/// </summary>
public sealed class MainViewModel : ReactiveObject
{
    private readonly ModCatalog _catalog = new();

    public ObservableCollection<ManagedMod> Mods { get; } = new();

    private string _status = string.Empty;
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public MainViewModel() => Refresh();

    public void Refresh()
    {
        Mods.Clear();
        foreach (var mod in _catalog.Scan().OrderBy(m => m.Kind).ThenBy(m => m.DisplayName))
            Mods.Add(mod);

        var path = _catalog.ModsPath;
        Status = path == null
            ? "Game not located — set MENACE_GAME_PATH."
            : $"{Mods.Count} mod(s) — {path}";
    }
}
