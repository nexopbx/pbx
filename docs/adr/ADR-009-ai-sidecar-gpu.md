# ADR-009 — AI locale in sidecar con gestione GPU

**Stato:** Proposta

## Contesto
Il firewall AI e (più avanti) STT/riassunti usano modelli locali. Un errore nativo o CUDA non deve mai toccare le chiamate.

## Decisione
- `ILocalLlm` + `Microsoft.Extensions.AI` (`IChatClient`). Adapter: **(a) sidecar gestito dall'app** (default, server
  compatibile llama.cpp/OpenAI su localhost: avvio, health-check, riavvio con backoff), (b) in-process opzionale,
  (c) server locale esterno (Ollama/vLLM) via URL.
- **Rilevamento GPU** all'avvio e in UI: NVIDIA (NVML / `nvidia-smi`), AMD/Intel (Vulkan, interrogando il sidecar stesso),
  ripiego CPU (AVX2/AVX-512). Impostazioni: dispositivo, layer offloaded, budget VRAM, contesto, quantizzazione, batch, concorrenza.
- **Modelli**: import di GGUF locali, checksum, registro, **hot-swap con health-check e rollback automatico**. Nessun modello incluso.
- **Priorità e budget VRAM**: firewall in tempo reale con budget rigido; STT/riassunti a bassa priorità; degrado a gradini
  (modello più piccolo → firewall solo deterministico).
- Output del modello vincolato da **grammatica/JSON schema** e **sempre validato** in C#.
- Osservabilità: modello, VRAM, token/s, latenza p50/p99, coda, errori → UI desktop, dashboard web, Prometheus. CLI `nexopbxd llm bench`.
- Distribuzione: **"AI pack" opzionale** (binari CUDA/Vulkan/CPU del sidecar), fuori dall'installer base.

## Conseguenze
Isolamento per processo e priorità; installer base leggero. I test CI usano un adapter finto/CPU.

**Nota dell'owner:** nessuna GPU disponibile in sviluppo. Il percorso di riferimento è CPU (verdetto AI sempre asincrono, pre-filtro deterministico che regge il carico);
il codice CUDA/Vulkan è validato con sonde finte e marcato "non validato su hardware reale" finché qualcuno con una GPU non lo prova.
