using System.Collections.Generic;
using System.Numerics;

namespace HoardFarm.Utils;

public static class DataIDs
{
    
    //////////// Onokoro IDs ////////////
    public const ushort RubySeaMapId = 613;
    public const uint OnokoroAetherytId = 106;
    public const uint KyuseiDataId = 1025846;
    public static readonly Vector3 KyuseiLocation = new(-2.02948f, 3.0059814f, -611.3528f);
    
    //////////// Limsa IDs ////////////
    public const ushort LimsaMapId = 129;
    public const uint LimsaAetherytId = 8;
    public const uint RetainerBellDataId = 2000401;
    public static readonly Vector3 RetainerBellLocation = new(-124.1676f, 18f, 19.9041f);

    public const ushort HoHMapId1 = 770;
    public const ushort HoHMapId11 = 771;
    public const ushort HoHMapId21 = 772;
    
    public const uint SenseHoardLogMessageId = 7272;
    public const uint NoHoardLogMessageId = 7273;
    public const uint HoardFoundLogMessageId = 7274;

    public const uint AbandonDutyMessageId = 2545;
    public const uint ConfirmPartyKoMessageId = 10483;
    
    public const uint VanishStatusId = 1496;
    public const uint ItemPenaltyStatusId = 1094;
    public const uint MagicitePenaltyStatusId = 1549;
    public const uint StunStatusId = 2;
    public const uint SleepStatusId = 3;

    // DeepDungeonMagicStone 第 4 列（奧汀）：頭目層才有額外價值，找寶藏時留到最後才用。
    public const byte OdinMagicStoneId = 4;
    public const uint AccursedHoardId = 2007542;
    
    // 上游的 3816（三萬個）台服 7.25 還沒有這一列；同系列（發現埋藏的寶藏）台服最高是 3184 的兩萬個。
    public const uint ForTheHoardAchievementId = 3184;
    public const int ForTheHoardAchievementFallbackTarget = 20000;
    
    public static readonly HashSet<uint> ChestIDs = 
        [1036, 1037, 1038, 1039, 1040, 1041, 1042, 1043, 1044, 1045, 1046, 1047, 1048, 1049, 2007357, 2007358];

    public static readonly uint[] ConcealmentChain = [1, 16, 18, 31, 2];
    public static readonly uint[] SafetyChain = [1, 16, 18, 19, 2];
    public static readonly uint[] IntuitionChain = [1, 16, 18, 32, 2];
    
    public static readonly uint[] MagiciteChain = [1, 54, 56, 57, 2];
}
