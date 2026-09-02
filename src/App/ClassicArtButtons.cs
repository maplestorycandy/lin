using System;
using Godot;

namespace IdleLineage.App;

internal static class ClassicArtButtons
{
	private const string ConfirmPath = "res://assets/ui/buttons/confirm.png";

	private const string CancelPath = "res://assets/ui/buttons/cancel.png";

	private const float NativeWidth = 75f;

	private const float NativeConfirmHeight = 18f;

	private const float NativeCancelHeight = 17f;

	private const float Scale = 2f;

	public static readonly Vector2 ConfirmSize = new Vector2(150f, 36f);

	public static readonly Vector2 CancelSize = new Vector2(150f, 34f);

	public static TextureButton Confirm(Action pressed, string tooltip = "確認")
	{
		return Make("res://assets/ui/buttons/confirm.png", ConfirmSize, pressed, tooltip);
	}

	public static TextureButton Cancel(Action pressed, string tooltip = "取消")
	{
		return Make("res://assets/ui/buttons/cancel.png", CancelSize, pressed, tooltip);
	}

	private static TextureButton Make(string path, Vector2 size, Action pressed, string tooltip)
	{
		TextureButton button = new TextureButton
		{
			TextureNormal = GD.Load<Texture2D>(path),
			IgnoreTextureSize = true,
			StretchMode = TextureButton.StretchModeEnum.Scale,
			TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
			Size = size,
			CustomMinimumSize = size,
			SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			FocusMode = Control.FocusModeEnum.None,
			TooltipText = tooltip
		};
		button.MouseEntered += delegate
		{
			button.Modulate = new Color(1.18f, 1.18f, 1.18f);
		};
		button.MouseExited += delegate
		{
			button.Modulate = Colors.White;
		};
		button.ButtonDown += delegate
		{
			button.Modulate = new Color(0.78f, 0.78f, 0.78f);
		};
		button.ButtonUp += delegate
		{
			button.Modulate = Colors.White;
		};
		button.Pressed += delegate
		{
			GameAudio.Instance?.PlayUi("inventoryAction", 40.0, 0.45f);
			pressed();
		};
		return button;
	}
}
