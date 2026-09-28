using System.IO.Compression;
using TradingTools.Blazor.Services.Screenshots;

namespace TradingTools.Blazor.Services.DataBackup
{
    /// <summary>Screenshots export (a zip of wwwroot/Screenshots) and import (extract a zip into it).</summary>
    public static class ScreenshotArchive
    {
        public sealed record ImportResult(int FilesWritten, int EntriesSkipped);

        /// <summary>
        /// Zips everything under <paramref name="screenshotsRoot"/>. Entry names are relative to that folder
        /// ("Demo Trading/SRS/15M/Sample Size 1/Trade 1/chart.png"). Screenshots are already compressed
        /// images, so they're stored without compression - much faster, barely bigger.
        /// </summary>
        public static void CreateZip(string screenshotsRoot, string zipPath)
        {
            if (!Directory.Exists(screenshotsRoot))
            {
                using var _ = ZipFile.Open(zipPath, ZipArchiveMode.Create);
                return;
            }

            ZipFile.CreateFromDirectory(screenshotsRoot, zipPath, CompressionLevel.NoCompression, includeBaseDirectory: false);
        }

        /// <summary>
        /// Extracts the zip into <paramref name="screenshotsRoot"/>, overwriting files with the same path.
        /// Accepts zips made by <see cref="CreateZip"/> as well as ones whose entries start with
        /// "Screenshots/", "ScreenshotsDev/" or "wwwroot/Screenshots/". Entries that would land outside the
        /// screenshots folder ("../") are skipped.
        /// </summary>
        /// <exception cref="InvalidDataException">The stream is not a zip file.</exception>
        public static ImportResult ImportZip(Stream zip, string screenshotsRoot)
        {
            string root = Path.GetFullPath(screenshotsRoot);
            string rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            Directory.CreateDirectory(root);
            using var archive = new ZipArchive(zip, ZipArchiveMode.Read);

            int written = 0, skipped = 0;
            foreach (var entry in archive.Entries)
            {
                if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\')) continue; // folder entry

                string? relative = NormalizeEntryPath(entry.FullName);
                if (relative is null)
                {
                    skipped++;
                    continue;
                }

                string destination = Path.GetFullPath(Path.Combine(root, relative));
                if (!destination.StartsWith(rootWithSeparator, comparison))
                {
                    skipped++;
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                entry.ExtractToFile(destination, overwrite: true);
                written++;
            }

            return new ImportResult(written, skipped);
        }

        /// <summary>
        /// The entry's path relative to the screenshots folder, with '/' separators; null for entries that
        /// must not be extracted (parent-folder segments, macOS metadata, nothing left after the prefix).
        /// </summary>
        public static string? NormalizeEntryPath(string entryName)
        {
            var segments = entryName.Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Where(segment => segment != ".")
                .ToList();

            if (segments.Count == 0 || segments.Contains("..") || segments[0] == "__MACOSX") return null;

            if (segments.Count > 1 && segments[0].Equals("wwwroot", StringComparison.OrdinalIgnoreCase))
                segments.RemoveAt(0);

            if (segments.Count > 1 &&
                (segments[0].Equals(ScreenshotStorage.RootFolderName, StringComparison.OrdinalIgnoreCase) ||
                 segments[0].Equals(ScreenshotStorage.LegacyRootFolderName, StringComparison.OrdinalIgnoreCase)))
                segments.RemoveAt(0);

            return string.Join('/', segments);
        }
    }
}
