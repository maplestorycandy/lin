using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class ConsumableRules
{
	public const string PotionCooldownBuff = "_cooldown_item_potion";

	public const double PotionCooldownSeconds = 0.1;

	public const string ItemDelayGroupBuffPrefix = "_cooldown_item_group_";

	public const string ItemReuseDelayBuffPrefix = "_cooldown_item_reuse_";

	public const string InternalCooldownBuffPrefix = "_cooldown_item_";

	private const string AntFruitKey = "new_item_141";

	public const string CureEffect = "cure";

	private static readonly IReadOnlyDictionary<string, (int Minimum, int Maximum)> HealingRanges = new Dictionary<string, (int, int)>(StringComparer.Ordinal)
	{
		["potion_heal"] = (6, 27),
		["potion_strong"] = (26, 68),
		["potion_ult"] = (44, 107),
		["new_item_141"] = (44, 107)
	};

	public static void DecayInternalCooldowns(Combatant actor, double deltaSeconds)
	{
		ArgumentNullException.ThrowIfNull(actor, "actor");
		if (deltaSeconds <= 0.0 || actor.Buffs.Count == 0)
		{
			return;
		}
		List<string> list = null;
		List<(string, double)> list2 = null;
		foreach (var (text2, num2) in actor.Buffs)
		{
			if (text2.StartsWith("_cooldown_item_", StringComparison.Ordinal))
			{
				double num3 = num2 - deltaSeconds;
				if (num3 <= 0.0)
				{
					(list ?? (list = new List<string>())).Add(text2);
				}
				else
				{
					(list2 ?? (list2 = new List<(string, double)>())).Add((text2, num3));
				}
			}
		}
		if (list2 != null)
		{
			foreach (var (key, value) in list2)
			{
				actor.Buffs[key] = value;
			}
		}
		if (list == null)
		{
			return;
		}
		foreach (string item in list)
		{
			actor.Buffs.Remove(item);
		}
	}

	public static IReadOnlyList<string> CurableStatusKinds(IGameData data, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentException.ThrowIfNullOrWhiteSpace(itemKey, "itemKey");
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject == null)
		{
			return Array.Empty<string>();
		}
		return CurableStatusKinds(jsonObject);
	}

	private static IReadOnlyList<string> CurableStatusKinds(JsonObject item)
	{
		if (ReadString(item, "eff") != "cure" || !(item["cure"] is JsonArray jsonArray))
		{
			return Array.Empty<string>();
		}
		List<string> list = new List<string>(jsonArray.Count);
		foreach (JsonNode item2 in jsonArray)
		{
			if (item2 is JsonValue jsonValue && jsonValue.TryGetValue<string>(out string value) && !string.IsNullOrWhiteSpace(value))
			{
				string text = StatusRules.NormalizeKind(value);
				if (!list.Contains<string>(text, StringComparer.Ordinal))
				{
					list.Add(text);
				}
			}
		}
		return list;
	}

	public static (int Minimum, int Maximum) BaseHealingRange(IGameData data, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentException.ThrowIfNullOrWhiteSpace(itemKey, "itemKey");
		if (HealingRanges.TryGetValue(itemKey, out (int, int) value))
		{
			return value;
		}
		JsonObject source = data.Item(itemKey) ?? throw new InvalidDataException("Healing consumable '" + itemKey + "' is not defined.");
		int num = Math.Max(1, CombatSkill.ReadInt(source, "valMin"));
		int item = Math.Max(num, CombatSkill.ReadInt(source, "valMax"));
		return (Minimum: num, Maximum: item);
	}

	public static ConsumableEvaluation Evaluate(IGameData data, Combatant actor, string itemUid, ConsumableUseContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentNullException.ThrowIfNull(actor, "actor");
		ArgumentException.ThrowIfNullOrWhiteSpace(itemUid, "itemUid");
		if ((object)context == null)
		{
			context = new ConsumableUseContext();
		}
		ValidateContext(context);
		ItemStack itemStack = actor.InventoryStacks.FirstOrDefault((ItemStack item) => item.Uid == itemUid);
		if (itemStack == null)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemNotFound);
		}
		string itemKey = itemStack.ItemKey;
		if (itemStack.Locked)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemLocked, itemKey);
		}
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject == null)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemDefinitionMissing, itemKey);
		}
		int num = CombatSkill.ReadInt(jsonObject, "delayGroupId");
		if (num > 0 && actor.Buffs.GetValueOrDefault("_cooldown_item_group_" + num) > 0.0)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemDelayActive, itemKey);
		}
		if (CombatSkill.ReadDouble(jsonObject, "delayEffectSeconds") > 0.0 && actor.Buffs.GetValueOrDefault("_cooldown_item_reuse_" + itemKey) > 0.0)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemReuseDelay, itemKey);
		}
		if (actor.Dead || actor.Hp <= 0.0)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ActorDead, itemKey);
		}
		if (context.ItemUseBlocked || actor.Buffs.GetValueOrDefault("sk_abs_barrier") > 0.0)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ItemUseBlocked, itemKey);
		}
		L1jConsumableSpec spec;
		bool flag = L1jConsumableRules.TryRead(data, itemKey, out spec);
		if (ReadBool(jsonObject, "noUse") && !flag)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.DirectUseDisabled, itemKey);
		}
		if (ReadString(jsonObject, "type") != "pot" && !flag)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.NotConsumable, itemKey);
		}
		if (!RequirementAllowsActor(jsonObject, actor) || (flag && !L1jConsumableRules.AllowsClass(spec, actor)))
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.ClassMismatch, itemKey);
		}
		double num2 = CombatSkill.ReadDouble(jsonObject, "minLvl");
		if (num2 > 0.0 && (double)actor.Level < num2)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.LevelTooLow, itemKey);
		}
		double num3 = CombatSkill.ReadDouble(jsonObject, "maxLvl");
		if (num3 > 0.0 && (double)actor.Level > num3)
		{
			return ConsumableEvaluation.Failed(ConsumableUseFailure.LevelTooHigh, itemKey);
		}
		if (IsHealingPotion(data, itemKey))
		{
			if (context.HealingPotionsBlocked)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.HealingBlocked, itemKey, ConsumableKind.Healing, "heal");
			}
			if (actor.Buffs.GetValueOrDefault("_cooldown_item_potion") > 0.0)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.PotionCooldown, itemKey, ConsumableKind.Healing, "heal");
			}
			if (context.Automatic && itemKey == "new_item_141")
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.ManualOnly, itemKey, ConsumableKind.Healing, "heal");
			}
			return ConsumableEvaluation.Success(ConsumableKind.Healing, itemKey, "heal");
		}
		string text = ((flag && spec.Effect.Length > 0) ? spec.Effect : ReadString(jsonObject, "eff"));
		if (flag && spec.Kind == ConsumableKind.TimedBuff)
		{
			return ConsumableEvaluation.Success(ConsumableKind.TimedBuff, itemKey, text, spec.DurationSeconds);
		}
		switch (text)
		{
		case "whetstone":
		{
			if (context.Automatic)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.ManualOnly, itemKey, ConsumableKind.Whetstone, text);
			}
			ItemStack itemStack2 = WeaponDurabilityRules.EquippedMainWeapon(actor);
			if (itemStack2 == null || itemStack2.BrokenBladeStacks <= 0)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.NothingToRepair, itemKey, ConsumableKind.Whetstone, text);
			}
			return ConsumableEvaluation.Success(ConsumableKind.Whetstone, itemKey, text);
		}
		case "food":
		{
			if (!SatietyRules.UsesSatiety(actor))
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.RequiresSpecialHandler, itemKey, ConsumableKind.Food, text);
			}
			double num5 = ReadDouble(jsonObject, "food");
			if (!double.IsFinite(num5) || num5 <= 0.0)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.RequiresSpecialHandler, itemKey, ConsumableKind.Special, text);
			}
			if (SatietyRules.Clamp(actor.Satiety) >= 225.0)
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.SatietyFull, itemKey, ConsumableKind.Food, text);
			}
			return ConsumableEvaluation.Success(ConsumableKind.Food, itemKey, text, 0.0, num5);
		}
		case "cure":
			if (!CurableStatusKinds(jsonObject).Any((string kind) => (!string.Equals(kind, "poison", StringComparison.Ordinal)) ? actor.HasStatus(kind) : L1jPoisonAttackRules.IsPoisoned(actor)))
			{
				return ConsumableEvaluation.Failed(ConsumableUseFailure.NothingToCure, itemKey, ConsumableKind.Cure, text);
			}
			return ConsumableEvaluation.Success(ConsumableKind.Cure, itemKey, text);
		case "petlure":
			return ConsumableEvaluation.Failed(ConsumableUseFailure.RequiresSpecialHandler, itemKey, ConsumableKind.Special, text);
		default:
		{
			double num4 = ReadDouble(jsonObject, "dur");
			if (text.Length > 0 && num4 > 0.0)
			{
				return ConsumableEvaluation.Success(ConsumableKind.TimedBuff, itemKey, text, num4);
			}
			return ConsumableEvaluation.Failed(ConsumableUseFailure.RequiresSpecialHandler, itemKey, ConsumableKind.Special, text);
		}
		}
	}

	public static ConsumableUseResult TryUse(IGameData data, Combatant actor, string itemUid, ICombatRandom random, ConsumableUseContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(random, "random");
		if ((object)context == null)
		{
			context = new ConsumableUseContext();
		}
		ConsumableEvaluation evaluation = Evaluate(data, actor, itemUid, context);
		if (!evaluation.Allowed)
		{
			return ConsumableUseResult.Failed(evaluation);
		}
		List<ItemStack> list = actor.InventoryStacks.Select((ItemStack itemStack2) => itemStack2.Copy()).ToList();
		int num = list.FindIndex((ItemStack itemStack2) => itemStack2.Uid == itemUid);
		if (num < 0)
		{
			return ConsumableUseResult.Failed(ConsumableEvaluation.Failed(ConsumableUseFailure.ItemNotFound));
		}
		ItemStack itemStack = list[num];
		if (itemStack.Quantity == 1)
		{
			list.RemoveAt(num);
		}
		else
		{
			itemStack.Quantity--;
		}
		double num2 = 0.0;
		double hpRestored = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		bool buffApplied = false;
		int num5 = 0;
		IReadOnlyList<string> curedStatusKinds = null;
		IReadOnlyList<string> replacedBuffKeys = null;
		if (evaluation.Kind == ConsumableKind.Healing)
		{
			JsonObject item = data.Item(evaluation.ItemKey) ?? throw new InvalidDataException("Consumable '" + evaluation.ItemKey + "' disappeared during use.");
			num2 = (L1jConsumableRules.TryRead(data, evaluation.ItemKey, out var spec) ? ApplyPotionHealingModifiers(data, actor, item, L1jConsumableRules.RollHealing(spec, random), context) : CalculateHealing(data, actor, evaluation.ItemKey, item, random, context));
			hpRestored = actor.Heal(num2);
			actor.Buffs["_cooldown_item_potion"] = Math.Max(actor.Buffs.GetValueOrDefault("_cooldown_item_potion"), 0.1);
		}
		else if (evaluation.Kind == ConsumableKind.Food)
		{
			num3 = SatietyRules.Restore(actor, evaluation.SatietyRestore);
			if (num3 <= 0.0)
			{
				return ConsumableUseResult.Failed(ConsumableEvaluation.Failed(ConsumableUseFailure.SatietyFull, evaluation.ItemKey, ConsumableKind.Food, evaluation.EffectKey));
			}
		}
		else if (evaluation.Kind == ConsumableKind.TimedBuff)
		{
			double valueOrDefault = actor.Buffs.GetValueOrDefault(evaluation.EffectKey);
			num4 = ((L1jConsumableRules.TryRead(data, evaluation.ItemKey, out var spec2) && spec2.AddsDuration) ? Math.Min(spec2.MaximumDurationSeconds, valueOrDefault + evaluation.DurationSeconds) : evaluation.DurationSeconds);
			if (valueOrDefault < num4)
			{
				actor.Buffs[evaluation.EffectKey] = num4;
				buffApplied = true;
				replacedBuffKeys = CombatModifierRules.ClearConflictingSpeedBuffs(actor, evaluation.EffectKey);
			}
		}
		else if (evaluation.Kind == ConsumableKind.Whetstone)
		{
			num5 = WeaponDurabilityRules.RepairOnePoint(actor);
			if (num5 == 0)
			{
				return ConsumableUseResult.Failed(ConsumableEvaluation.Failed(ConsumableUseFailure.NothingToRepair, evaluation.ItemKey, ConsumableKind.Whetstone, evaluation.EffectKey));
			}
		}
		else if (evaluation.Kind == ConsumableKind.Cure)
		{
			JsonObject? item2 = data.Item(evaluation.ItemKey) ?? throw new InvalidDataException("Consumable '" + evaluation.ItemKey + "' disappeared during use.");
			List<string> list2 = new List<string>();
			foreach (string item3 in CurableStatusKinds(item2))
			{
				if (string.Equals(item3, "poison", StringComparison.Ordinal) ? L1jPoisonAttackRules.Cure(actor) : actor.Statuses.Remove(item3))
				{
					list2.Add(item3);
				}
			}
			if (list2.Count == 0)
			{
				return ConsumableUseResult.Failed(ConsumableEvaluation.Failed(ConsumableUseFailure.NothingToCure, evaluation.ItemKey, ConsumableKind.Cure, evaluation.EffectKey));
			}
			curedStatusKinds = list2;
		}
		JsonObject jsonObject = data.Item(evaluation.ItemKey);
		if (jsonObject != null)
		{
			int num6 = CombatSkill.ReadInt(jsonObject, "delayGroupId");
			double num7 = CombatSkill.ReadDouble(jsonObject, "delayGroupMs");
			if (num6 > 0 && num7 > 0.0)
			{
				string key = "_cooldown_item_group_" + num6;
				actor.Buffs[key] = Math.Max(actor.Buffs.GetValueOrDefault(key), num7 / 1000.0);
			}
			double num8 = CombatSkill.ReadDouble(jsonObject, "delayEffectSeconds");
			if (num8 > 0.0)
			{
				string key2 = "_cooldown_item_reuse_" + evaluation.ItemKey;
				actor.Buffs[key2] = Math.Max(actor.Buffs.GetValueOrDefault(key2), num8);
			}
		}
		actor.InventoryStacks = list;
		CombatInventory.SyncLegacyView(actor);
		return new ConsumableUseResult(Success: true, ConsumableUseFailure.None, evaluation.Kind, evaluation.ItemKey, evaluation.EffectKey, 1L, num2, hpRestored, num4, buffApplied, num3, num5, curedStatusKinds, replacedBuffKeys);
	}

	public static double RollHealingAmount(IGameData data, Combatant recipient, string sourceItemKey, ICombatRandom random, ConsumableUseContext? context = null)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentNullException.ThrowIfNull(recipient, "recipient");
		ArgumentException.ThrowIfNullOrWhiteSpace(sourceItemKey, "sourceItemKey");
		ArgumentNullException.ThrowIfNull(random, "random");
		if ((object)context == null)
		{
			context = new ConsumableUseContext();
		}
		ValidateContext(context);
		JsonObject item = data.Item(sourceItemKey) ?? throw new KeyNotFoundException("Healing potion source '" + sourceItemKey + "' was not found.");
		if (!IsHealingPotion(data, sourceItemKey))
		{
			throw new InvalidDataException("Item '" + sourceItemKey + "' is not a healing potion source.");
		}
		if (!L1jConsumableRules.TryRead(data, sourceItemKey, out var spec))
		{
			return CalculateHealing(data, recipient, sourceItemKey, item, random, context);
		}
		return ApplyPotionHealingModifiers(data, recipient, item, L1jConsumableRules.RollHealing(spec, random), context);
	}

	private static double CalculateHealing(IGameData data, Combatant actor, string itemKey, JsonObject item, ICombatRandom random, ConsumableUseContext context)
	{
		(int Minimum, int Maximum) tuple = BaseHealingRange(data, itemKey);
		int item2 = tuple.Minimum;
		int item3 = tuple.Maximum;
		int num = item2 + random.Roll(1, item3 - item2 + 1) - 1;
		if (itemKey == "new_item_141")
		{
			return Math.Max(1.0, Math.Floor((double)num * 1.0));
		}
		return ApplyPotionHealingModifiers(data, actor, item, num, context);
	}

	private static double ApplyPotionHealingModifiers(IGameData data, Combatant actor, JsonObject item, double rolled, ConsumableUseContext context)
	{
		double num = EquippedPotionBonus(data, actor) + CollectionRules.Bonuses(actor).PotionHealingPercent + context.AdditionalPotionHealingPercent;
		double num2 = Math.Floor(rolled * Math.Max(0.0, 1.0 + num / 100.0));
		num2 = Math.Floor(num2 * 1.0);
		if (actor.Hp < actor.MaxHp * 0.2 && actor.EquippedItems.Values.Any((ItemStack equipped) => ReadBool(data.Item(equipped.ItemKey), "lowHpPotionX2")))
		{
			num2 *= 2.0;
		}
		if (actor.HasStatus("potionFrost"))
		{
			num2 = Math.Max(1.0, Math.Floor(num2 * 0.5));
		}
		if (actor.HasStatus("foulWater"))
		{
			num2 = Math.Max(1.0, Math.Floor(num2 * 0.5));
		}
		return Math.Max(1.0, num2);
	}

	private static double EquippedPotionBonus(IGameData data, Combatant actor)
	{
		return actor.EquippedItems.Values.Sum((ItemStack equipped) => ReadDouble(data.Item(equipped.ItemKey), "potionBonus"));
	}

	private static bool IsHealingPotion(IGameData data, string itemKey)
	{
		if (L1jConsumableRules.TryRead(data, itemKey, out var spec))
		{
			return spec.Kind == ConsumableKind.Healing;
		}
		bool result;
		switch (itemKey)
		{
		case "potion_heal":
		case "potion_strong":
		case "potion_ult":
		case "new_item_141":
			result = true;
			break;
		default:
			result = false;
			break;
		}
		return result;
	}

	public static bool RequirementAllows(IGameData data, string itemKey, Combatant actor)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentNullException.ThrowIfNull(actor, "actor");
		ArgumentException.ThrowIfNullOrWhiteSpace(itemKey, "itemKey");
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject != null)
		{
			return RequirementAllowsActor(jsonObject, actor);
		}
		return false;
	}

	private static bool RequirementAllowsActor(JsonObject item, Combatant actor)
	{
		string text = ReadString(item, "req");
		if (text.Length == 0 || text == "all")
		{
			return true;
		}
		string value = ClassKitRegistry.NormalizeClassId(actor.ClassId);
		return text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains<string>(value, StringComparer.Ordinal);
	}

	private static void ValidateContext(ConsumableUseContext context)
	{
		if (!double.IsFinite(context.AdditionalPotionHealingPercent))
		{
			throw new ArgumentOutOfRangeException("context", "Additional potion healing must be finite.");
		}
	}

	private static string ReadString(JsonObject? source, string propertyName)
	{
		if (!(source?[propertyName] is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value))
		{
			return "";
		}
		return value ?? "";
	}

	private static double ReadDouble(JsonObject? source, string propertyName)
	{
		if (source == null)
		{
			return 0.0;
		}
		return CombatSkill.ReadDouble(source, propertyName);
	}

	private static bool ReadBool(JsonObject? source, string propertyName)
	{
		bool value = default(bool);
		return source?[propertyName] is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value) && value;
	}
}
