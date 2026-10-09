# ADR-010 — Firewall: deterministico prima, AI asincrona poi

**Stato:** Proposta

## Contesto
Proteggere SIP/RTP/web/API da scanner, brute force, toll fraud e flood con un modello locale dotato di memoria, senza
mai rallentare le chiamate e senza bloccare utenti legittimi.

## Decisione
1. **Pre-parse**: lookup O(1) su snapshot immutabile (trie CIDR) → drop in microsecondi.
2. **Pre-filtro deterministico**: whitelist (vince sempre), range locali (consentiti ma contati), token-bucket per IP/subnet/account,
   malformati, User-Agent da scanner noti.
3. **AI solo per i casi ambigui**, con budget di 50 ms e *fail-mode* configurabile. Verdetto
   `{action, confidence, reasons[], ttl_seconds}` validato; invalido ⇒ "unknown". L'AI **non pilota mai direttamente** le azioni.
4. **"Decidi ora, impara in modo asincrono"**: il verdetto AI si applica soprattutto alle richieste successive (cache con TTL).
5. **Memoria** compatta per IP/subnet/account; il modello riceve un riassunto, non log grezzi; stringhe d'attaccante sanificate
   e passate come dati citati.
6. **Rete di sicurezza**: modalità *monitor* per tenant, mai auto-blocco con sessione autenticata attiva senza allarme umano,
   blocchi con TTL, override in un clic, `FirewallDecision` ricercabile.
7. **Enforcer OS opzionale** (nftables / Windows Filtering) per spostare i blocchi fuori dall'app durante un flood.
8. Decisioni ripetitive **aggregate** (contatori) per limitare il volume.

## Conseguenze
Il firewall non sostituisce difese di rete a monte contro DDoS volumetrico (lo diciamo chiaramente in `docs/`).
Rischi n. 4 e 5.
