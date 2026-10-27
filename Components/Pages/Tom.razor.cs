using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Models.Tom;
using MudBlazor;
using TradingTools.Blazor.Services;
using TradingTools.Blazor.Services.Screenshots;
using TradingTools.Blazor.Services.Tom;

namespace TradingTools.Blazor.Components.Pages
{
    /// <summary>
    /// The Tom section: the streams of Tom and Andi and the trades they took in them, to learn from. The page edits the
    /// loaded streams and trades directly and Save stores all of it; screenshots are stored (and deleted) right away.
    /// </summary>
    public partial class Tom
    {
        [Inject] private ITomService TomService { get; set; } = default!;
        [Inject] private ISnackbar Snackbar { get; set; } = default!;
        [Inject] private IDialogService DialogService { get; set; } = default!;
        [Inject] private IJSRuntime JS { get; set; } = default!;

        private const long MaxFileSizeBytes = 10 * 1024 * 1024;
        private const string NewTradeInput = "tomNewTradeInput";
        private const string MoreScreenshotsInput = "tomMoreScreenshotsInput";

        private List<TomStream> _streams = [];
        private TomStream? _stream;
        private int _tradeIndex;
        private int _screenshotIndex;

        private bool _loading = true;
        private bool _busy;

        private bool _creating;
        private DateTime? _newDate;
        private string? _newUrl;
        // A new stream starts as Tom's EU session, the most common one; both can be changed before creating it.
        private TomSession _newSession = TomSession.EU;
        private TomTrader _newTrader = TomTrader.Tom;

        private TomFilter _filter = new();

        /// <summary>The streams the filters let through.</summary>
        private List<TomStream> Shown => [.. _filter.Apply(_streams)];

        // What was typed in "Time in video" while it isn't a valid time yet.
        private string? _timeDraft;
        private string? _timeError;

        private TomTrade? CurrentTrade =>
            _stream is { Trades.Count: > 0 } stream ? stream.Trades[Math.Clamp(_tradeIndex, 0, stream.Trades.Count - 1)] : null;

        private bool HasUnsavedChanges => TomService.HasUnsavedChanges;

        protected override async Task OnInitializedAsync()
        {
            _streams = await TomService.GetStreamsAsync();
            _stream = TomMonths.Group(_streams).FirstOrDefault()?.Streams[0];
            _loading = false;
        }

        private static bool LinkIsValid(string? url) => string.IsNullOrWhiteSpace(url) || TomLinks.IsWebLink(url);

        #region Streams

        private void StartNewStream()
        {
            _newDate = DateTime.Today;
            _newUrl = null;
            _newSession = TomSession.EU;
            _newTrader = TomTrader.Tom;
            _creating = true;
        }

        private async Task CreateStreamAsync()
        {
            await RunAsync(async () =>
            {
                var stream = await TomService.CreateStreamAsync(DateOnly.FromDateTime(_newDate ?? DateTime.Today), _newUrl, _newSession, _newTrader);
                _streams.Add(stream);
                _creating = false;
                SelectStream(stream);
                Snackbar.Add("Stream added. Add its trades with Add trade.", Severity.Success);
            }, "adding the stream");
        }

        /// <summary>
        /// Applies new filters. When the stream on show isn't among the streams they let through, the newest one that is
        /// opens instead (or none).
        /// </summary>
        private void SetFilter(TomFilter filter)
        {
            _filter = filter;
            if (_stream is not null && _filter.Matches(_stream)) return;
            _stream = TomMonths.Group(Shown).FirstOrDefault()?.Streams[0];
            ShowTrade(0);
        }

        private void SelectStream(TomStream stream)
        {
            _stream = stream;
            ShowTrade(0);
        }

        private async Task ConfirmDeleteStreamAsync()
        {
            if (_stream is not { } stream) return;

            int trades = stream.Trades.Count;
            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                $"Delete the stream of {stream.Date.ToString("dd MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}?",
                $"Its notes, its {trades} {(trades == 1 ? "trade" : "trades")} and their screenshots are deleted. This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");
            if (confirmed != true) return;

            await RunAsync(async () =>
            {
                await TomService.DeleteStreamAsync(stream);
                _streams.Remove(stream);
                _stream = TomMonths.Group(_streams).FirstOrDefault()?.Streams[0];
                ShowTrade(0);
                Snackbar.Add("Stream deleted.", Severity.Success);
            }, "deleting the stream");
        }

        private async Task SaveAsync() =>
            await RunAsync(async () =>
            {
                await TomService.SaveAsync();
                Snackbar.Add("Saved.", Severity.Success);
            }, "saving");

        #endregion

        #region Trades

        private void ShowTrade(int index)
        {
            _tradeIndex = index;
            _screenshotIndex = 0;
            _timeDraft = null;
            _timeError = null;
        }

        private void PrevTrade() { if (_tradeIndex > 0) ShowTrade(_tradeIndex - 1); }

        private void NextTrade() { if (_stream is { } s && _tradeIndex < s.Trades.Count - 1) ShowTrade(_tradeIndex + 1); }

        private void PrevScreenshot() { if (_screenshotIndex > 0) _screenshotIndex--; }

        private void NextScreenshot() { if (CurrentTrade?.ScreenshotsUrls is { } urls && _screenshotIndex < urls.Count - 1) _screenshotIndex++; }

        private string TimeText => _timeDraft ?? (CurrentTrade?.VideoSeconds is { } s ? VideoTime.Format(s) : "");

        private void OnTimeChanged(string? text)
        {
            if (CurrentTrade is not { } trade) return;

            if (VideoTime.TryParse(text, out var seconds))
            {
                trade.VideoSeconds = seconds;
                _timeDraft = null;
                _timeError = null;
            }
            else
            {
                _timeDraft = text;
                _timeError = "A time like 1:23:45 or 23:45";
            }
        }

        // The hidden file inputs are clicked from JS (see the Trades page for why a <label> isn't used).
        private Task PickFiles(string inputId) => JS.InvokeVoidAsync("triggerFileInputClick", inputId).AsTask();

        /// <summary>The screenshots of a new trade: saved by date, the oldest first, like everywhere else.</summary>
        private async Task OnNewTradeFilesAsync(InputFileChangeEventArgs e)
        {
            if (_stream is not { } stream) return;

            await RunAsync(async () =>
            {
                var files = await ToFormFilesAsync(e);
                if (files.Length == 0) return;
                await TomService.AddTradeAsync(stream, files);
                ShowTrade(stream.Trades.Count - 1);
                Snackbar.Add("Trade added.", Severity.Success);
            }, "adding the trade");
        }

        private async Task OnMoreScreenshotsAsync(InputFileChangeEventArgs e)
        {
            if (_stream is not { } stream || CurrentTrade is not { } trade) return;

            await RunAsync(async () =>
            {
                var files = await ToFormFilesAsync(e);
                if (files.Length == 0) return;
                await TomService.AddScreenshotsAsync(stream, trade, files);
                Snackbar.Add("Screenshots added.", Severity.Success);
            }, "adding the screenshots");
        }

        private static async Task<Microsoft.AspNetCore.Http.IFormFile[]> ToFormFilesAsync(InputFileChangeEventArgs e)
        {
            var files = ScreenshotOrder.OldestFirst(e.GetMultipleFiles(20));
            var formFiles = await Task.WhenAll(files.Select(f => BrowserFileFormFile.CreateAsync(f, MaxFileSizeBytes)));
            return [.. formFiles];
        }

        private async Task ConfirmDeleteTradeAsync()
        {
            if (_stream is not { } stream || CurrentTrade is not { } trade) return;

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                $"Delete trade {_tradeIndex + 1} of {stream.Trades.Count}?",
                "Its notes and its screenshots are deleted. This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");
            if (confirmed != true) return;

            await RunAsync(async () =>
            {
                await TomService.DeleteTradeAsync(stream, trade);
                ShowTrade(Math.Max(0, Math.Min(_tradeIndex, stream.Trades.Count - 1)));
                Snackbar.Add("Trade deleted.", Severity.Success);
            }, "deleting the trade");
        }

        /// <summary>The screenshot on show: identified by its path, taken before the dialog opens.</summary>
        private async Task ConfirmDeleteScreenshotAsync()
        {
            if (CurrentTrade is not { ScreenshotsUrls: { } urls } trade || _screenshotIndex < 0 || _screenshotIndex >= urls.Count) return;

            int position = _screenshotIndex;
            string path = urls[position];
            string name = Path.GetFileName(path.Replace('\\', '/'));

            bool? confirmed = await DialogService.ShowMessageBoxAsync(
                $"Delete screenshot {position + 1} of {urls.Count}?",
                $"{name} is removed from this trade and deleted from the Screenshots folder. This cannot be undone.",
                yesText: "Delete", cancelText: "Cancel");
            if (confirmed != true) return;

            await RunAsync(async () =>
            {
                var outcome = await TomService.DeleteScreenshotAsync(trade, path);
                _screenshotIndex = Math.Max(0, Math.Min(position, (trade.ScreenshotsUrls?.Count ?? 0) - 1));
                Snackbar.Add(outcome switch
                {
                    ScreenshotFileOutcome.Deleted => "Screenshot deleted.",
                    ScreenshotFileOutcome.WasMissing => "Screenshot removed (its file was already gone).",
                    ScreenshotFileOutcome.KeptBecauseShared => "Screenshot removed from this trade. Its file is kept because another trade uses it.",
                    _ => "Screenshot removed from this trade, but its file could not be deleted."
                }, outcome is ScreenshotFileOutcome.Deleted or ScreenshotFileOutcome.WasMissing ? Severity.Success : Severity.Warning);
            }, "deleting the screenshot");
        }

        #endregion

        /// <summary>Runs an action that changes data, with the buttons disabled meanwhile and any error shown.</summary>
        private async Task RunAsync(Func<Task> action, string what)
        {
            _busy = true;
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                Snackbar.Add($"Error while {what}: {ex.Message}", Severity.Error);
            }
            finally
            {
                _busy = false;
            }
        }
    }
}
