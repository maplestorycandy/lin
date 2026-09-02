using System;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class RelicProcRules
{
	public static RelicWeaponSpellProc? WeaponSpell(IGameData? data, Combatant actor)
	{
		JsonObject jsonObject = MainWeapon(data, actor);
		if (!(jsonObject?["spellProc"] is JsonObject jsonObject2))
		{
			return null;
		}
		string text = CombatSkill.ReadString(jsonObject2, "skn");
		if (text.Length == 0)
		{
			text = "weapon-spell";
		}
		int diceCount = 0;
		int diceSides = 0;
		if (jsonObject2["dice"] is JsonArray { Count: >=2 } jsonArray)
		{
			diceCount = Math.Max(0, jsonArray[0]?.GetValue<int>() ?? 0);
			diceSides = Math.Max(0, jsonArray[1]?.GetValue<int>() ?? 0);
		}
		JsonObject jsonObject3 = jsonObject2["status"] as JsonObject;
		int val = (jsonObject2.ContainsKey("fix") ? CombatSkill.ReadInt(jsonObject2, "fix") : CombatSkill.ReadInt(jsonObject2, "flat"));
		return new RelicWeaponSpellProc(text, Math.Clamp(CombatSkill.ReadDouble(jsonObject, "procRateBase", 1.0), 0.0, 100.0), Math.Max(0, val), Math.Max(0, CombatSkill.ReadInt(jsonObject2, "rnd")), CombatSkill.NormalizeElement(CombatSkill.ReadString(jsonObject2, "ele")), CombatSkill.ReadInt(jsonObject2, "area"), diceCount, diceSides, (jsonObject3 == null) ? "" : CombatSkill.ReadString(jsonObject3, "kind"), (jsonObject3 == null) ? 0.0 : Math.Clamp(CombatSkill.ReadDouble(jsonObject3, "pct"), 0.0, 100.0), (jsonObject3 != null) ? Math.Max(0, CombatSkill.ReadInt(jsonObject3, "dur") * 10) : 0);
	}

	public static JsonObject? MainWeapon(IGameData? data, Combatant actor)
	{
		if (data == null)
		{
			return null;
		}
		string text = MainWeaponStack(actor)?.ItemKey ?? actor.MainWeaponId;
		if (text.Length <= 0)
		{
			return null;
		}
		return data.Item(text);
	}

	public static ItemStack? MainWeaponStack(Combatant actor)
	{
		if (!actor.EquippedItems.TryGetValue("wpn", out ItemStack value))
		{
			return null;
		}
		return value;
	}
}
