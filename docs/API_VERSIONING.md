# API versioning policy

The public API is the `nexopbx.v1` gRPC contract in `src/NexoPBX.Contracts/Protos/nexopbx/v1/`. The REST/JSON view
(`/openapi/v1.json`), gRPC-Web and the SSE mirror are all derived from it. The web UI, the desktop app, the sample client
and third parties use nothing else (an architecture test keeps clients from referencing server code).

## Rules inside `v1` — additive changes only

Allowed at any time:

- new services, new RPCs, new messages, new enums;
- new fields (always with a new, never reused field number);
- new enum values (clients must tolerate values they do not know);
- new error `reason` codes (clients must treat unknown reasons as a generic failure of the gRPC status code);
- new event types and new keys in `Event.data`.

Never allowed within `v1` (CI blocks them with `buf breaking`, rule set `FILE`):

- removing or renaming a field, RPC, message, enum or enum value;
- changing a field's number, type, cardinality or JSON name;
- changing the HTTP path or verb of an RPC;
- tightening validation so that a previously valid request is rejected without a documented migration period.

A change that must break something is a new package `nexopbx.v2` served side by side with `v1`; `v1` stays supported
for at least two minor releases after `v2` ships, and the deprecation is announced in the release notes first.

## Conventions every RPC follows

| Topic | Rule |
|---|---|
| Streaming | Unary and server-streaming only (gRPC-Web in browsers cannot do client or bidirectional streaming). |
| Updates | `PATCH`-style RPCs take a `google.protobuf.FieldMask` and an optional `etag`; a stale `etag` fails with `ABORTED` / `ETAG_MISMATCH`. |
| Lists | `page_size` + `page_token`; the token is opaque and only valid for the same filters. |
| Errors | Standard gRPC status codes plus a `google.rpc.ErrorInfo` detail (`domain` = `nexopbx.v1`, `reason` = stable code). Field problems add a `google.rpc.BadRequest`. In REST the body is the `google.rpc.Status` JSON. |
| Authentication | `authorization: Bearer nxp_...` (gRPC metadata or HTTP header). No cookies, so there is no CSRF surface. |
| Authorization | Each RPC requires a named scope (`system.read`, `users.write`, ...), visible as `x-required-scopes` in the OpenAPI document. |
| Time | RFC 3339 UTC in JSON (`google.protobuf.Timestamp`). |
| IDs | UUID strings. |

## How the contract is checked

1. `buf lint` (rule set `BASIC`) on every push.
2. `buf breaking` against the default branch on every push and pull request.
3. The OpenAPI 3.1 document is generated from the compiled descriptors at runtime, and tests check that every RPC has a REST
   mapping and an operation, that every `$ref` resolves, and that the scopes it advertises are the ones the code enforces.
4. A test fails if any RPC is public by omission: each one needs a known scope or an explicit `[AllowAnonymous]`
   (today only `UserService.Login`).
