# ADR-011 — Web UI: React + Vite + xyflow + client Connect

**Stato:** Proposta

## Contesto
Il Visual Planner è il cuore del prodotto: grafo infinito, undo/redo profondo, lint live, simulatore. La UI deve usare solo l'API pubblica.

## Decisione
- **TypeScript + React + Vite**, **React Flow (xyflow)** per il grafo, **Tailwind** + piccolo design system, tema chiaro/scuro,
  tablet-friendly per i supervisori. Asset statici **incorporati nel binario** e serviti da Kestrel.
- Client generato dai proto con **Connect-ES** (`@bufbuild/protobuf` + `@connectrpc/connect-web`, protocollo gRPC-Web).
- Stato del Planner con un **modello a comandi** (ogni modifica è un comando reversibile) → undo/redo profondo e *collaborative-ready*.
- i18n con i18next (IT/EN); test con Vitest e Playwright (Chromium già disponibile nel container).

## Alternative
`grpc-web` classico (client meno ergonomico); Svelte/Vue (meno ecosistema per editor a grafo).

## Conseguenze
Lint e validazione del flusso **vivono sul server** (stesso codice del runtime) ed esposti via API; il client li mostra, non li reimplementa.
Le versioni esatte delle librerie vengono fissate a inizio M7 (anche rispetto alla data di rilascio).
