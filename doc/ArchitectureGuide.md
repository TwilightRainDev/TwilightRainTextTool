# Architecture Guide

This document explains the key abstractions and design decisions in TextTool.  
Read this first if you are new to the project or need to make changes.

---

## 1. Theme System (`Services/ThemeManager.cs` + `Services/ControlsHelper.cs`)

**Architecture:** Semantic color palette + rendering-layer overrides.

`ThemeManager` provides a **mode-aware semantic palette**. It does not know about buttons, labels, or any control type:

| Property   | Dark Mode           | Light Mode          |
|------------|---------------------|---------------------|
| `Bg`       | `#1E1E1E`           | `White`             |
| `Fg`       | `#DCDCDC`           | `Black`             |
| `ControlBg`| `#2D2D2D`           | `White`             |
| `MutedFg`  | `#B4B4B4`           | `Gray`              |

**Button inversion** (white-on-dark-bg → black-on-light-bg) is defined in `ControlsHelper.ButtonBg/ButtonFg`, because it's a rendering-layer decision — not a palette property. This separation means:

- ThemeManager can be reused in projects with different button styles
- Adding a new control type doesn't require changing ThemeManager
- The "special dimmed label" colors are unified via `MutedFg`

**How to theme a new control:**
```csharp
// In ApplyTheme() for your tab:
BackColor = ThemeManager.Bg;
ForeColor = ThemeManager.Fg;
someTextBox.BackColor = ThemeManager.ControlBg;
someLabel.ForeColor = ThemeManager.MutedFg;
someActionButton.BackColor = ControlsHelper.ButtonBg;
someActionButton.ForeColor = ControlsHelper.ButtonFg;
```

---

## 2. ThemedFlatButton (`Services/ThemedFlatButton.cs`)

**Purpose:** Workaround a WinForms bug where `FlatStyle.Flat` buttons in `Enabled = false` state ignore `ForeColor` and always render with `SystemColors.GrayText`.

**How it works:**
- `Enabled = true` → delegates to `base.OnPaint()` (normal WinForms rendering)
- `Enabled = false` → manually fills the background and draws text with `TextRenderer.DrawText` using the actual `ForeColor`

**Cache:** The background `SolidBrush` is cached (recreated via `OnBackColorChanged`) to avoid GDI allocations on every paint.

**When to use:** Every `FlatStyle.Flat` button that has `Enabled = false` during normal operation (e.g., Process/Execute buttons that disable during work). For always-enabled buttons, plain `Button` with `FlatStyle.Flat` is fine.

---

## 3. Tab Control Pattern

Each tab is a `UserControl` with a consistent interface:

| Method | When called | Responsibility |
|--------|-------------|----------------|
| Constructor | MainForm.InitializeComponent | Create controls, attach events |
| `ApplyTheme()` | On load + on theme toggle | Set BackColor/ForeColor for every child |
| `ApplyLocalization()` | On load + on language switch | Update all `Text` properties via `Loc.T()` |

**ApplyTheme() approaches** (both are valid):

- **Per-control assignment** (MergeTab, JoinTab): Set each named control's colors explicitly. Best when the layout is simple and flat.
- **Recursive walker** (ReplaceTab, AboutTab): Walk `Controls` tree and dispatch by type (`Label → Fg`, `Button → ButtonBg/ButtonFg`, etc.). Best when the layout has many nested containers.

**Don't mix both** — if you use a recursive walker, remove explicit label/button assignments that the walker already handles (the walker runs last and overwrites them).

---

## 4. Shared Helpers (`Services/ControlsHelper.cs`)

| Method | Purpose | Used by |
|--------|---------|---------|
| `MakePrimaryButton(text)` | Create a styled action button (Process/Join/Execute) | MergeTab, JoinTab, ReplaceTab |
| `MakeLabel(text)` | Create a right-aligned label | All tabs |
| `RevealInExplorer(filePath)` | Open Explorer selecting a file | JoinTab, ReplaceTab |
| `RevealFolder(folder)` | Open Explorer to a folder | MergeTab |
| `CreateTextFileDialog(title)` | OpenFileDialog for .txt files | MergeTab, ReplaceTab |

These eliminate ~80 lines of duplicated factory code across tabs.

---

## 5. Localization (`Localization/Strings.cs`)

**Singleton:** `Loc` is a static class (not a true singleton — all members are static).

**Lifecycle:**
1. `MainForm` constructor → `ThemeManager.Init()` (reads `app_config.json` once, exposes `InitialLanguage`)
2. `Loc.Init()` → uses `ThemeManager.InitialLanguage` (no second file read) or auto-detects system language
3. `SetLanguage(code)` → switches locale, fires `LanguageChanged` event
4. `MainForm` subscribes to `LanguageChanged` → calls `ApplyLocalization()` on all tabs

**Adding a new language:**
1. Add `Localization/{code}.json` with all keys
2. Add the code to `DetectSystemLanguage()` if it should be auto-detected
3. Add locale file to publish in `Publish.md`

**Locale key naming:** PascalCase (e.g., `BtnProcess`, `LabelSourceFile`).

---

## 6. Processing Pipeline (`Services/ProcessingPipeline.cs`)

**Single-pass design:** Read → Threshold merge → CJK fix → Punct. fix → Replace → Write.

All post-processing runs on in-memory lines (not intermediate files). Only one input read and one output write per file.

**`MergeOptions` + `PostProcessOptions`:** Data classes that carry all config from the UI to the pipeline. No WinForms dependency in processing code — testable without a UI.

---

## 7. Batch Processing Pattern

Both MergeTab and ReplaceTab support batch multi-file processing:

```
Select files → _selectedFiles list → Process loop → Per-file encoding detection
                                                → Per-file read/transform/write
                                                → Success counter
                                                → Batch result message (all | partial)
```

Key implementation detail: `_selectedFiles` is a `List<string>` cleared on each new selection. The "Preview" button is only enabled when exactly 1 file is selected (preview is inherently single-file).

---

## 8. File Layout

```
TextTool/
├── TextTool.csproj              # .NET 7 WinForms
├── Directory.Build.props        # Centralized version
├── Program.cs                   # Entry + GBK encoding registration
├── MainForm.cs                  # Tab host, theme & localization dispatch
│
├── Controls/                    # Tab pages
│   ├── MergeTabControl.cs       # Tab 1: Line merge
│   ├── JoinTabControl.cs        # Tab 2: File join
│   ├── ReplaceTabControl.cs     # Tab 3: Punct. replace
│   ├── AboutTabControl.cs       # Tab 4: About + settings
│   └── PreviewForm.cs           # Preview dialog (merge result)
│
├── Services/                    # Core logic & infrastructure
│   ├── ThemeManager.cs          # Semantic color palette
│   ├── ControlsHelper.cs        # Shared UI factories
│   ├── ThemedFlatButton.cs      # Disabled-state button fix
│   ├── EncodingDetector.cs      # BOM → UTF-8 → GBK detection
│   ├── LineMerger.cs            # Threshold merge algorithm
│   ├── FileJoiner.cs            # Directory file concatenation
│   ├── CjkParagraphMerger.cs    # CJK truncation fix
│   ├── PunctTruncationMerger.cs # Punctuation truncation fix
│   ├── PunctuationReplacer.cs   # Find-&-replace engine + RuleStore
│   └── ProcessingPipeline.cs    # Single-pass orchestration
│
├── Localization/                # i18n
│   ├── Strings.cs               # Loc singleton
│   ├── zh_CN.json
│   ├── zh_TW.json
│   └── en_US.json
│
└── doc/                         # Documentation
    ├── ARCHITECTURE.md          # ← This file
    ├── Publish.md               # Release checklist
    └── adr/                     # Architecture Decision Records
```

---

## 9. Adding a New Tab

1. Create `Controls/NewTabControl.cs` extending `UserControl`
2. Implement `InitializeComponent()`, `ApplyTheme()`, `ApplyLocalization()`
3. Register events: `StatusChanged`, `ErrorOccurred`
4. In `MainForm`, add a field, create in `InitializeComponent()`, add to `TabControl`
5. Wire: `.ApplyTheme()` in `ApplyTheme()`, `.ApplyLocalization()` in `ApplyLocalization()`
6. Optionally subscribe to `ThemeManager.ThemeChanged` and `Loc.LanguageChanged` if needed

**Checklist for a new tab:**
- [ ] `ApplyTheme()` sets colors on all controls (or uses recursive walker)
- [ ] `ApplyLocalization()` translates all visible text
- [ ] `StatusChanged` event fires for status bar updates
- [ ] `ErrorOccurred` event fires for error messages
- [ ] Theme is applied at startup (via `MainForm.ApplyTheme()`)
- [ ] Localization is applied at startup (via `MainForm.ApplyLocalization()`)
