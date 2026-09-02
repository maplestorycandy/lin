using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;

namespace IdleLineage.Data;

public sealed class L1jMapRuleCatalog
{
	public const string TableName = "L1J_MAP_RULES";

	public const int ExpectedRuleCount = 555;

	private static readonly ConditionalWeakTable<IGameData, L1jMapRuleCatalog> Cache = new ConditionalWeakTable<IGameData, L1jMapRuleCatalog>();

	public IReadOnlyDictionary<int, L1jMapRule> ById { get; }

	public IReadOnlyDictionary<string, L1jMapRule> ByKey { get; }

	public IReadOnlyDictionary<int, string> TargetAliases { get; }

	private L1jMapRuleCatalog(IReadOnlyDictionary<int, L1jMapRule> byId, IReadOnlyDictionary<string, L1jMapRule> byKey, IReadOnlyDictionary<int, string> targetAliases)
	{
		ById = byId;
		ByKey = byKey;
		TargetAliases = targetAliases;
	}

	public static L1jMapRuleCatalog Load(IGameData data)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		return Cache.GetValue(data, Build);
	}

	private static L1jMapRuleCatalog Build(IGameData data)
	{
		JsonObject obj = (data.Table("L1J_MAP_RULES") as JsonObject) ?? throw Invalid("table must be a JSON object");
		if (!(obj["maps"] is JsonArray jsonArray))
		{
			throw Invalid("maps must be an array");
		}
		if (!(obj["mapIdByKey"] is JsonObject jsonObject))
		{
			throw Invalid("mapIdByKey must be an object");
		}
		if (!(obj["targetAliases"] is JsonObject jsonObject2))
		{
			throw Invalid("targetAliases must be an object");
		}
		Dictionary<int, L1jMapRule> dictionary = new Dictionary<int, L1jMapRule>();
		foreach (JsonNode item in jsonArray)
		{
			JsonObject owner = (item as JsonObject) ?? throw Invalid("map row must be an object");
			JsonObject owner2 = RequiredObject(owner, "bounds");
			int num = RequiredInt(owner, "mapId");
			L1jMapRule value = new L1jMapRule(num, RequiredString(owner, "locationName"), new L1jMapBounds(RequiredInt(owner2, "startX"), RequiredInt(owner2, "endX"), RequiredInt(owner2, "startY"), RequiredInt(owner2, "endY")), RequiredDouble(owner, "monsterAmount"), RequiredDouble(owner, "dropRate"), RequiredBool(owner, "underwater"), RequiredBool(owner, "markable"), RequiredBool(owner, "teleportable"), RequiredBool(owner, "escapable"), RequiredBool(owner, "resurrection"), RequiredBool(owner, "painwand"), RequiredBool(owner, "penalty"), RequiredBool(owner, "takePets"), RequiredBool(owner, "recallPets"), RequiredBool(owner, "usableItem"), RequiredBool(owner, "usableSkill"), ReadStrings(owner, "mapKeys"));
			if (!dictionary.TryAdd(num, value))
			{
				throw Invalid($"duplicate map id {num}");
			}
		}
		if (dictionary.Count != 555)
		{
			throw Invalid($"rule count must be {555}, got {dictionary.Count}");
		}
		Dictionary<string, L1jMapRule> dictionary2 = new Dictionary<string, L1jMapRule>(StringComparer.Ordinal);
		string key;
		JsonNode value2;
		foreach (KeyValuePair<string, JsonNode> item2 in jsonObject)
		{
			item2.Deconstruct(out key, out value2);
			string text = key;
			int value3 = (value2 ?? throw Invalid("mapIdByKey." + text + " must be an integer")).GetValue<int>();
			if (!dictionary.TryGetValue(value3, out var value4))
			{
				throw Invalid($"mapIdByKey.{text} references unknown map id {value3}");
			}
			if (!value4.MapKeys.Contains<string>(text, StringComparer.Ordinal) || !dictionary2.TryAdd(text, value4))
			{
				throw Invalid("map key binding is inconsistent or duplicate: " + text);
			}
		}
		foreach (L1jMapRule value7 in dictionary.Values)
		{
			foreach (string mapKey in value7.MapKeys)
			{
				if (!dictionary2.TryGetValue(mapKey, out var value5) || value5.MapId != value7.MapId)
				{
					throw Invalid($"map row {value7.MapId} has an unbound key {mapKey}");
				}
			}
		}
		Dictionary<int, string> dictionary3 = new Dictionary<int, string>();
		foreach (KeyValuePair<string, JsonNode> item3 in jsonObject2)
		{
			item3.Deconstruct(out key, out value2);
			string text2 = key;
			JsonNode jsonNode = value2;
			if (!int.TryParse(text2, out var result) || !dictionary.TryGetValue(result, out var value6) || value6.MapKeys.Count != 0)
			{
				throw Invalid("target alias source is invalid or already physical: " + text2);
			}
			string text3 = jsonNode?.GetValue<string>() ?? throw Invalid("targetAliases." + text2 + " must be a string");
			if (!dictionary2.ContainsKey(text3) || !dictionary3.TryAdd(result, text3))
			{
				throw Invalid("target alias is duplicate or references unknown key: " + text2);
			}
		}
		return new L1jMapRuleCatalog(new ReadOnlyDictionary<int, L1jMapRule>(dictionary), new ReadOnlyDictionary<string, L1jMapRule>(dictionary2), new ReadOnlyDictionary<int, string>(dictionary3));
	}

	public bool TryForMapId(int mapId, out L1jMapRule? rule)
	{
		return ById.TryGetValue(mapId, out rule);
	}

	public IReadOnlyList<string> RuntimeTargetKeys(int mapId)
	{
		if (!ById.TryGetValue(mapId, out L1jMapRule value))
		{
			return Array.Empty<string>();
		}
		if (value.MapKeys.Count > 0)
		{
			return value.MapKeys;
		}
		if (!TargetAliases.TryGetValue(mapId, out string value2))
		{
			return Array.Empty<string>();
		}
		return new string[1] { value2 };
	}

	public bool TryForMapKey(string mapKey, out L1jMapRule? rule)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(mapKey, "mapKey");
		return ByKey.TryGetValue(mapKey, out rule);
	}

	public L1jMapRule RequireForMapKey(string mapKey)
	{
		if (TryForMapKey(mapKey, out L1jMapRule rule) && (object)rule != null)
		{
			return rule;
		}
		throw Invalid("runtime map '" + mapKey + "' has no L1j-TW-main mapids.sql rule");
	}

	private static IReadOnlyList<string> ReadStrings(JsonObject owner, string name)
	{
		return new ReadOnlyCollection<string>(((owner[name] as JsonArray) ?? throw Invalid(name + " must be an array")).Select((JsonNode node) => node?.GetValue<string>() ?? throw Invalid(name + " contains null")).ToList());
	}

	private static JsonObject RequiredObject(JsonObject owner, string name)
	{
		return (owner[name] as JsonObject) ?? throw Invalid(name + " must be an object");
	}

	private static string RequiredString(JsonObject owner, string name)
	{
		return owner[name]?.GetValue<string>() ?? throw Invalid(name + " must be a string");
	}

	private static int RequiredInt(JsonObject owner, string name)
	{
		return (owner[name] ?? throw Invalid(name + " must be an integer")).GetValue<int>();
	}

	private static double RequiredDouble(JsonObject owner, string name)
	{
		return (owner[name] ?? throw Invalid(name + " must be numeric")).GetValue<double>();
	}

	private static bool RequiredBool(JsonObject owner, string name)
	{
		return (owner[name] ?? throw Invalid(name + " must be a boolean")).GetValue<bool>();
	}

	private static InvalidDataException Invalid(string message)
	{
		return new InvalidDataException("L1J_MAP_RULES: " + message);
	}
}
