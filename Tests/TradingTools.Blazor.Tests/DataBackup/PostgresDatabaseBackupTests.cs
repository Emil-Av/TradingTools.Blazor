using Npgsql;
using TradingTools.Blazor.Services.DataBackup;

namespace TradingTools.Blazor.Tests.DataBackup
{
    public class PostgresDatabaseBackupTests : IDisposable
    {
        private const string Password = "S3cret!pw";
        private static readonly NpgsqlConnectionStringBuilder Connection =
            new("Host=db.example;Port=5433;Database=TradingTools;Username=emil;Password=" + Password);

        private readonly TempFolder _temp = new();

        public void Dispose() => _temp.Dispose();

        #region Arguments

        public static TheoryData<string, List<string>> AllArgumentLists => new()
        {
            { "dump", PostgresDatabaseBackup.DumpArguments(Connection, "out.dump") },
            { "restore", PostgresDatabaseBackup.RestoreArguments(Connection, "reset.sql", "in.sql") },
        };

        [Theory]
        [MemberData(nameof(AllArgumentLists))]
        public void The_password_is_never_on_the_command_line(string _, List<string> arguments)
        {
            // It's passed in PGPASSWORD instead - a command line is visible to every user on the machine.
            arguments.Should().NotContain(a => a.Contains(Password));
        }

        [Theory]
        [MemberData(nameof(AllArgumentLists))]
        public void Every_tool_connects_to_the_configured_database_without_prompting(string _, List<string> arguments)
        {
            arguments.Should().ContainInConsecutiveOrder("--host", "db.example");
            arguments.Should().ContainInConsecutiveOrder("--port", "5433");
            arguments.Should().ContainInConsecutiveOrder("--username", "emil");
            arguments.Should().ContainInConsecutiveOrder("--dbname", "TradingTools");
            arguments.Should().Contain("--no-password", "a missing password must fail, not hang waiting for input");
        }

        [Fact]
        public void Dump_is_custom_format_without_owners_to_the_given_file()
        {
            var arguments = PostgresDatabaseBackup.DumpArguments(Connection, "out.dump");

            arguments.Should().Contain(["--format=custom", "--no-owner", "--no-privileges"]);
            arguments.Should().ContainInConsecutiveOrder("--file", "out.dump");
        }

        [Fact]
        public void Restore_resets_the_schema_then_runs_the_dump_all_or_nothing()
        {
            var arguments = PostgresDatabaseBackup.RestoreArguments(Connection, "reset.sql", "in.sql");

            arguments.Should().Contain("--single-transaction");
            arguments.Should().ContainInConsecutiveOrder("--set", "ON_ERROR_STOP=1");
            // Both files in the one transaction, the reset first.
            arguments.Should().ContainInConsecutiveOrder("--file", "reset.sql", "--file", "in.sql");
        }

        [Fact]
        public void The_reset_empties_the_public_schema_so_tables_newer_than_the_dump_go_too()
        {
            PostgresDatabaseBackup.ResetSchemaSql.Should().Contain("DROP SCHEMA IF EXISTS public CASCADE;");
            PostgresDatabaseBackup.ResetSchemaSql.Should().Contain("CREATE SCHEMA public;");
            PostgresDatabaseBackup.ResetSchemaSql.Should().NotContainAny("COMMIT", "BEGIN", "because it has to stay inside the restore's transaction");
        }

        [Fact]
        public void A_custom_dump_is_converted_to_sql_without_touching_a_database()
        {
            var arguments = PostgresDatabaseBackup.ToSqlArguments("in.dump", "out.sql");

            arguments.Should().ContainInConsecutiveOrder("--file", "out.sql");
            arguments[^1].Should().Be("in.dump");
            arguments.Should().Contain(["--no-owner", "--no-privileges"]);
            arguments.Should().NotContain(a => a.StartsWith("--dbname") || a.StartsWith("--host") || a == "--clean");
        }

        [Fact]
        public void Default_port_is_used_when_the_connection_string_has_none()
        {
            var connection = new NpgsqlConnectionStringBuilder("Host=localhost;Database=TradingTools;Username=emil");

            PostgresDatabaseBackup.DumpArguments(connection, "x").Should().ContainInConsecutiveOrder("--port", "5432");
        }

        #endregion

        #region Dump format

        [Fact]
        public void A_custom_format_dump_is_recognised_by_its_signature()
        {
            string file = _temp.File("backup.dump", "PGDMP\u0001\u000e\u0000...");

            PostgresDatabaseBackup.IsCustomFormat(file).Should().BeTrue();
        }

        [Theory]
        [InlineData("--\n-- PostgreSQL database dump\n--\nCREATE TABLE x();")]
        [InlineData("PGD")]
        [InlineData("")]
        public void Anything_else_is_treated_as_plain_sql(string content)
        {
            string file = _temp.File("backup.sql", content);

            PostgresDatabaseBackup.IsCustomFormat(file).Should().BeFalse();
        }

        #endregion

        #region Finding the tools

        private static string Exe(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

        [Fact]
        public void A_configured_folder_is_used_when_set()
        {
            string expected = _temp.File("pgbin/" + Exe("pg_dump"));

            PostgresDatabaseBackup.ResolveTool("pg_dump", _temp["pgbin"], windowsInstallRoot: _temp["none"]).Should().Be(expected);
        }

        [Fact]
        public void A_configured_folder_without_the_tool_is_a_clear_error()
        {
            var resolve = () => PostgresDatabaseBackup.ResolveTool("pg_dump", _temp["empty"], windowsInstallRoot: _temp["none"]);

            resolve.Should().Throw<InvalidOperationException>()
                .WithMessage($"*{Exe("pg_dump")} was not found*{PostgresDatabaseBackup.BinPathSetting}*");
        }

        [Fact]
        public void Without_configuration_the_newest_installed_version_is_used_on_windows_or_the_path_elsewhere()
        {
            _temp.File("PostgreSQL/9.6/bin/" + Exe("pg_dump"));
            _temp.File("PostgreSQL/16/bin/" + Exe("pg_dump"));
            string newest = _temp.File("PostgreSQL/18/bin/" + Exe("pg_dump"));
            _temp.File("PostgreSQL/pgAdmin 4/bin/readme.txt");    // not a version folder

            string resolved = PostgresDatabaseBackup.ResolveTool("pg_dump", configuredBinPath: null, windowsInstallRoot: _temp["PostgreSQL"]);

            if (OperatingSystem.IsWindows())
                resolved.Should().Be(newest, "18 is newer than 16 and 9.6 (compared as numbers, not text)");
            else
                resolved.Should().Be("pg_dump", "on Linux the tools are found on the PATH");
        }

        [Fact]
        public void With_nothing_installed_the_bare_name_is_left_for_the_path()
        {
            PostgresDatabaseBackup.ResolveTool("pg_restore", configuredBinPath: null, windowsInstallRoot: _temp["none"])
                .Should().Be(Exe("pg_restore"));
        }

        #endregion
    }
}
