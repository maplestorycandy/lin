using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class MagicDollRules
{
	public const string TableName = "MAGIC_DOLLS";

	public const string BagItemKey = "doll_bag";

	public const string DollTypeCounter = "doll:type";

	public const int CrystalCost = 50;

	public const int MaxDollCount = 1;

	public const double DurationSeconds = 1800.0;

	public const double RegenIntervalSeconds = 64.0;

	public const double HealthRegenAmount = 40.0;

	public const double ManaRegenAmount = 15.0;

	public const double AttackChancePercent = 3.0;

	public const double AttackBonusDamage = 15.0;

	public const double ShieldChancePercent = 4.0;

	public const double ShieldReduction = 15.0;

	public const double WeightReliefMultiplier = 1.2;

	public const double BowHitBonus = 1.0;

	public const double BowDamageBonus = 1.0;

	public const double ArmorImprovement = 1.0;

	private static readonly ConditionalWeakTable<IGameData, MagicDollCatalog> Cache = new ConditionalWeakTable<IGameData, MagicDollCatalog>();

	public static MagicDollCatalog LoadCatalog(IGameData data)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		return Cache.GetValue(data, Build);
	}

	public static bool TryReadDoll(IGameData data, string itemKey, out MagicDollDefinition definition)
	{
		definition = null;
		if (itemKey.Length > 0)
		{
			return LoadCatalog(data).ByItemKey.TryGetValue(itemKey, out definition);
		}
		return false;
	}

	public static string RollBagReward(IGameData data, ICombatRandom random)
	{
		ArgumentNullException.ThrowIfNull(random, "random");
		MagicDollCatalog magicDollCatalog = LoadCatalog(data);
		int val = magicDollCatalog.BagPool.Sum<(string, int)>(((string ItemKey, int Weight) entry) => entry.Weight);
		int num = random.Roll(1, Math.Max(1, val));
		foreach (var item3 in magicDollCatalog.BagPool)
		{
			string item = item3.ItemKey;
			int item2 = item3.Weight;
			num -= item2;
			if (num <= 0)
			{
				return item;
			}
		}
		IReadOnlyList<(string ItemKey, int Weight)> bagPool = magicDollCatalog.BagPool;
		return bagPool[bagPool.Count - 1].ItemKey;
	}

	public static int? ActiveDollType(Combatant owner)
	{
		ArgumentNullException.ThrowIfNull(owner, "owner");
		if (!owner.Counters.TryGetValue("doll:type", out var value))
		{
			return null;
		}
		return value;
	}

	private static MagicDollAbility ActiveAbility(Combatant owner)
	{
		int? num = ActiveDollType(owner);
		if (num.HasValue)
		{
			int valueOrDefault = num.GetValueOrDefault();
			return MagicDollDefinition.AbilityOf(valueOrDefault);
		}
		return MagicDollAbility.None;
	}

	public static double WeightCapacityMultiplier(Combatant owner)
	{
		if (ActiveAbility(owner) != MagicDollAbility.WeightRelief)
		{
			return 1.0;
		}
		return 1.2;
	}

	public static double BowHitAdjustment(Combatant attacker)
	{
		if (ActiveAbility(attacker) != MagicDollAbility.BowMastery)
		{
			return 0.0;
		}
		return 1.0;
	}

	public static double BowDamageAdjustment(Combatant attacker)
	{
		if (ActiveAbility(attacker) != MagicDollAbility.BowMastery)
		{
			return 0.0;
		}
		return 1.0;
	}

	public static double ArmorClassAdjustment(Combatant target)
	{
		if (ActiveAbility(target) != MagicDollAbility.ArmorBonus)
		{
			return 0.0;
		}
		return -1.0;
	}

	public static bool RollAttackBonus(Combatant attacker, ICombatRandom random)
	{
		ArgumentNullException.ThrowIfNull(random, "random");
		if (ActiveAbility(attacker) == MagicDollAbility.AttackDamage)
		{
			return random.NextDouble() * 100.0 < 3.0;
		}
		return false;
	}

	public static bool RollDamageShield(Combatant target, ICombatRandom random)
	{
		ArgumentNullException.ThrowIfNull(random, "random");
		if (ActiveAbility(target) == MagicDollAbility.DamageShield)
		{
			return random.NextDouble() * 100.0 < 4.0;
		}
		return false;
	}

	private static MagicDollCatalog Build(IGameData data)
	{
		if (!(data.Table("MAGIC_DOLLS") is JsonObject jsonObject))
		{
			throw new InvalidDataException("MAGIC_DOLLS table failed to load.");
		}
		Dictionary<string, MagicDollDefinition> dictionary = new Dictionary<string, MagicDollDefinition>(StringComparer.Ordinal);
		foreach (JsonNode item in jsonObject["dolls"].AsArray())
		{
			JsonObject jsonObject2 = item.AsObject();
			MagicDollDefinition magicDollDefinition = new MagicDollDefinition(jsonObject2["key"].GetValue<string>(), jsonObject2["n"].GetValue<string>(), jsonObject2["l1jItemId"].GetValue<int>(), jsonObject2["npcId"].GetValue<int>(), jsonObject2["type"].GetValue<int>(), jsonObject2["gfx"].GetValue<int>());
			if (data.Item(magicDollDefinition.ItemKey) == null)
			{
				throw new InvalidDataException("Magic doll item '" + magicDollDefinition.ItemKey + "' is missing from DB.items.");
			}
			dictionary.Add(magicDollDefinition.ItemKey, magicDollDefinition);
		}
		if (dictionary.Count != 15)
		{
			throw new InvalidDataException($"MAGIC_DOLLS must define exactly 15 dolls, got {dictionary.Count}.");
		}
		string value = jsonObject["crystal"]["key"].GetValue<string>();
		(string, int)[] bagPool = (from node in jsonObject["bagPool"].AsArray()
			select (node["key"].GetValue<string>(), node["weight"].GetValue<int>())).ToArray();
		JsonObject jsonObject3 = jsonObject["arkaRecipe"].AsObject();
		(string, int)[] arkaMaterials = (from node in jsonObject3["materials"].AsArray()
			select (node["key"].GetValue<string>(), node["count"].GetValue<int>())).ToArray();
		return new MagicDollCatalog(dictionary, value, bagPool, arkaMaterials, jsonObject3["output"]["key"].GetValue<string>());
	}
}
