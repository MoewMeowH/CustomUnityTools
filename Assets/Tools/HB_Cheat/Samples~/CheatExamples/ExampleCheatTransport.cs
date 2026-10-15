#if DEV_CHEAT
using HB.Cheat;
using UnityEngine;

namespace CheatExamples
{
    /// <summary>
    /// 서버 치트 전송 연결 예제: CheatSystem이 만든 문자열을 C2P_RequestCheat로 보내고,
    /// P2C_ResultCheat를 받으면 ReportServerResult로 결과를 알려준다.
    /// </summary>
    public sealed class ExampleCheatTransport : ICheatServerTransport
    {
        public bool TrySend(string command, out string error)
        {
            if (!ExampleNetwork.IsConnected)
            {
                error = "서버에 연결되지 않음";
                return false;
            }

            ExampleNetwork.Send(new C2P_RequestCheat { Command = command });
            error = null;
            return true;
        }

        private static void OnResultCheat(P2C_ResultCheat packet)
        {
            // 응답에 요청 식별자가 없어도 된다: CheatSystem이 한 번에 하나만 보내므로 지금 기다리는 건에 대응된다
            CheatSystem.ReportServerResult(packet.Result == CheatResultCode.Success, packet.Result.ToString());
        }

        /// <summary>게임 시작 시 한 번 등록 (실제 프로젝트에서는 네트워크 초기화 코드에서 하면 된다)</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            // 샘플이므로 프로젝트가 이미 등록한 전송은 덮어쓰지 않는다 (실제 프로젝트에서는 바로 대입하면 된다)
            if (CheatSystem.ServerTransport != null) return;
            CheatSystem.ServerTransport = new ExampleCheatTransport();

            ExampleNetwork.OnResultCheat -= OnResultCheat;
            ExampleNetwork.OnResultCheat += OnResultCheat;

            // 독립 패널에 프로젝트 UI 테마·한글 폰트를 쓰고 싶다면:
            // CheatSystem.Options.PanelSettings = Resources.Load<PanelSettings>("UI/GamePanelSettings");
        }
    }
}
#endif
