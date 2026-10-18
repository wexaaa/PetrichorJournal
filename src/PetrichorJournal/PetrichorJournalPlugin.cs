using BepInEx;
using BepInEx.Configuration;
using System;
using UnityEngine;

namespace PetrichorJournal;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class PetrichorJournalPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "com.petrichorjournal.core";
    public const string PluginName = "PetrichorJournal";
    public const string PluginVersion = "0.1.3";

    private RunCapture? capture;
    private JournalWindow? window;

    private void Awake()
    {
        ConfigEntry<KeyboardShortcut> toggleKey = Config.Bind(
            "General",
            "Open Journal",
            new KeyboardShortcut(KeyCode.F8),
            "Opens the local run archive. Press Escape to close it.");
        ConfigEntry<float> autosaveSeconds = Config.Bind(
            "Recording",
            "Autosave interval seconds",
            10f,
            "How often an active run is flushed to disk after data changes. Minimum: 2 seconds.");
        ConfigEntry<bool> recordModList = Config.Bind(
            "Recording",
            "Record loaded mods",
            true,
            "Stores plugin GUIDs and versions in each local report so modded runs can be identified later.");
        ConfigEntry<bool> recordItemTimeline = Config.Bind(
            "Recording",
            "Record item timeline",
            true,
            "Records local inventory gains and losses during a run.");
        ConfigEntry<bool> redactPlayerNames = Config.Bind(
            "Privacy",
            "Redact player names",
            true,
            "Stores Player 1, Player 2, etc. instead of multiplayer display names.");

        var store = new ArchiveStore(Logger);
        store.MarkInterruptedRuns();
        window = new JournalWindow(store, toggleKey);
        capture = new RunCapture(store, Logger, autosaveSeconds, recordModList, recordItemTimeline, redactPlayerNames);
        capture.ArchiveChanged += window.Refresh;
        capture.InstallHooks();
        Logger.LogInfo("Petrichor Journal is ready. Press F8 to open the archive.");
    }

    private void Update()
    {
        capture?.Tick();
        window?.Tick();
    }

    private void OnGUI()
    {
        window?.Draw();
    }

    private void OnDestroy()
    {
        if (capture != null)
        {
            capture.RemoveHooks();
            if (window != null)
                capture.ArchiveChanged -= window.Refresh;
        }
    }
}
