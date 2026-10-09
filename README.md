# NexoPBX

Centralino telefonico (PBX / contact-center) per piccole e medie imprese, scritto da zero in C# / .NET 10, con un
**Visual Planner** per disegnare l'intera esperienza di chiamata.

> **Stato:** in sviluppo — Milestone 1 (Foundation) completata. Non ancora utilizzabile in produzione.

## Provalo in 1 minuto

Serve il [.NET 10 SDK](https://dotnet.microsoft.com/download). Poi:

```bash
bash scripts/demo/m1-hello.sh          # Windows: ./scripts/demo/m1-hello.ps1
```

Pubblica il server, crea il primo Owner e un token, avvia `nexopbxd`, chiama REST e gRPC e ascolta gli eventi live con il
client di esempio (`samples/NexoPBX.SampleClient`). Tutto in una cartella temporanea.

## Cosa c'è oggi

Host unico `nexopbxd` (servizio Windows / systemd / console) · API pubblica `nexopbx.v1` (gRPC, REST + OpenAPI 3.1, gRPC-Web, SSE) ·
utenti con ruoli, argon2id, 2FA TOTP, token con scope, audit · SQLite (predefinito) e PostgreSQL con migrazioni ·
event bus con backpressure · health/ready · configurazione YAML + variabili d'ambiente · CI su Windows e Linux.

## Documentazione

* Piano: [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) · [`docs/ROADMAP.md`](docs/ROADMAP.md) · [`docs/DECISIONS.md`](docs/DECISIONS.md) · [`docs/adr/`](docs/adr/README.md)
* Uso: [`docs/OPERATIONS.md`](docs/OPERATIONS.md) · [`deploy/nexopbx.sample.yaml`](deploy/nexopbx.sample.yaml)
* API: [`docs/API_VERSIONING.md`](docs/API_VERSIONING.md) · `GET /openapi/v1.json` sul server in esecuzione
* Limiti noti: [`docs/STUBS.md`](docs/STUBS.md)

## Sviluppo

```bash
dotnet build NexoPBX.slnx && dotnet test NexoPBX.slnx
NEXOPBX_TEST_POSTGRES="Host=localhost;Username=...;Password=...;Database=postgres" dotnet test tests/NexoPBX.Store.Tests   # test su PostgreSQL
```

## Licenza

**GNU AGPL-3.0** — vedi [`LICENSE`](LICENSE). Le dipendenze di terze parti sono elencate in [`THIRD_PARTY_NOTICES.md`](THIRD_PARTY_NOTICES.md).
