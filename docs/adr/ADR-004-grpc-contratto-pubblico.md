# ADR-004 — gRPC come contratto pubblico unico

**Stato:** Proposta

## Contesto
Web UI, desktop e terze parti devono usare la stessa API, senza porte di servizio nascoste.

## Decisione
- `NexoPBX.Contracts` con proto `nexopbx.v1` (`Grpc.AspNetCore`) come **unica fonte di verità**.
- **REST/JSON + OpenAPI** tramite gRPC JSON transcoding; **gRPC-Web** per il browser; mirror **SSE/WebSocket** degli eventi.
- **Solo unary e server-streaming** (gRPC-Web non supporta client/bidi streaming) — regola di progetto.
- Concorrenza ottimistica (`etag`/`version`), *field mask*, paginazione a token, modello d'errore uniforme.
- Compatibilità additiva dentro `v1` imposta da **`buf breaking`** in CI (`docs/API_VERSIONING.md`).
- Listener separati: HTTP/1.1 (REST, gRPC-Web, SSE), HTTP/2 in chiaro (gRPC nativo; senza TLS non esiste ALPN per condividere la porta), HTTPS con entrambi.
- Collegamento locale: **named pipe** (Windows) / **Unix domain socket** (Linux), oltre a TCP+TLS; mTLS opzionale per client macchina.
- Client TypeScript generato dagli stessi proto (ADR-011).

## Esito dello spike OpenAPI 3.1 (M1)
Il generatore integrato di ASP.NET (`Microsoft.AspNetCore.OpenApi`) produce OpenAPI 3.1 ma **non include le rotte gRPC
trascodificate** (verificato: elencava solo l'endpoint SSE). Soluzione adottata: `ProtoOpenApiGenerator` costruisce il
documento OpenAPI **3.1** a runtime (`/openapi/v1.json`) direttamente dai descrittori dei `.proto` e dalle regole
`google.api.http`; gli scope richiesti da ogni operazione (`x-required-scopes`) sono letti dagli attributi `[Authorize]`
delle implementazioni. Così il documento non può divergere dal contratto. `Microsoft.AspNetCore.OpenApi` non è più una
dipendenza.

Altre scoperte di M1: `Grpc.StatusProto` porta già i tipi `google.api.*` (i `.proto` vendored servono solo come import);
il serializzatore JSON del transcoding richiede `TypeRegistry` con `ErrorInfo`/`BadRequest`, altrimenti gli errori con
dettagli rispondono 500.

## Conseguenze
Nessuna funzione è "solo per la UI": ciò che fa la web app lo può fare un client C#. Il `SampleClient` lo dimostra in CI.
