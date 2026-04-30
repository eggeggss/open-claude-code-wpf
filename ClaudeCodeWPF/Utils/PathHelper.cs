using System;
using System.IO;

namespace OpenClaudeCodeWPF.Utils
{
    /// <summary>
    /// 跨工具共用的路徑工具：處理 ~ 展開與相對路徑解析。
    /// 所有 Tool 的 ResolvePath 都應委派給此類別。
    /// </summary>
    public static class PathHelper
    {
        private static readonly string UserHome =
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>
        /// 展開 ~ 並將相對路徑解析為絕對路徑。
        /// <list type="bullet">
        ///   <item>~ 或 ~/ 或 ~\ → %USERPROFILE%</item>
        ///   <item>相對路徑 → 基於 <see cref="Environment.CurrentDirectory"/></item>
        /// </list>
        /// </summary>
        public static string Resolve(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Expand ~ (Unix-style home shorthand)
            if (path == "~")
                return UserHome;
            if (path.StartsWith("~/") || path.StartsWith("~\\"))
                path = UserHome + path.Substring(1);

            if (!Path.IsPathRooted(path))
                path = Path.Combine(Environment.CurrentDirectory, path);

            return Path.GetFullPath(path);
        }

        /// <summary>
        /// 展開 workingDir 字串；若空則回傳 <see cref="Environment.CurrentDirectory"/>。
        /// </summary>
        public static string ResolveWorkingDir(string workingDir)
        {
            if (string.IsNullOrWhiteSpace(workingDir))
                return Environment.CurrentDirectory;
            return Resolve(workingDir);
        }
    }
}
