# ADR-001 — Avalonia UI invece di .NET MAUI

**Stato:** Proposta

## Contesto
La UI desktop deve funzionare su **Windows e Linux** con lo stesso codice. .NET MAUI non ha supporto Linux ufficiale.
WPF e WinForms sono solo Windows (e il prompt li esclude).

## Decisione
**Avalonia UI** con MVVM (`CommunityToolkit.Mvvm`). L'app desktop parla col server **solo** tramite l'API pubblica
(ADR-004), anche quando gira nello stesso processo.

## Alternative scartate
- **MAUI**: niente Linux ufficiale → viola il requisito di parità.
- **WPF / WinForms**: solo Windows.
- **Uno Platform**: valido, ma più pesante da portare e meno diffuso per strumenti di amministrazione.
- **Electron / wrapper web**: duplica la UI e non è C#.

## Conseguenze
- Skia/HarfBuzz sono librerie native → ADR-003 (l'assembly Desktop si carica solo in modalità desktop).
- Test UI con `Avalonia.Headless` in CI.
- Licenza di Avalonia (MIT) da confermare dal *license check* a inizio M8; versione stabile da verificare allora.
