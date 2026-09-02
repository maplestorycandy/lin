using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using IdleLineage.Combat;

namespace IdleLineage.App;

public static class SaveManager
{
	public readonly record struct SlotInfo(int Slot, bool Empty, string ClassName, int Level, string Town, string Identity = "", double MagicResist = 0.0, double EarthResist = 0.0, double MeleeDamage = 0.0, double RangedDamage = 0.0, double MagicDamage = 0.0, double SpellPower = 0.0, string ClassId = "", string Avatar = "", bool Male = true, double Alignment = 0.0, double Hp = 0.0, double MaxHp = 0.0, double Mp = 0.0, double MaxMp = 0.0, double ArmorClass = 10.0, double Str = 0.0, double Dex = 0.0, double Con = 0.0, double Int = 0.0, double Wis = 0.0, double Cha = 0.0, string CharacterName = "")
	{
		public string DisplayName
		{
			get
			{
				if (!string.IsNullOrWhiteSpace(CharacterName))
				{
					return CharacterName;
				}
				return ClassName;
			}
		}
	}

	public enum ImportOutcome
	{
		Success,
		EmptyClipboard,
		BadFormat,
		InvalidSave,
		WriteFailed
	}

	private sealed class SaveEnvelope
	{
		public int V { get; set; } = 8;

		public string Player { get; set; } = "";

		public string Warehouse { get; set; } = "";

		public string Town { get; set; } = "town_aden";

		public string HuntMap { get; set; } = "";

		public double HuntX { get; set; }

		public double HuntY { get; set; }

		public string Party { get; set; } = "";

		public string AutoCast { get; set; } = "";

		public string QuickItems { get; set; } = "";

		public string QuickSkills { get; set; } = "";

		public int QuickPage { get; set; }

		public string AutoUseItems { get; set; } = "";

		public int AutoPotionHpPercent { get; set; } = 70;

		public int CompanionAutoHealSkillHpPercent { get; set; } = 70;

		public int CompanionAutoPotionHpPercent { get; set; } = 70;

		public int AutoSkillMpPercent { get; set; }

		public int AutoSkillHpPercent { get; set; } = 50;

		public string AttackPriorities { get; set; } = "";

		public string CompanionAttackPriorities { get; set; } = "";

		public string Collections { get; set; } = "";

		public string Identity { get; set; } = "";

		public string Pets { get; set; } = "";

		public bool Pvp { get; set; }

		public long LastPk { get; set; }

		public long LastPkElf { get; set; }

		public string ClassName { get; set; } = "";

		public string CharacterName { get; set; } = "";

		public string ClassId { get; set; } = "";

		public string Avatar { get; set; } = "";

		public bool Male { get; set; } = true;

		public int Level { get; set; }

		public double Alignment { get; set; }

		public double Hp { get; set; }

		public double MaxHp { get; set; }

		public double Mp { get; set; }

		public double MaxMp { get; set; }

		public double Ac { get; set; } = 10.0;

		public double Str { get; set; }

		public double Dex { get; set; }

		public double Con { get; set; }

		public double Int { get; set; }

		public double Wis { get; set; }

		public double Cha { get; set; }

		public double Mr { get; set; }

		public double EarthResist { get; set; }

		public double MeleeDmg { get; set; }

		public double RangedDmg { get; set; }

		public double MagicDmg { get; set; }

		public double SpellPower { get; set; }
	}

	public const int SlotCount = 8;

	private const int EnvelopeVersion = 8;

	private static readonly JsonSerializerOptions CurrentJsonOptions = new JsonSerializerOptions
	{
		UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
	};

	public static int CurrentSlot { get; set; } = 1;

	private static string PathFor(int slot)
	{
		return $"user://idle_save_{slot}.json";
	}

	public static bool HasSave(int slot)
	{
		return Godot.FileAccess.FileExists(PathFor(slot));
	}

	public static bool HasAnySave()
	{
		for (int i = 1; i <= 8; i++)
		{
			if (HasSave(i))
			{
				return true;
			}
		}
		return false;
	}

	public static SlotInfo ReadSlot(int slot)
	{
		string path = PathFor(slot);
		if (!Godot.FileAccess.FileExists(path))
		{
			return new SlotInfo(slot, Empty: true, "", 0, "");
		}
		try
		{
			string asText;
			using (Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
			{
				if (fileAccess == null)
				{
					return new SlotInfo(slot, Empty: true, "", 0, "");
				}
				asText = fileAccess.GetAsText();
			}
			SaveEnvelope saveEnvelope = ParseCurrentEnvelope(asText);
			string className = saveEnvelope.ClassName;
			ClassDef classDef = ClassCatalog.Find(string.IsNullOrWhiteSpace(saveEnvelope.ClassId) ? className : saveEnvelope.ClassId);
			string classId = ((!string.IsNullOrWhiteSpace(saveEnvelope.ClassId)) ? saveEnvelope.ClassId : (classDef?.Id ?? ""));
			string avatar = ((!string.IsNullOrWhiteSpace(saveEnvelope.Avatar)) ? saveEnvelope.Avatar : (classDef?.Avatar(saveEnvelope.Male) ?? ""));
			return new SlotInfo(slot, Empty: false, className, saveEnvelope.Level, saveEnvelope.Town, saveEnvelope.Identity, saveEnvelope.Mr, saveEnvelope.EarthResist, saveEnvelope.MeleeDmg, saveEnvelope.RangedDmg, saveEnvelope.MagicDmg, saveEnvelope.SpellPower, classId, avatar, saveEnvelope.Male, saveEnvelope.Alignment, saveEnvelope.Hp, saveEnvelope.MaxHp, saveEnvelope.Mp, saveEnvelope.MaxMp, saveEnvelope.Ac, saveEnvelope.Str, saveEnvelope.Dex, saveEnvelope.Con, saveEnvelope.Int, saveEnvelope.Wis, saveEnvelope.Cha, saveEnvelope.CharacterName);
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[SaveManager] 第 {slot} 槽摘要讀取失敗：{ex.Message}");
			return new SlotInfo(slot, Empty: false, "（讀取失敗）", 0, "");
		}
	}

	public static SlotInfo[] ReadAllSlots()
	{
		SlotInfo[] array = new SlotInfo[8];
		for (int i = 0; i < 8; i++)
		{
			array[i] = ReadSlot(i + 1);
		}
		return array;
	}

	public static bool DeleteSlot(int slot)
	{
		string path = PathFor(slot);
		if (!Godot.FileAccess.FileExists(path))
		{
			return true;
		}
		string identity = ReadSlot(slot).Identity;
		try
		{
			File.Delete(ProjectSettings.GlobalizePath(path));
			ClanStore.Forget(identity);
			return true;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[SaveManager] 第 {slot} 槽刪除失敗：{ex.Message}");
			return false;
		}
	}

	public static bool Save(GameSession session)
	{
		try
		{
			string text = JsonSerializer.Serialize(new SaveEnvelope
			{
				Player = PlayerSave.Capture(session.Player),
				Warehouse = WarehouseSave.Capture(session.Warehouse),
				Town = session.TownKey,
				HuntMap = session.LastHuntMap,
				HuntX = session.LastHuntX,
				HuntY = session.LastHuntY,
				Party = PartySave.Capture(session.Party),
				AutoCast = string.Join(",", session.AutoCast),
				QuickItems = string.Join(",", session.QuickItems.Select((string k) => k ?? "")),
				QuickSkills = string.Join(",", session.QuickSkills.Select((string k) => k ?? "")),
				QuickPage = session.QuickPage,
				AutoUseItems = string.Join(",", session.AutoUseItems),
				AutoPotionHpPercent = session.AutoPotionHpPercent,
				CompanionAutoHealSkillHpPercent = session.CompanionAutoHealSkillHpPercent,
				CompanionAutoPotionHpPercent = session.CompanionAutoPotionHpPercent,
				AutoSkillMpPercent = session.AutoSkillMpPercent,
				AutoSkillHpPercent = session.AutoSkillHpPercent,
				AttackPriorities = PlayerAttackPriorityRules.Serialize(PlayerAttackPriorityRules.Normalize(session.AttackPriorities)),
				CompanionAttackPriorities = CompanionAttackPriorityRules.Serialize(CompanionAttackPriorityRules.Normalize(session.CompanionAttackPriorities)),
				Collections = CollectionSave.Capture(session.Collections),
				Identity = session.Identity,
				Pets = PetRosterSave.Capture(session.Pets),
				Pvp = session.PvpEnabled,
				LastPk = session.LastPlayerKillUnixSeconds,
				LastPkElf = session.LastElfKillUnixSeconds,
				ClassName = session.Build.ClassName,
				CharacterName = session.Build.DisplayName,
				ClassId = session.Build.ClassId,
				Avatar = session.Build.Avatar,
				Male = session.Build.Male,
				Level = session.Player.Level,
				Alignment = session.Player.Alignment,
				Hp = session.Player.Hp,
				MaxHp = session.Player.MaxHp,
				Mp = session.Player.Mp,
				MaxMp = session.Player.MaxMp,
				Ac = session.Player.D.ArmorClass,
				Str = session.Player.D.Str,
				Dex = session.Player.D.Dex,
				Con = session.Player.D.Con,
				Int = session.Player.D.Int,
				Wis = session.Player.D.Wis,
				Cha = session.Player.D.Cha,
				Mr = session.Player.D.MagicResist,
				EarthResist = session.Player.D.ResistEarth,
				MeleeDmg = session.Player.D.MeleeDamage,
				RangedDmg = session.Player.D.RangedDamage,
				MagicDmg = session.Player.D.MagicDamage,
				SpellPower = session.Player.D.IntelligenceSpellPower + session.Player.D.ItemSpellPower
			}, CurrentJsonOptions);
			string text2 = PathFor(CurrentSlot);
			string path = text2 + ".tmp";
			using (Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
			{
				if (fileAccess == null)
				{
					GD.PushWarning($"[SaveManager] 無法開檔寫入（{Godot.FileAccess.GetOpenError()}）");
					return false;
				}
				fileAccess.StoreString(text);
			}
			File.Move(ProjectSettings.GlobalizePath(path), ProjectSettings.GlobalizePath(text2), overwrite: true);
			return true;
		}
		catch (Exception ex)
		{
			GD.PushWarning("[SaveManager] 存檔失敗（保留上一份檢查點）：" + ex.Message);
			return false;
		}
	}

	public static GameSession? Load(int slot)
	{
		if (!HasSave(slot))
		{
			return null;
		}
		string path = PathFor(slot);
		try
		{
			string asText;
			using (Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read))
			{
				if (fileAccess == null)
				{
					return null;
				}
				asText = fileAccess.GetAsText();
			}
			if (string.IsNullOrWhiteSpace(asText))
			{
				return null;
			}
			SaveEnvelope saveEnvelope = ParseCurrentEnvelope(asText);
			ValidateEnvelopeSettings(saveEnvelope);
			Combatant combatant = PlayerSave.Restore(GameDataProvider.Shared, saveEnvelope.Player);
			GameSession gameSession = new GameSession(BuildFromPlayer(combatant), combatant);
			gameSession.Warehouse = WarehouseSave.Restore(GameDataProvider.Shared, saveEnvelope.Warehouse);
			gameSession.Party = PartySave.Restore(GameDataProvider.Shared, saveEnvelope.Party);
			gameSession.Party.EnforceDeploymentLimits(GameDataProvider.Shared, gameSession.Player);
			gameSession.TownKey = saveEnvelope.Town;
			gameSession.LastHuntMap = saveEnvelope.HuntMap;
			gameSession.LastHuntX = saveEnvelope.HuntX;
			gameSession.LastHuntY = saveEnvelope.HuntY;
			if (!string.IsNullOrWhiteSpace(saveEnvelope.AutoCast))
			{
				string[] array = saveEnvelope.AutoCast.Split(',', StringSplitOptions.RemoveEmptyEntries);
				foreach (string item in array)
				{
					gameSession.AutoCast.Add(item);
				}
			}
			gameSession.AutoCast.RemoveWhere((string id) => SkillExecutionRules.IsManualOnly(GameDataProvider.Shared, id));
			if (!string.IsNullOrEmpty(saveEnvelope.QuickItems))
			{
				string[] array2 = saveEnvelope.QuickItems.Split(',');
				for (int num = 0; num < gameSession.QuickItems.Length && num < array2.Length; num++)
				{
					gameSession.QuickItems[num] = ((array2[num].Length > 0) ? array2[num] : null);
				}
			}
			if (!string.IsNullOrEmpty(saveEnvelope.QuickSkills))
			{
				string[] array3 = saveEnvelope.QuickSkills.Split(',');
				for (int num2 = 0; num2 < gameSession.QuickSkills.Length && num2 < array3.Length; num2++)
				{
					string text = ((array3[num2].Length > 0) ? array3[num2] : null);
					gameSession.QuickSkills[num2] = ((text != null && BeginnerKitRules.IsLegacyCreationSkill(gameSession.Player, text)) ? null : text);
				}
			}
			if (BeginnerKitRules.IsLegacyCreationSkill(gameSession.Player, "sk_lightarrow"))
			{
				gameSession.AutoCast.Remove("sk_lightarrow");
			}
			gameSession.QuickPage = saveEnvelope.QuickPage;
			if (!string.IsNullOrWhiteSpace(saveEnvelope.AutoUseItems))
			{
				string[] array = saveEnvelope.AutoUseItems.Split(',', StringSplitOptions.RemoveEmptyEntries);
				foreach (string item2 in array)
				{
					gameSession.AutoUseItems.Add(item2);
				}
			}
			gameSession.AutoPotionHpPercent = saveEnvelope.AutoPotionHpPercent;
			gameSession.CompanionAutoHealSkillHpPercent = saveEnvelope.CompanionAutoHealSkillHpPercent;
			gameSession.CompanionAutoPotionHpPercent = saveEnvelope.CompanionAutoPotionHpPercent;
			gameSession.AutoSkillMpPercent = saveEnvelope.AutoSkillMpPercent;
			gameSession.AutoSkillHpPercent = saveEnvelope.AutoSkillHpPercent;
			if (!PlayerAttackPriorityRules.TryParseSaved(saveEnvelope.AttackPriorities, out PlayerAttackPriority[] priorities))
			{
				throw new InvalidDataException("Save envelope attack priorities are invalid.");
			}
			gameSession.Player.AttackPriorities = priorities;
			gameSession.AttackPriorities = PlayerAttackPriorityRules.KeysOf(priorities);
			if (!CompanionAttackPriorityRules.TryParseSaved(saveEnvelope.CompanionAttackPriorities, out CompanionAttackPriority[] priorities2))
			{
				throw new InvalidDataException("Save envelope companion attack priorities are invalid.");
			}
			gameSession.Player.CompanionAttackPriorities = priorities2;
			gameSession.CompanionAttackPriorities = CompanionAttackPriorityRules.KeysOf(priorities2);
			gameSession.Collections = CollectionSave.Restore(GameDataProvider.Shared, saveEnvelope.Collections);
			gameSession.Identity = saveEnvelope.Identity;
			gameSession.Pets = PetRosterSave.Restore(GameDataProvider.Shared, saveEnvelope.Pets);
			PetCollarRules.EnsureCollars(GameDataProvider.Shared, gameSession.Pets, gameSession.Player, gameSession.Warehouse.Items);
			gameSession.PvpEnabled = saveEnvelope.Pvp;
			gameSession.LastPlayerKillUnixSeconds = saveEnvelope.LastPk;
			gameSession.LastElfKillUnixSeconds = saveEnvelope.LastPkElf;
			CollectionRules.Attach(GameDataProvider.Shared, gameSession.Player, gameSession.Collections, gameSession.Warehouse);
			CurrentSlot = slot;
			return gameSession;
		}
		catch (Exception ex)
		{
			GD.PushWarning($"[SaveManager] 第 {slot} 槽讀檔失敗：{ex.Message}");
			return null;
		}
	}

	private static PlayerBuild BuildFromPlayer(Combatant p)
	{
		ClassDef[] all = ClassCatalog.All;
		foreach (ClassDef classDef in all)
		{
			if (classDef.Id == p.ClassId)
			{
				bool male = p.Avatar == classDef.MaleAvatar;
				string avatar = (string.IsNullOrEmpty(p.Avatar) ? classDef.Avatar(male) : p.Avatar);
				return new PlayerBuild(classDef.Id, classDef.Name, avatar, classDef.Weapon, male, p.Level)
				{
					CharacterName = p.Disp
				};
			}
		}
		return new PlayerBuild(p.ClassId, p.ClassId, p.Avatar, "", Male: true, p.Level)
		{
			CharacterName = p.Disp
		};
	}

	public static bool ExportToClipboard(int slot)
	{
		if (!HasSave(slot))
		{
			return false;
		}
		try
		{
			string asText;
			using (Godot.FileAccess fileAccess = Godot.FileAccess.Open(PathFor(slot), Godot.FileAccess.ModeFlags.Read))
			{
				if (fileAccess == null)
				{
					return false;
				}
				asText = fileAccess.GetAsText();
			}
			if (string.IsNullOrWhiteSpace(asText))
			{
				return false;
			}
			DisplayServer.ClipboardSet(Convert.ToBase64String(Encoding.UTF8.GetBytes(asText)));
			return true;
		}
		catch (Exception ex)
		{
			GD.PushWarning("[SaveManager] 匯出失敗：" + ex.Message);
			return false;
		}
	}

	public static ImportOutcome ImportFromClipboard(int slot)
	{
		string text;
		try
		{
			text = DisplayServer.ClipboardGet();
		}
		catch
		{
			return ImportOutcome.EmptyClipboard;
		}
		if (string.IsNullOrWhiteSpace(text))
		{
			return ImportOutcome.EmptyClipboard;
		}
		string text2;
		try
		{
			text2 = Encoding.UTF8.GetString(Convert.FromBase64String(text.Trim()));
		}
		catch
		{
			return ImportOutcome.BadFormat;
		}
		if (string.IsNullOrWhiteSpace(text2))
		{
			return ImportOutcome.BadFormat;
		}
		if (!IsValidSaveText(text2))
		{
			return ImportOutcome.InvalidSave;
		}
		try
		{
			string text3 = PathFor(slot);
			if (HasSave(slot))
			{
				try
				{
					File.Copy(ProjectSettings.GlobalizePath(text3), ProjectSettings.GlobalizePath($"user://idle_save_{slot}.bak.json"), overwrite: true);
				}
				catch (Exception ex)
				{
					GD.PushWarning("[SaveManager] 匯入前備份失敗（仍續行）：" + ex.Message);
				}
			}
			string path = text3 + ".tmp";
			using (Godot.FileAccess fileAccess = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
			{
				if (fileAccess == null)
				{
					return ImportOutcome.WriteFailed;
				}
				fileAccess.StoreString(text2);
			}
			File.Move(ProjectSettings.GlobalizePath(path), ProjectSettings.GlobalizePath(text3), overwrite: true);
			return ImportOutcome.Success;
		}
		catch (Exception ex2)
		{
			GD.PushWarning("[SaveManager] 匯入寫入失敗：" + ex2.Message);
			return ImportOutcome.WriteFailed;
		}
	}

	private static bool IsValidSaveText(string text)
	{
		try
		{
			SaveEnvelope env = ParseCurrentEnvelope(text);
			ValidateEnvelopeSettings(env);
			ValidateEnvelopeBlobs(env);
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static SaveEnvelope ParseCurrentEnvelope(string text)
	{
		SaveEnvelope saveEnvelope;
		try
		{
			saveEnvelope = JsonSerializer.Deserialize<SaveEnvelope>(text, CurrentJsonOptions) ?? throw new InvalidDataException("Save envelope is empty.");
		}
		catch (JsonException innerException)
		{
			throw new InvalidDataException("Save envelope is not valid JSON.", innerException);
		}
		if (saveEnvelope.V != 8 || string.IsNullOrWhiteSpace(saveEnvelope.Player) || string.IsNullOrWhiteSpace(saveEnvelope.Warehouse) || string.IsNullOrWhiteSpace(saveEnvelope.Town) || string.IsNullOrWhiteSpace(saveEnvelope.Party) || string.IsNullOrWhiteSpace(saveEnvelope.Collections) || string.IsNullOrWhiteSpace(saveEnvelope.Identity) || string.IsNullOrWhiteSpace(saveEnvelope.Pets) || string.IsNullOrWhiteSpace(saveEnvelope.ClassName) || string.IsNullOrWhiteSpace(saveEnvelope.CharacterName) || string.IsNullOrWhiteSpace(saveEnvelope.ClassId) || string.IsNullOrWhiteSpace(saveEnvelope.Avatar))
		{
			throw new InvalidDataException("Save envelope is not the current schema.");
		}
		return saveEnvelope;
	}

	private static void ValidateEnvelopeSettings(SaveEnvelope env)
	{
		if (env.QuickPage < 0 || env.QuickPage >= 2 || env.QuickItems.Split(',').Length != 16 || env.QuickSkills.Split(',').Length != 16 || env.AutoPotionHpPercent < 1 || env.AutoPotionHpPercent > 100 || env.CompanionAutoHealSkillHpPercent < 1 || env.CompanionAutoHealSkillHpPercent > 100 || env.CompanionAutoPotionHpPercent < 1 || env.CompanionAutoPotionHpPercent > 100 || env.AutoSkillMpPercent < 0 || env.AutoSkillMpPercent > 100 || env.AutoSkillHpPercent < 0 || env.AutoSkillHpPercent > 100 || !PlayerAttackPriorityRules.TryParseSaved(env.AttackPriorities, out PlayerAttackPriority[] _) || !CompanionAttackPriorityRules.TryParseSaved(env.CompanionAttackPriorities, out CompanionAttackPriority[] _) || !double.IsFinite(env.HuntX) || !double.IsFinite(env.HuntY))
		{
			throw new InvalidDataException("Save envelope settings are invalid.");
		}
	}

	private static void ValidateEnvelopeBlobs(SaveEnvelope env)
	{
		PlayerSave.Restore(GameDataProvider.Shared, env.Player);
		WarehouseSave.Restore(GameDataProvider.Shared, env.Warehouse);
		PartySave.Restore(GameDataProvider.Shared, env.Party);
		CollectionSave.Restore(GameDataProvider.Shared, env.Collections);
		PetRosterSave.Restore(GameDataProvider.Shared, env.Pets);
	}

	private static T DeserializeRequired<T>(string blob, string name)
	{
		try
		{
			T val = JsonSerializer.Deserialize<T>(blob, CurrentJsonOptions);
			if (val == null)
			{
				throw new InvalidDataException(name + " save is empty.");
			}
			return val;
		}
		catch (JsonException innerException)
		{
			throw new InvalidDataException(name + " save is not valid JSON.", innerException);
		}
	}
}
