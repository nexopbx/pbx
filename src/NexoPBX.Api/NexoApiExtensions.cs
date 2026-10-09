// SPDX-License-Identifier: AGPL-3.0-only
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Google.Protobuf.Reflection;
using Google.Rpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexoPBX.Abstractions.Events;
using NexoPBX.Abstractions.Security;
using NexoPBX.Abstractions.Time;
using NexoPBX.Api.Infrastructure;
using NexoPBX.Api.OpenApi;
using NexoPBX.Api.Services;

namespace NexoPBX.Api;

/// <summary>Wires the public API (gRPC, REST transcoding, gRPC-Web, SSE, health, OpenAPI) into a web host.</summary>
public static class NexoApiExtensions
{
    private const string CorsPolicy = "nexopbx";

    /// <summary>Registers the API services. Call after <c>AddNexoAuth</c> and a store provider.</summary>
    public static IServiceCollection AddNexoApi(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(ApiOptions.SectionName);
        services.AddOptions<ApiOptions>().Bind(section);
        var api = section.Get<ApiOptions>() ?? new ApiOptions();

        services.TryAddSingleton(sp => new HostInfo("headless", sp.GetRequiredService<IClock>().UtcNow));

        services.AddGrpc(o =>
        {
            o.Interceptors.Add<ErrorMappingInterceptor>();
            o.EnableDetailedErrors = false;
        }).AddJsonTranscoding(o =>
        {
            // Error details are google.rpc messages packed in Any; the JSON writer needs them in its registry.
            o.TypeRegistry = TypeRegistry.FromMessages(ErrorInfo.Descriptor, BadRequest.Descriptor);
        });

        services.AddHealthChecks().AddCheck<StoreHealthCheck>("database", tags: ["ready"]);

        if (api.CorsAllowedOrigins.Length > 0)
        {
            services.AddCors(o => o.AddPolicy(CorsPolicy, p => p
                .WithOrigins(api.CorsAllowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .WithExposedHeaders("Grpc-Status", "Grpc-Message", "Grpc-Encoding", "Grpc-Accept-Encoding")));
        }

        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                var ip = http.Connection.RemoteIpAddress;
                var path = http.Request.Path.Value ?? string.Empty;
                var isLogin = path.EndsWith("/UserService/Login", StringComparison.Ordinal) || path.EndsWith("/auth:login", StringComparison.Ordinal);

                // Local pipe / unix-socket clients have no address and are already authorised by file permissions.
                if (ip is null && !isLogin)
                {
                    return RateLimitPartition.GetNoLimiter("local");
                }

                var who = ip?.ToString() ?? "local";
                return RateLimitPartition.GetFixedWindowLimiter(
                    (isLogin ? "login:" : "api:") + who,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = isLogin ? api.LoginRateLimitPerMinute : api.RateLimitPerMinute,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                    });
            });
        });

        return services;
    }

    /// <summary>Adds the middleware in the right order. Call once, before <see cref="MapNexoApi"/>.</summary>
    public static WebApplication UseNexoApi(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseRouting();
        app.UseGrpcWeb(new GrpcWebOptions { DefaultEnabled = true });
        if (app.Configuration.GetSection(ApiOptions.SectionName).Get<ApiOptions>() is { CorsAllowedOrigins.Length: > 0 })
        {
            app.UseCors(CorsPolicy);
        }

        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }

    /// <summary>Maps services and endpoints.</summary>
    public static IEndpointRouteBuilder MapNexoApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGrpcService<SystemGrpcService>();
        endpoints.MapGrpcService<UserGrpcService>();
        endpoints.MapGrpcService<EventGrpcService>();

        // Liveness: the process answers. Readiness: dependencies are usable. Both anonymous and minimal.
        endpoints.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        endpoints.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = r => r.Tags.Contains("ready") });

        endpoints.MapGet("/v1/events/stream", StreamEventsAsync).RequireAuthorization(Scopes.EventsSubscribe);

        if (endpoints.ServiceProvider.GetService<Microsoft.Extensions.Options.IOptions<ApiOptions>>()?.Value.EnableOpenApi == true)
        {
            var document = OpenApiDocument();
            endpoints.MapGet("/openapi/v1.json", () => Results.Text(document.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), "application/json"))
                .ExcludeFromDescription();
        }

        return endpoints;
    }

    /// <summary>The OpenAPI 3.1 document of the public REST surface, generated from the proto descriptors.</summary>
    public static System.Text.Json.Nodes.JsonObject OpenApiDocument()
    {
        var sse = new System.Text.Json.Nodes.JsonObject
        {
            ["/v1/events/stream"] = System.Text.Json.Nodes.JsonNode.Parse(
                """
                {"get":{"operationId":"Events_Stream","tags":["EventService"],"summary":"Server-Sent Events mirror of EventService.Subscribe",
                "parameters":[{"name":"types","in":"query","schema":{"type":"string"},"description":"Comma-separated type prefixes."}],
                "responses":{"200":{"description":"text/event-stream; each `data:` line is a JSON event.","content":{"text/event-stream":{"schema":{"type":"string"}}}}},
                "security":[{"bearerAuth":[]}],"x-required-scopes":["events.subscribe"],"x-streaming":"server"}}
                """),
        };

        return ProtoOpenApiGenerator.Generate(
            [
                new ProtoOpenApiGenerator.ServiceBinding(Contracts.V1.SystemService.Descriptor, typeof(SystemGrpcService)),
                new ProtoOpenApiGenerator.ServiceBinding(Contracts.V1.UserService.Descriptor, typeof(UserGrpcService)),
                new ProtoOpenApiGenerator.ServiceBinding(Contracts.V1.EventService.Descriptor, typeof(EventGrpcService)),
            ],
            "NexoPBX API",
            "1.0.0",
            [sse]);
    }

    // Server-Sent Events mirror of EventService.Subscribe, for browsers and curl.
    private static async Task StreamEventsAsync(HttpContext http, IEventBus bus, Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime, string? types, CancellationToken requestAborted)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(requestAborted, lifetime.ApplicationStopping);
        var cancellationToken = linked.Token;
        var caller = Caller.From(http);
        http.Response.Headers.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        var prefixes = (types ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        using var subscription = bus.Subscribe(new SubscriptionOptions { TenantId = caller.TenantId, TypePrefixes = prefixes });

        using var writeLock = new SemaphoreSlim(1, 1);
        async Task WriteAsync(string text)
        {
            await writeLock.WaitAsync(cancellationToken);
            try
            {
                await http.Response.WriteAsync(text, cancellationToken);
                await http.Response.Body.FlushAsync(cancellationToken);
            }
            finally
            {
                writeLock.Release();
            }
        }

        await WriteAsync(": subscribed\n\n");
        using var keepAlive = new PeriodicTimer(TimeSpan.FromSeconds(15));
        var keepAliveTask = Task.Run(async () =>
        {
            while (await keepAlive.WaitForNextTickAsync(cancellationToken))
            {
                await WriteAsync(": keepalive\n\n");
            }
        }, cancellationToken);

        try
        {
            await foreach (var e in subscription.ReadAllAsync(cancellationToken))
            {
                var json = JsonSerializer.Serialize(new SseEvent(e.Id, e.Type, e.TenantId, e.OccurredAtUtc, e.Subject, e.Data));
                await WriteAsync($"id: {e.Id}\nevent: {e.Type}\ndata: {json}\n\n");
            }
        }
        catch (OperationCanceledException)
        {
            // Client disconnected.
        }
        finally
        {
            keepAlive.Dispose();
            try
            {
                await keepAliveTask;
            }
            catch (OperationCanceledException)
            {
                // expected on shutdown
            }
        }
    }

    private sealed record SseEvent(
        [property: JsonPropertyName("id")] Guid Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("tenant_id")] Guid TenantId,
        [property: JsonPropertyName("occurred_at")] DateTime OccurredAt,
        [property: JsonPropertyName("subject")] string? Subject,
        [property: JsonPropertyName("data")] IReadOnlyDictionary<string, string> Data);
}
