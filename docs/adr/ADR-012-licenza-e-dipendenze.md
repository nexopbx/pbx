# ADR-012 — Licenza del progetto e policy sulle dipendenze

**Stato:** **Accettata** — NexoPBX è rilasciato sotto **GNU AGPL-3.0** (decisione dell'owner). Resta **una questione aperta da
chiudere prima di M2**: l'interazione con SIPSorcery (vedi sotto).

## Contesto
Il prompt descriveva NexoPBX come prodotto proprietario, ma il repository conteneva già un `LICENSE` AGPL-3.0. L'owner ha scelto di
**mantenere l'AGPL-3.0**: il prodotto è open source; chiunque lo usi in rete deve poter ottenere il sorgente corrispondente.

## Decisione
- Licenza del progetto: **`AGPL-3.0-only`** (scelta più conservativa: si potrà allargare a "or-later" in futuro, il contrario no).
  L'identificatore SPDX è impostato in `Directory.Build.props`; `LICENSE` non viene modificato.
- **Allow-list dipendenze** (controllata in CI): MIT, BSD-2/3-Clause, Apache-2.0, ISC, MS-PL, Unlicense/CC0, Zlib; **LGPL** solo con
  collegamento dinamico; GPL/AGPL accettate solo se compatibili con la v3. Vietate SSPL, licenze "non-commercial" e simili.
- Strumenti GPL usati come tester esterni (es. SIPp) non sono linkati né distribuiti.
- Il titolare dei diritti sul codice NexoPBX è l'owner; contributi di terzi richiederanno un accordo (es. DCO) prima di essere accettati.

## Questione aperta: AGPL + SIPSorcery  *(da decidere prima di M2)*
Correggo una mia affermazione precedente: avevo scritto che l'allow-list era "compatibile con tutte le opzioni", ma **SIPSorcery non
è una semplice dipendenza permissiva**. La sua clausola d'uso territoriale è una *restrizione ulteriore*, e la GPL/AGPL v3
vieta di imporre restrizioni ulteriori su chi riceve il lavoro combinato. Le licenze con restrizioni sul campo d'uso sono
generalmente considerate **incompatibili con la GPL**. Il problema riguarda la **distribuzione** del prodotto che include la libreria,
non l'uso privato.

Strade possibili (in ordine di preferenza di chi scrive; **serve un parere legale** per scegliere):
1. **Eccezione di collegamento (AGPL §7 "additional permission")** concessa dall'owner, titolare del codice NexoPBX, che permette
   di collegare NexoPBX a SIPSorcery. È la tecnica usata per altre librerie non compatibili (es. l'eccezione OpenSSL). Non elimina
   la clausola di SIPSorcery, che resterebbe per quella parte.
2. **Adapter SIPSorcery come plugin opzionale** (assembly separato, non distribuito nel pacchetto base) e **stack SIP proprio come
   default**: il piano B di ADR-002 diventa il percorso principale.
3. Stack proprio e basta (nessuna dipendenza con clausole).

`ISignaling` rende le tre strade equivalenti per il resto del codice. **M1 non usa SIPSorcery**, quindi non è bloccata.

## Conseguenze
`THIRD_PARTY_NOTICES` elenca le dipendenze e le loro licenze (generato in CI). Header SPDX nei nuovi file sorgente.
La CI ha un'eccezione nominale per SIPSorcery **solo** se la strada 1 viene scelta.
