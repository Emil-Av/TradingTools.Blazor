using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using TradingTools.Blazor.Services.Validation;

namespace TradingTools.Blazor.Tests.Validation
{
    public class TradeValidationWorkerTests
    {
        private static readonly TradeValidationReport SomeReport = new(DateTime.UnixEpoch, SharedEnums.Enums.SampleSizeType.DemoTrading, 5, 9, []);

        private static (TradeValidationWorker Worker, TradeValidationMonitor Monitor, ITradeValidationService Service) Create()
        {
            var service = Substitute.For<ITradeValidationService>();
            service.ValidateAllAsync(Arg.Any<CancellationToken>()).Returns(SomeReport);

            var provider = new ServiceCollection().AddScoped(_ => service).BuildServiceProvider();
            var monitor = new TradeValidationMonitor();
            var worker = new TradeValidationWorker(monitor, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TradeValidationWorker>.Instance);
            return (worker, monitor, service);
        }

        [Fact]
        public async Task A_run_publishes_the_report_and_notifies()
        {
            var (worker, monitor, _) = Create();
            int notifications = 0;
            monitor.Changed += () => notifications++;

            await worker.RunOnceAsync(CancellationToken.None);

            monitor.LatestReport.Should().BeSameAs(SomeReport);
            monitor.IsRunning.Should().BeFalse();
            monitor.LastError.Should().BeNull();
            notifications.Should().Be(2, "once when it starts, once when it finishes");
        }

        [Fact]
        public async Task A_failed_run_keeps_the_previous_report_and_records_the_error()
        {
            var (worker, monitor, service) = Create();
            await worker.RunOnceAsync(CancellationToken.None);

            service.ValidateAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database is down"));
            await worker.RunOnceAsync(CancellationToken.None);

            monitor.LatestReport.Should().BeSameAs(SomeReport);
            monitor.LastError.Should().Be("database is down");
            monitor.IsRunning.Should().BeFalse();
        }

        [Fact]
        public async Task A_successful_run_clears_an_earlier_error()
        {
            var (worker, monitor, service) = Create();
            service.ValidateAllAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));
            await worker.RunOnceAsync(CancellationToken.None);

            service.ValidateAllAsync(Arg.Any<CancellationToken>()).Returns(SomeReport);
            await worker.RunOnceAsync(CancellationToken.None);

            monitor.LastError.Should().BeNull();
        }

        [Fact]
        public async Task The_worker_validates_at_startup_and_on_request()
        {
            var (worker, monitor, service) = Create();
            var firstRun = new TaskCompletionSource();
            monitor.Changed += () => { if (monitor.LatestReport is not null) firstRun.TrySetResult(); };

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await worker.StartAsync(cts.Token);
            await firstRun.Task.WaitAsync(cts.Token);

            monitor.RequestValidation();
            await WaitUntil(() => service.ReceivedCalls().Count() >= 2, cts.Token);

            await worker.StopAsync(CancellationToken.None);
            service.ReceivedCalls().Count().Should().Be(2);
        }

        [Fact]
        public void Requests_made_while_one_is_already_waiting_are_merged()
        {
            var monitor = new TradeValidationMonitor();

            for (int i = 0; i < 50; i++) monitor.RequestValidation();

            int queued = 0;
            while (monitor.Requests.TryRead(out _)) queued++;
            queued.Should().Be(1, "saving 50 trades in a row must not queue 50 validation runs");
        }

        private static async Task WaitUntil(Func<bool> condition, CancellationToken cancellationToken)
        {
            while (!condition()) await Task.Delay(10, cancellationToken);
        }
    }
}
