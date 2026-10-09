// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NexoPBX.Abstractions.Identity;

namespace NexoPBX.Store.Postgres;

/// <summary>Used only by <c>dotnet ef</c> to generate migrations; never at runtime.</summary>
internal sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<NexoDbContext>
{
    public NexoDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<NexoDbContext>()
            .UseNpgsql("Host=localhost;Database=nexopbx_design", b => b.MigrationsAssembly(typeof(PostgresDesignTimeFactory).Assembly.GetName().Name))
            .Options;
        return new NexoDbContext(options, new TenantContext());
    }
}
