using Microsoft.AspNetCore.Components.Forms;

namespace TradingTools.Blazor.Services.Screenshots
{
    /// <summary>
    /// The order screenshots are uploaded and saved in: by their date, the oldest first - which is also the order
    /// the carousel shows them in. The date is the file's own modified date; files with the same date keep a
    /// stable order by name (the screenshots' names carry their date and time too).
    /// </summary>
    public static class ScreenshotOrder
    {
        public static List<IBrowserFile> OldestFirst(IEnumerable<IBrowserFile> files) =>
            [.. files.OrderBy(file => file.LastModified).ThenBy(file => file.Name, StringComparer.OrdinalIgnoreCase)];
    }
}
