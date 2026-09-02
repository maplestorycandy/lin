using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IdleLineage.Combat;
using IdleLineage.Data;

namespace IdleLineage.App;

public static class QuickBar
{
	public const int Slots = 8;

	public const string SkillDragPrefix = "skill:";

	public const int Pages = 2;

	public const int TotalSlots = 16;

	public const double AutoHealBelow = 0.7;

	public const double ManualPotionCooldown = 1.0;

	public const double AutoPotionCooldown = 1.0;

	private static readonly IReadOnlyCollection<string> NeverAutoUse = new HashSet<string>(StringComparer.Ordinal) { "scroll_teleport", "scroll_return", "item_whetstone" };

	private const string FoodEffect = "food";

	public static int PageOf(int globalSlot)
	{
		return globalSlot / 8;
	}

	public static int LocalSlot(int globalSlot)
	{
		return globalSlot % 8;
	}

	public static int GlobalSlot(int page, int localSlot)
	{
		return page * 8 + localSlot;
	}

	public static bool CanAssign(IGameData data, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		if (!string.IsNullOrWhiteSpace(itemKey) && data.Item(itemKey) != null)
		{
			return !MonsterCompanionPotionRules.IsCompanionPotion(data, itemKey);
		}
		return false;
	}

	public static string AssignmentFor(ItemAction action, ItemStack stack)
	{
		ArgumentNullException.ThrowIfNull(stack, "stack");
		if (action != ItemAction.Equip)
		{
			return stack.ItemKey;
		}
		return ItemDragPayload.Encode(stack.ItemKey, stack.Uid);
	}

	public static (string ItemKey, string StackUid, bool IsInstance) DecodeAssignment(string assignment)
	{
		return ItemDragPayload.Decode(assignment);
	}

	public static void RemapEquipmentAssignment(string?[] assignments, string oldUid, string newUid, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(assignments, "assignments");
		if (string.IsNullOrWhiteSpace(oldUid) || string.IsNullOrWhiteSpace(newUid) || string.IsNullOrWhiteSpace(itemKey) || string.Equals(oldUid, newUid, StringComparison.Ordinal))
		{
			return;
		}
		for (int i = 0; i < assignments.Length; i++)
		{
			string text = assignments[i];
			if (text != null && text.Length != 0)
			{
				(string ItemKey, string StackUid, bool IsInstance) tuple = DecodeAssignment(text);
				var (a, a2, _) = tuple;
				if (tuple.IsInstance && string.Equals(a2, oldUid, StringComparison.Ordinal) && string.Equals(a, itemKey, StringComparison.Ordinal))
				{
					assignments[i] = ItemDragPayload.Encode(itemKey, newUid);
				}
			}
		}
	}

	public static bool CanAutoUse(IGameData data, string itemKey)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		if (MonsterCompanionPotionRules.IsCompanionPotion(data, itemKey))
		{
			return false;
		}
		if (NeverAutoUse.Contains(itemKey))
		{
			return false;
		}
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject == null || ReadString(jsonObject, "type") != "pot")
		{
			return false;
		}
		if (ReadBool(jsonObject, "noUse"))
		{
			return false;
		}
		if (!PetAcquisitionRules.IsTamingItem(data, itemKey))
		{
			return !ItemActivation.IsPetEvolutionFruit(data, itemKey);
		}
		return false;
	}

	public static bool ShouldAutoUse(IGameData data, Combatant actor, string itemKey)
	{
		return ShouldAutoUse(data, actor, itemKey, 0.7);
	}

	public static bool ShouldAutoUse(IGameData data, Combatant actor, string itemKey, double autoHealBelow)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		ArgumentNullException.ThrowIfNull(actor, "actor");
		if (!CanAutoUse(data, itemKey))
		{
			return false;
		}
		JsonObject jsonObject = data.Item(itemKey);
		if (jsonObject == null)
		{
			return false;
		}
		string text = ReadString(jsonObject, "eff");
		if (text.Length == 0)
		{
			return actor.Hp < actor.MaxHp * Math.Clamp(autoHealBelow, 0.01, 1.0);
		}
		if (text == "food")
		{
			double num = ReadDouble(jsonObject, "food");
			if (num > 0.0)
			{
				return actor.Satiety + num <= 225.0;
			}
			return false;
		}
		return !actor.Buffs.ContainsKey(text);
	}

	private static string ReadString(JsonObject? item, string key)
	{
		if (!(item?[key] is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value))
		{
			return "";
		}
		return value ?? "";
	}

	private static bool ReadBool(JsonObject? item, string key)
	{
		bool value = default(bool);
		return item?[key] is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value) && value;
	}

	private static double ReadDouble(JsonObject? item, string key)
	{
		if (!(item?[key] is JsonValue jsonValue) || !jsonValue.TryGetValue<double>(out var value))
		{
			return 0.0;
		}
		return value;
	}
}
