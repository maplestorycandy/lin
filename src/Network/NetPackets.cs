using System;
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
    Ping = 7
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

    [JsonPropertyName("lvl")]
    public int Level { get; set; } = 1;

    [JsonPropertyName("hp")]
    public int Hp { get; set; } = 100;

    [JsonPropertyName("maxHp")]
    public int MaxHp { get; set; } = 100;

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

    [JsonPropertyName("map")]
    public string MapKey { get; set; } = "";
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
