using BepInEx.Bootstrap;
using BepInEx.Configuration;
using BepInEx.Logging;
using RoR2;
using RoR2.Stats;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PetrichorJournal;

public sealed class RunCapture
{
    private readonly ArchiveStore store;
    private readonly ManualLogSource log;
    private readonly ConfigEntry<float> autosaveSeconds;
    private readonly ConfigEntry<bool> recordModList;
    private readonly ConfigEntry<bool> recordItemTimeline;
    private readonly ConfigEntry<bool> redactPlayerNames;

    private JournalRun? current;
    private CharacterMaster? localMaster;
    private readonly Dictionary<string, int> previousItems = new(StringComparer.Ordinal);
    private readonly HashSet<string> disabledHandlers = new(StringComparer.Ordinal);
    private bool dirty;
    private float nextAutosaveAt;
    private float nextSceneProbeAt;
    private int lastSceneHandle = int.MinValue;

    public event Action? ArchiveChanged;
    public JournalRun? Current => current;

    public RunCapture(
        ArchiveStore store,
        ManualLogSource log,
        ConfigEntry<float> autosaveSeconds,
        ConfigEntry<bool> recordModList,
        ConfigEntry<bool> recordItemTimeline,
        ConfigEntry<bool> redactPlayerNames)
    {
        this.store = store;
        this.log = log;
        this.autosaveSeconds = autosaveSeconds;
        this.recordModList = recordModList;
        this.recordItemTimeline = recordItemTimeline;
        this.redactPlayerNames = redactPlayerNames;
    }

    public void InstallHooks()
    {
        Run.onRunStartGlobal += SafeOnRunStart;
        Run.onClientGameOverGlobal += SafeOnClientGameOver;
        Run.onRunDestroyGlobal += SafeOnRunDestroyed;
        SceneCatalog.onMostRecentSceneDefChanged += SafeOnSceneChanged;
        Inventory.onInventoryChangedGlobal += SafeOnInventoryChanged;
#pragma warning disable CS0618 // Client-global hook is intentional: a non-host client still needs its own death record.
        GlobalEventManager.onCharacterDeathGlobal += SafeOnCharacterDeath;
#pragma warning restore CS0618
    }

    public void RemoveHooks()
    {
        Run.onRunStartGlobal -= SafeOnRunStart;
        Run.onClientGameOverGlobal -= SafeOnClientGameOver;
        Run.onRunDestroyGlobal -= SafeOnRunDestroyed;
        SceneCatalog.onMostRecentSceneDefChanged -= SafeOnSceneChanged;
        Inventory.onInventoryChangedGlobal -= SafeOnInventoryChanged;
#pragma warning disable CS0618 // See InstallHooks: the server-only replacement would lose client death data.
        GlobalEventManager.onCharacterDeathGlobal -= SafeOnCharacterDeath;
#pragma warning restore CS0618
    }

    private void SafeOnRunStart(Run run)
    {
        const string handler = nameof(OnRunStart);
        if (disabledHandlers.Contains(handler)) return;
        try { OnRunStart(run); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeOnClientGameOver(Run run, RunReport report)
    {
        const string handler = nameof(OnClientGameOver);
        if (disabledHandlers.Contains(handler)) return;
        try { OnClientGameOver(run, report); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeOnRunDestroyed(Run run)
    {
        const string handler = nameof(OnRunDestroyed);
        if (disabledHandlers.Contains(handler)) return;
        try { OnRunDestroyed(run); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeOnSceneChanged(SceneDef scene)
    {
        const string handler = nameof(CaptureStage);
        if (disabledHandlers.Contains(handler)) return;
        try { CaptureStage(scene); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeProbeCurrentScene()
    {
        const string handler = nameof(CaptureStage);
        if (disabledHandlers.Contains(handler)) return;
        try
        {
            SceneDef? scene = SceneCatalog.GetSceneDefForCurrentScene();
            if (scene != null)
                CaptureStage(scene);
        }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeOnInventoryChanged(Inventory inventory)
    {
        const string handler = nameof(OnInventoryChanged);
        if (disabledHandlers.Contains(handler)) return;
        try { OnInventoryChanged(inventory); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void SafeOnCharacterDeath(DamageReport report)
    {
        const string handler = nameof(OnCharacterDeath);
        if (disabledHandlers.Contains(handler)) return;
        try { OnCharacterDeath(report); }
        catch (Exception error) { DisableHandler(handler, error); }
    }

    private void DisableHandler(string handler, Exception error)
    {
        disabledHandlers.Add(handler);
        log.LogError($"Petrichor Journal disabled {handler} for this session so recording cannot interrupt gameplay: {error}");
    }

    public void Tick()
    {
        if (current == null)
            return;

        if (Time.unscaledTime >= nextSceneProbeAt)
        {
            nextSceneProbeAt = Time.unscaledTime + 0.5f;
            SafeProbeCurrentScene();
        }

        if (!dirty || Time.unscaledTime < nextAutosaveAt)
            return;

        SaveCurrent();
        nextAutosaveAt = Time.unscaledTime + Mathf.Max(2f, autosaveSeconds.Value);
    }

    private void OnRunStart(Run run)
    {
        if (current != null && current.status == "active")
            FinalizeWithoutReport("abandoned");

        DifficultyDef? difficulty = DifficultyCatalog.GetDifficultyDef(run.selectedDifficulty);
        current = new JournalRun
        {
            id = Guid.NewGuid().ToString("N"),
            status = "active",
            outcome = "in_progress",
            startedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            gameVersion = Application.version,
            journalVersion = PetrichorJournalPlugin.PluginVersion,
            difficultyInternal = run.selectedDifficulty.ToString(),
            difficultyDisplay = Localize(difficulty?.nameToken, run.selectedDifficulty.ToString()),
            isModded = Chainloader.PluginInfos.Count > 1
        };

        if (recordModList.Value)
        {
            foreach (var info in Chainloader.PluginInfos.Values)
            {
                current.mods.Add(new JournalMod
                {
                    guid = info.Metadata.GUID,
                    name = info.Metadata.Name,
                    version = info.Metadata.Version.ToString()
                });
            }
            current.mods.Sort((left, right) => string.Compare(left.guid, right.guid, StringComparison.OrdinalIgnoreCase));
        }

        localMaster = FindLocalMaster();
        lastSceneHandle = int.MinValue;
        nextSceneProbeAt = 0f;
        previousItems.Clear();
        dirty = true;
        SaveCurrent();
        log.LogInfo($"Started journal entry {current.id} with {current.mods.Count} loaded plugins.");
    }

    private void CaptureStage(SceneDef scene)
    {
        if (current == null || Run.instance == null)
            return;

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.handle == lastSceneHandle && current.stages.Count > 0)
            return;

        lastSceneHandle = activeScene.handle;
        CloseCurrentStage();
        localMaster ??= FindLocalMaster();

        var entry = new JournalStage
        {
            index = current.stages.Count + 1,
            internalName = scene.cachedName ?? activeScene.name,
            displayName = Localize(scene.nameToken, scene.cachedName ?? activeScene.name),
            enteredAtSeconds = Stopwatch(),
            localPlayer = CaptureBody(localMaster?.GetBody()),
            inventory = CaptureInventory(localMaster?.inventory)
        };

        current.stages.Add(entry);
        SeedPreviousItems(entry.inventory);
        dirty = true;
        SaveCurrent();
        log.LogInfo($"Recorded stage {entry.index}: {entry.internalName}.");
    }

    private void OnInventoryChanged(Inventory inventory)
    {
        if (current == null || !recordItemTimeline.Value)
            return;

        CharacterMaster? owner = inventory.GetComponent<CharacterMaster>();
        if (owner == null)
            return;

        localMaster ??= FindLocalMaster();
        if (owner != localMaster)
            return;

        var newItems = CaptureInventory(inventory).ToDictionary(item => item.internalName, item => item.count, StringComparer.Ordinal);
        foreach (string key in previousItems.Keys.Union(newItems.Keys).Distinct(StringComparer.Ordinal))
        {
            previousItems.TryGetValue(key, out int oldCount);
            newItems.TryGetValue(key, out int newCount);
            int delta = newCount - oldCount;
            if (delta == 0)
                continue;

            ItemDef? definition = FindItemDefinition(key);
            current.itemEvents.Add(new JournalItemEvent
            {
                timeSeconds = Stopwatch(),
                stageIndex = current.stages.Count,
                internalName = key,
                displayName = Localize(definition?.nameToken, key),
                delta = delta,
                newCount = newCount
            });
        }

        previousItems.Clear();
        foreach (KeyValuePair<string, int> pair in newItems)
            previousItems[pair.Key] = pair.Value;
        dirty = true;
    }

    private void OnCharacterDeath(DamageReport report)
    {
        if (current == null || report.victimMaster == null)
            return;

        localMaster ??= FindLocalMaster();
        if (report.victimMaster != localMaster)
            return;

        current.deaths.Add(new JournalDeathEvent
        {
            timeSeconds = Stopwatch(),
            stageIndex = current.stages.Count,
            victim = DisplayBodyName(report.victimBody),
            attacker = DisplayBodyName(report.attackerBody, "The Planet"),
            finalDamage = report.damageInfo.damage,
            damageType = report.damageInfo.damageType.ToString()
        });
        dirty = true;
        SaveCurrent();
    }

    private void OnClientGameOver(Run run, RunReport report)
    {
        if (current == null)
            return;

        CloseCurrentStage();
        current.status = "complete";
        current.endedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        current.elapsedSeconds = Stopwatch();
        current.endingInternal = report.gameEnding?.cachedName ?? "Unknown";
        current.endingDisplay = Localize(report.gameEnding?.endingTextToken, current.endingInternal);
        current.outcome = report.gameEnding != null && report.gameEnding.isWin ? "win" : "loss";
        current.players = CapturePlayerSummaries(report);

        RunReport.PlayerInfo? local = FindLocalPlayer(report);
        if (local != null)
            current.stagesCompleted = (int)local.statSheet.GetStatValueULong(StatDef.totalStagesCompleted);

        SaveCurrent();
        ArchiveChanged?.Invoke();
        log.LogInfo($"Completed journal entry {current.id} ({current.outcome}).");
        current = null;
        localMaster = null;
        lastSceneHandle = int.MinValue;
        previousItems.Clear();
    }

    private void OnRunDestroyed(Run run)
    {
        if (current != null && current.status == "active")
            FinalizeWithoutReport("abandoned");
    }

    private void FinalizeWithoutReport(string outcome)
    {
        if (current == null)
            return;

        CloseCurrentStage();
        current.status = outcome;
        current.outcome = outcome;
        current.endedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
        current.elapsedSeconds = Stopwatch();
        SaveCurrent();
        ArchiveChanged?.Invoke();
        current = null;
        localMaster = null;
        lastSceneHandle = int.MinValue;
        previousItems.Clear();
    }

    private void CloseCurrentStage()
    {
        if (current == null || current.stages.Count == 0)
            return;

        JournalStage stage = current.stages[current.stages.Count - 1];
        if (stage.exitedAtSeconds > 0f)
            return;

        localMaster ??= FindLocalMaster();
        stage.exitedAtSeconds = Stopwatch();
        stage.localPlayer = CaptureBody(localMaster?.GetBody());
        stage.inventory = CaptureInventory(localMaster?.inventory);
    }

    private List<JournalPlayerSummary> CapturePlayerSummaries(RunReport report)
    {
        var summaries = new List<JournalPlayerSummary>();
        for (int index = 0; index < report.playerInfoCount; index++)
        {
            RunReport.PlayerInfo? player = report.GetPlayerInfoSafe(index);
            if (player == null)
                continue;
            StatSheet stats = player.statSheet;
            var summary = new JournalPlayerSummary
            {
                playerName = redactPlayerNames.Value ? $"Player {index + 1}" : FindPlayerName(player, index),
                survivorInternal = player.bodyName ?? player.bodyIndex.ToString(),
                survivorDisplay = Localize(BodyCatalog.GetBodyPrefabBodyComponent(player.bodyIndex)?.baseNameToken, player.bodyName),
                timeAliveSeconds = stats.GetStatValueAsDouble(StatDef.totalTimeAlive),
                kills = stats.GetStatValueAsDouble(StatDef.totalKills),
                eliteKills = stats.GetStatValueAsDouble(StatDef.totalEliteKills),
                minionKills = stats.GetStatValueAsDouble(StatDef.totalMinionKills),
                deaths = stats.GetStatValueAsDouble(StatDef.totalDeaths),
                damageDealt = stats.GetStatValueAsDouble(StatDef.totalDamageDealt),
                minionDamageDealt = stats.GetStatValueAsDouble(StatDef.totalMinionDamageDealt),
                damageTaken = stats.GetStatValueAsDouble(StatDef.totalDamageTaken),
                healingReceived = stats.GetStatValueAsDouble(StatDef.totalHealthHealed),
                highestDamage = stats.GetStatValueAsDouble(StatDef.highestDamageDealt),
                goldCollected = stats.GetStatValueAsDouble(StatDef.goldCollected),
                distanceTravelled = stats.GetStatValueAsDouble(StatDef.totalDistanceTraveled),
                purchases = stats.GetStatValueAsDouble(StatDef.totalPurchases),
                dronesPurchased = stats.GetStatValueAsDouble(StatDef.totalDronesPurchased),
                turretsPurchased = stats.GetStatValueAsDouble(StatDef.totalTurretsPurchased),
                finalInventory = CaptureInventory(player.itemStacks)
            };

            foreach (EquipmentIndex equipmentIndex in player.equipment ?? Array.Empty<EquipmentIndex>())
            {
                EquipmentDef? equipment = EquipmentCatalog.GetEquipmentDef(equipmentIndex);
                if (equipment != null)
                    summary.equipment.Add(Localize(equipment.nameToken, equipment.name));
            }
            summaries.Add(summary);
        }
        return summaries;
    }

    private static string FindPlayerName(RunReport.PlayerInfo player, int index)
    {
        PlayerCharacterMasterController? controller = PlayerCharacterMasterController.instances
            .FirstOrDefault(candidate => candidate.master == player.master);
        string? name = controller?.networkUser?.userName;
        return string.IsNullOrWhiteSpace(name) ? $"Player {index + 1}" : name;
    }

    private RunReport.PlayerInfo? FindLocalPlayer(RunReport report)
    {
        LocalUser? localUser = LocalUserManager.GetFirstLocalUser();
        if (localUser != null)
        {
            RunReport.PlayerInfo? local = report.FindPlayerInfo(localUser);
            if (local != null)
                return local;
        }

        return report.playerInfoCount > 0 ? report.GetPlayerInfoSafe(0) : null;
    }

    private static CharacterMaster? FindLocalMaster()
    {
        return LocalUserManager.GetFirstLocalUser()?.cachedMaster;
    }

    private static JournalBodySnapshot CaptureBody(CharacterBody? body)
    {
        if (body == null)
            return new JournalBodySnapshot();

        return new JournalBodySnapshot
        {
            survivorInternal = body.name,
            survivorDisplay = Localize(body.baseNameToken, body.name),
            level = body.level,
            maxHealth = body.maxHealth,
            maxShield = body.maxShield,
            regeneration = body.regen,
            armor = body.armor,
            damage = body.damage,
            attackSpeed = body.attackSpeed,
            criticalChance = body.crit,
            moveSpeed = body.moveSpeed
        };
    }

    private static List<JournalItemStack> CaptureInventory(Inventory? inventory)
    {
        if (inventory == null)
            return new List<JournalItemStack>();

        var result = new List<JournalItemStack>();
        foreach (ItemDef item in ItemCatalog.allItemDefs)
        {
            int count = inventory.GetItemCountPermanent(item.itemIndex);
            if (count > 0)
                result.Add(CreateItemStack(item, count));
        }
        return result.OrderBy(stack => stack.tier).ThenBy(stack => stack.displayName).ToList();
    }

    private static List<JournalItemStack> CaptureInventory(int[]? itemStacks)
    {
        var result = new List<JournalItemStack>();
        if (itemStacks == null)
            return result;

        for (int index = 0; index < itemStacks.Length; index++)
        {
            if (itemStacks[index] <= 0)
                continue;
            ItemDef? item = ItemCatalog.GetItemDef((ItemIndex)index);
            if (item != null)
                result.Add(CreateItemStack(item, itemStacks[index]));
        }
        return result.OrderBy(stack => stack.tier).ThenBy(stack => stack.displayName).ToList();
    }

    private static JournalItemStack CreateItemStack(ItemDef item, int count)
    {
        return new JournalItemStack
        {
            internalName = item.name,
            displayName = Localize(item.nameToken, item.name),
            tier = item.tier.ToString(),
            count = count
        };
    }

    private static ItemDef? FindItemDefinition(string internalName)
    {
        return ItemCatalog.allItemDefs.FirstOrDefault(item => item.name == internalName);
    }

    private void SeedPreviousItems(IEnumerable<JournalItemStack> items)
    {
        previousItems.Clear();
        foreach (JournalItemStack item in items)
            previousItems[item.internalName] = item.count;
    }

    private void SaveCurrent()
    {
        if (current == null)
            return;
        try
        {
            current.elapsedSeconds = Stopwatch();
            store.Save(current);
            dirty = false;
            ArchiveChanged?.Invoke();
        }
        catch (Exception error)
        {
            log.LogError($"Could not save journal entry: {error}");
        }
    }

    private static float Stopwatch() => Run.instance != null ? Run.instance.GetRunStopwatch() : 0f;

    private static string DisplayBodyName(CharacterBody? body, string fallback = "Unknown")
    {
        return body == null ? fallback : Localize(body.baseNameToken, body.name);
    }

    private static string Localize(string? token, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(token))
        {
            string localized = Language.GetString(token);
            if (!string.IsNullOrWhiteSpace(localized) && localized != token)
                return localized;
        }
        return string.IsNullOrWhiteSpace(fallback) ? "Unknown" : fallback!;
    }
}
