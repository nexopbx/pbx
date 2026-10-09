# Architecture Decision Records

Una ADR breve per ogni decisione importante. Stati: **Proposta** (in attesa del tuo OK) · **Accettata** · **Sostituita**.
Formato: Contesto · Decisione · Alternative · Conseguenze.

| ADR | Titolo | Stato |
|---|---|---|
| [001](ADR-001-avalonia-invece-di-maui.md) | Avalonia UI invece di MAUI | Proposta |
| [002](ADR-002-stack-sip.md) | Stack SIP: SIPSorcery dietro `ISignaling` | **Accettata** (decisione dell'owner) |
| [003](ADR-003-dotnet-lts-e-modalita-app.md) | .NET 10 LTS, app unica e modalità headless/desktop | Proposta |
| [004](ADR-004-grpc-contratto-pubblico.md) | gRPC come contratto pubblico unico | Proposta |
| [005](ADR-005-interprete-deterministico.md) | Interprete di flusso deterministico a effetti | Proposta |
| [006](ADR-006-modello-concorrenza.md) | Modello di concorrenza: attori + thread media dedicati | Proposta |
| [007](ADR-007-persistenza.md) | Persistenza: SQLite/PostgreSQL, stato caldo in memoria | Proposta |
| [008](ADR-008-media-e-webrtc.md) | Media in B2BUA, G.711 e WebRTC con ICE-lite | Proposta |
| [009](ADR-009-ai-sidecar-gpu.md) | AI locale in sidecar con gestione GPU | Proposta |
| [010](ADR-010-firewall.md) | Firewall: deterministico prima, AI asincrona poi | Proposta |
| [011](ADR-011-web-ui.md) | Web UI: React + Vite + xyflow + client Connect | Proposta |
| [012](ADR-012-licenza-e-dipendenze.md) | Licenza del progetto (AGPL-3.0) e policy sulle dipendenze | **Accettata** — resta aperta l'interazione con SIPSorcery (da chiudere prima di M2) |
