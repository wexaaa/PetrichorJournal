using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace PetrichorJournal;

public sealed class JournalWindow
{
    private readonly ArchiveStore store;
    private readonly ConfigEntry<KeyboardShortcut> toggleKey;
    private List<JournalRun> runs = new();
    private JournalRun? selected;
    private string search = "";
    private string noteBuffer = "";
    private int outcomeFilter;
    private bool open;
    private Rect windowRect = new(80f, 60f, 1120f, 720f);
    private Vector2 listScroll;
    private Vector2 detailScroll;
    private CursorLockMode previousLockMode;
    private bool previousCursorVisible;

    public JournalWindow(ArchiveStore store, ConfigEntry<KeyboardShortcut> toggleKey)
    {
        this.store = store;
        this.toggleKey = toggleKey;
        Refresh();
    }

    public void Tick()
    {
        if (toggleKey.Value.IsDown())
            SetOpen(!open);
        if (open && Input.GetKeyDown(KeyCode.Escape))
            SetOpen(false);
    }

    public void Refresh()
    {
        string? selectedId = selected?.id;
        runs = store.LoadAll();
        selected = selectedId == null ? runs.FirstOrDefault() : runs.FirstOrDefault(run => run.id == selectedId);
        noteBuffer = selected?.notes ?? "";
    }

    public void Draw()
    {
        if (!open)
            return;
        windowRect = GUI.Window(934117, windowRect, DrawWindow, "Petrichor Journal — local run archive");
    }

    private void SetOpen(bool value)
    {
        if (open == value)
            return;
        open = value;
        if (open)
        {
            previousLockMode = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Refresh();
        }
        else
        {
            Cursor.lockState = previousLockMode;
            Cursor.visible = previousCursorVisible;
        }
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginVertical();
        GUILayout.BeginHorizontal();
        GUILayout.Label("Search", GUILayout.Width(48f));
        search = GUILayout.TextField(search, GUILayout.MinWidth(180f));
        if (GUILayout.Button(FilterLabel(), GUILayout.Width(105f)))
            outcomeFilter = (outcomeFilter + 1) % 3;
        if (GUILayout.Button("Refresh", GUILayout.Width(80f)))
            Refresh();
        if (GUILayout.Button("Close", GUILayout.Width(70f)))
            SetOpen(false);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        DrawRunList();
        DrawDetails();
        GUILayout.EndHorizontal();
        GUILayout.EndVertical();
        GUI.DragWindow(new Rect(0f, 0f, windowRect.width, 28f));
    }

    private void DrawRunList()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.Width(330f), GUILayout.ExpandHeight(true));
        IEnumerable<JournalRun> filtered = runs.Where(MatchesFilter);
        GUILayout.Label($"Runs: {filtered.Count()} / {runs.Count}");
        listScroll = GUILayout.BeginScrollView(listScroll);
        foreach (JournalRun run in filtered)
        {
            string marker = run.id == selected?.id ? "▶ " : "";
            string label = marker + RunTitle(run);
            if (GUILayout.Button(label, GUILayout.Height(48f)))
            {
                selected = run;
                noteBuffer = run.notes ?? "";
                detailScroll = Vector2.zero;
            }
        }
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private void DrawDetails()
    {
        GUILayout.BeginVertical(GUI.skin.box, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
        if (selected == null)
        {
            GUILayout.Label("No archived runs yet. Finish or leave a run and it will appear here.");
            GUILayout.EndVertical();
            return;
        }

        detailScroll = GUILayout.BeginScrollView(detailScroll);
        GUILayout.Label($"{OutcomeGlyph(selected)}  {PrimarySurvivor(selected)} — {selected.difficultyDisplay}");
        GUILayout.Label($"{FormatUtc(selected.startedAtUtc)}  •  {FormatDuration(selected.elapsedSeconds)}  •  {selected.stages.Count} stages  •  {selected.mods.Count} plugins");
        GUILayout.Label($"Status: {selected.outcome}    Ending: {Fallback(selected.endingDisplay, "—")}");

        GUILayout.Space(8f);
        GUILayout.Label("Players");
        foreach (JournalPlayerSummary player in selected.players)
        {
            GUILayout.Label($"{player.playerName} — {player.survivorDisplay}");
            GUILayout.Label($"Damage {FormatNumber(player.damageDealt)}  |  Taken {FormatNumber(player.damageTaken)}  |  Healing {FormatNumber(player.healingReceived)}  |  Kills {FormatNumber(player.kills)}");
            GUILayout.Label($"Highest hit {FormatNumber(player.highestDamage)}  |  Gold {FormatNumber(player.goldCollected)}  |  Deaths {FormatNumber(player.deaths)}");
            GUILayout.Label($"Final build: {FormatInventory(player.finalInventory)}");
            if (player.equipment.Count > 0)
                GUILayout.Label($"Equipment: {string.Join(", ", player.equipment)}");
        }

        GUILayout.Space(8f);
        GUILayout.Label("Stage timeline");
        foreach (JournalStage stage in selected.stages)
        {
            string range = $"{FormatDuration(stage.enteredAtSeconds)}–{FormatDuration(stage.exitedAtSeconds)}";
            GUILayout.Label($"{stage.index}. {stage.displayName}  ({range}) — {stage.inventory.Sum(item => item.count)} items");
            if (stage.localPlayer != null && !string.IsNullOrWhiteSpace(stage.localPlayer.survivorDisplay))
                GUILayout.Label($"    Lv {stage.localPlayer.level:0}  HP {stage.localPlayer.maxHealth:0}  DMG {stage.localPlayer.damage:0.0}  APS {stage.localPlayer.attackSpeed:0.00}  Armor {stage.localPlayer.armor:0}");
            GUILayout.Label($"    Build: {FormatInventory(stage.inventory)}");
        }

        if (selected.deaths.Count > 0)
        {
            GUILayout.Space(8f);
            GUILayout.Label("Deaths");
            foreach (JournalDeathEvent death in selected.deaths)
                GUILayout.Label($"{FormatDuration(death.timeSeconds)} — {death.attacker}, final hit {death.finalDamage:0} ({death.damageType})");
        }

        if (selected.itemEvents.Count > 0)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Item timeline ({selected.itemEvents.Count} changes)");
            foreach (JournalItemEvent item in selected.itemEvents.TakeLast(40))
            {
                string sign = item.delta > 0 ? "+" : "";
                GUILayout.Label($"{FormatDuration(item.timeSeconds)}  S{item.stageIndex}  {sign}{item.delta} {item.displayName} → {item.newCount}");
            }
            if (selected.itemEvents.Count > 40)
                GUILayout.Label("Showing the latest 40 changes. The JSON archive contains the full timeline.");
        }

        if (selected.mods.Count > 0)
        {
            GUILayout.Space(8f);
            GUILayout.Label($"Loaded plugins ({selected.mods.Count})");
            foreach (JournalMod mod in selected.mods)
                GUILayout.Label($"{mod.name} {mod.version}  [{mod.guid}]");
        }

        GUILayout.Space(8f);
        GUILayout.Label("Notes");
        noteBuffer = GUILayout.TextArea(noteBuffer, GUILayout.MinHeight(65f));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Save note", GUILayout.Width(110f)))
        {
            selected.notes = noteBuffer;
            store.Save(selected);
            Refresh();
        }
        if (GUILayout.Button("Copy summary", GUILayout.Width(120f)))
            GUIUtility.systemCopyBuffer = BuildSummary(selected);
        GUILayout.EndHorizontal();
        GUILayout.EndScrollView();
        GUILayout.EndVertical();
    }

    private bool MatchesFilter(JournalRun run)
    {
        if (outcomeFilter == 1 && run.outcome != "win")
            return false;
        if (outcomeFilter == 2 && run.outcome == "win")
            return false;
        if (string.IsNullOrWhiteSpace(search))
            return true;

        string needle = search.Trim();
        return Contains(run.difficultyDisplay, needle)
            || Contains(run.outcome, needle)
            || Contains(run.notes, needle)
            || Contains(run.tags, needle)
            || run.players.Any(player => Contains(player.survivorDisplay, needle) || Contains(player.playerName, needle))
            || run.players.Any(player => player.finalInventory.Any(item => Contains(item.displayName, needle) || Contains(item.internalName, needle)))
            || run.stages.Any(stage => Contains(stage.displayName, needle)
                || stage.inventory.Any(item => Contains(item.displayName, needle) || Contains(item.internalName, needle)))
            || run.mods.Any(mod => Contains(mod.name, needle) || Contains(mod.guid, needle) || Contains(mod.version, needle));
    }

    private string FilterLabel() => outcomeFilter switch
    {
        1 => "Wins only",
        2 => "Non-wins",
        _ => "All outcomes"
    };

    private static string RunTitle(JournalRun run)
    {
        return $"{OutcomeGlyph(run)} {PrimarySurvivor(run)} — {run.difficultyDisplay}\n{FormatUtc(run.startedAtUtc)}  •  {FormatDuration(run.elapsedSeconds)}";
    }

    private static string PrimarySurvivor(JournalRun run)
    {
        return run.players.FirstOrDefault()?.survivorDisplay
            ?? run.stages.FirstOrDefault()?.localPlayer?.survivorDisplay
            ?? "Unknown survivor";
    }

    private static string OutcomeGlyph(JournalRun run) => run.outcome switch
    {
        "win" => "✓",
        "loss" => "✕",
        "interrupted" => "⚠",
        "abandoned" => "↩",
        _ => "•"
    };

    private static string FormatUtc(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "Unknown date";
    }

    private static string FormatDuration(float seconds)
    {
        if (seconds <= 0f)
            return "00:00";
        TimeSpan span = TimeSpan.FromSeconds(seconds);
        return span.TotalHours >= 1d ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
    }

    private static string FormatNumber(double value) => value.ToString("N0", CultureInfo.CurrentCulture);
    private static string FormatInventory(IEnumerable<JournalItemStack> items)
    {
        string text = string.Join(", ", items.Select(item => item.count > 1 ? $"{item.displayName} ×{item.count}" : item.displayName));
        return string.IsNullOrWhiteSpace(text) ? "—" : text;
    }
    private static string Fallback(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!;
    private static bool Contains(string? haystack, string needle) => haystack?.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string BuildSummary(JournalRun run)
    {
        var result = new StringBuilder();
        result.AppendLine($"Petrichor Journal — {PrimarySurvivor(run)} — {run.difficultyDisplay}");
        result.AppendLine($"{run.outcome.ToUpperInvariant()} | {FormatDuration(run.elapsedSeconds)} | {run.stages.Count} stages | {FormatUtc(run.startedAtUtc)}");
        foreach (JournalPlayerSummary player in run.players)
            result.AppendLine($"{player.playerName}: {FormatNumber(player.damageDealt)} damage, {FormatNumber(player.kills)} kills, {FormatNumber(player.deaths)} deaths");
        if (!string.IsNullOrWhiteSpace(run.notes))
            result.AppendLine("Notes: " + run.notes.Trim());
        return result.ToString().TrimEnd();
    }
}
