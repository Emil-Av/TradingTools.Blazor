using NSubstitute;
using SharedEnums.Enums;
using TradingTools.Blazor.Services.Settings;

namespace TradingTools.Blazor.Tests
{
    /// <summary>A stand-in for the settings the services read: the default account and the spread of each instrument.</summary>
    internal static class TestSettings
    {
        /// <summary>No instrument has a spread, so results are the plain points. The default account is demo.</summary>
        public static ISettingsService NoSpreads() => WithSpreads();

        /// <summary>The given spreads in points by symbol, e.g. ("DAX", 1.2). The default account is demo.</summary>
        public static ISettingsService WithSpreads(params (string Symbol, double Spread)[] spreads) =>
            Create(SampleSizeType.DemoTrading, spreads);

        /// <summary>Settings whose default account is <paramref name="account"/>.</summary>
        public static ISettingsService WithDefaultAccount(SampleSizeType account) => Create(account);

        private static ISettingsService Create(SampleSizeType account, params (string Symbol, double Spread)[] spreads)
        {
            var settings = Substitute.For<ISettingsService>();
            settings.GetDefaultAccountAsync(Arg.Any<CancellationToken>()).Returns(account);
            settings.GetSpreadsAsync(Arg.Any<CancellationToken>())
                .Returns(new SpreadTable(spreads.Select(s => KeyValuePair.Create(s.Symbol, s.Spread))));
            return settings;
        }
    }
}
