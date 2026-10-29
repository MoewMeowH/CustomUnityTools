using System;

namespace HB.E2String.Benchmarks
{
    /// <summary>연속 값 100개 (switch 점프 테이블)</summary>
    [E2String(E2StringOptions.All)]
    public enum BenchEnum100
    {
        Item000, Item001, Item002, Item003, Item004, Item005, Item006, Item007, Item008, Item009,
        Item010, Item011, Item012, Item013, Item014, Item015, Item016, Item017, Item018, Item019,
        Item020, Item021, Item022, Item023, Item024, Item025, Item026, Item027, Item028, Item029,
        Item030, Item031, Item032, Item033, Item034, Item035, Item036, Item037, Item038, Item039,
        Item040, Item041, Item042, Item043, Item044, Item045, Item046, Item047, Item048, Item049,
        Item050, Item051, Item052, Item053, Item054, Item055, Item056, Item057, Item058, Item059,
        Item060, Item061, Item062, Item063, Item064, Item065, Item066, Item067, Item068, Item069,
        Item070, Item071, Item072, Item073, Item074, Item075, Item076, Item077, Item078, Item079,
        Item080, Item081, Item082, Item083, Item084, Item085, Item086, Item087, Item088, Item089,
        Item090, Item091, Item092, Item093, Item094, Item095, Item096, Item097, Item098, Item099,
    }

    /// <summary>띄엄띄엄한 값 100개 (switch 이진 탐색)</summary>
    [E2String(E2StringOptions.All)]
    public enum BenchSparse100
    {
        Item000 = 7, Item001 = 1016, Item002 = 2025, Item003 = 3034, Item004 = 4043,
        Item005 = 5052, Item006 = 6061, Item007 = 7070, Item008 = 8079, Item009 = 9088,
        Item010 = 10097, Item011 = 11106, Item012 = 12115, Item013 = 13124, Item014 = 14133,
        Item015 = 15142, Item016 = 16151, Item017 = 17160, Item018 = 18169, Item019 = 19178,
        Item020 = 20187, Item021 = 21196, Item022 = 22205, Item023 = 23214, Item024 = 24223,
        Item025 = 25232, Item026 = 26241, Item027 = 27250, Item028 = 28259, Item029 = 29268,
        Item030 = 30277, Item031 = 31286, Item032 = 32295, Item033 = 33304, Item034 = 34313,
        Item035 = 35322, Item036 = 36331, Item037 = 37340, Item038 = 38349, Item039 = 39358,
        Item040 = 40367, Item041 = 41376, Item042 = 42385, Item043 = 43394, Item044 = 44403,
        Item045 = 45412, Item046 = 46421, Item047 = 47430, Item048 = 48439, Item049 = 49448,
        Item050 = 50457, Item051 = 51466, Item052 = 52475, Item053 = 53484, Item054 = 54493,
        Item055 = 55502, Item056 = 56511, Item057 = 57520, Item058 = 58529, Item059 = 59538,
        Item060 = 60547, Item061 = 61556, Item062 = 62565, Item063 = 63574, Item064 = 64583,
        Item065 = 65592, Item066 = 66601, Item067 = 67610, Item068 = 68619, Item069 = 69628,
        Item070 = 70637, Item071 = 71646, Item072 = 72655, Item073 = 73664, Item074 = 74673,
        Item075 = 75682, Item076 = 76691, Item077 = 77700, Item078 = 78709, Item079 = 79718,
        Item080 = 80727, Item081 = 81736, Item082 = 82745, Item083 = 83754, Item084 = 84763,
        Item085 = 85772, Item086 = 86781, Item087 = 87790, Item088 = 88799, Item089 = 89808,
        Item090 = 90817, Item091 = 91826, Item092 = 92835, Item093 = 93844, Item094 = 94853,
        Item095 = 95862, Item096 = 96871, Item097 = 97880, Item098 = 98889, Item099 = 99898,
    }

    /// <summary>실무에서 흔한 작은 enum</summary>
    [E2String(E2StringOptions.All)]
    public enum BenchEnum8
    {
        Idle, Walk, Run, Jump, Fall, Attack, Hit, Die,
    }

    [Flags, E2String(E2StringOptions.All)]
    public enum BenchFlags8
    {
        None = 0, A = 1, B = 2, C = 4, D = 8, E = 16, F = 32, G = 64, H = 128,
    }
}
