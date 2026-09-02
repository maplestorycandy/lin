using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class L1jConsumableRules
{
	private static readonly IReadOnlyDictionary<int, L1jConsumableSpec> Specs = new Dictionary<int, L1jConsumableSpec>
	{
		[40015] = new L1jConsumableSpec(40015, ConsumableKind.TimedBuff, 0, "blue", 600.0),
		[40018] = new L1jConsumableSpec(40018, ConsumableKind.TimedBuff, 0, "haste", 1800.0),
		[40019] = new L1jConsumableSpec(40019, ConsumableKind.Healing, 15),
		[40020] = new L1jConsumableSpec(40020, ConsumableKind.Healing, 45),
		[40021] = new L1jConsumableSpec(40021, ConsumableKind.Healing, 75),
		[40022] = new L1jConsumableSpec(40022, ConsumableKind.Healing, 20),
		[40023] = new L1jConsumableSpec(40023, ConsumableKind.Healing, 30),
		[40024] = new L1jConsumableSpec(40024, ConsumableKind.Healing, 55),
		[40025] = new L1jConsumableSpec(40025, ConsumableKind.TimedBuff, 0, "blind", 16.0),
		[40027] = new L1jConsumableSpec(40027, ConsumableKind.Healing, 25),
		[40029] = new L1jConsumableSpec(40029, ConsumableKind.Healing, 15),
		[40032] = new L1jConsumableSpec(40032, ConsumableKind.TimedBuff, 0, "underwater_breath", 1800.0, 7200.0, AddsDuration: true),
		[40041] = new L1jConsumableSpec(40041, ConsumableKind.TimedBuff, 0, "underwater_breath", 300.0, 7200.0, AddsDuration: true),
		[40043] = new L1jConsumableSpec(40043, ConsumableKind.Healing, 600),
		[41261] = new L1jConsumableSpec(41261, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41262] = new L1jConsumableSpec(41262, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41268] = new L1jConsumableSpec(41268, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41269] = new L1jConsumableSpec(41269, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41271] = new L1jConsumableSpec(41271, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41272] = new L1jConsumableSpec(41272, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[41273] = new L1jConsumableSpec(41273, ConsumableKind.TimedBuff, 0, "haste", 24.0),
		[49158] = new L1jConsumableSpec(49158, ConsumableKind.TimedBuff, 0, "brave", 480.0, 0.0, AddsDuration: false, "dragonknight,illusionist")
	};

	public static bool TryRead(IGameData data, string itemKey, out L1jConsumableSpec spec)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		spec = default(L1jConsumableSpec);
		JsonObject jsonObject = data.Item(itemKey);
		int key = ((jsonObject != null) ? CombatSkill.ReadInt(jsonObject, "l1jItemId") : 0);
		return Specs.TryGetValue(key, out spec);
	}

	public static bool AllowsClass(L1jConsumableSpec spec, Combatant actor)
	{
		if (spec.RequiredClass.Length == 0)
		{
			return true;
		}
		string value = ClassKitRegistry.NormalizeClassId(actor.ClassId);
		return spec.RequiredClass.Split(',', StringSplitOptions.RemoveEmptyEntries).Contains<string>(value, StringComparer.Ordinal);
	}

	public static double RollHealing(L1jConsumableSpec spec, ICombatRandom random)
	{
		double d = Math.Max(double.Epsilon, random.NextDouble());
		double num = random.NextDouble();
		double num2 = Math.Sqrt(-2.0 * Math.Log(d)) * Math.Cos(Math.PI * 2.0 * num);
		return Math.Max(0.0, Math.Floor((double)spec.HealingBase * (num2 / 5.0 + 1.0)));
	}
}
