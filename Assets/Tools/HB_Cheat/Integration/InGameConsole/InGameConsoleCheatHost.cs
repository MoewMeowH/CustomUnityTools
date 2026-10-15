using HB.Cheat.UI;
using InGameConsoleSystem;
using UnityEngine;
using UnityEngine.UIElements;

namespace HB.Cheat.Integration
{
    /// <summary>
    /// InGameConsole이 설치돼 있을 때만 컴파일된다 (HB_CHEAT_HAS_CONSOLE).
    /// CheatSystem.Initialize 시점에 콘솔이 준비돼 있으면 콘솔 탭으로 붙고, 아니면 CheatSystem이 독립 패널을 쓴다.
    /// </summary>
    internal sealed class InGameConsoleCheatHost : ICheatPanelHost
    {
        private const string ViewName = "Cheat";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Register()
        {
            CheatSystem.ConsoleHost = new InGameConsoleCheatHost();
        }

        public bool TryAttach(VisualElement panel)
        {
            if (!InGameConsole.IsInitialized)
            {
                // 콘솔과 치트 모두 AfterSceneLoad에서 자동 초기화되며 순서가 정해져 있지 않다.
                // 콘솔이 자동 시작 설정이면 지금 초기화해도 결과가 같으므로 먼저 초기화한다. 수동 초기화 설정이면 건드리지 않는다.
                var settings = InGameConsoleSettings.Instance;
                if (settings == null || settings.InitMode != ConsoleInitMode.AutoOnGameStart) return false;
                InGameConsole.Initialize();
            }

            if (!InGameConsole.AddExternalView(ViewName, panel)) return false;

            if (panel is CheatPanel cheatPanel) cheatPanel.ShowCloseButton = false;
            return true;
        }

        public void Open() => InGameConsole.ShowExternalView(ViewName);

        public void Close() => InGameConsole.Close();

        public bool IsOpen => InGameConsole.IsExternalViewActive(ViewName);

        public void Detach() => InGameConsole.RemoveExternalView(ViewName);
    }
}
