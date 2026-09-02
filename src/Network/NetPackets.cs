using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IdleLineage.Network;

public enum NetPacketType
{
    Handshake = 1,
    Move = 2,
    Action = 3,
    Chat = 4,
    Leave = 5,
    SyncRequest = 6,
    Ping = 7,
    Equip = 8,
    MobSpawn = 9,
    MobBatchMove = 10,
    MobHit = 11,
    MobHpSync = 12,
    MobDeath = 13,
    PlayerHpSync = 14
}

public class NetEnvelope
{
    [JsonPropertyName("type")]
    public NetPacketType Type { get; set; }

    [JsonPropertyName("payload")]
    public string Payload { get; set; } = "";

    public static NetEnvelope Create<T>(NetPacketType type, T payload)
    {
        return new NetEnvelope
        {
            Type = type,
            Payload = JsonSerializer.Serialize(payload)
        };
    }

    public T? Deserialize<T>()
    {
        if (string.IsNullOrEmpty(Payload)) return default;
        return JsonSerializer.Deserialize<T>(Payload);
    }
}

public class HandshakePacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("class")]
    public string ClassId { get; set; } = "knight";

    [JsonPropertyName("avatar")]
    public string Avatar { get; set; } = "";

    [JsonPropertyName("weapon")]
    public string WeaponPrefix { get; set; } = "";

    [JsonPropertyName("mainWeapon")]
    public string MainWeaponId { get; set; } = "";

    [JsonPropertyName("lvl")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("hp")]
    public double Hp { get; set; } = 100;

    [JsonPropertyName("maxHp")]
    public double MaxHp { get; set; } = 100;

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("facing")]
    public int Facing8 { get; set; } = 4;

    [JsonPropertyName("map")]
    public string MapKey { get; set; } = "";
}

public class MovePacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("facing")]
    public int Facing8 { get; set; }

    [JsonPropertyName("stepping")]
    public bool Stepping { get; set; }

    [JsonPropertyName("hp")]
    public double Hp { get; set; } = 100;

    [JsonPropertyName("maxHp")]
    public double MaxHp { get; set; } = 100;

    [JsonPropertyName("map")]
    public string MapKey { get; set; } = "";
}

public class EquipPacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("mainWeapon")]
    public string MainWeaponId { get; set; } = "";

    [JsonPropertyName("weaponPrefix")]
    public string WeaponPrefix { get; set; } = "";
}

public class PlayerHpSyncPacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("hp")]
    public double Hp { get; set; }

    [JsonPropertyName("maxHp")]
    public double MaxHp { get; set; }

    [JsonPropertyName("dmg")]
    public double DamageTaken { get; set; }
}

public class ActionPacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";

    [JsonPropertyName("action")]
    public string ActionType { get; set; } = "attack";

    [JsonPropertyName("skill")]
    public string SkillId { get; set; } = "";

    [JsonPropertyName("morph")]
    public string MorphName { get; set; } = "";

    [JsonPropertyName("tx")]
    public double TargetX { get; set; }

    [JsonPropertyName("ty")]
    public double TargetY { get; set; }
}

public class ChatPacket
{
    [JsonPropertyName("id")]
    public string SenderId { get; set; } = "";

    [JsonPropertyName("sender")]
    public string SenderName { get; set; } = "";

    [JsonPropertyName("msg")]
    public string Message { get; set; } = "";

    [JsonPropertyName("color")]
    public string ColorHex { get; set; } = "#66d9ef";
}

public class LeavePacket
{
    [JsonPropertyName("id")]
    public string PlayerId { get; set; } = "";
}

public class MobSpawnPacket
{
    [JsonPropertyName("mobId")]
    public string MobId { get; set; } = "";

    [JsonPropertyName("mobKey")]
    public string MobKey { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("hp")]
    public double Hp { get; set; }

    [JsonPropertyName("maxHp")]
    public double MaxHp { get; set; }

    [JsonPropertyName("facing")]
    public int Facing8 { get; set; }

    [JsonPropertyName("map")]
    public string MapKey { get; set; } = "";
}

public class MobMoveEntry
{
    [JsonPropertyName("mobId")]
    public string MobId { get; set; } = "";

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("facing")]
    public int Facing8 { get; set; }

    [JsonPropertyName("stepping")]
    public bool Stepping { get; set; }
}

public class MobBatchMovePacket
{
    [JsonPropertyName("moves")]
    public List<MobMoveEntry> Moves { get; set; } = new();
}

public class MobHitPacket
{
    [JsonPropertyName("mobId")]
    public string MobId { get; set; } = "";

    [JsonPropertyName("attackerId")]
    public string AttackerId { get; set; } = "";

    [JsonPropertyName("damage")]
    public double Damage { get; set; }
}

public class MobHpSyncPacket
{
    [JsonPropertyName("mobId")]
    public string MobId { get; set; } = "";

    [JsonPropertyName("hp")]
    public double CurrentHp { get; set; }

    [JsonPropertyName("damage")]
    public double DamageTaken { get; set; }

    [JsonPropertyName("attackerId")]
    public string AttackerId { get; set; } = "";
}

public class MobDeathPacket
{
    [JsonPropertyName("mobId")]
    public string MobId { get; set; } = "";

    [JsonPropertyName("killerId")]
    public string KillerId { get; set; } = "";
}
