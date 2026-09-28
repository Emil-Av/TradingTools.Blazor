namespace TradingTools.Blazor.Services.Validation
{
    /// <summary>
    /// Runs trade validation in the background: once at startup, then whenever
    /// <see cref="ITradeValidationMonitor.RequestValidation"/> is called (after saving, deleting or
    /// importing trades, or from the Data check page).
    /// </summary>
    public sealed class TradeValidationWorker(
        TradeValidationMonitor monitor,
        IServiceScopeFactory scopeFactory,
        ILogger<TradeValidationWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Don't hold up application startup.
            await Task.Yield();

            monitor.RequestValidation();

            await foreach (var _ in monitor.Requests.ReadAllAsync(stoppingToken))
            {
                await RunOnceAsync(stoppingToken);
            }
        }

        internal async Task RunOnceAsync(CancellationToken cancellationToken)
        {
            monitor.Started();
            try
            {
                // IUnitOfWork / DbContext are scoped; a fresh scope per run keeps them short-lived.
                using var scope = scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ITradeValidationService>();

                var report = await service.ValidateAllAsync(cancellationToken);
                monitor.Completed(report);

                logger.LogInformation("Trade validation finished: {Checked} trades checked, {Invalid} with problems.",
                    report.TradesChecked, report.InvalidTrades.Count);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                monitor.Failed("Validation was cancelled.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Trade validation failed.");
                monitor.Failed(ex.Message);
            }
        }
    }
}
