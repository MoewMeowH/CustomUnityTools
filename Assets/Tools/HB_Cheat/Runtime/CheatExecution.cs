using System.Text;

namespace HB.Cheat
{
    /// <summary>
    /// 치트 실행 한 건. Local은 즉시 Succeeded/Failed, Server는 Pending → 응답 시 Succeeded/Failed, 시간 초과 시 TimedOut.
    /// </summary>
    public sealed class CheatExecution
    {
        public int Id { get; }

        /// <summary>Raw 실행이면 null</summary>
        public CheatDefinition Definition { get; }

        public object[] Values { get; }

        /// <summary>서버로 보낸 명령 문자열 (Local이면 null)</summary>
        public string Command { get; internal set; }

        public CheatStatus Status { get; internal set; }
        public string Message { get; internal set; }
        public float StartedAt { get; }

        /// <summary>서버로 실제 보낸 시각 (앞 요청을 기다렸다면 StartedAt보다 늦다)</summary>
        public float SentAt { get; internal set; }
        public float FinishedAt { get; internal set; }

        public bool IsDone => Status != CheatStatus.Pending;
        public bool IsServer => Definition == null || Definition.Target == CheatTarget.Server;

        internal CheatExecution(int id, CheatDefinition definition, object[] values, float startedAt)
        {
            Id = id;
            Definition = definition;
            Values = values;
            StartedAt = startedAt;
            Status = CheatStatus.Pending;
        }

        /// <summary>"Item/Enchant itemId=1001 level=15" 또는 Raw 명령</summary>
        public string Describe()
        {
            if (Definition == null) return Command ?? string.Empty;

            var sb = new StringBuilder(Definition.Path);
            for (int i = 0; i < Definition.Args.Length && i < Values.Length; i++)
            {
                sb.Append(' ').Append(Definition.Args[i].Name).Append('=').Append(CheatValueParser.ToText(Values[i]));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 서버 치트 전송. 프로젝트에서 구현해 CheatSystem.ServerTransport에 등록한다 (예: C2P_RequestCheat 패킷 전송).
    /// 서버 응답을 받으면 CheatSystem.ReportServerResult를 호출한다 (네트워크 스레드에서 불러도 다음 프레임에 메인 스레드에서 처리).
    /// 시간 초과가 나면 서버 큐가 멈춘다. 재접속 처리에서 CheatSystem.ResumeServerQueue를 부르면 재개된다.
    /// </summary>
    public interface ICheatServerTransport
    {
        /// <summary>
        /// 전송 시작에 실패하면 false와 이유를 반환한다 (미접속 등).
        /// 한 번에 하나만 호출된다 (응답 또는 시간 초과 후 다음 건). 이 안에서 ReportServerResult를 바로 호출해도 된다.
        /// </summary>
        bool TrySend(string command, out string error);
    }

    /// <summary>
    /// 서버 없이 확인용: 명령을 로그로만 남긴다. 응답은 CheatSystem.ReportServerResult로 직접 넣는다.
    /// </summary>
    public sealed class CheatLogTransport : ICheatServerTransport
    {
        public bool TrySend(string command, out string error)
        {
            UnityEngine.Debug.Log($"[HB_Cheat] (log transport) {command}");
            error = null;
            return true;
        }
    }

    /// <summary>
    /// 외부 자동화 통로 (ADB, 로컬 소켓 등). 받은 줄을 CheatProtocol.Handle에 넘기고 결과 JSON을 돌려주면 된다.
    /// CheatSystem.RegisterBridge로 등록하면 Initialize 시 Start, Shutdown 시 Stop이 호출된다.
    /// </summary>
    public interface ICheatBridge
    {
        string Name { get; }
        void Start();
        void Stop();
    }
}
