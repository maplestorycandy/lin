using System;

namespace IdleLineage.Combat;

/// <summary>
/// 遊戲全域倍率設定中心（可隨時調整）
/// </summary>
public static class GameRateConfig
{
    /// <summary>
    /// 全域經驗值倍率（例如 10.0 = 10倍經驗，100.0 = 100倍經驗）
    /// </summary>
    public static double GlobalExpRate = 10.0;

    /// <summary>
    /// 全域金幣（金幣量）掉落倍率（例如 10.0 = 每次掉落金額 x10）
    /// </summary>
    public static double GlobalGoldAmountRate = 10.0;

    /// <summary>
    /// 全域金幣掉落機率倍率（例如 5.0 = 掉落機率提高 5 倍，最高 100% 必掉）
    /// </summary>
    public static double GlobalGoldChanceRate = 5.0;

    /// <summary>
    /// 全域道具/裝備掉寶機率倍率（例如 3.0 = 掉寶率提高 3 倍）
    /// </summary>
    public static double GlobalItemDropRate = 3.0;

    /// <summary>
    /// 是否關閉負重限制（true = 無限負重，負重永遠為 0%，自然回血施法不受限）
    /// </summary>
    public static bool DisableWeightPenalty = true;

    /// <summary>
    /// 是否全地圖永晝/視野全開（true = 關閉黑夜遮罩，全螢幕明亮清晰；false = 原版天堂日夜交替與燈籠系統）
    /// </summary>
    public static bool AlwaysDaylight = true;
}
