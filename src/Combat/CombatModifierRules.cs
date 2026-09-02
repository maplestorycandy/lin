using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.Combat;

public static class CombatModifierRules
{
	public const string WindShackleStatus = "windshackle";

	public const double WindShackleSlowFactor = 1.25;

	public const double HasteAttackIntervalFactor = 1.3333333333333333;

	public const double BraveAttackIntervalFactor = 1.3333333333333333;

	public const string BraveBuff = "brave";

	public const string ElfCookieBuff = "elfcookie";

	public static readonly IReadOnlyList<string> BraveSpeedMutexGroup = new string[6] { "sk_holy_dash", "sk_dark_walkhaste", "sk_elf_winddash", "brave", "elfcookie", "sk_dragon_bloodlust" };

	public const double DashMoveSpeedFactor = 1.33;

	public const double DarkWalkHasteMoveSpeedFactor = 1.15;

	public static int SkillMpCost(Combatant actor, JsonObject skill, string skillId = "")
	{
		return Math.Max(0, L1jSkillHandover.BaseMpCost(skill));
	}

	public static int SkillHpCost(Combatant actor, JsonObject skill, string skillId = "")
	{
		return Math.Max(0, L1jSkillHandover.BaseHpCost(skill));
	}

	public static double EffectiveMaxMp(Combatant actor, double baseMaxMp)
	{
		return Math.Max(0.0, baseMaxMp);
	}

	public static bool HasBraveSpeed(Combatant actor)
	{
		ArgumentNullException.ThrowIfNull(actor, "actor");
		if (!(actor.Buffs.GetValueOrDefault("brave") > 0.0))
		{
			return actor.Buffs.GetValueOrDefault("elfcookie") > 0.0;
		}
		return true;
	}

	public static IReadOnlyList<string> ClearConflictingSpeedBuffs(Combatant actor, string appliedBuffId)
	{
		ArgumentNullException.ThrowIfNull(actor, "actor");
		if (!BraveSpeedMutexGroup.Contains<string>(appliedBuffId, StringComparer.Ordinal))
		{
			return Array.Empty<string>();
		}
		List<string> list = null;
		foreach (string item in BraveSpeedMutexGroup)
		{
			if (!string.Equals(item, appliedBuffId, StringComparison.Ordinal) && actor.Buffs.Remove(item))
			{
				if (list == null)
				{
					list = new List<string>();
				}
				list.Add(item);
			}
		}
		IReadOnlyList<string> readOnlyList = list;
		return readOnlyList ?? Array.Empty<string>();
	}

	public static double PhysicalCriticalRateBonus(Combatant actor)
	{
		return (ClassKitRegistry.NormalizeClassId(actor.ClassId) == "dark") ? 3 : 0;
	}

	public static double EffectiveAttackInterval(Combatant actor, IGameData? data)
	{
		return Math.Max(0.1, actor.D.AttackInterval * AttackIntervalMultiplier(actor, data));
	}

	public static double AttackIntervalMultiplier(Combatant actor, IGameData? data)
	{
		double num = 1.0;
		if (actor.Buffs.GetValueOrDefault("haste") > 0.0 || HasEquipmentHaste(actor, data))
		{
			num /= 1.3333333333333333;
		}
		if (HasBraveSpeed(actor))
		{
			num /= 1.3333333333333333;
		}
		if (actor.HasStatus("windshackle"))
		{
			num *= 1.25;
		}
		return num * BehaviorBuffRules.AttackIntervalMultiplier(actor);
	}

	public static double EffectiveMoveSpeed(Combatant actor)
	{
		return EffectiveMoveSpeed(actor, null);
	}

	public static double EffectiveMoveSpeed(Combatant actor, IGameData? data)
	{
		double num = 1.0;
		bool flag = (actor.Kind == CombatantKind.Player || HostilePlayerRules.IsHostilePlayer(actor)) && HasEquipmentHaste(actor, data);
		if (actor.Buffs.GetValueOrDefault("haste") > 0.0 || flag)
		{
			num *= 1.3333333333333333;
		}
		if (HasBraveSpeed(actor))
		{
			num *= 1.3333333333333333;
		}
		if (actor.Buffs.GetValueOrDefault("sk_elf_winddash") > 0.0 || actor.Buffs.GetValueOrDefault("sk_holy_dash") > 0.0)
		{
			num *= 1.33;
		}
		if (actor.Buffs.GetValueOrDefault("sk_dark_walkhaste") > 0.0)
		{
			num *= 1.15;
		}
		if (actor.Buffs.GetValueOrDefault("poly") > 0.0 && actor.PolymorphGait > 0.0 && actor.PolymorphGait != 16.0)
		{
			num *= 16.0 / actor.PolymorphGait;
		}
		return Math.Max(0.0, actor.MoveSpeed * num);
	}

	private static bool HasEquipmentHaste(Combatant actor, IGameData? data)
	{
		JsonObject jsonObject = data?.Item(actor.MainWeaponId);
		if (jsonObject == null)
		{
			return false;
		}
		if (!WeaponCombatProfile.ReadBool(jsonObject, "equipHaste"))
		{
			return string.Equals(WeaponCombatProfile.WeaponEffect(jsonObject), "haste", StringComparison.Ordinal);
		}
		return true;
	}

	public static double ActiveMagicDamageBonus(Combatant actor)
	{
		return (actor.Buffs.GetValueOrDefault("cautious") > 0.0) ? 2 : 0;
	}

	public static double ActiveManaRegenBonus(Combatant actor)
	{
		double num = ((actor.Buffs.GetValueOrDefault("blue") > 0.0) ? (Math.Max(Math.Floor(actor.D.Wis), 11.0) - 10.0) : 0.0);
		if (actor.Buffs.GetValueOrDefault("cautious") > 0.0)
		{
			num += 2.0;
		}
		return num;
	}

	public static double ActiveHealthRegenBonus(Combatant actor)
	{
		return (actor.Buffs.GetValueOrDefault("sk_elf_lifespring") > 0.0) ? 15 : 0;
	}

	public static bool UsesMagicWeaponAttack(Combatant actor, IGameData? data)
	{
		if (ClassKitRegistry.NormalizeClassId(actor.ClassId) == "illusion")
		{
			JsonObject jsonObject = data?.Item(actor.MainWeaponId);
			if (jsonObject != null && !WeaponCombatProfile.ReadBool(jsonObject, "isBow"))
			{
				return WeaponCombatProfile.ReadBool(jsonObject, "qigu");
			}
		}
		return false;
	}

	public static bool HasPiercingWeapon(Combatant actor, IGameData? data)
	{
		JsonObject jsonObject = data?.Item(actor.MainWeaponId);
		if (jsonObject != null)
		{
			return WeaponCombatProfile.WeaponEffect(jsonObject) == "pierce";
		}
		return false;
	}

	public static double TitanThreshold(Combatant actor)
	{
		return 0.4;
	}

	public static int ArmorBodyReduction(Combatant actor)
	{
		if (!actor.LearnedSkills.Contains("sk_warrior_armorbody") && !actor.GrantedSkills.Contains("sk_warrior_armorbody"))
		{
			return 0;
		}
		return Math.Max(0, (int)Math.Floor((10.0 - actor.D.ArmorClass) / 10.0));
	}

	public static double PreciseTargetDamageMultiplier(Combatant provider)
	{
		if (!(provider.Buffs.GetValueOrDefault("sk_royal_precise") <= 0.0))
		{
			return 1.0 + (1.0 + (double)Math.Max(1, provider.Level) / 15.0) / 100.0;
		}
		return 1.0;
	}

	private static bool TryReadInt(JsonObject source, string field, out int value)
	{
		if (source[field] is JsonValue jsonValue && jsonValue.TryGetValue<double>(out var value2))
		{
			value = Math.Max(0, (int)Math.Floor(value2));
			return true;
		}
		value = 0;
		return false;
	}

	private static int ReadInt(JsonObject source, string field)
	{
		if (!TryReadInt(source, field, out var value))
		{
			return 0;
		}
		return value;
	}
}
