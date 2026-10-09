# NexoPBX — Decisioni di default

Scelte ragionevoli che ho preso dove il prompt era ambiguo, così non mi fermo a chiedere. Ognuna è reversibile a basso
costo; se non ti convince la cambiamo. Le decisioni grandi stanno invece nelle [ADR](adr/README.md).

| ID | Decisione | Motivo / costo di cambiarla |
|---|---|---|
| D-001 | Binario unico `nexopbxd` con sottocomandi: `run`, `service`, `desktop`, `llm bench`, `flow simulate`, `db migrate` | Un solo artefatto da distribuire; basso costo |
| D-002 | Aggiungo `NexoPBX.Abstractions` (clock, id, event bus, tenant, primitive di dominio) e `NexoPBX.Testing` (clock virtuale, telefonia finta, runner di scenari) al layout | Servono per far rispettare le regole di dipendenza e per i test deterministici |
| D-003 | `tenant_id` su ogni riga fin da subito; l'UI mostra un solo tenant "default" finché non serve | Aggiungerlo dopo costerebbe molto di più |
| D-004 | Porte di default configurabili: SIP 5060 (UDP/TCP), 5061 (TLS), HTTPS 8443, HTTP 8080 solo su loopback, RTP 16384–32767 (pari) | Convenzionali; basso costo |
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
