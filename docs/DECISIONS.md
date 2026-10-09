# NexoPBX — Decisioni di default

Scelte ragionevoli che ho preso dove il prompt era ambiguo, così non mi fermo a chiedere. Ognuna è reversibile a basso
costo; se non ti convince la cambiamo. Le decisioni grandi stanno invece nelle [ADR](adr/README.md).

| ID | Decisione | Motivo / costo di cambiarla |
|---|---|---|
| D-001 | Binario unico `nexopbxd` con sottocomandi: `run`, `service`, `desktop`, `llm bench`, `flow simulate`, `db migrate` | Un solo artefatto da distribuire; basso costo |
| D-002 | Aggiungo `NexoPBX.Abstractions` (clock, id, event bus, tenant, primitive di dominio) e `NexoPBX.Testing` (clock virtuale, telefonia finta, runner di scenari) al layout | Servono per far rispettare le regole di dipendenza e per i test deterministici |
| D-003 | `tenant_id` su ogni riga fin da subito; l'UI mostra un solo tenant "default" finché non serve | Aggiungerlo dopo costerebbe molto di più |
| D-004 | Porte di default configurabili, tutte su loopback finché non si configura altro: HTTP/1.1 8080, gRPC h2c 8081, HTTPS 8443 (spento); SIP 5060 (UDP/TCP), 5061 (TLS), RTP 16384–32767 (pari) arriveranno con M2 | Sicuro per impostazione predefinita |
| D-005 | Configurazione in **YAML** (accetta anche JSON) con override `NEXOPBX__SEZIONE__CHIAVE` | Leggibile da umani, ottimo per Docker |
| D-006 | Il PBX resta sempre nel percorso media (B2BUA); nessun *direct media* | Serve per prompt, attese, STT, registrazione |
| D-007 | Softphone web: SIP su WSS + G.711/SRTP; Opus opzionale | Riuso dello stack SIP; nessun codec nativo obbligatorio |
| D-008 | Obiettivo EWT: ≥ 80% delle chiamate entro ±max(60 s, 25%) su traffico stazionario | Da ratificare con dati reali in M4 |
| D-009 | Firewall: parte in **modalità monitor** (solo log); fail-mode `fail_open_for_known` / `fail_closed_for_unknown`; budget 50 ms | Evita blocchi accidentali al primo avvio |
| D-010 | Parametri argon2id: minimo OWASP, tarati con benchmark in M1 | Dipendono dall'hardware |
| D-011 | Date in UTC nel DB; fuso orario per tenant per orari d'ufficio e slot | Standard |
| D-012 | Numeri telefonici normalizzati in **E.164** con regione di default `IT` (porting di libphonenumber) | Prompt in italiano; modificabile per tenant |
| D-013 | Musica d'attesa fornita solo **royalty-free / CC0 o sintetizzata**; l'utente può caricare la propria | Evita problemi di licenza |
| D-014 | Lingue: IT di default, EN seconda; stringhe esterne (`.resx` lato server, JSON/i18next lato web) | Come da prompt |
| D-015 | Package manager web: **npm** con lockfile (meno parti mobili su CI Windows) | Cambiabile in pnpm |
| D-016 | .NET: *Central Package Management*, lock file NuGet, `global.json` con roll-forward alla feature band più recente | Build riproducibili |
| D-017 | Le chiamate non sopravvivono al riavvio (vedi ARCHITECTURE §7) | Il dialogo SIP/RTP non è ricostruibile |
| D-018 | Un PR per milestone dal ramo di lavoro, **aperto solo quando lo chiedi** | Nessuna sorpresa sul repo |
| D-019 | Test di tempo reale su runner CI condivisi sono **tolleranti**; le misure di prestazione vanno in un job separato non bloccante | I runner condivisi hanno jitter imprevedibile |
| D-020 | Il container di lavoro usa il .NET SDK da `apt` (Ubuntu); la CI usa `actions/setup-dotnet` ufficiale | `dot.net` è bloccato dalla rete del container |
| D-021 | Signaling con **SIPSorcery** dietro `ISignaling` (ADR-002 accettata); `THIRD_PARTY_NOTICES` e eccezione nominale nel license check | Decisione dell'owner; piano B = stack proprio |
| D-022 | Nessun SMS (il trunk non lo supporta): link "posizione in coda" via QR/widget web; `ISmsSender` con adapter `null` + webhook | Si può aggiungere un provider in seguito senza toccare il resto |
| D-023 | Nessuna GPU in sviluppo: CI e sviluppo su **CPU**; CUDA/Vulkan scritti dietro sonde finte e marcati "non validati su hardware reale" in `docs/STUBS.md` finché non provati | Il supporto GPU è per altri utenti |
| D-024 | Matrice di interoperabilità iniziale: **un softphone + un Yealink + il trunk dell'owner**; altri profili si aggiungono quando disponibili | Hardware realmente disponibile |
| D-025 | Licenza del progetto: **AGPL-3.0-only** (SPDX in `Directory.Build.props`); `LICENSE` invariato; header SPDX nei nuovi file | Decisione dell'owner (ADR-012); `or-later` si può aggiungere, il contrario no |
| D-026 | Hash delle password con argon2id (libreria MIT) e token API opachi (256 bit casuali) salvati **solo come hash SHA-256** | Un database sottratto non rivela né password né token |
| D-027 | Gli id sono UUIDv7 **monotoni** (contatore a 12 bit, RFC 9562): `Guid.CreateVersion7()` non ordina nello stesso millisecondo e rendeva instabile "più recente per primo" | Scoperto da un test intermittente in M1 |
| D-028 | OpenAPI 3.1 generato a runtime dai descrittori proto (non da `Microsoft.AspNetCore.OpenApi`) | Spike M1, vedi ADR-004 |
| D-029 | Le chiavi di configurazione sono in snake_case (`http_listen`) e vengono normalizzate una volta all'avvio | Coerenza con l'API; il binder .NET non fa corrispondere gli underscore |
| D-030 | I comandi CLI che toccano lo schema applicano le migrazioni se `database.auto_migrate` è attivo, come il server | Su installazione nuova `user bootstrap` deve funzionare da solo |
| D-031 | Gate di coverage in CI: Abstractions 95%, Auth 90%, Store 90%, Api 80% (soglie sotto i valori misurati; mai abbassarle per far passare la CI) | `scripts/ci/coverage_gate.py` |
| D-032 | La suite di integrazione `Api.Tests` non gira in parallelo (usa variabili d'ambiente di processo che leggono tutti gli host) | Durata ~5 s; correttezza prima della velocità |
