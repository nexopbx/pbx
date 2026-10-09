# ADR-008 — Media in B2BUA, G.711 e WebRTC con ICE-lite

**Stato:** Proposta

## Contesto
Prompt, musica d'attesa, tap STT e registrazione richiedono che il PBX sia nel percorso media; l'audio non deve avere glitch ai cambi.
.NET non ha DTLS nativo; Opus richiede un codec nativo.

## Decisione
- **B2BUA sempre nel percorso media**; SSRC, sequenza e timestamp **riscritti** dal PBX → passaggi *relay ↔ mix* senza glitch.
- **G.711 a-law/µ-law obbligatorio**; **Opus opzionale** via binding nativo a libopus (BSD-3) con binari per `win-x64`/`linux-x64`.
- DTMF **RFC 4733** + fallback in-band; SRTP (AES-CM/HMAC-SHA1, via `System.Security.Cryptography`).
- **Softphone browser**: SIP su **WSS** (RFC 7118) con media WebRTC; lato server **ICE-lite** (il PBX ha un IP raggiungibile)
  e **DTLS-SRTP** tramite una libreria matura (candidata: BouncyCastle, licenza da confermare col *license check*).
  Codec di default **PCMU/PCMA** (obbligatori in WebRTC) → Opus non è necessario per partire.
- TURN fuori dal prodotto (coturn documentato) per NAT simmetrici; `external_ip` e range RTP configurabili.

## Alternative
Direct media/re-INVITE per bypassare il PBX (impossibile con prompt/tap/registrazione); segnalazione WebRTC custom su gRPC
(più lavoro, nessun beneficio di interoperabilità).

## Conseguenze
Più CPU sul PBX (ogni leg passa da lì) ma con relay senza trascodifica nel caso comune. Rischio n. 2 (NAT/WebRTC) gestito con spike in M7d.
