using System;
using System.Text.Json.Nodes;
using IdleLineage.Data;

namespace IdleLineage.App;

internal static class ClassicInventoryTabRules
{
	public static bool Matches(IGameData data, string itemKey, ClassicInventoryTab tab)
	{
		ArgumentNullException.ThrowIfNull(data, "data");
		string text = TypeOf(data.Item(itemKey));
		switch (tab)
		{
		case ClassicInventoryTab.Potion:
			return text == "pot";
		case ClassicInventoryTab.Equipment:
		{
			bool flag;
			switch (text)
			{
			case "wpn":
			case "arm":
			case "acc":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return flag;
		}
		case ClassicInventoryTab.Scroll:
			return text == "scroll";
		default:
		{
			bool flag;
			switch (text)
			{
			case "pot":
			case "wpn":
			case "arm":
			case "acc":
			case "scroll":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			return !flag;
		}
		}
	}

	private static string? TypeOf(JsonObject? definition)
	{
		if (!(definition?["type"] is JsonValue jsonValue) || !jsonValue.TryGetValue<string>(out string value))
		{
			return null;
		}
		return value;
	}
}
