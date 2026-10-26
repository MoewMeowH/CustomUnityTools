using System;
using System.IO;

namespace HB.TestFrameworkHelper.Editor
{
    /// <summary>
    /// OS 임시 폴더 아래에 <c>HBTest_&lt;guid&gt;</c> 폴더를 만들고 Dispose에서 지웁니다.
    /// <para>읽기 전용 파일 · 폴더도 지웁니다. 폴더 안의 링크(심볼릭 링크 · 정션)는 따라가지 않고 링크만 지웁니다.
    /// 열린 파일 핸들은 풀지 않으므로 그런 파일이 있으면 예외를 던집니다 (TestScope 안이면 나머지 정리는 계속됩니다).</para>
    /// <para>하드 링크로 넣은 읽기 전용 파일은 읽기 전용을 풀면 원본 파일에도 그대로 남습니다. OS 임시 폴더는 공유될 수 있으니(Linux /tmp 등) 비밀 값이 든 파일은 넣지 마세요.</para>
    /// </summary>
    public sealed class TempDirectory : IDisposable
    {
        private bool m_Disposed;

        public string FullPath { get; }

        public TempDirectory()
        {
            FullPath = Path.Combine(Path.GetTempPath(), "HBTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(FullPath);
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            if (Directory.Exists(FullPath)) DeleteTree(new DirectoryInfo(FullPath));
        }

        private static void DeleteTree(DirectoryInfo dir)
        {
            ClearReadOnly(dir);
            foreach (FileInfo file in dir.GetFiles())
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0) ClearReadOnly(file);
                file.Delete();
            }
            foreach (DirectoryInfo sub in dir.GetDirectories())
            {
                // 링크는 대상 안으로 들어가지 않고 링크 자체만 지운다
                if ((sub.Attributes & FileAttributes.ReparsePoint) != 0) sub.Delete(false);
                else DeleteTree(sub);
            }
            dir.Delete(false);
        }

        private static void ClearReadOnly(FileSystemInfo info)
        {
            if ((info.Attributes & FileAttributes.ReadOnly) != 0) info.Attributes &= ~FileAttributes.ReadOnly;
        }
    }
}
