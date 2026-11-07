namespace HB.TestFrameworkHelper.Samples
{
    /// <summary>
    /// 게임 전역 설정을 흉내 낸 정적 값입니다.
    /// 테스트가 바꾸고 되돌리지 않으면 다음 테스트로 값이 샙니다 → 테스트에서는 <c>Scope.Preserve</c>로 되돌립니다.
    /// </summary>
    public static class ExampleGameSettings
    {
        public static float GameSpeed = 1f;
        public static string PlayerName = "Player";
    }
}
