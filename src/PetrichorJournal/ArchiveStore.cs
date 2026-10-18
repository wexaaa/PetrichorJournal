using BepInEx;
using BepInEx.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PetrichorJournal;

public sealed class ArchiveStore
{
    private readonly ManualLogSource log;

    public string RootDirectory { get; }
    public string ArchiveDirectory { get; }

    public ArchiveStore(ManualLogSource log)
    {
        this.log = log;
        RootDirectory = Path.Combine(Paths.ConfigPath, "PetrichorJournal");
        ArchiveDirectory = Path.Combine(RootDirectory, "archive");
        Directory.CreateDirectory(ArchiveDirectory);
    }

    public string GetPath(string id) => Path.Combine(ArchiveDirectory, id + ".json");

    public void Save(JournalRun run)
    {
        if (string.IsNullOrWhiteSpace(run.id))
            throw new InvalidOperationException("A journal entry must have an id before it is saved.");

        Normalize(run);
        Directory.CreateDirectory(ArchiveDirectory);
        string target = GetPath(run.id);
        string temporary = target + ".tmp";
        string json = JournalJson.Serialize(run);

        File.WriteAllText(temporary, json, new UTF8Encoding(false));
        try
        {
            if (File.Exists(target))
                File.Replace(temporary, target, null);
            else
                File.Move(temporary, target);
        }
        catch (PlatformNotSupportedException)
        {
            File.Copy(temporary, target, true);
            File.Delete(temporary);
        }
        catch (IOException)
        {
            File.Copy(temporary, target, true);
            File.Delete(temporary);
        }
    }

    public JournalRun? Load(string id)
    {
        string path = GetPath(id);
        if (!File.Exists(path))
            return null;

        try
        {
            JournalRun run = JournalJson.Deserialize(File.ReadAllText(path, Encoding.UTF8));
            return Normalize(run);
        }
        catch (Exception error)
        {
            log.LogWarning($"Could not read journal entry '{path}': {error.Message}");
            return null;
        }
    }

    public List<JournalRun> LoadAll()
    {
        var result = new List<JournalRun>();
        if (!Directory.Exists(ArchiveDirectory))
            return result;

        foreach (string path in Directory.EnumerateFiles(ArchiveDirectory, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                JournalRun run = JournalJson.Deserialize(File.ReadAllText(path, Encoding.UTF8));
                if (run != null && !string.IsNullOrWhiteSpace(run.id))
                    result.Add(Normalize(run));
            }
            catch (Exception error)
            {
                log.LogWarning($"Skipping unreadable journal entry '{path}': {error.Message}");
            }
        }

        return result
            .OrderByDescending(run => ParseUtc(run.startedAtUtc))
            .ToList();
    }

    public void MarkInterruptedRuns()
    {
        foreach (JournalRun run in LoadAll().Where(run => run.status == "active"))
        {
            run.status = "interrupted";
            run.outcome = "interrupted";
            run.endedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
            Save(run);
        }
    }

    private static DateTime ParseUtc(string value)
    {
        return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime parsed)
            ? parsed
            : DateTime.MinValue;
    }

    private static JournalRun Normalize(JournalRun run)
    {
        run.stages ??= new List<JournalStage>();
        run.itemEvents ??= new List<JournalItemEvent>();
        run.deaths ??= new List<JournalDeathEvent>();
        run.players ??= new List<JournalPlayerSummary>();
        run.mods ??= new List<JournalMod>();

        foreach (JournalStage stage in run.stages)
        {
            stage.localPlayer ??= new JournalBodySnapshot();
            stage.inventory ??= new List<JournalItemStack>();
        }

        foreach (JournalPlayerSummary player in run.players)
        {
            player.finalInventory ??= new List<JournalItemStack>();
            player.equipment ??= new List<string>();
        }

        return run;
    }
}
