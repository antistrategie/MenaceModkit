using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform.Storage;
using Menace.Modkit.ModManagement;
using Menace.ModManager.ViewModels;

namespace Menace.ModManager.Views;

public partial class MainWindow : Window
{
    private bool _forceClose;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragEnterEvent, OnDragEnter);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        // Paste-to-install: on Wayland the compositor's XWayland DnD bridge often never
        // delivers drags to this (X11) window, but the clipboard bridge is reliable —
        // copy a file in the file manager, Ctrl+V here.
        AddHandler(KeyDownEvent, OnKeyDown);
        Closing += OnClosing;
    }

    private MainViewModel? Vm => DataContext as MainViewModel;

    /// <summary>
    /// Run an async UI handler with a crash guard: an exception escaping an
    /// <c>async void</c> handler rethrows on the sync context and kills the process,
    /// so surface it in the error banner instead.
    /// </summary>
    private async Task GuardedAsync(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Vm?.ReportError($"Unexpected error: {ex.Message}");
        }
    }

    // Closing mid-install would kill background copy/extract threads mid-write; make the
    // user opt into that explicitly. (Installs swap complete trees into place, so the
    // damage is bounded — but a busy exit is still never what they meant to do.)
    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose || Vm is not { IsBusy: true })
            return;

        e.Cancel = true;
        _ = GuardedAsync(async () =>
        {
            var confirmed = await new ConfirmDialog(
                "An operation is still running.\n\nQuitting now may leave it unfinished. Quit anyway?")
                .ShowDialog<bool>(this);
            if (confirmed)
            {
                _forceClose = true;
                Close();
            }
        });
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        if (Vm is not { IsBusy: false } vm)
            return;

        await GuardedAsync(async () =>
        {
            var paths = await GetClipboardPathsAsync();
            if (paths.Count > 0)
                await vm.InstallManyAsync(paths);
        });
    }

    /// <summary>
    /// Local paths on the clipboard: real file objects when the platform exposes them
    /// (Windows Explorer copy carries no text at all), otherwise text parsed as a
    /// uri-list / plain paths.
    /// </summary>
    private async Task<List<string>> GetClipboardPathsAsync()
    {
        var paths = new List<string>();
        if (Clipboard is not { } clipboard)
            return paths;

        try
        {
            var files = await clipboard.TryGetFilesAsync();
            if (files is not null)
            {
                foreach (var item in files)
                {
                    var path = item.TryGetLocalPath();
                    if (!string.IsNullOrEmpty(path))
                        paths.Add(path);
                }
            }
        }
        catch
        {
            // Some platforms don't expose file objects on the clipboard — text fallback below.
        }

        if (paths.Count > 0)
            return paths;

        return ParsePathsFromText(await clipboard.TryGetTextAsync());
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        // Diagnostic for Wayland/XWayland DnD issues: if this line never prints, the drag
        // is not reaching the window at all (compositor-level gap, not an app bug).
        Console.WriteLine($"[dnd] DragEnter formats: {string.Join(", ", e.DataTransfer?.Formats.Select(f => f.ToString()) ?? new[] { "(none)" })}");
        SetEffect(e);
    }

    private void OnDragOver(object? sender, DragEventArgs e) => SetEffect(e);

    // Accept the drag if it carries files — or text, since some Linux file managers
    // deliver drags as a text/uri-list that never surfaces through the File format.
    // Gated on the format (known during the drag), not on GetFiles() (whose data may
    // not be materialised until the actual drop).
    private static void SetEffect(DragEventArgs e)
        => e.DragEffects = e.DataTransfer?.Formats.Any(f => f == DataFormat.File || f == DataFormat.Text) == true
            ? DragDropEffects.Copy
            : DragDropEffects.None;

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        // Ignore drops while an operation is already running.
        if (Vm is not { IsBusy: false } vm)
            return;

        Console.WriteLine($"[dnd] Drop received, formats: {string.Join(", ", e.DataTransfer?.Formats.Select(f => f.ToString()) ?? new[] { "(none)" })}");
        // Paths must be extracted synchronously within the drop event; the batch installs
        // as one operation so a failure in one file isn't hidden by the next succeeding.
        var paths = ExtractDroppedPaths(e);
        await GuardedAsync(() => vm.InstallManyAsync(paths));
    }

    /// <summary>
    /// Local paths from a drop: the File format when available, otherwise a text
    /// payload parsed as a uri-list / plain paths (one per line, file:// or absolute).
    /// </summary>
    private static List<string> ExtractDroppedPaths(DragEventArgs e)
    {
        var paths = new List<string>();

        var files = e.DataTransfer?.TryGetFiles();
        if (files is not null)
        {
            foreach (var item in files)
            {
                var path = item.TryGetLocalPath();
                if (!string.IsNullOrEmpty(path))
                    paths.Add(path);
            }
        }

        if (paths.Count > 0)
            return paths;

        return ParsePathsFromText(e.DataTransfer?.TryGetText());
    }

    /// <summary>
    /// Parse a uri-list / newline-separated path payload (file:// URIs, absolute Unix
    /// paths, or drive-letter Windows paths) into existing local paths.
    /// </summary>
    private static List<string> ParsePathsFromText(string? text)
    {
        var paths = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return paths;

        foreach (var raw in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (raw.StartsWith('#'))
                continue; // uri-list comment line

            string? path = null;
            if (raw.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(raw, UriKind.Absolute, out var uri))
                path = uri.LocalPath;
            else if (raw.StartsWith('/'))
                path = raw;
            else if (raw.Length >= 3 && char.IsAsciiLetter(raw[0]) && raw[1] == ':' && raw[2] is '\\' or '/')
                path = raw; // Windows drive-letter path (pasted "C:\...\mod.zip" text)

            if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
                paths.Add(path);
        }

        return paths;
    }

    private void OnRefreshClick(object? sender, RoutedEventArgs e) => Vm?.Refresh();

    private async void OnLocateGameClick(object? sender, RoutedEventArgs e)
    {
        await GuardedAsync(async () =>
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select the MENACE install folder (contains Menace.exe)",
                AllowMultiple = false,
            });

            var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(path))
                Vm?.SetGamePath(path);
        });
    }

    private void OnDismissErrorClick(object? sender, RoutedEventArgs e) => Vm?.DismissError();

    private void OnToggleClick(object? sender, RoutedEventArgs e)
    {
        if (sender is CheckBox { DataContext: ManagedMod mod })
            Vm?.Toggle(mod);
    }

    private async void OnInstallClick(object? sender, RoutedEventArgs e)
    {
        await GuardedAsync(async () =>
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select a mod archive or DLL to install",
                AllowMultiple = false,
                FileTypeFilter = new[]
                {
                    new FilePickerFileType("Mods (archive or DLL)")
                    {
                        Patterns = new[] { "*.zip", "*.7z", "*.rar", "*.tar", "*.tar.gz", "*.tgz", "*.dll" },
                    },
                    FilePickerFileTypes.All,
                },
            });

            var path = files.Count > 0 ? files[0].TryGetLocalPath() : null;
            if (!string.IsNullOrEmpty(path) && Vm is { } vm)
                await vm.InstallAsync(path);
        });
    }

    private async void OnGetJiangyuClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await GuardedAsync(vm.InstallJiangyuLoaderAsync);
    }

    private async void OnInstallMelonLoaderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await GuardedAsync(vm.InstallMelonLoaderAsync);
    }

    private async void OnInstallModpackLoaderClick(object? sender, RoutedEventArgs e)
    {
        if (Vm is { } vm)
            await GuardedAsync(vm.InstallModpackLoaderAsync);
    }

    private async void OnUninstallClick(object? sender, RoutedEventArgs e)
    {
        if (Vm?.Selected is not { } mod)
            return;

        if (!mod.CanToggle)
        {
            Vm.Refresh(); // no-op refresh; protected mods can't be removed
            return;
        }

        await GuardedAsync(async () =>
        {
            var confirmed = await new ConfirmDialog(
                $"Delete \"{mod.DisplayName}\" from disk?\n\nThis removes its files and cannot be undone.")
                .ShowDialog<bool>(this);

            if (confirmed && Vm is { } vm)
                await vm.UninstallSelectedAsync();
        });
    }
}
