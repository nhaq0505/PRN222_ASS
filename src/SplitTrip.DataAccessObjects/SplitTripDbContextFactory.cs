using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SplitTrip.DataAccessObjects;

public sealed class SplitTripDbContextFactory : IDesignTimeDbContextFactory<SplitTripDbContext>
{
    public SplitTripDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SPLITTRIP_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = "Server=(localdb)\\mssqllocaldb;Database=SplitTripDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
        }

        var optionsBuilder = new DbContextOptionsBuilder<SplitTripDbContext>();
        optionsBuilder.UseSqlServer(connectionString);

        return new SplitTripDbContext(optionsBuilder.Options);
    }
}
