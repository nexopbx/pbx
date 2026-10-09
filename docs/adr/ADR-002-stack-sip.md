# ADR-002 — Stack SIP: SIPSorcery dietro `ISignaling`

**Stato:** **Accettata** (decisione dell'owner; in origine era stata proposta l'implementazione propria)

## Contesto
Il prompt chiede di valutare una libreria matura (es. SIPSorcery) contro un parser proprio, in base a licenza,
interoperabilità e prestazioni.

SIPSorcery è sotto **BSD 3-Clause con una clausola aggiuntiva di limitazione d'uso** nel proprio `LICENSE`: uso,
modifica e distribuzione non sono consentiti in Israele e nei Territori Occupati finché non si verificano le condizioni
indicate nel testo, e in caso di conflitto la clausola è "interpretata come superiore" alla BSD. Il modulo
`SIPSorceryMedia.FFmpeg` è inoltre sotto LGPL 2.1. Non è una licenza standard/OSI.

## Decisione
L'owner ha scelto di **usare SIPSorcery** (non opera in quei territori). Perimetro:

- **SIPSorcery per il signaling** (trasporto, parsing, transazioni, Digest, WebSocket) **dietro `ISignaling`**.
- **Restano nostri**: il B2BUA e la logica di chiamata, l'intera pipeline media (RTP, jitter buffer, G.711, mixer, prompt
  player, tap STT), come richiesto dal prompt.
- **Non** si usa `SIPSorceryMedia.FFmpeg` (LGPL).
- Per il WebRTC (M7d) si confronta in spike la parte WebRTC/SRTP/ICE di SIPSorcery con BouncyCastle usata direttamente (ADR-008).

## Obblighi che ne derivano
1. Ogni ridistribuzione deve **mantenere il testo di licenza e la clausola**: file `THIRD_PARTY_NOTICES` e avviso/EULA che la riportino
   (vale anche per i clienti che installano il prodotto).
2. Il *license check* in CI ha un'**eccezione nominale** per SIPSorcery, con commento che rimanda a questa ADR (ADR-012).
3. Versioni **fissate** e aggiornamenti ragionati.
4. Consigliato un parere legale prima di distribuire il prodotto a terzi, perché la clausola ricade anche su chi lo usa e lo ridistribuisce.

## Verifiche anticipate (spike di M2)
Carico di signaling 100 → 500 chiamate, allocazioni, interoperabilità con **Yealink**, softphone e trunk reale. Se lo spike
fallisce, scatta il **piano B** (stack proprio) **senza toccare** CallEngine, Flow o Queue, grazie all'astrazione.

## Alternative
- **Stack proprio** (era la proposta iniziale): nessuna dipendenza con vincoli, ma +2–3 settimane in M2 e più rischio di interoperabilità. Ora è il piano B.
- **Altre librerie .NET**: nessuna nota con maturità, manutenzione e licenza tutte adeguate.

## Conseguenze
La stima di M2 scende da 7–10 a **5–7 settimane**. `ISignaling` resta come via d'uscita.
