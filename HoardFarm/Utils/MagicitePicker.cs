using System;

namespace HoardFarm.Utils;

public static class MagicitePicker
{
    /// <summary>held＝深宮導演的三格魔石（值＝DeepDungeonMagicStone 列號，0＝空格）。回要用的格子，沒有就回 null。</summary>
    /// <remarks>依格子順序先用奧汀以外的；只剩奧汀時才用奧汀。</remarks>
    public static int? PickSlot(ReadOnlySpan<byte> held)
    {
        var fallback = -1;
        for (var i = 0; i < held.Length; i++)
        {
            var kind = held[i];
            if (kind == 0) continue;
            if (kind != DataIDs.OdinMagicStoneId) return i;
            if (fallback < 0) fallback = i;
        }

        return fallback >= 0 ? fallback : null;
    }

    public static int HeldCount(ReadOnlySpan<byte> held)
    {
        var n = 0;
        foreach (var kind in held)
            if (kind != 0) n++;
        return n;
    }
}
