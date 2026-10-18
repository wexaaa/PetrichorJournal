using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace PetrichorJournal;

[DataContract]
public sealed class JournalRun
{
    [DataMember]
    public int schemaVersion = 2;
    [DataMember]
    public string id = "";
    [DataMember]
    public string status = "active";
    [DataMember]
    public string outcome = "unknown";
    [DataMember]
    public string startedAtUtc = "";
    [DataMember]
    public string endedAtUtc = "";
    [DataMember]
    public string gameVersion = "";
    [DataMember]
    public string journalVersion = "";
    [DataMember]
    public string difficultyInternal = "";
    [DataMember]
    public string difficultyDisplay = "";
    [DataMember]
    public string endingInternal = "";
    [DataMember]
    public string endingDisplay = "";
    [DataMember]
    public float elapsedSeconds;
    [DataMember]
    public int stagesCompleted;
    [DataMember]
    public string notes = "";
    [DataMember]
    public string tags = "";
    [DataMember]
    public bool isModded;
    [DataMember]
    public List<JournalStage> stages = new();
    [DataMember]
    public List<JournalItemEvent> itemEvents = new();
    [DataMember]
    public List<JournalDeathEvent> deaths = new();
    [DataMember]
    public List<JournalPlayerSummary> players = new();
    [DataMember]
    public List<JournalMod> mods = new();
}

[DataContract]
public sealed class JournalStage
{
    [DataMember]
    public int index;
    [DataMember]
    public string internalName = "";
    [DataMember]
    public string displayName = "";
    [DataMember]
    public float enteredAtSeconds;
    [DataMember]
    public float exitedAtSeconds;
    [DataMember]
    public JournalBodySnapshot localPlayer = new();
    [DataMember]
    public List<JournalItemStack> inventory = new();
}

[DataContract]
public sealed class JournalBodySnapshot
{
    [DataMember]
    public string survivorInternal = "";
    [DataMember]
    public string survivorDisplay = "";
    [DataMember]
    public float level;
    [DataMember]
    public float maxHealth;
    [DataMember]
    public float maxShield;
    [DataMember]
    public float regeneration;
    [DataMember]
    public float armor;
    [DataMember]
    public float damage;
    [DataMember]
    public float attackSpeed;
    [DataMember]
    public float criticalChance;
    [DataMember]
    public float moveSpeed;
}

[DataContract]
public sealed class JournalItemStack
{
    [DataMember]
    public string internalName = "";
    [DataMember]
    public string displayName = "";
    [DataMember]
    public string tier = "";
    [DataMember]
    public int count;
}

[DataContract]
public sealed class JournalItemEvent
{
    [DataMember]
    public float timeSeconds;
    [DataMember]
    public int stageIndex;
    [DataMember]
    public string internalName = "";
    [DataMember]
    public string displayName = "";
    [DataMember]
    public int delta;
    [DataMember]
    public int newCount;
}

[DataContract]
public sealed class JournalDeathEvent
{
    [DataMember]
    public float timeSeconds;
    [DataMember]
    public int stageIndex;
    [DataMember]
    public string victim = "";
    [DataMember]
    public string attacker = "";
    [DataMember]
    public float finalDamage;
    [DataMember]
    public string damageType = "";
}

[DataContract]
public sealed class JournalPlayerSummary
{
    [DataMember]
    public string playerName = "";
    [DataMember]
    public string survivorInternal = "";
    [DataMember]
    public string survivorDisplay = "";
    [DataMember]
    public double timeAliveSeconds;
    [DataMember]
    public double kills;
    [DataMember]
    public double eliteKills;
    [DataMember]
    public double minionKills;
    [DataMember]
    public double deaths;
    [DataMember]
    public double damageDealt;
    [DataMember]
    public double minionDamageDealt;
    [DataMember]
    public double damageTaken;
    [DataMember]
    public double healingReceived;
    [DataMember]
    public double highestDamage;
    [DataMember]
    public double goldCollected;
    [DataMember]
    public double distanceTravelled;
    [DataMember]
    public double purchases;
    [DataMember]
    public double dronesPurchased;
    [DataMember]
    public double turretsPurchased;
    [DataMember]
    public List<JournalItemStack> finalInventory = new();
    [DataMember]
    public List<string> equipment = new();
}

[DataContract]
public sealed class JournalMod
{
    [DataMember]
    public string guid = "";
    [DataMember]
    public string name = "";
    [DataMember]
    public string version = "";
}
