# ADR-005 — Interprete di flusso deterministico a effetti

**Stato:** Proposta

## Contesto
Lo stesso flusso deve girare in produzione, nel simulatore del Planner, nella CLI e nei test, con risultati identici.
Il flusso pubblicato non deve interrompere le chiamate in corso.

## Decisione
`Flow.Step(FlowSnapshot, CallState, FlowEvent) → (CallState', Effects[])` è una **funzione pura**: niente I/O, niente
lettura dell'ora, niente casualità non seminata. Gli **effetti** (`Answer`, `PlayPrompt`, `CollectInput`, `Enqueue`,
`CallApi`, `StartTimer`, `Bridge`, `Hangup`, …) sono eseguiti dal `CallEngine`, che restituisce gli esiti come eventi.
Il flusso è un **`FlowSnapshot` immutabile e versionato**; ogni chiamata resta sulla versione con cui è iniziata.
Tempo: sempre tramite `IClock`/`ITimerScheduler` (clock virtuale nei test).

## Alternative
Interprete che esegue direttamente I/O (più semplice all'inizio, ma non simulabile e non deterministico).

## Conseguenze
Simulatore, CLI e produzione condividono il codice; pubblicazione atomica; test a golden file; lo stato di una
chiamata è serializzabile (utile a debug e CDR). Costo: ogni nodo va modellato come coppia evento→effetti.
