// SPDX-License-Identifier: AGPL-3.0-only
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NexoPBX.Abstractions.Identity;
using NexoPBX.Store.Entities;

namespace NexoPBX.Store;

/// <summary>
/// The single EF Core model for both providers. Tenant isolation is enforced twice: reads are filtered by a global
/// query filter, writes are stamped and checked in <see cref="ApplyConventions"/>.
/// </summary>
public sealed class NexoDbContext : DbContext
{
    private static readonly DateTime SeedTime = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly ITenantContext _tenant;

    /// <summary>Creates a context for the tenant described by <paramref name="tenant"/>.</summary>
    public NexoDbContext(DbContextOptions<NexoDbContext> options, ITenantContext tenant)
        : base(options)
    {
        _tenant = tenant;
    }

    /// <summary>Tenants (not tenant-filtered).</summary>
    public DbSet<Tenant> Tenants => Set<Tenant>();

    /// <summary>Users of the current tenant.</summary>
    public DbSet<User> Users => Set<User>();

    /// <summary>Tokens of the current tenant.</summary>
    public DbSet<ApiToken> ApiTokens => Set<ApiToken>();

    /// <summary>Audit records of the current tenant.</summary>
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Dates are UTC everywhere (DECISIONS D-011); this keeps Npgsql (timestamptz) and SQLite (text) in agreement.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();

        // Enums are stored by name: readable in the database and safe against reordering.
        configurationBuilder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Tenant>(e =>
        {
            e.ToTable("tenants");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.HasData(new Tenant { Id = TenantIds.Default, Name = "Default", CreatedAt = SeedTime });
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Username).HasMaxLength(100).IsRequired();
            e.Property(x => x.NormalizedUsername).HasMaxLength(100).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            e.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            e.Property(x => x.TotpSecretProtected).HasMaxLength(1024);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.TenantId, x.NormalizedUsername }).IsUnique();
        });

        modelBuilder.Entity<ApiToken>(e =>
        {
            e.ToTable("api_tokens");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.Prefix).HasMaxLength(16).IsRequired();
            e.Property(x => x.Scopes).HasMaxLength(1024).IsRequired();
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.TenantId, x.UserId });
        });

        modelBuilder.Entity<AuditEvent>(e =>
        {
            e.ToTable("audit_events");
            e.HasKey(x => x.Id);
            e.HasQueryFilter(x => x.TenantId == _tenant.TenantId);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.TargetType).HasMaxLength(100);
            e.Property(x => x.TargetId).HasMaxLength(100);
            e.Property(x => x.RemoteIp).HasMaxLength(64);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => new { x.TenantId, x.At });
        });
    }

    /// <inheritdoc />
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyConventions();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc />
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplyConventions();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyConventions()
    {
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is TenantEntity tenantEntity)
            {
                if (entry.State == EntityState.Added)
                {
                    if (tenantEntity.TenantId == Guid.Empty)
                    {
                        tenantEntity.TenantId = _tenant.TenantId;
                    }
                    else if (tenantEntity.TenantId != _tenant.TenantId)
                    {
                        throw new InvalidOperationException("Refusing to write a row for a tenant other than the current one.");
                    }
                }
                else if (entry.State is EntityState.Modified or EntityState.Deleted && tenantEntity.TenantId != _tenant.TenantId)
                {
                    throw new InvalidOperationException("Refusing to change a row of another tenant.");
                }
            }

            if (entry is { State: EntityState.Modified, Entity: IVersioned versioned })
            {
                BumpVersion(entry, versioned);
            }
        }
    }

    private static void BumpVersion(EntityEntry entry, IVersioned versioned)
    {
        // The caller's loaded (or explicitly set) original value is what the UPDATE compares against.
        var property = entry.Property(nameof(IVersioned.Version));
        property.IsModified = true;
        versioned.Version = (long)property.OriginalValue! + 1;
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(v => ToUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }

        private static DateTime ToUtc(DateTime v) => v.Kind switch
        {
            DateTimeKind.Utc => v,
            DateTimeKind.Local => v.ToUniversalTime(),
            _ => throw new InvalidOperationException("Refusing to store a DateTime of unspecified kind: use UTC."),
        };
    }

    private sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue ? new UtcDateTimeConverter().ConvertToProvider(v.Value) as DateTime? : null,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : null)
        {
        }
    }
}
