# SimpleRollCall

[简体中文](README_CHS.md) | English

A simple and modern roll call (random name picker) application for Windows, built with WPF and ModernWpf.

## Features

- **Random drawing** — roll through a name list with rolling text and a progress bar
- **Multi-person mode** — draw 1 to 10 people at once, displayed as tags in a WrapPanel
- **Two draw modes**
  - *Manual*: names keep rolling until you press pause; everyone stops instantly
  - *Auto stop*: the draw finishes automatically within 3 seconds (multi-person slots stop progressively between 2.1s and 3.0s)
- **Machine learning mode** — drawn people enter a temporary memory list and are not drawn again until everyone has been drawn once, then the cycle restarts
- **Responsive UI** — the draw text scales with the window; single names up to 56px, multi-person tags up to 48px
- **Themes** — Light / Dark / follow the system, with accent colors taken dynamically from the system theme (no hardcoded colors)
- **Bilingual UI** — Simplified Chinese and English, switchable in-app via .NET resource files (i18n)
- **Encoding support** — name lists can be read as UTF-8 or GB18030
- **Portable** — all settings are stored in `SRC_Config.ini` next to the executable; single-file builds available

## Language

The application UI is available in Simplified Chinese (default) and English.

## Design

- **Tech stack**: .NET 8 (with a .NET 6 build target), WPF, [ModernWpf](https://github.com/Kinnara/ModernWpf)
- **UI style**: Fluent Design-inspired modern UI; the toolbar buttons share a themed backdrop with separators, and dialogs follow the system accent and light/dark theme
- **Layout**: three-row responsive layout — a scalable text area, a slim progress bar, and a fixed-height toolbar
- **Architecture**: configuration is a plain INI file (`SRC_Config.ini`); name lists are plain text files with one name per line

## License

This project is licensed under the [MIT License](LICENSE).

Copyright (c) 2026 SXZ11454
