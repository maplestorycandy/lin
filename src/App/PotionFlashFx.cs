using System;
using System.Collections.Generic;
using Godot;

namespace IdleLineage.App;

public static class PotionFlashFx
{
	private const double FramesPerSecond = 12.0;

	private const string AnimationName = "flash";

	private static readonly IReadOnlyDictionary<string, string> FlashByItem = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		["potion_heal"] = "heal",
		["potion_strong"] = "strong",
		["potion_ult"] = "ult",
		["potion_haste"] = "haste",
		["potion_brave"] = "brave",
		["new_item_140"] = "brave"
	};

	private static readonly Dictionary<string, SpriteFrames?> Cache = new Dictionary<string, SpriteFrames>(StringComparer.Ordinal);

	public static void TryPlay(Node2D parent, string itemKey, Func<Vector2> follow)
	{
		ArgumentNullException.ThrowIfNull(parent, "parent");
		ArgumentNullException.ThrowIfNull(follow, "follow");
		if (!string.IsNullOrEmpty(itemKey) && FlashByItem.TryGetValue(itemKey, out string value))
		{
			SpriteFrames spriteFrames = BuildFrames(value);
			if (spriteFrames != null)
			{
				PotionFlashNode potionFlashNode = new PotionFlashNode();
				parent.AddChild(potionFlashNode, forceReadableName: false, Node.InternalMode.Disabled);
				potionFlashNode.Start(spriteFrames, follow);
			}
		}
	}

	private static SpriteFrames? BuildFrames(string prefix)
	{
		if (Cache.TryGetValue(prefix, out SpriteFrames value))
		{
			return value;
		}
		SpriteFrames spriteFrames = new SpriteFrames();
		spriteFrames.AddAnimation("flash");
		spriteFrames.SetAnimationLoopMode("flash", SpriteFrames.LoopMode.None);
		spriteFrames.SetAnimationSpeed("flash", 12.0);
		int num = 0;
		for (int i = 0; i < 6; i++)
		{
			string path = $"res://assets/effects/potionfx/{prefix}_{i}.png";
			if (!ResourceLoader.Exists(path))
			{
				break;
			}
			spriteFrames.AddFrame("flash", GD.Load<Texture2D>(path));
			num++;
		}
		SpriteFrames spriteFrames2 = ((num > 0) ? spriteFrames : null);
		Cache[prefix] = spriteFrames2;
		return spriteFrames2;
	}
}
