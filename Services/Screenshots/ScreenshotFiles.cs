namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>Finding the file an old screenshot path points at, safely: only ever inside the Screenshots folder.</summary>
    public static class ScreenshotFiles
    {
        /// <summary>A path as a comparable key: forward slashes, no leading slash (older research paths have both forms).</summary>
        public static string Key(string path) => path.Trim().Replace('\\', '/').TrimStart('/');

        /// <summary>
        /// The full path of the file a database path points at, or null when it doesn't lead to a file strictly
        /// inside the Screenshots folder (an empty path, a rooted one, one with "." or ".." in it).
        /// </summary>
        public static string? ResolveFile(string webRootPath, string path)
        {
            string relative = Key(path);
            if (relative.Length == 0 || relative.Split('/').Any(part => part is "." or "..")) return null;

            char separator = Path.DirectorySeparatorChar;
            string root = Path.GetFullPath(ScreenshotStorage.GetRoot(webRootPath)).TrimEnd(separator, Path.AltDirectorySeparatorChar) + separator;
            string full = Path.GetFullPath(Path.Combine(webRootPath, relative.Replace('/', separator)));

            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(root, comparison) && full.Length > root.Length ? full : null;
        }
    }
}
