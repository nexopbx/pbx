# ADR-007 — Persistenza: SQLite (WAL) / PostgreSQL, stato caldo in memoria

**Stato:** Proposta

## Contesto
Zero-config di default, PostgreSQL per installazioni grandi, stesso codice di dominio; stato caldo veloce; recupero dopo crash.

## Decisione
- **EF Core** (e Dapper sui percorsi caldi) dietro interfacce di repository. Provider **SQLite WAL** (default) e **PostgreSQL**,
  con **assembly di migrazioni separati per provider**, entrambi testati in CI (PostgreSQL via service container).
- `tenant_id` su ogni riga (filtri globali + indici compositi).
- **Stato caldo in memoria** (chiamate, posizioni in coda) con snapshot periodico; **write-through** per ciò che deve
  sopravvivere (virtual hold, richiamate, legami sticky, flussi); **append-only a batch** per CDR/eventi/decisioni firewall.
- SQLite ha un solo scrittore: writer seriale con group commit.
- Numeri di sequenza monotoni dal DB per l'ordinamento deterministico.

## Alternative
Solo PostgreSQL (meno zero-config); event sourcing completo (troppo costoso per il beneficio).

## Conseguenze
Tabella di recovery in ARCHITECTURE §7. Le chiamate vive non sopravvivono al riavvio (D-017). Il volume di decisioni firewall
va tenuto sotto controllo con aggregazione e retention (rischio n. 5).
