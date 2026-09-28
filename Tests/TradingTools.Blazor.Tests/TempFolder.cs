namespace TradingTools.Blazor.Tests
{
    /// <summary>A throwaway folder for file system tests, deleted on dispose.</summary>
    internal sealed class TempFolder : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tt-tests-" + Guid.NewGuid().ToString("N"));

        public TempFolder() => Directory.CreateDirectory(Path);

        public string this[string relative] => System.IO.Path.Combine(Path, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        /// <summary>Creates a file (and its folders) with the given content; returns its full path.</summary>
        public string File(string relative, string content = "x")
        {
            string full = this[relative];
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
            System.IO.File.WriteAllText(full, content);
            return full;
        }

        /// <summary>Every file under <paramref name="relativeFolder"/>, as forward-slash paths relative to it.</summary>
        public List<string> Files(string relativeFolder = "") =>
            Directory.Exists(this[relativeFolder])
                ? [.. Directory.EnumerateFiles(this[relativeFolder], "*", SearchOption.AllDirectories)
                    .Select(f => System.IO.Path.GetRelativePath(this[relativeFolder], f).Replace('\\', '/'))
                    .Order()]
                : [];

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* best effort */ }
        }
    }
}
