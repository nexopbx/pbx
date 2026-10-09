// SPDX-License-Identifier: AGPL-3.0-only
using Google.Protobuf.WellKnownTypes;
using Google.Rpc;
using Grpc.Core;

namespace NexoPBX.Api.Infrastructure;

/// <summary>Builds errors in the one machine-readable shape the API uses: gRPC status + <c>ErrorInfo</c> detail.</summary>
public static class ApiErrors
{
    /// <summary>Error domain placed in every <see cref="ErrorInfo"/>.</summary>
    public const string Domain = "nexopbx.v1";

    /// <summary>Creates an <see cref="RpcException"/> carrying a stable <paramref name="reason"/> code.</summary>
    public static RpcException Create(
        Grpc.Core.StatusCode code,
        string reason,
        string message,
        IReadOnlyDictionary<string, string>? metadata = null,
        IEnumerable<(string Field, string Description)>? fieldViolations = null)
    {
        var info = new ErrorInfo { Reason = reason, Domain = Domain };
        if (metadata is not null)
        {
            foreach (var (key, value) in metadata)
            {
                info.Metadata[key] = value;
            }
        }

        var status = new Google.Rpc.Status { Code = (int)code, Message = message };
        status.Details.Add(Any.Pack(info));

        if (fieldViolations is not null)
        {
            var bad = new BadRequest();
            foreach (var (field, description) in fieldViolations)
            {
                bad.FieldViolations.Add(new BadRequest.Types.FieldViolation { Field = field, Description = description });
            }

            if (bad.FieldViolations.Count > 0)
            {
                status.Details.Add(Any.Pack(bad));
            }
        }

        return status.ToRpcException();
    }

    /// <summary>The caller lacks permission.</summary>
    public static RpcException Denied(string reason, string message) => Create(Grpc.Core.StatusCode.PermissionDenied, reason, message);

    /// <summary>The request is malformed.</summary>
    public static RpcException Invalid(string field, string message) =>
        Create(Grpc.Core.StatusCode.InvalidArgument, "VALIDATION_FAILED", message, fieldViolations: [(field, message)]);

    /// <summary>The thing asked for does not exist.</summary>
    public static RpcException NotFound(string what) => Create(Grpc.Core.StatusCode.NotFound, "NOT_FOUND", $"{what} was not found.");
}
