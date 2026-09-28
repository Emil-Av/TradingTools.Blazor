using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TradingTools.Blazor.Services.Screenshots;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Services.DataBackup
{
    /// <summary>
    /// Export / import endpoints used by the Backup page. Plain HTTP endpoints rather than Blazor
    /// events: downloads and multi-hundred-MB uploads go straight through the browser, not over the
    /// SignalR circuit. All of them require a signed-in user; the imports are antiforgery-protected
    /// form posts (the Backup page renders the token).
    /// </summary>
    public static class BackupEndpoints
    {
        public const string PagePath = "/backup";

        /// <summary>No cap from the app itself - an nginx in front has its own (see docs/VPS-SETUP.md).</summary>
        private const long MaxUploadBytes = long.MaxValue;

        public static void MapBackupEndpoints(this IEndpointRouteBuilder app)
        {
            var group = app.MapGroup(PagePath).RequireAuthorization();

            group.MapGet("/export/database", ExportDatabase);
            group.MapGet("/export/screenshots", ExportScreenshots);

            group.MapPost("/import/database", ImportDatabase)
                .WithMetadata(new DisableRequestSizeLimitAttribute())
                .WithFormOptions(multipartBodyLengthLimit: MaxUploadBytes);

            group.MapPost("/import/screenshots", ImportScreenshots)
                .WithMetadata(new DisableRequestSizeLimitAttribute())
                .WithFormOptions(multipartBodyLengthLimit: MaxUploadBytes);
        }

        private static async Task<IResult> ExportDatabase(IDatabaseBackup backup, TimeProvider time, CancellationToken cancellationToken)
        {
            string file = TempFile(".dump");
            try
            {
                await backup.DumpAsync(file, cancellationToken);
            }
            catch (Exception ex)
            {
                TryDelete(file);
                return BackToPage(error: $"Database export failed: {ex.Message}");
            }

            return Results.File(OpenAndDeleteOnClose(file), "application/octet-stream", $"tradingtools-db-{Stamp(time)}.dump");
        }

        private static IResult ExportScreenshots(IWebHostEnvironment environment, TimeProvider time)
        {
            string file = TempFile(".zip");
            try
            {
                ScreenshotArchive.CreateZip(ScreenshotStorage.GetRoot(environment.WebRootPath), file);
            }
            catch (Exception ex)
            {
                TryDelete(file);
                return BackToPage(error: $"Screenshots export failed: {ex.Message}");
            }

            return Results.File(OpenAndDeleteOnClose(file), "application/zip", $"tradingtools-screenshots-{Stamp(time)}.zip");
        }

        private static async Task<IResult> ImportDatabase(
            IFormFile? file,
            IDatabaseBackup backup,
            IDbContextFactory dbContextFactory,
            LegacyScreenshotMigration screenshotMigration,
            ITradeValidationMonitor validation,
            CancellationToken cancellationToken)
        {
            if (file is null || file.Length == 0) return BackToPage(error: "Choose a database dump file to import.");

            string temp = TempFile(Path.GetExtension(file.FileName));
            try
            {
                await using (var stream = File.Create(temp))
                {
                    await file.CopyToAsync(stream, cancellationToken);
                }

                await backup.RestoreAsync(temp, cancellationToken);
            }
            catch (Exception ex)
            {
                return BackToPage(error: $"Database import failed, nothing was changed: {ex.Message}");
            }
            finally
            {
                TryDelete(temp);
            }

            // A dump made before the latest database changes (e.g. before add-ons) is brought up to date,
            // in every environment - the app can't run against an older schema.
            try
            {
                await using var context = dbContextFactory.CreateDbContext();
                await context.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                return BackToPage(error: $"The database was imported, but updating it to the current version failed: {ex.Message}");
            }

            // An older dump may still use "ScreenshotsDev/..." paths.
            await screenshotMigration.RunAsync(cancellationToken);
            validation.RequestValidation();

            return BackToPage(message: $"Database imported from {Path.GetFileName(file.FileName)}.");
        }

        private static IResult ImportScreenshots(IFormFile? file, IWebHostEnvironment environment)
        {
            if (file is null || file.Length == 0) return BackToPage(error: "Choose a .zip file of screenshots to import.");

            try
            {
                using var stream = file.OpenReadStream();
                var result = ScreenshotArchive.ImportZip(stream, ScreenshotStorage.GetRoot(environment.WebRootPath));

                string skipped = result.EntriesSkipped > 0 ? $" {result.EntriesSkipped} unsafe or empty entr{(result.EntriesSkipped == 1 ? "y was" : "ies were")} skipped." : "";
                return BackToPage(message: $"Imported {result.FilesWritten} screenshot file(s) from {Path.GetFileName(file.FileName)}.{skipped}");
            }
            catch (InvalidDataException)
            {
                return BackToPage(error: $"{Path.GetFileName(file.FileName)} is not a valid .zip file.");
            }
            catch (Exception ex)
            {
                return BackToPage(error: $"Screenshots import failed: {ex.Message}");
            }
        }

        private static IResult BackToPage(string? message = null, string? error = null)
        {
            var query = new Dictionary<string, string?>();
            if (message is not null) query["message"] = message;
            if (error is not null) query["error"] = error;
            return Results.LocalRedirect(Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(PagePath, query));
        }

        private static string TempFile(string extension) =>
            Path.Combine(Path.GetTempPath(), $"tradingtools-{Guid.NewGuid():N}{extension}");

        /// <summary>The temp file is removed as soon as the download stream is closed.</summary>
        private static FileStream OpenAndDeleteOnClose(string file) =>
            new(file, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);

        private static string Stamp(TimeProvider time) =>
            time.GetLocalNow().ToString("yyyy-MM-dd-HHmm", CultureInfo.InvariantCulture);

        private static void TryDelete(string file)
        {
            try { File.Delete(file); } catch { /* temp file, best effort */ }
        }
    }
}
