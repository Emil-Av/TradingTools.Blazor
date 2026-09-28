using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Npgsql;

namespace TradingTools.Blazor.Services.DataBackup
{
    public interface IDatabaseBackup
    {
        /// <summary>Writes a pg_dump of the database (custom format) to <paramref name="outputFile"/>.</summary>
        Task DumpAsync(string outputFile, CancellationToken cancellationToken = default);

        /// <summary>
        /// Overwrites the database with <paramref name="dumpFile"/>: a custom-format dump (pg_restore) or a
        /// plain SQL dump (psql). Runs in a single transaction, so a failed import leaves the database as it was.
        /// </summary>
        Task RestoreAsync(string dumpFile, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Database export/import through the PostgreSQL client tools (pg_dump, pg_restore, psql).
    /// Where the tools are found: the "Backup:PostgresBinPath" setting if set; otherwise, on Windows,
    /// the newest C:\Program Files\PostgreSQL\{version}\bin; otherwise the PATH (the usual case on Linux).
    /// </summary>
    public sealed class PostgresDatabaseBackup(IConfiguration configuration) : IDatabaseBackup
    {
        public const string BinPathSetting = "Backup:PostgresBinPath";

        /// <summary>Every custom-format pg_dump file starts with these bytes.</summary>
        private static readonly byte[] CustomFormatSignature = "PGDMP"u8.ToArray();

        private NpgsqlConnectionStringBuilder Connection => new(
            configuration.GetConnectionString("PostgreSqlConnection")
            ?? throw new InvalidOperationException("PostgreSqlConnection string is missing."));

        private string? ConfiguredBinPath => configuration[BinPathSetting];

        public async Task DumpAsync(string outputFile, CancellationToken cancellationToken = default)
        {
            var connection = Connection;
            await RunAsync(ResolveTool("pg_dump", ConfiguredBinPath), DumpArguments(connection, outputFile), connection.Password, cancellationToken);
        }

        /// <summary>
        /// Empties the database's public schema and runs the dump in one psql transaction. Not
        /// pg_restore --clean: that only drops what the dump itself contains, so a table added since the
        /// dump was made (e.g. TradeAddOns in a pre-add-on dump) stays and blocks dropping the tables it
        /// references. A custom-format dump is first turned into SQL by pg_restore (no database needed).
        /// </summary>
        public async Task RestoreAsync(string dumpFile, CancellationToken cancellationToken = default)
        {
            var connection = Connection;
            string resetFile = Path.GetTempFileName();
            string? convertedFile = null;
            try
            {
                string sqlFile = dumpFile;
                if (IsCustomFormat(dumpFile))
                {
                    convertedFile = Path.GetTempFileName();
                    await RunAsync(ResolveTool("pg_restore", ConfiguredBinPath), ToSqlArguments(dumpFile, convertedFile), password: null, cancellationToken);
                    sqlFile = convertedFile;
                }

                await File.WriteAllTextAsync(resetFile, ResetSchemaSql, cancellationToken);
                await RunAsync(ResolveTool("psql", ConfiguredBinPath), RestoreArguments(connection, resetFile, sqlFile), connection.Password, cancellationToken);
            }
            finally
            {
                File.Delete(resetFile);
                if (convertedFile is not null) File.Delete(convertedFile);
            }
        }

        /// <summary>Runs first, inside the restore's transaction: if the dump fails, the drop is rolled back too.</summary>
        internal const string ResetSchemaSql =
            """
            SET client_min_messages = warning;
            DROP SCHEMA IF EXISTS public CASCADE;
            CREATE SCHEMA public;
            """;

        #region Arguments (the password is never one of them - it goes in PGPASSWORD)

        internal static List<string> DumpArguments(NpgsqlConnectionStringBuilder connection, string outputFile) =>
        [
            "--format=custom", "--no-owner", "--no-privileges",
            .. ConnectionArguments(connection),
            "--file", outputFile,
        ];

        /// <summary>A custom-format dump as a plain SQL script. Without --dbname pg_restore only writes the script.</summary>
        internal static List<string> ToSqlArguments(string dumpFile, string sqlFile) =>
        [
            "--no-owner", "--no-privileges",
            "--file", sqlFile,
            dumpFile,
        ];

        /// <summary>
        /// The reset script, then the dump. --single-transaction wraps every --file in one transaction and
        /// ON_ERROR_STOP aborts it on the first error: all or nothing.
        /// </summary>
        internal static List<string> RestoreArguments(NpgsqlConnectionStringBuilder connection, string resetFile, string sqlFile) =>
        [
            "--single-transaction", "--set", "ON_ERROR_STOP=1", "--quiet",
            .. ConnectionArguments(connection),
            "--file", resetFile,
            "--file", sqlFile,
        ];

        private static IEnumerable<string> ConnectionArguments(NpgsqlConnectionStringBuilder connection) =>
        [
            "--no-password",
            "--host", connection.Host ?? "localhost",
            "--port", connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--username", connection.Username ?? "postgres",
            "--dbname", connection.Database ?? throw new InvalidOperationException("The connection string has no Database."),
        ];

        #endregion

        internal static bool IsCustomFormat(string file)
        {
            using var stream = File.OpenRead(file);
            Span<byte> header = stackalloc byte[CustomFormatSignature.Length];
            return stream.Read(header) == header.Length && header.SequenceEqual(CustomFormatSignature);
        }

        /// <summary>Full path (or bare name, to be found on the PATH) of a PostgreSQL client tool.</summary>
        internal static string ResolveTool(string tool, string? configuredBinPath, string? windowsInstallRoot = null)
        {
            string executable = OperatingSystem.IsWindows() ? tool + ".exe" : tool;

            if (!string.IsNullOrWhiteSpace(configuredBinPath))
            {
                string configured = Path.Combine(configuredBinPath, executable);
                return File.Exists(configured)
                    ? configured
                    : throw new InvalidOperationException($"{executable} was not found in {configuredBinPath} (setting {BinPathSetting}).");
            }

            if (OperatingSystem.IsWindows())
            {
                string root = windowsInstallRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL");
                if (Directory.Exists(root))
                {
                    string? newest = Directory.GetDirectories(root)
                        .Select(dir => (Dir: dir, Version: decimal.TryParse(Path.GetFileName(dir), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : -1))
                        .Where(x => x.Version >= 0 && File.Exists(Path.Combine(x.Dir, "bin", executable)))
                        .OrderByDescending(x => x.Version)
                        .Select(x => Path.Combine(x.Dir, "bin", executable))
                        .FirstOrDefault();
                    if (newest is not null) return newest;
                }
            }

            return executable;
        }

        private static async Task RunAsync(string tool, IReadOnlyList<string> arguments, string? password, CancellationToken cancellationToken)
        {
            var startInfo = new ProcessStartInfo(tool)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                StandardOutputEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
            if (!string.IsNullOrEmpty(password)) startInfo.Environment["PGPASSWORD"] = password;

            // Untranslated (English, ASCII) messages: otherwise they follow the OS language - and on
            // Windows its code page - and come out garbled when read as UTF-8.
            startInfo.Environment["LC_ALL"] = "C";
            startInfo.Environment["LC_MESSAGES"] = "C";
            startInfo.Environment["LANG"] = "C";

            Process process;
            try
            {
                process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{tool} could not be started.");
            }
            catch (Win32Exception ex)
            {
                throw new InvalidOperationException(
                    $"{Path.GetFileName(tool)} was not found ({ex.Message}). Install the PostgreSQL client tools, or set {BinPathSetting} to their bin folder.", ex);
            }

            using (process)
            {
                var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var errors = process.StandardError.ReadToEndAsync(cancellationToken);
                try
                {
                    await process.WaitForExitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    process.Kill(entireProcessTree: true);
                    throw;
                }

                if (process.ExitCode != 0)
                {
                    string message = (await errors).Trim();
                    if (message.Length == 0) message = (await output).Trim();
                    throw new InvalidOperationException($"{Path.GetFileName(tool)} failed (exit code {process.ExitCode}): {message}");
                }
            }
        }
    }
}
