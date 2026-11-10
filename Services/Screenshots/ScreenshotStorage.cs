namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// The old Screenshots folder, wwwroot/Screenshots, from before the screenshots moved into the database. Nothing is
    /// written there any more; it is kept as it was (and never served, see Program.cs) and read once, by
    /// <see cref="ScreenshotDatabaseImport"/>. The older "ScreenshotsDev" root is merged into it at startup by
    /// <see cref="LegacyScreenshotMigration"/>. The old paths in the database are relative to wwwroot, with forward
    /// slashes, e.g. "Screenshots/Demo Trading/SRS/15M/Sample Size 3/Trade 7/chart.png".
    /// </summary>
    public static class ScreenshotStorage
    {
        public const string RootFolderName = "Screenshots";
        public const string LegacyRootFolderName = "ScreenshotsDev";

        public static string GetRoot(string webRootPath) => Path.Combine(webRootPath, RootFolderName);
    }
}
