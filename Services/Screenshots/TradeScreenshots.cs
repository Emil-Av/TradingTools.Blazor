using DataAccess.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Models.Screenshots;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// Screenshots of trades in the database: building them from uploaded files, saving them, their order and the
    /// address the pages show them from (<see cref="Url"/>, served by <see cref="ScreenshotEndpoints"/>).
    /// </summary>
    public static class TradeScreenshots
    {
        /// <summary>Where a page shows the screenshot from (after login).</summary>
        public static string Url(Screenshot screenshot) => $"/screenshot/{screenshot.Id}";

        /// <summary>The screenshots in carousel order (by position).</summary>
        public static void Sort(List<Screenshot> screenshots) => screenshots.Sort((a, b) => a.Position.CompareTo(b.Position));

        /// <summary>The position after the last one, so new screenshots follow the ones there.</summary>
        public static int NextPosition(IEnumerable<Screenshot> existing) => existing.Select(s => s.Position).DefaultIfEmpty(-1).Max() + 1;

        /// <summary>
        /// New screenshots from uploaded files, in the order given, at positions <paramref name="firstPosition"/>,
        /// +1, ... Each carries its image; the owner is set by the caller (or by adding them to a trade).
        /// </summary>
        public static async Task<List<Screenshot>> FromFilesAsync(IEnumerable<IFormFile> files, int firstPosition, CancellationToken cancellationToken = default)
        {
            var screenshots = new List<Screenshot>();
            int position = firstPosition;
            foreach (var file in files)
            {
                using var memory = new MemoryStream();
                await file.CopyToAsync(memory, cancellationToken);

                screenshots.Add(new Screenshot
                {
                    Position = position++,
                    FileName = FileNameOf(file.FileName),
                    CreatedAt = DateTime.UtcNow,
                    Content = new ScreenshotContent { Data = memory.ToArray() }
                });
            }
            return screenshots;
        }

        /// <summary>
        /// After saving: lets go of the images of these screenshots, so they don't stay in memory for as long as the
        /// page lives (its database context does). The screenshots themselves stay tracked, without their images.
        /// </summary>
        public static void ReleaseImages(ApplicationDbContext db, IEnumerable<Screenshot> screenshots)
        {
            foreach (var screenshot in screenshots.ToList())
            {
                // Only saved ones: an unsaved screenshot keeps its image, so saving again still saves it.
                if (screenshot.Content is not { } content || db.Entry(screenshot).State != EntityState.Unchanged) continue;

                // Both are detached first: with the screenshot tracked, clearing its content would read as deleting it.
                db.Entry(content).State = EntityState.Detached;
                db.Entry(screenshot).State = EntityState.Detached;
                screenshot.Content = null;
                db.Attach(screenshot);
            }
        }

        /// <summary>
        /// The browser-supplied name can contain '/' or '\' whatever the server's OS; only the file name is kept.
        /// An empty name becomes "screenshot.png".
        /// </summary>
        public static string FileNameOf(string name)
        {
            string fileName = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "";
            return fileName.Length == 0 ? "screenshot.png" : fileName;
        }

        private static readonly FileExtensionContentTypeProvider ContentTypes = new();

        /// <summary>The image type from the file name; "application/octet-stream" when it is not known.</summary>
        public static string ContentTypeOf(string fileName) =>
            ContentTypes.TryGetContentType(fileName, out var type) ? type : "application/octet-stream";
    }

    /// <summary>Serves a screenshot's image from the database, only to someone logged in.</summary>
    public static class ScreenshotEndpoints
    {
        public static void MapScreenshotEndpoints(this IEndpointRouteBuilder app)
        {
            app.MapGet("/screenshot/{id:int}", ServeAsync).RequireAuthorization();
        }

        private static async Task<IResult> ServeAsync(int id, ApplicationDbContext db, HttpContext http, CancellationToken cancellationToken)
        {
            var image = await db.Screenshots.AsNoTracking()
                .Where(s => s.Id == id)
                .Select(s => new { s.FileName, Data = s.Content == null ? null : s.Content.Data })
                .FirstOrDefaultAsync(cancellationToken);
            if (image?.Data is null) return Results.NotFound();

            // A screenshot's image never changes (a new one gets a new id), so the browser may keep it.
            http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return Results.File(image.Data, TradeScreenshots.ContentTypeOf(image.FileName),
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{id}\""));
        }
    }
}
