using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace TradingTools.Blazor.Services
{
    /// <summary>
    /// Wraps a Blazor <see cref="IBrowserFile"/> (from an InputFile component) as an
    /// <see cref="IFormFile"/> so the existing screenshot-saving services (which were written
    /// against ASP.NET Core's multipart form upload model) can be reused unchanged.
    /// </summary>
    public class BrowserFileFormFile : IFormFile
    {
        private readonly MemoryStream _content;

        private BrowserFileFormFile(MemoryStream content, string fileName, string contentType)
        {
            _content = content;
            FileName = fileName;
            ContentType = contentType;
            Name = "files";
        }

        public static async Task<BrowserFileFormFile> CreateAsync(IBrowserFile file, long maxFileSizeBytes)
        {
            var stream = new MemoryStream();
            await using (var browserStream = file.OpenReadStream(maxFileSizeBytes))
            {
                await browserStream.CopyToAsync(stream);
            }
            stream.Position = 0;

            return new BrowserFileFormFile(stream, file.Name, file.ContentType);
        }

        public string ContentType { get; }
        public string ContentDisposition => new ContentDispositionHeaderValue("form-data")
        {
            Name = Name,
            FileName = FileName
        }.ToString();
        public IHeaderDictionary Headers { get; } = new HeaderDictionary();
        public long Length => _content.Length;
        public string Name { get; }
        public string FileName { get; }

        public void CopyTo(Stream target)
        {
            _content.Position = 0;
            _content.CopyTo(target);
        }

        public async Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
        {
            _content.Position = 0;
            await _content.CopyToAsync(target, cancellationToken);
        }

        public Stream OpenReadStream()
        {
            _content.Position = 0;
            return _content;
        }
    }
}
