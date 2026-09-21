using DataAccess.Data;
using Microsoft.EntityFrameworkCore;

namespace TradingTools.Blazor.Services
{
    public interface IDbContextFactory
    {
        DbContext CreateDbContext();
    }

    public class DbContextFactory(IConfiguration configuration) : IDbContextFactory
    {
        private readonly IConfiguration _configuration = configuration;

        public DbContext CreateDbContext()
        {
            var connectionString = _configuration.GetConnectionString("PostgreSqlConnection")
                ?? throw new InvalidOperationException("PostgreSqlConnection string is missing.");

            var options = new DbContextOptionsBuilder<PostgreSqlDbContext>()
                .UseNpgsql(connectionString, x => x.MigrationsAssembly("DataAccess"))
                .Options;

            return new PostgreSqlDbContext(options);
        }
    }
}
