using UnityEngine.UIElements;

namespace HB.Cheat
{
    /// <summary>
    /// 런타임 옵션. 에디터 설정(HB Cheat 창)의 값이 생성 레지스트리를 통해 SubsystemRegistration에서 채워진다.
    /// 코드에서 Initialize 전에 직접 바꿔도 된다.
    /// (빌드 제외를 위해 Resources 에셋을 쓰지 않는다)
    /// </summary>
    public sealed class CheatRuntimeOptions
    {
        public CheatInitMode InitMode = CheatInitMode.AutoOnGameStart;

        /// <summary>InGameConsole이 있으면 그 안의 탭으로 붙인다. 없거나 초기화 안 됐으면 독립 패널.</summary>
        public bool PreferConsoleTab = true;

        /// <summary>독립 패널일 때 화면에 여는 버튼을 띄운다 (모바일용).</summary>
        public bool ShowFloatingButton = true;

        /// <summary>PC 열기/닫기 키. Input System은 Key 이름, 구 Input Manager는 KeyCode 이름 (예: F8). 비우면 사용 안 함.</summary>
        public string ToggleKey = "F8";

        /// <summary>
        /// 서버 응답 대기 시간 (초, 최소 0.5). 넘으면 TimedOut이 되고 서버 큐가 멈춘다
        /// (늦은 응답이 도착하거나 ResumeServerQueue를 부르면 재개).
        /// </summary>
        public float ServerTimeoutSeconds = 10f;

        public int HistoryCapacity = 50;

        /// <summary>false면 씬이 바뀔 때 패널이 함께 파괴되어 종료된다. 다시 쓰려면 CheatSystem.Initialize를 부른다.</summary>
        public bool DontDestroyOnLoad = true;

        /// <summary>
        /// 독립 패널에 쓸 PanelSettings. 프로젝트 UI의 PanelSettings(테마·한글 폰트 포함)를 넣는 것을 권장.
        /// null이면 코드로 만든 기본값 + 내장 폰트를 쓴다.
        /// </summary>
        public PanelSettings PanelSettings;
    }

    /// <summary>
    /// 치트 패널을 다른 UI 안에 넣어주는 호스트 (InGameConsole 연동이 구현).
    /// </summary>
    public interface ICheatPanelHost
    {
        /// <summary>패널을 붙일 수 있으면 붙이고 true. 호스트가 준비되지 않았으면 false (→ 독립 패널).</summary>
        bool TryAttach(VisualElement panel);
        void Open();
        void Close();
        bool IsOpen { get; }

        /// <summary>CheatSystem 종료 시 패널을 떼어 낸다 (콘솔이 더 오래 살아 있어도 패널을 붙잡지 않도록).</summary>
        void Detach();
    }
}
