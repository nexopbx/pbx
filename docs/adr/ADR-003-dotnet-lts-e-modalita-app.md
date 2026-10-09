# ADR-003 — .NET 10 LTS, app unica, modalità headless e desktop

**Stato:** Proposta

## Contesto
Il prompt richiede di verificare l'LTS corrente all'avvio. Verificato (policy Microsoft): **.NET 10 è LTS, supportato
fino al 14/11/2028**; .NET 8 e 9 terminano il 10/11/2026. Una sola applicazione deve funzionare come servizio senza
schermo **e** con UI desktop.

## Decisione
- **.NET 10 (LTS)**, soluzione unica, `Nullable` attivo, warning come errori, analizzatori attivi, *Central Package
  Management*, `global.json` con roll-forward.
- Binario `nexopbxd` con modalità: `run`/`service` (headless: Windows Service, systemd con `Type=notify`, Docker) e
  `desktop`.
- **Desktop = client dell'API pubblica.** Se un daemon è già attivo, il desktop si **collega** (pipe/UDS locale o TLS
  remoto); altrimenti può **ospitare l'host in-process** (*embedded*). Un lock di istanza singola evita doppi host.
- In servizio Windows (sessione 0) non si può mostrare UI: il tray è sempre un client.
- L'assembly `NexoPBX.Desktop` e le librerie native di Avalonia/Skia vengono **caricati solo in modalità desktop**, così un
  container headless non li tocca.

## Alternative
- Due eseguibili distinti (daemon + app desktop): più semplice per il packaging, ma meno fedele a "un'unica applicazione".
  **Spike in M8** per decidere fra *single-file con caricamento a richiesta* e *due output dallo stesso progetto*.

## Conseguenze
Il publish *self-contained single-file* è garantito per l'headless; per il desktop dipende dallo spike. Il container di
lavoro usa l'SDK 10.0.112 di Ubuntu; la CI l'SDK ufficiale (D-020).
