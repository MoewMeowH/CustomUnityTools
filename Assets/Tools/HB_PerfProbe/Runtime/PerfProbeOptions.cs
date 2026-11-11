using System;

namespace HB.PerfProbe
{
    /// <summary>Probe 설정. <see cref="PerfProbe.Start"/>에 넘긴다. 기본값은 PC 스크립트 기본값과 맞춰 둔다.</summary>
    [Serializable]
    public sealed class PerfProbeOptions
    {
        /// <summary>명령 소켓 포트 (127.0.0.1). 0이면 빈 포트.</summary>
        public int Port = 34999;

        /// <summary>요약 한 줄을 내보내는 간격 (초).</summary>
        public float IntervalSeconds = 1f;

        /// <summary>캡처에 담을 최근 프레임 구간 (초).</summary>
        public float RingSeconds = 30f;

        /// <summary>프레임 링 버퍼 크기. RingSeconds × 최대 FPS보다 크게.</summary>
        public int FrameCapacity = 4096;

        /// <summary>캡처에 담을 최근 요약 개수 (간격 1초면 10분).</summary>
        public int SummaryCapacity = 600;

        /// <summary>캡처에 담을 최근 로그 줄 수.</summary>
        public int LogLines = 300;

        /// <summary>기기에 남겨 둘 캡처 폴더 상한. 넘으면 오래된 것부터 지운다.</summary>
        public int MaxCaptures = 20;

        /// <summary>캡처 폴더 위치. 비우면 Application.persistentDataPath/HBProbe.</summary>
        public string CaptureRoot;

        /// <summary>요약 줄을 Android logcat(태그 HBProbe)에 쓴다. Android 플레이어에서만 동작한다.</summary>
        public bool WriteToLogcat = true;
    }
}
