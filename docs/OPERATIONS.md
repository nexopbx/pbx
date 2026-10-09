# Guida operativa (Milestone 1)

Come installare, avviare e controllare `nexopbxd`. Questa guida cresce a ogni milestone.

## Primo avvio in 2 minuti (qualsiasi sistema)

```bash
dotnet publish src/NexoPBX.App -c Release -r linux-x64 --self-contained -o ./nexopbx   # o win-x64
cd nexopbx
printf 'una passphrase lunga almeno 12 caratteri\n' | ./nexopbxd user bootstrap --username admin --password-stdin
./nexopbxd run
```

Apri `http://127.0.0.1:8080/health/ready`. Il database SQLite viene creato e migrato da solo nella cartella `./data`.
Lo script `scripts/demo/m1-hello.sh` (o `.ps1`) fa tutto questo da zero e prova anche il client di esempio.

## Cosa ascolta (di default solo `127.0.0.1`)

| Listener | Predefinito | Protocolli | Per chi |
|---|---|---|---|
| `server.http_listen` | `127.0.0.1:8080` | HTTP/1.1 | REST, gRPC-Web (browser), SSE, health, OpenAPI |
| `server.grpc_listen` | `127.0.0.1:8081` | HTTP/2 in chiaro | client gRPC nativi (SampleClient, strumenti) |
| `server.https_listen` | spento | HTTP/1.1 + HTTP/2 con TLS | produzione: richiede `certificate_path` |

Per esporre il servizio in rete configura `https_listen` con un certificato. Non mettere `http_listen` su `0.0.0.0`
senza TLS: i token viaggerebbero in chiaro.

## Configurazione

File YAML (esempio commentato: `deploy/nexopbx.sample.yaml`), tutte le chiavi opzionali. Ogni chiave si può impostare
anche da variabile d'ambiente: `NEXOPBX__SEZIONE__CHIAVE` (es. `NEXOPBX__DATABASE__PROVIDER=postgres`).
Precedenza: valori predefiniti < file < ambiente.

| Modo | File di configurazione | Cartella dati |
|---|---|---|
| `nexopbxd run` | `./nexopbx.yaml` | `./data` |
| `nexopbxd service` (Windows) | `C:\ProgramData\NexoPBX\nexopbx.yaml` | `C:\ProgramData\NexoPBX\data` |
| `nexopbxd service` (Linux) | `/etc/nexopbx/nexopbx.yaml` | `/var/lib/nexopbx` |

Si possono cambiare con `--config`, `--data-dir`, o con `NEXOPBX_CONFIG` / `NEXOPBX_DATA_DIR`.

## Installare come servizio

**Linux (systemd)**
```bash
sudo useradd --system --home-dir /var/lib/nexopbx --shell /usr/sbin/nologin nexopbx
sudo mkdir -p /opt/nexopbx && sudo cp -r ./nexopbx/. /opt/nexopbx/
sudo cp deploy/systemd/nexopbxd.service /etc/systemd/system/
sudo systemctl daemon-reload && sudo systemctl enable --now nexopbxd
# primo utente (usa gli stessi percorsi del servizio):
printf 'passphrase lunga\n' | sudo -u nexopbx /opt/nexopbx/nexopbxd --config /etc/nexopbx/nexopbx.yaml --data-dir /var/lib/nexopbx user bootstrap --username admin --password-stdin
```

**Windows** (PowerShell come amministratore)
```powershell
.\deploy\windows\install-service.ps1 -BinDir 'C:\Program Files\NexoPBX'
# disinstallare (i dati restano):  .\deploy\windows\uninstall-service.ps1
```

La CI installa davvero il pacchetto come unit systemd e come servizio Windows a ogni modifica (`scripts/ci/smoke-*`).

## Database

* **SQLite** (predefinito): un file `nexopbx.db` (modalità WAL). Backup consigliato a servizio attivo:
  `sqlite3 data/nexopbx.db ".backup 'backup.db'"`. Non copiare il solo `.db` mentre il servizio scrive.
* **PostgreSQL**: `database.provider: postgres` e `database.connection_string`. Preferisci
  `database.auto_migrate: false` e applica le migrazioni tu con `nexopbxd db migrate` durante gli aggiornamenti.
* Comandi: `nexopbxd db status` (codice 0 = aggiornato, 2 = migrazioni da applicare o database irraggiungibile), `nexopbxd db migrate`.

## Utenti e token da riga di comando

* `nexopbxd user bootstrap --username NOME --password-stdin`: crea il primo Owner (rifiutato se esiste già un utente).
  La password non si passa mai come argomento (resterebbe nella cronologia della shell).
* `nexopbxd token create --user NOME --name ETICHETTA --scope system.read --scope events.subscribe [--days N]`:
  stampa il token **una sola volta**. Un token non può avere più permessi dell'utente a cui appartiene.
* Dopo 5 password errate l'account si blocca per 15 minuti; il login è limitato a 10 tentativi/minuto per indirizzo.

## Controlli di salute

| URL | Significato |
|---|---|
| `/health/live` | il processo risponde |
| `/health/ready` | database raggiungibile e migrato (503 altrimenti) |
| `GET /v1/system/health` (con token) | dettaglio per componente |

## Codici di uscita di `nexopbxd`

`0` ok · `1` errore di configurazione o uso · `2` operazione fallita · `3` un'altra istanza usa già la cartella dati.

## Log

JSON strutturato su console (`logging.format: text` per leggerlo a occhio). Con `logging.file` scrive anche un file giornaliero
(14 conservati). Le password, i token e i codici 2FA non vengono mai scritti nei log né nell'audit.

## Sicurezza in breve

Ascolto solo in locale per impostazione predefinita · password con argon2id · token da 256 bit salvati solo come hash ·
2FA TOTP con protezione dal riutilizzo del codice · segreti cifrati a riposo · nessun cookie (nessun CSRF) ·
CORS chiuso salvo `api.cors_allowed_origins` · rate limit su ogni endpoint.
