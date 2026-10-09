# ADR-012 — Licenza del progetto e policy sulle dipendenze

**Stato:** Proposta — **richiede la tua decisione** (domanda bloccante n. 1)

## Contesto
Il prompt descrive NexoPBX come prodotto **proprietario**. Il repository però contiene un `LICENSE` **GNU AGPL-3.0**.
Le due cose non vanno d'accordo: l'AGPL impone di offrire il codice sorgente completo (anche a chi usa il software via rete)
e permette a chiunque di ridistribuirlo. Dalla licenza dipendono la policy di CI e cosa possiamo includere come dipendenza.

## Opzioni
- **A. Proprietario** (come nel prompt): sostituire `LICENSE` con un avviso/EULA proprietario. Allow-list CI: **MIT, BSD, Apache-2.0,
  MS-PL, ISC**; **LGPL solo con collegamento dinamico**; vietati GPL/AGPL/SSPL nel prodotto distribuito. Strumenti GPL usati solo come
  tester esterni non distribuiti (es. SIPp).
- **B. Mantenere AGPL-3.0**: prodotto open source (eventualmente *open core* o doppia licenza se sei l'unico titolare dei diritti).
  L'allow-list resta compatibile con dipendenze permissive; nessuna dipendenza proprietaria nel codice distribuito.
- **C. Doppia licenza** (AGPL + commerciale): richiede che tu detenga tutti i diritti sul codice.

## Decisione proposta
Nessuna finché non mi dici cosa vuoi: **non modifico `LICENSE`**. Intanto la CI viene configurata con l'allow-list permissiva
comune a tutte le opzioni (MIT/BSD/Apache-2.0/MS-PL/ISC), che è compatibile con A, B e C.

## Conseguenze
La decisione cambia: testo di `LICENSE`, header dei file, README, installer (EULA) e eventuale parere legale.
Collegata a ADR-002 (clausola d'uso di SIPSorcery).
