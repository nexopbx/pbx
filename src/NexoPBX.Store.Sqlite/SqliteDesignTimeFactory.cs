// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NexoPBX.Abstractions.Identity;

namespace NexoPBX.Store.Sqlite;

/// <summary>Used only by <c>dotnet ef</c> to generate migrations; never at runtime.</summary>
internal sealed class SqliteDesignTimeFactory : IDesignTimeDbContextFactory<NexoDbContext>
{
    public NexoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NexoDbContext>()
            .UseSqlite("Data Source=design-time.db", b => b.MigrationsAssembly(typeof(SqliteDesignTimeFactory).Assembly.GetName().Name))
            .Options;
        return new NexoDbContext(options, new TenantContext());
    }
}
