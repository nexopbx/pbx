# NexoPBX — Roadmap proposta

> Stato: **PROPOSTA, in attesa di approvazione** — aggiornata con le risposte dell'owner (SIPSorcery, nessuna GPU, trunk senza SMS, prove con softphone + Yealink).
> Le stime sono in **settimane-persona di un ingegnere senior** (intervallo basso–alto) e sono indicative: con
> Claude Code la scrittura del codice è più rapida, ma i tempi reali sono dominati dai cicli di revisione,
> dai test di interoperabilità su dispositivi veri e dal tuning delle prestazioni. Le rivedo a fine di ogni milestone.

## Regole valide per OGNI milestone

Ogni milestone si chiude solo con: **codice funzionante · test verdi · documentazione aggiornata · script di demo in
`scripts/demo/` · commit chiaro · CI verde su Windows e Linux**. Tutto ciò che è "finto" sta dietro un'interfaccia,
marcato `TODO(milestone-N)` ed elencato in `docs/STUBS.md`. Le scelte ambigue finiscono in `docs/DECISIONS.md`.

**Gate di qualità in CI (attivi dalla M1):** build con warning-as-errors e analizzatori · `dotnet format --verify-no-changes`
· test su `windows-latest` + `ubuntu-latest` · `buf lint` + `buf breaking` sui proto · scansione vulnerabilità
(`dotnet list package --vulnerable`, `npm audit`) · **license check con allow-list** · scansione segreti ·
test di architettura sulle regole di dipendenza · soglie di coverage sui pacchetti critici.

## Quadro d'insieme

| M | Titolo | Stima (sett.-persona) | Rischio | Dipende da |
|---|---|---|---|---|
| 0 | Pianificazione + verifica toolchain (**questo documento**) | — | — | — |
| 1 | Foundation — **completata, in attesa di review** | 3–4 | Basso | 0 |
| 2 | Signaling & media core | 5–7 | **Alto** | 1 |
| 3 | Flow engine | 4–5 | Medio | 1, 2 (fake) |
| 4 | Code, agenti, EWT, hold program, CDR | 6–8 | Medio | 2, 3 |
| 5 | Sticky agent | 2 | Basso | 4 |
| 6 | Richiamate (never-drop + programmate) | 5–6 | Medio-alto | 4, 5 |
| 7 | Web UI + Visual Planner (+ WebRTC bridge) | 11–15 | **Alto** | 3, 4, 6 |
| 8 | Desktop Avalonia | 5–6 | Medio | 1, 7 (contratto) |
| 9 | Integrazioni (API node, Instatus, webhook) | 4–5 | Basso-medio | 3, 7 |
| 10 | AI locale + GPU, poi AI Firewall | 8–10 | **Alto** | 1, 2 |
| 11 | STT-ready, registrazione, consenso | 3–4 | Medio | 2, 10 |
| 12 | Hardening & release candidate | 6–8 | Medio | tutte |
| | **Totale indicativo** | **≈ 62–80** | | |

> Le milestone 8, 9 e 10 possono procedere **in parallelo** dopo la 7 se vorrai più flussi di lavoro.

---

## M1 — Foundation  *(3–4 sett.)*

**Contenuto:** soluzione con `Directory.Build.props` (nullable, warnings-as-errors, analizzatori), *Central
Package Management*, `global.json`; configurazione YAML/JSON + override da ambiente; Serilog JSON; `Store` con
migrazioni **SQLite e PostgreSQL**; `Auth` (utenti, ruoli, argon2id, TOTP, token scoped, audit); scheletro
`Contracts` (`SystemService`, `UserService`, `EventService`); health/ready; event bus; host con modalità
**headless** (`nexopbxd run|service`), servizio Windows e unit systemd; CI a matrice; `buf` in CI.

**Spike (limitati nel tempo, esiti in ADR):** (a) OpenAPI 3.1 da gRPC JSON transcoding; (b) servizio Windows
+ systemd su runner reali; (c) licenza del toolchain MSI; (d) misura base di timer/sleep media su Windows e Linux.

**Esito (M1 completata):** tutto il contenuto sopra è realizzato. Spike: (a) OpenAPI 3.1 → il generatore integrato non include le rotte trascodificate, generiamo il documento dai descrittori (ADR-004); (b) servizio Windows e systemd → script di installazione e smoke test eseguiti in CI; (c) toolchain MSI → rimandato allo spike di M8/M12 (nessun MSI in M1); (d) timer/sleep media → rimandato a M2, dove serve davvero. Dettagli e limiti dichiarati: `docs/STUBS.md`.

**Accettazione:** `nexopbxd run` parte su entrambi gli OS; `SampleClient` fa login con token e legge lo stato; migrazioni
applicate su SQLite e PostgreSQL in CI; health/ready rispondono; shutdown con drain.
**Demo:** `scripts/demo/m1-hello.{sh,ps1}`.

## M2 — Signaling & media core  *(5–7 sett.)*  — *milestone più rischiosa*

**Contenuto:** signaling su **SIPSorcery** (parser, transazioni, Digest, UDP/TCP/TLS/WS) dietro `ISignaling`, con **B2BUA, dialoghi e logica di chiamata nostri** · registrazione
interni · chiamate inbound/outbound in B2BUA · RTP/SRTP · **G.711 a-law/µ-law** · jitter buffer adattivo · DTMF RFC 4733
+ fallback in-band · mixer · **prompt player** · switch relay/mix senza glitch · **telefonia finta + clock virtuale**
(`NexoPBX.Testing`) · fuzzing dei nostri parser · suite di conformità (scenari SIPp) · **spike di prestazioni e interoperabilità di SIPSorcery** (decide se restare o attivare il piano B).

**Spike di carico anticipato:** 100 → 500 chiamate G.711 su 4 vCPU con misura di jitter e pause GC.

**Accettazione:** softphone e Yealink si chiamano tramite il PBX con audio bidirezionale; prompt riprodotto con sequenza RTP
continua; DTMF rilevato in entrambe le modalità; il generatore di carico regge l'obiettivo o documenta il limite
misurato; nessun leak di task/handle dopo 10 000 chiamate sintetiche.
**Demo:** `scripts/demo/m2-call.{sh,ps1}`.

## M3 — Flow engine  *(4–5 sett.)*

**Contenuto:** modello a grafo, **schema JSON versionato** (import/export, validazione lato server), validazione e lint,
versioni draft → publish → rollback con `FlowSnapshot` immutabili, **interprete deterministico** a effetti, nodi base
(Entry, Time Condition, Play/TTS, Menu, Set Variable, Condition, Hangup, Transfer, Voicemail, Sub-flow, Note), adapter
**TTS** con pre-render e cache, `FlowService`, **CLI simulatore** (`nexopbxd flow simulate`).

**Accettazione:** golden test per ogni nodo; fuzz dell'import JSON; pubblicazione che non tocca chiamate in corso
(scenario n. 6 del prompt, in versione base); stesso interprete in produzione e in simulazione.
**Demo:** `scripts/demo/m3-flow-sim`.

## M4 — Code, agenti, EWT, hold program, CDR  *(6–8 sett.)*

**Contenuto:** strategie di coda (ring-all, round-robin, least-recent, fewest-calls, skill-based, priorità) ·
presenza e stati agente · riservazioni a **lease** · **motore EWT** (proiezione a min-heap, AHT con media mobile
esponenziale, residuo degli agenti occupati, banda di confidenza, isteresi) · annunci di posizione/EWT (§5.6) ·
**Hold Program** (§5.1: regole, finestre orarie, condizioni, rotazione, barge-in, hot-reload) · **harness di
simulazione EWT** con target in regressione · **CDR e report** (service level, abbandoni, ASA, AHT) ·
pagina **"posizione in coda"** per il chiamante (token a scadenza) e muro supervisore (API) · link consegnato via **QR / widget web** (il trunk non ha SMS); `ISmsSender` resta un'interfaccia con adapter `null` e webhook per chi lo vorrà.

**Accettazione:** test deterministici di equità/ordinamento; obiettivo EWT rispettato sul traffico sintetico; prompt
che non si sovrappongono; DTMF che interrompe quando il nodo lo consente.
**Demo:** `scripts/demo/m4-queue`.

## M5 — Sticky agent  *(2 sett.)*

**Contenuto:** Service Rule con `sticky`, `sticky_window`, chiave (CLI o CLI+regola) · riconnessione diretta allo stesso agente
· slot di attesa sticky dedicato con priorità e tempo massimo, poi ripiego motivato nella coda normale · contesto
all'agente ("richiamata dopo caduta alle 14:32") · casi limite: agente offline, cambio stato, più chiamanti per lo
stesso agente (FIFO), numero nascosto (nessuno sticky, mai indovinare), scadenza finestra.

**Accettazione:** scenario n. 1 del prompt (sticky con agente occupato) + test dei casi limite.

## M6 — Richiamate  *(5–6 sett.)*

**Contenuto:** **never-drop (§5.8)**: virtual hold persistito, riservazione agente, modalità customer-first/agent-first,
retry con degrado documentato, idempotenza, ripresa dopo riavvio · **programmata (§5.7)**: offerta oltre soglia
EWT, generazione slot da disponibilità/orari, **priorità a livello di agente** nello slot, capacità per slot, ticket
all'agente con whisper/screen-pop, chiamata in uscita al cliente, retry con backoff, messaggeria/no-answer, abort ·
numeri in E.164, liste DNC, orari consentiti.

**Accettazione:** scenari n. 2 e 3 del prompt; test property-based sull'ordinamento; test di crash/restart a metà
macchina a stati.

## M7 — Web UI + Visual Planner  *(11–15 sett.)*  — *il cuore del prodotto*

Tre fette verticali, ciascuna rilasciabile:

- **7a — Canvas e nodi:** infinite canvas, minimap, snap, auto-layout, multi-selezione, copia/incolla, undo/redo,
  palette comandi `Ctrl+K`, ricerca, tutti i tipi di nodo, porte tipizzate, validazione archi, import/export JSON.
- **7b — Editing in-place e lint:** drawer laterale (doppio clic su Queue → config completa), creazione/modifica di
  code, gruppi di agenti, prompt, orari e integrazioni **senza lasciare il canvas**; pannello lint (irraggiungibili,
  loop senza uscita, prompt mancanti, code senza agenti, percorsi senza hangup).
- **7c — Versioni, simulatore, overlay:** draft/validate/publish/rollback, diff, **"Test call"** (DTMF virtuali, mock API,
  mock coda, viaggio nel tempo), overlay live (chiamate, profondità coda, EWT).
- **7d — WebRTC bridge:** softphone agente (SIP su WSS + ICE-lite + DTLS-SRTP, G.711 di default, Opus opzionale), dashboard,
  admin, tablet-friendly per supervisori, tema chiaro/scuro, i18n IT/EN, **wizard di primo avvio** (sistema di esempio
  funzionante in < 5 minuti).

**Accettazione:** E2E Playwright "crea 4 code, collegale, pubblica, simula" verde su entrambi gli OS; la UI usa
solo API pubbliche (test che lo impone); un agente risponde da browser a una chiamata reale.

## M8 — Desktop Avalonia  *(5–6 sett.)*

**Contenuto:** tray e centro di controllo (stato, start/stop/restart, chiamate live, config, log, firewall, monitor
GPU/LLM, aggiornamenti, "apri web admin") · connessione **locale** (pipe/UDS) o **remota** (TLS/mTLS) · modalità
*embedded* con lock di istanza singola · assembly Desktop caricato a richiesta · **spike packaging** (single-file vs
due output) · test Avalonia.Headless.

**Accettazione:** il desktop usa **solo** il contratto pubblico (verificato da test di architettura); CI desktop verde
su entrambi gli OS; collegamento remoto a un server headless.

## M9 — Integrazioni  *(4–5 sett.)*

**Contenuto:** nodo **API Call** (GET/POST, segreti, timeout, retry, circuit breaker, cache TTL, JSONPath → variabili, mock
per il simulatore) · **connettore Instatus** (`status.degraded`, `status.incident_title`, `status.components[]`) ·
**template "annuncio dinamico"** (API → Condition → TTS con variabili → Menu con timeout) · fail-safe con budget di
risposta · **webhook firmati** con retry e log di consegna · documentazione API pubblica e `SampleClient` completo
(login, code, stream chiamate, pubblicazione flusso) eseguito in CI su entrambi gli OS.

**Accettazione:** scenario n. 4 del prompt (Instatus degradato → tasto 1).

## M10 — AI locale + GPU, poi AI Firewall  *(8–10 sett.)*

- **10a — Ai/GPU (≈ 3–4):** `ILocalLlm`/`IChatClient`, supervisor del sidecar (avvio, health, riavvio con backoff),
  rilevamento GPU, VRAM budget, import GGUF con checksum, hot-swap + rollback, metriche (p50/p99, token/s, VRAM),
  `nexopbxd llm bench`, degrado a gradini.
- **10b — Firewall (≈ 5–6):** pre-filtro deterministico, `IPIntel` (GeoIP/ASN offline, DNS inverso, cache), memoria per
  IP/subnet/account, verdetto JSON validato (grammatica + validazione), modalità monitor/learning, `FirewallDecision`
  ricercabile, whitelist/blocklist (IP, CIDR, ASN, paese) con scadenza, feed live, pagina profilo IP, override in un
  clic, *enforcer* OS opzionale, sanificazione anti prompt-injection.

**Accettazione:** scenario n. 5 del prompt (blocca scanner, lascia passare la LAN); p99 deterministico < 50 ms (obiettivo reale
< 1 ms); sidecar ucciso → chiamate invariate; i percorsi GPU si validano con sonde finte (nessuna GPU disponibile all'owner): la prova su GPU vera arriverà da chi ne ha una.

## M11 — STT-ready  *(3–4 sett.)*

**Contenuto:** interfaccia `stt.Engine` in streaming · **tap** per-leg (8/16 kHz) a impatto zero · adapter locale
(compatibile whisper.cpp) + `null` + `mock` · switch per flusso/coda, **annuncio di consenso obbligatorio**, retention,
hook di redazione PII · eventi di trascrizione su stream e API · nodo Planner "ascolta intento/cifre/sì-no" ·
**registrazione chiamate con consenso**.

**Accettazione:** tutto compila e gira senza runtime STT; tap con carico simulato senza alterare i numeri di jitter.

## M12 — Hardening & release candidate  *(6–8 sett.)*

**Contenuto:** test di carico (≥ 500 chiamate) e **soak 24 h** con numeri pubblicati · fuzzing esteso · **export/cancellazione GDPR**
(chiamante, memoria firewall, trascrizioni) · installer **MSI/MSIX**, pacchetti **.deb/.rpm**, **immagine Docker** +
`docker compose up` con flusso demo · aggiornamento in-app (anche offline) · Prometheus/OpenTelemetry rifiniti ·
documentazione completa (README, architettura, riferimento API generato, guida al Planner, guida al firewall,
operations, note di sicurezza) · scansioni finali (vulnerabilità, licenze).

**Accettazione:** *Definition of Done* globale del prompt (§10).

---

## Cosa verifico dove

| Dove | Cosa |
|---|---|
| Container di lavoro (Linux) | Build, unit, simulazione a clock virtuale, fuzzing, integrazione SIP/RTP locale, Playwright, PostgreSQL via Docker |
| CI GitHub Actions | Stessi test su **Windows + Linux**, SampleClient, desktop headless, scansioni |
| **Hardware tuo** | Registrazione del softphone e dello **Yealink**, prova con il trunk vero, qualità audio percepita (nessuna GPU: i benchmark GPU restano da fare con chi ne ha una) |

## Rischi di calendario da tenere d'occhio

M2 (media ad alte prestazioni + integrazione con SIPSorcery) e M7 (Planner) pesano più di metà del rischio. Se serve
accorciare, i tagli meno dolorosi sono: Opus, Vulkan/AMD (restare su CUDA+CPU), registrazione chiamate, arm64.
