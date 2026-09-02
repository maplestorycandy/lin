using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace IdleLineage.Combat;

public sealed record NpcActionEffect
{
	public required string Kind { get; init; }

	public string? QuestId { get; init; }

	public int QuestStep { get; init; }

	public string? IfQuestId { get; init; }

	public int? IfQuestStep { get; init; }

	public string? HtmlId { get; init; }

	public int X { get; init; }

	public int Y { get; init; }

	public int MapId { get; init; }

	public int Heading { get; init; }

	public int Price { get; init; }

	[CompilerGenerated]
	[SetsRequiredMembers]
	private NpcActionEffect(NpcActionEffect original)
	{
		Kind = original.Kind;
		QuestId = original.QuestId;
		QuestStep = original.QuestStep;
		IfQuestId = original.IfQuestId;
		IfQuestStep = original.IfQuestStep;
		HtmlId = original.HtmlId;
		X = original.X;
		Y = original.Y;
		MapId = original.MapId;
		Heading = original.Heading;
		Price = original.Price;
	}
}
