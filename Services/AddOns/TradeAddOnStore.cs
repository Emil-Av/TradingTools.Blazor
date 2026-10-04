using DataAccess.Data;
using Microsoft.EntityFrameworkCore;
using Models.Trades;

namespace TradingTools.Blazor.Services.AddOns
{
    public interface ITradeAddOnStore
    {
        /// <summary>
        /// Makes the trade's saved add-ons match <paramref name="addOns"/>: new ones (Id 0) are added,
        /// existing ones updated and the ones no longer in the list removed. Only stages the changes -
        /// they're written by the unit of work's next SaveAsync, together with the trade itself.
        /// </summary>
        Task StageReplaceAsync(int tradeId, IReadOnlyList<TradeAddOn> addOns);
    }

    /// <summary>
    /// The trade repository's UpdateAsync only copies a trade's own fields, so add-ons are saved
    /// here. Works on the same scoped <see cref="ApplicationDbContext"/> as the unit of work.
    /// </summary>
    public class TradeAddOnStore(ApplicationDbContext db) : ITradeAddOnStore
    {
        private readonly ApplicationDbContext _db = db;

        public async Task StageReplaceAsync(int tradeId, IReadOnlyList<TradeAddOn> addOns)
        {
            // Tracked instances come back as-is (the Trades page edits the tracked trade's add-ons).
            var saved = await _db.TradeAddOns.Where(a => a.BaseTradeId == tradeId).ToListAsync();
            var keptIds = addOns.Where(a => a.Id != 0).Select(a => a.Id).ToHashSet();

            foreach (var removed in saved.Where(a => !keptIds.Contains(a.Id)))
            {
                _db.TradeAddOns.Remove(removed);
            }

            foreach (var addOn in addOns)
            {
                addOn.BaseTradeId = tradeId;

                if (addOn.Id == 0)
                {
                    if (_db.Entry(addOn).State == EntityState.Detached) _db.TradeAddOns.Add(addOn);
                }
                else if (saved.FirstOrDefault(a => a.Id == addOn.Id) is { } existing && !ReferenceEquals(existing, addOn))
                {
                    _db.Entry(existing).CurrentValues.SetValues(addOn);
                }
            }
        }
    }
}
