using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Godot;

namespace IdleLineage.App;

public static class ItemIcons
{
	public const int Size = 28;

	private const int GenericGroundGfx = 19;

	private static readonly IReadOnlyDictionary<string, string> Paths = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["gold"] = "res://assets/icons/items/金幣.png",
		["scroll_return"] = "res://assets/ui/items/scroll_return.png"
	};

	private static readonly Dictionary<string, Texture2D?> InventoryCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

	private static readonly Dictionary<string, Texture2D?> GroundCache = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

	private static readonly Dictionary<string, Rect2I> ContentCache = new Dictionary<string, Rect2I>(StringComparer.Ordinal);

	public static bool Has(string itemKey)
	{
		return For(itemKey) != null;
	}

	public static Texture2D? For(string itemKey, bool forGround = false)
	{
		if (string.IsNullOrEmpty(itemKey))
		{
			return null;
		}
		Dictionary<string, Texture2D> dictionary = (forGround ? GroundCache : InventoryCache);
		if (dictionary.TryGetValue(itemKey, out var value))
		{
			return value;
		}
		string text = ResolvePath(itemKey, forGround);
		return dictionary[itemKey] = ((text != null && ResourceLoader.Exists(text)) ? GD.Load<Texture2D>(text) : null);
	}

	public static Rect2I ContentRect(string itemKey)
	{
		if (ContentCache.TryGetValue(itemKey, out var value))
		{
			return value;
		}
		Texture2D texture2D = For(itemKey);
		Rect2I rect2I = default(Rect2I);
		if (texture2D != null)
		{
			Rect2I rect2I2 = new Rect2I(Vector2I.Zero, (Vector2I)texture2D.GetSize().Round());
			Rect2I rect2I3 = texture2D.GetImage()?.GetUsedRect() ?? default(Rect2I);
			rect2I = ((rect2I3.Size.X > 0 && rect2I3.Size.Y > 0) ? rect2I3 : rect2I2);
		}
		ContentCache[itemKey] = rect2I;
		return rect2I;
	}

	public static Control Slot(string itemKey)
	{
		Texture2D texture2D = For(itemKey);
		if (texture2D == null)
		{
			return new Control
			{
				CustomMinimumSize = new Vector2(28f, 28f)
			};
		}
		return new TextureRect
		{
			Texture = texture2D,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			Size = new Vector2(28f, 28f),
			CustomMinimumSize = new Vector2(28f, 28f),
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
		};
	}

	private static string? ResolvePath(string itemKey, bool forGround)
	{
		if (!forGround && Paths.TryGetValue(itemKey, out string value))
		{
			return value;
		}
		JsonObject jsonObject = GameDataProvider.Shared.Item(itemKey);
		if (jsonObject == null)
		{
			return null;
		}
		if (forGround)
		{
			int value3;
			int value2 = ((jsonObject["l1jGroundGfx"] is JsonValue jsonValue && jsonValue.TryGetValue<int>(out value3) && value3 > 0) ? value3 : 19);
			string text = $"res://assets/icons/ground/{value2}.png";
			if (ResourceLoader.Exists(text))
			{
				return text;
			}
			string text2 = $"res://assets/icons/ground/{19}.png";
			if (!ResourceLoader.Exists(text2))
			{
				return null;
			}
			return text2;
		}
		string text3 = jsonObject["img"]?.GetValue<string>();
		if (!string.IsNullOrWhiteSpace(text3))
		{
			return "res://" + text3.Replace('\\', '/').TrimStart('/');
		}
		string text4 = jsonObject["n"]?.GetValue<string>();
		string text5 = jsonObject["iconStem"]?.GetValue<string>();
		string text6 = jsonObject["type"]?.GetValue<string>();
		if (string.IsNullOrWhiteSpace(text4))
		{
			return null;
		}
		string value4 = text6 switch
		{
			"wpn" => "weapons", 
			"arm" => "armors", 
			"acc" => "accessories", 
			_ => "items", 
		};
		return $"res://assets/icons/{value4}/{SafeFileStem(string.IsNullOrWhiteSpace(text5) ? text4 : text5)}.png";
	}

	internal static string SafeFileStem(string name)
	{
		char[] obj = new char[6] { '<', '>', '"', '|', '?', '*' };
		string text = name.Replace(':', '：').Replace('/', '／').Replace('\\', '＼');
		char[] array = obj;
		foreach (char oldChar in array)
		{
			text = text.Replace(oldChar, '_');
		}
		text = text.TrimEnd('.', ' ');
		if (text.Length <= 0)
		{
			return "_";
		}
		return text;
	}
}
