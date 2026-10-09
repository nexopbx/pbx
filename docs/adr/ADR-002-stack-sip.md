# ADR-002 — Stack SIP: implementazione propria dietro `ISignaling`

**Stato:** Proposta — **richiede la tua decisione** (è la domanda bloccante n. 2)

## Contesto
Il prompt chiede di valutare una libreria matura (es. SIPSorcery) contro un parser proprio, in base a licenza,
interoperabilità e prestazioni.

Ho verificato la licenza di SIPSorcery sul `LICENSE` del suo repository: è la **BSD 3-Clause con una clausola
aggiuntiva di limitazione d'uso** ("il software non deve essere usato per sostenere le politiche di apartheid dello
stato di Israele"; uso, modifica e distribuzione sono non consentiti in Israele e nei Territori Occupati finché non si
verificano condizioni indicate nel testo; in caso di conflitto la clausola è "interpretata come superiore" alla BSD).
Il modulo `SIPSorceryMedia.FFmpeg` è inoltre sotto LGPL 2.1.

Conseguenze pratiche, indipendentemente dal giudizio di merito: **non è una licenza standard/OSI**; un *license check*
automatico con allow-list standard la rifiuterebbe; e, trattandosi di una restrizione d'uso/territoriale, può
**ricadere sui nostri clienti e sulla distribuzione** del prodotto. Serve un parere legale prima di dipendervi.

## Decisione proposta
Scrivere **uno stack SIP proprio** (sottoinsieme RFC 3261 che ci serve: parser/serializzatore su `Span<byte>`,
livello transazioni, livello dialoghi, Digest con SHA-256 opzionale, UDP/TCP/TLS/WSS, session timers, re-INVITE,
REFER, 100rel dove serve) dietro l'interfaccia `ISignaling`.

Qualità garantita da: torture test RFC 4475, scenari **SIPp** (strumento esterno GPL, usato solo come tester, mai
linkato né distribuito), fuzzing continuo del parser, matrice di prova su client/trunk reali.

## Alternative
- **A. Stack proprio** (raccomandata): nessuna dipendenza con vincoli di licenza, controllo totale su prestazioni
  e comportamento; costo: +2–3 settimane in M2 (già incluse nella stima) e rischio di interoperabilità (rischio n. 1).
- **B. SIPSorcery**: più rapido, ma con la clausola sopra; da scegliere solo dopo parere legale e con tua conferma esplicita.
- **C. Altre librerie .NET**: da valutare caso per caso; non ne conosco una con maturità, manutenzione e licenza
  tutte adeguate — se ne conosci una, la valuto volentieri.

## Conseguenze
L'astrazione `ISignaling` permette di cambiare idea (piano B) senza toccare CallEngine, Flow o Queue.
