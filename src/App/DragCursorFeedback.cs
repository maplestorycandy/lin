using Godot;

namespace IdleLineage.App;

internal static class DragCursorFeedback
{
	public static void Begin()
	{
		Input.SetDefaultCursorShape(Input.CursorShape.Drag);
	}

	public static void End()
	{
		Input.SetDefaultCursorShape(Input.CursorShape.Arrow);
	}
}
