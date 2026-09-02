using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class L1jShopCatalog
{
	private sealed record Loaded(IReadOnlyDictionary<int, L1jShopDefinition> Shops, IReadOnlyList<L1jNpcSpawn> Spawns, IReadOnlyDictionary<string, int> CheapestSellUnitPrice);

	public const string TableName = "L1J_NPC_SHOPS";

	private static readonly ConditionalWeakTable<IGameData, Loaded> Cache = new ConditionalWeakTable<IGameData, Loaded>();

	public const int UnpricedBuybackUnitPrice = 1;

	public static IReadOnlyDictionary<int, L1jShopDefinition> Shops(IGameData data)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		return Cache.GetValue(data, Build).Shops;
	}

	public static IReadOnlyList<L1jNpcSpawn> Spawns(IGameData data)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		return Cache.GetValue(data, Build).Spawns;
	}

	public static bool TryResolveShopNpcId(IGameData data, string displayName, out int npcId)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		npcId = 0;
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return false;
		}
		if (!(data.Table("L1J_NPC_SHOPS") is JsonObject jsonObject) || !(jsonObject["shopByName"] is JsonObject jsonObject2) || !(jsonObject2[displayName] is JsonValue jsonValue) || !jsonValue.TryGetValue<int>(out var value))
		{
			return false;
		}
		npcId = value;
		return true;
	}

	public static IReadOnlyList<L1jShopItem> SellList(IGameData data, int npcId)
	{
		if (!Shops(data).TryGetValue(npcId, out L1jShopDefinition value))
		{
			return Array.Empty<L1jShopItem>();
		}
		return value.Items.Where((L1jShopItem item) => item.SellPrice >= 0 && item.ItemKey != null).ToArray();
	}

	public static bool IsBuybackShop(IGameData data, int npcId)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		if (!Shops(data).ContainsKey(npcId))
		{
			return L1jNpcSkillLearningRules.IsMainMagicInstructor(npcId);
		}
		return true;
	}

	public static int BuyPriceOf(IGameData data, int npcId, string itemKey, ItemBlessing blessing = ItemBlessing.Normal)
	{
		ArgumentNullException.ThrowIfNull(itemKey, "itemKey");
		Shops(data).TryGetValue(npcId, out L1jShopDefinition value);
		if ((object)value == null && !IsBuybackShop(data, npcId))
		{
			return -1;
		}
		L1jShopItem l1jShopItem = value?.Items.FirstOrDefault((L1jShopItem item) => string.Equals(item.ItemKey, itemKey, StringComparison.Ordinal) && item.Blessing == blessing && item.BuyPrice >= 0);
		if ((object)l1jShopItem != null)
		{
			return l1jShopItem.BuyPrice / Math.Max(1, l1jShopItem.PackCount);
		}
		return UniversalBuybackPrice(data, itemKey);
	}

	public static int UniversalBuybackPrice(IGameData data, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentNullException.ThrowIfNull(itemKey, "itemKey");
		if (Cache.GetValue(data, Build).CheapestSellUnitPrice.TryGetValue(itemKey, out var value))
		{
			return value / 2;
		}
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject == null)
		{
			return -1;
		}
		double num = CombatSkill.ReadDouble(jsonObject, "p");
		if (!(num >= 2.0))
		{
			return 1;
		}
		return (int)Math.Min(2147483647.0, Math.Floor(num) / 2.0);
	}

	private static Loaded Build(IGameData data)
	{
		if (!(data.Table("L1J_NPC_SHOPS") is JsonObject jsonObject))
		{
			throw new InvalidDataException("L1J_NPC_SHOPS table failed to load.");
		}
		Dictionary<int, L1jShopDefinition> dictionary = new Dictionary<int, L1jShopDefinition>();
		foreach (KeyValuePair<string, JsonNode> item in jsonObject["shops"].AsObject())
		{
			item.Deconstruct(out var _, out var value);
			JsonObject jsonObject2 = value.AsObject();
			L1jShopItem[] items = (from entry in jsonObject2["items"].AsArray()
				select new L1jShopItem(entry["id"].GetValue<int>(), (entry["key"] is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value4)) ? value4 : null, ReadBlessing(entry["blessing"]?.GetValue<string>()), entry["order"].GetValue<int>(), entry["sell"].GetValue<int>(), Math.Max(1, entry["pack"].GetValue<int>()), entry["buy"].GetValue<int>())).ToArray();
			int value2 = jsonObject2["npcId"].GetValue<int>();
			dictionary[value2] = new L1jShopDefinition(value2, jsonObject2["name"].GetValue<string>(), jsonObject2["impl"].GetValue<string>(), items);
		}
		L1jNpcSpawn[] array = (from node in jsonObject["spawns"].AsArray()
			select node.AsObject() into row
			select new L1jNpcSpawn(row["npcId"].GetValue<int>(), row["name"].GetValue<string>(), row["impl"].GetValue<string>(), row["gfx"].GetValue<int>(), row["mapKey"].GetValue<string>(), row["cellX"].GetValue<int>(), row["cellY"].GetValue<int>(), row["heading"].GetValue<int>(), row["hasShop"].GetValue<bool>(), row["level"].GetValue<int>(), row["hp"].GetValue<int>(), row["mp"].GetValue<int>(), row["ac"].GetValue<int>(), row["str"].GetValue<int>(), row["con"].GetValue<int>(), row["dex"].GetValue<int>(), row["wis"].GetValue<int>(), row["int"].GetValue<int>(), row["mr"].GetValue<int>(), row["exp"].GetValue<int>(), row["lawful"].GetValue<int>(), row["size"].GetValue<string>(), row["ranged"].GetValue<int>(), row["moveIntervalMs"].GetValue<int>(), row["attackIntervalMs"].GetValue<int>(), row["aggressive"].GetValue<bool>(), row["detectInvisible"].GetValue<bool>(), row["family"].GetValue<string>(), row["damageReduction"].GetValue<int>())).ToArray();
		if (dictionary.Count == 0 || array.Length == 0)
		{
			throw new InvalidDataException("L1J_NPC_SHOPS is empty.");
		}
		Dictionary<string, int> dictionary2 = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (L1jShopItem item2 in dictionary.Values.SelectMany((L1jShopDefinition shop) => shop.Items))
		{
			if (item2.SellPrice >= 0 && item2.ItemKey != null)
			{
				int num = item2.SellPrice / Math.Max(1, item2.PackCount);
				if (!dictionary2.TryGetValue(item2.ItemKey, out var value3) || num < value3)
				{
					dictionary2[item2.ItemKey] = num;
				}
			}
		}
		return new Loaded(dictionary, array, dictionary2);
	}

	private static ItemBlessing ReadBlessing(string? value)
	{
		if (!(value == "blessed"))
		{
			if (value == "cursed")
			{
				return ItemBlessing.Cursed;
			}
			return ItemBlessing.Normal;
		}
		return ItemBlessing.Blessed;
	}
}
