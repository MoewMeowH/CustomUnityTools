using UnityEngine;

namespace HB.CustomBuild.Editor
{
    /// <summary>
    /// SDK 하나가 요구하는 Xcode 설정 묶음 (Info.plist 키 · Capabilities · Build Settings · 프레임워크 · 파일).
    /// 공용 HBXcodeSettings의 Fragments 목록에 넣고 켜고 끕니다. SDK를 빼면 묶음만 끄면 됩니다.
    /// </summary>
    [CreateAssetMenu(fileName = "XcodeFragment_New", menuName = "HB_Build/Xcode Fragment (SDK)", order = 61)]
    public class HBXcodeFragment : ScriptableObject
    {
        [Tooltip("어떤 SDK · 버전의 문서를 따랐는지 (SDK를 올릴 때 비교용)")]
        [TextArea(1, 4)]
        [SerializeField] public string Note = string.Empty;

        [SerializeField] public XcodeSettingsData Data = new XcodeSettingsData();
    }
}
