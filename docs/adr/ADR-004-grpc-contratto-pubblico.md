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
- Collegamento locale: **named pipe** (Windows) / **Unix domain socket** (Linux), oltre a TCP+TLS; mTLS opzionale per client macchina.
- Client TypeScript generato dagli stessi proto (ADR-011).

## Da verificare (spike M1)
Se il transcoding ufficiale genera **OpenAPI 3.1**; in caso contrario si genera a build-time dai `.proto`
(plugin OpenAPI) senza cambiare il contratto.

## Conseguenze
Nessuna funzione è "solo per la UI": ciò che fa la web app lo può fare un client C#. Il `SampleClient` lo dimostra in CI.
