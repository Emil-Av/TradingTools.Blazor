using TradingTools.Blazor.Services.Dashboard;

namespace TradingTools.Blazor.Services.Interfaces
{
    public interface IDashboardService
    {
        /// <summary>Closed SRS and Espresso trades (research excluded), oldest first.</summary>
        Task<List<DashboardTrade>> GetTradesAsync();
    }
}
