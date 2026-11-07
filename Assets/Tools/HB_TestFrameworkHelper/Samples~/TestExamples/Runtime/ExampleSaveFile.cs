using System.IO;

namespace HB.TestFrameworkHelper.Samples
{
    /// <summary>폴더에 세이브 파일을 쓰고 읽습니다. 테스트에서는 실제 저장 폴더 대신 임시 폴더를 넘깁니다.</summary>
    public static class ExampleSaveFile
    {
        public const string FileName = "save.txt";

        /// <summary>저장한 파일 경로를 돌려줍니다.</summary>
        public static string Save(string directory, string playerName, int level)
        {
            string path = Path.Combine(directory, FileName);
            File.WriteAllText(path, playerName + "\n" + level);
            return path;
        }

        public static bool TryLoad(string directory, out string playerName, out int level)
        {
            playerName = null;
            level = 0;

            string path = Path.Combine(directory, FileName);
            if (!File.Exists(path)) return false;

            string[] lines = File.ReadAllLines(path);
            if (lines.Length < 2 || !int.TryParse(lines[1], out level)) return false;
            playerName = lines[0];
            return true;
        }
    }
}
