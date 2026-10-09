# ADR-006 — Modello di concorrenza: attori + thread media dedicati

**Stato:** Proposta

## Contesto
500 chiamate concorrenti su 4 vCPU, nessun glitch audio, nessun task o handle perso, zero data race nella logica.

## Decisione
- **Attori basati su `Channel<T>`** (singolo lettore): uno per chiamata, uno per coda. Niente lock nella logica di dominio.
- **Thread media dedicati** (K ≈ core/2), tick a 20 ms, priorità alta tramite astrazione (`IThreadPriority`: MMCSS su
  Windows; `nice`/`SCHED_FIFO` opzionale su Linux, con ripiego).
- Ricezione RTP → **ring SPSC lock-free** → jitter buffer; **tap** STT su ring separato che scarta se in ritardo.
- Writer di persistenza **seriale a batch**.
- Ogni `Channel` è **limitato** con politica di saturazione scritta (drop-oldest / coalesce / blocca-con-timeout / degrada).
- `CancellationToken` ovunque; `CallScope` possiede CTS, timer e media; test di leak obbligatori.

## Alternative
- Tutto su `async/await` e thread pool: i tick a 20 ms soffrirebbero della latenza di scheduling → media a parte.
- Un solo lock globale per coda/chiamata: semplice ma fragile e difficile da testare.

## Conseguenze
Logica deterministica e testabile; carico del media isolabile. Costo: disciplina sui messaggi e misura continua di GC e jitter (rischio n. 3).
