using System.Threading.Channels;

namespace TradingTools.Blazor.Services.Validation
{
    /// <summary>
    /// Holds the latest validation report and lets anything ask for a fresh one. The work itself
    /// happens in <see cref="TradeValidationWorker"/>, off the UI thread.
    /// </summary>
    public interface ITradeValidationMonitor
    {
        TradeValidationReport? LatestReport { get; }

        bool IsRunning { get; }

        /// <summary>Set when the last run failed; cleared by the next successful run.</summary>
        string? LastError { get; }

        /// <summary>Raised (on a background thread) whenever the state above changes.</summary>
        event Action? Changed;

        /// <summary>
        /// Asks for a validation run and returns immediately. Requests made while a run is already
        /// queued are merged into it, so saving several trades in a row doesn't queue several runs.
        /// </summary>
        void RequestValidation();
    }

    public sealed class TradeValidationMonitor : ITradeValidationMonitor
    {
        // Capacity 1 + DropWrite: at most one run waits behind the one in progress.
        private readonly Channel<bool> _requests = Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite, SingleReader = true });

        public TradeValidationReport? LatestReport { get; private set; }

        public bool IsRunning { get; private set; }

        public string? LastError { get; private set; }

        public event Action? Changed;

        internal ChannelReader<bool> Requests => _requests.Reader;

        public void RequestValidation() => _requests.Writer.TryWrite(true);

        internal void Started()
        {
            IsRunning = true;
            Changed?.Invoke();
        }

        internal void Completed(TradeValidationReport report)
        {
            LatestReport = report;
            LastError = null;
            IsRunning = false;
            Changed?.Invoke();
        }

        internal void Failed(string error)
        {
            LastError = error;
            IsRunning = false;
            Changed?.Invoke();
        }
    }
}
