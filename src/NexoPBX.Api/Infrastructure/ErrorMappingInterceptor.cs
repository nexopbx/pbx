// SPDX-License-Identifier: AGPL-3.0-only
using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.Extensions.Logging;
using NexoPBX.Auth;
using NexoPBX.Store;

namespace NexoPBX.Api.Infrastructure;

/// <summary>Turns domain exceptions into the uniform error model. Unknown failures are logged and hidden from the client.</summary>
public sealed partial class ErrorMappingInterceptor : Interceptor
{
    private readonly ILogger<ErrorMappingInterceptor> _logger;

    /// <summary>Creates the interceptor.</summary>
    public ErrorMappingInterceptor(ILogger<ErrorMappingInterceptor> logger) => _logger = logger;

    /// <inheritdoc />
    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            return await continuation(request, context);
        }
        catch (Exception ex) when (ex is not RpcException && ex is not OperationCanceledException)
        {
            throw Map(ex, context.Method);
        }
    }

    /// <inheritdoc />
    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        try
        {
            await continuation(request, responseStream, context);
        }
        catch (Exception ex) when (ex is not RpcException && ex is not OperationCanceledException)
        {
            throw Map(ex, context.Method);
        }
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Error, Message = "Unhandled exception in {Method}")]
    private partial void LogUnhandled(Exception exception, string method);

    private RpcException Map(Exception ex, string method)
    {
        switch (ex)
        {
            case NexoValidationException v:
                return ApiErrors.Invalid(v.Field, v.Message);
            case DuplicateException d:
                return ApiErrors.Create(StatusCode.AlreadyExists, "ALREADY_EXISTS", d.Message);
            case ConcurrencyConflictException c:
                return ApiErrors.Create(StatusCode.Aborted, "ETAG_MISMATCH", c.Message);
            case KeyNotFoundException k:
                return ApiErrors.Create(StatusCode.NotFound, "NOT_FOUND", k.Message);
            case ArgumentException a:
                return ApiErrors.Create(StatusCode.InvalidArgument, "INVALID_ARGUMENT", a.Message);
            case InvalidOperationException io:
                return ApiErrors.Create(StatusCode.FailedPrecondition, "FAILED_PRECONDITION", io.Message);
            default:
                LogUnhandled(ex, method);
                return ApiErrors.Create(StatusCode.Internal, "INTERNAL", "Internal error.");
        }
    }
}
