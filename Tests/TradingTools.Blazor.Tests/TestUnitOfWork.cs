using System.Linq.Expressions;
using DataAccess.Repository.IRepository;
using Models;
using Models.Trades;
using NSubstitute;

namespace TradingTools.Blazor.Tests
{
    /// <summary>
    /// An in-memory IUnitOfWork whose BaseTrade and SampleSize repositories apply the service's real
    /// filter expressions (compiled) to the given lists.
    /// </summary>
    internal static class TestUnitOfWork
    {
        public static IUnitOfWork Create(IEnumerable<SampleSize> sampleSizes, IEnumerable<BaseTrade> trades)
        {
            var tradeList = trades.ToList();
            var sampleSizeList = sampleSizes.ToList();

            var tradeRepository = Substitute.For<IBaseTradeRepository>();
            tradeRepository.GetAllAsync(Arg.Any<Expression<Func<BaseTrade, bool>>?>(), Arg.Any<string?>())
                .Returns(call => Where(tradeList, call.ArgAt<Expression<Func<BaseTrade, bool>>?>(0)));
            tradeRepository.GetAsync(Arg.Any<Expression<Func<BaseTrade, bool>>>(), Arg.Any<string?>(), Arg.Any<bool>())
                .Returns(call => Task.FromResult(tradeList.FirstOrDefault(call.ArgAt<Expression<Func<BaseTrade, bool>>>(0).Compile())!));

            var sampleSizeRepository = Substitute.For<ISampleSizeRepository>();
            sampleSizeRepository.GetAllAsync(Arg.Any<Expression<Func<SampleSize, bool>>?>(), Arg.Any<string?>())
                .Returns(call => Where(sampleSizeList, call.ArgAt<Expression<Func<SampleSize, bool>>?>(0)));

            var unitOfWork = Substitute.For<IUnitOfWork>();
            unitOfWork.BaseTrade.Returns(tradeRepository);
            unitOfWork.SampleSize.Returns(sampleSizeRepository);
            return unitOfWork;
        }

        private static Task<List<T>> Where<T>(IEnumerable<T> items, Expression<Func<T, bool>>? filter) =>
            Task.FromResult(filter is null ? items.ToList() : items.Where(filter.Compile()).ToList());
    }
}
