using Microsoft.AspNetCore.Http;
using Models.ViewModels;

namespace TradingTools.Blazor.Services.Interfaces
{
    public interface INewTradeService
    {
        Task SaveTradeAsync(NewTradeVM viewModel, IFormFile[] files);
    }
}
