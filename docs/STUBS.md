# Stub, lacune note e rimandi

Tutto ciò che è volutamente incompleto, con la milestone in cui verrà chiuso. Niente di questo finge di funzionare:
sono funzioni assenti o parziali, dichiarate qui e nel codice con `TODO(milestone-N)`.

## Milestone 1

| Cosa | Stato | Chiude in |
|---|---|---|
| Esportazione metriche Prometheus / OpenTelemetry | Gli strumenti (`Meter` `NexoPBX.Events`) esistono; manca l'exporter HTTP (il pacchetto Prometheus per ASP.NET è ancora solo pre-release) | `TODO(milestone-12)` |
| Listener IPC (named pipe Windows / socket Unix) per il desktop | Implementato dietro `server.ipc_enabled` (spento), **non coperto da test** né dalla CI | `TODO(milestone-8)` |
| Disattivare il 2FA / reset 2FA o password da parte di un amministratore | Non esiste ancora né in API né in CLI | `TODO(milestone-7)` |
| Endpoint di lettura dell'audit (`audit.read`) | Lo scope esiste, le registrazioni sono scritte e annunciate sul bus, ma non c'è ancora un RPC per leggerle | `TODO(milestone-7)` |
| Gestione dei tenant (crea/elenca) | Il modello ha `tenant_id` ovunque ma esiste solo il tenant `Default`; nessuna API per crearne altri | `TODO(milestone-12)` |
| Rate limit dietro reverse proxy | La chiave è l'indirizzo remoto: dietro un proxy tutti condividono lo stesso. Serve `server.trusted_proxies` + forwarded headers | `TODO(milestone-12)` |
| Payload tipizzati degli eventi | Oggi `Event.data` è una mappa chiave/valore; i payload tipizzati si aggiungeranno in modo additivo | con le prime funzioni di chiamata (M4) |
| Chiave master per i segreti cifrati | Le chiavi di data protection stanno nella cartella dati (permessi 0700; su Windows sigillate con DPAPI). KMS/chiave esterna non ancora supportati | `TODO(milestone-12)` |
| Pacchetto single-file / installer / immagine Docker | Il publish self-contained funziona (usato in CI); single-file, MSI/MSIX, .deb/.rpm e Docker sono in M12 (spike packaging in M8) | `TODO(milestone-12)` |
| Soglie di coverage per i moduli non ancora esistenti | Gate attivi su Abstractions, Auth, Store, Api; si estenderanno a Flow, Queue, Media quando nascono | man mano |
