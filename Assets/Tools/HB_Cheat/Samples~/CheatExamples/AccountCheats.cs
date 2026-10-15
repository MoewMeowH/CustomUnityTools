#if DEV_CHEAT
using HB.Cheat;

namespace CheatExamples
{
    /// <summary>
    /// 서버 치트 예제 (재화·우편). 탭: Account > Currency / Mail
    /// </summary>
    public static class AccountCheats
    {
        // enum + long (큰 수량)
        [Cheat("account.currency.add", "Account/Currency/Add", Target = CheatTarget.Server)]
        public static string AddCurrency(
            [CheatArg("재화")] CurrencyType type,
            [CheatArg("수량"), CheatRange(1, 1_000_000_000, Step = 1000)] long amount = 10000)
        {
            return CheatCommand.Join("currency_add", type, amount);   // "currency_add 1 10000"
        }

        [Cheat("account.currency.reset", "Account/Currency/Reset", Target = CheatTarget.Server, Description = "모든 재화 0으로")]
        public static string ResetCurrency() => "currency_reset";

        // 문자열 인자 (OS 키보드로 입력). 공백이 있으면 CheatCommand가 "..."로 감싼다
        [Cheat("account.mail.send", "Account/Mail/Send", Target = CheatTarget.Server)]
        public static string SendMail(
            [CheatArg("제목")] string title = "운영자 선물",
            [CheatArg("아이템"), CheatOptions(typeof(ItemProvider))] int itemId = 3001,
            [CheatArg("개수"), CheatRange(1, 999)] int count = 1)
        {
            return CheatCommand.Join("mail_send", title, itemId, count);   // mail_send "운영자 선물" 3001 1
        }
    }
}
#endif
