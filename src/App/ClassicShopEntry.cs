using System;

namespace IdleLineage.App;

internal sealed record ClassicShopEntry(string ItemKey, string Name, string Detail, bool Enabled, string Tooltip, Action Activate);
