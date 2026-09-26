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
| `ApplyTheme(root)` | Recursive theme walker: dispatches by control type | All 5 tabs, incl. VN |

These eliminate ~80 lines of duplicated factory code across tabs.

---

## 5. Localization (`Localization/Strings.cs`)

**Singleton:** `Loc` is a static class (not a true singleton — all members are static).

**Lifecycle:**

1. `MainForm` constructor → `ThemeManager.Init()` (reads `app_config.json` once, exposes `CurrentLanguage`)
2. `Loc.Init()` → uses `ThemeManager.CurrentLanguage` (no second file read) or auto-detects system language
3. `SetLanguage(code)` → switches locale, fires `LanguageChanged` event
4. `MainForm` subscribes to `LanguageChanged` → calls `ApplyLocalization()` on all tabs

**Adding a new language:**

1. Add `Localization/{code}.json` with all keys
2. Add the code to `DetectSystemLanguage()` if it should be auto-detected
3. No publish-list edit needed — the CI zip ships `Localization/*.json` as a
   wildcard (see `Publish.md` §三「发布产物内容」)

**Locale key naming:** PascalCase (e.g., `BtnProcess`, `LabelSourceFile`).

---

## 6. Processing Pipeline (`TextTool.Core/ProcessingPipeline.cs`)

**Single-pass design:** Read → Threshold merge → CJK fix → Punct. fix → Replace → Write.

All post-processing runs on in-memory lines (not intermediate files). Only one input read and one output write per file.

**`MergeOptions` + `PostProcessOptions`:** Data classes that carry all config from the UI to the pipeline. No WinForms dependency in processing code — testable without a UI.

---

## 7. Batch Processing Pattern

Both MergeTab and ReplaceTab support batch multi-file processing:

```text
Select files → _selectedFiles list → Process loop → Per-file encoding detection
                                                → Per-file read/transform/write
                                                → Success counter
                                                → Batch result message (all | partial)
```

Key implementation detail: `_selectedFiles` is a `List<string>` cleared on each new selection. The "Preview" button is only enabled when exactly 1 file is selected (preview is inherently single-file).

---

## 8. File Layout

```text
TextTool/
├── TextTool.csproj              # .NET 8 WinForms
├── Directory.Build.props        # Centralized version
├── Program.cs                   # Entry + GBK encoding registration
├── MainForm.cs                  # Tab host, theme & localization dispatch
│
├── Controls/                    # Tab pages & dialogs (all tabs implement IThemedTab)
│   ├── MergeTabControl.cs       # Tab 1: Line merge
│   ├── JoinTabControl.cs        # Tab 2: File join
│   ├── ReplaceTabControl.cs     # Tab 3: Punct. replace
│   ├── VNTabControl.cs          # Tab 4: Visual novel
│   ├── LintTabControl.cs        # Tab 5: AI-tone lint (report only)
│   ├── AboutTabControl.cs       # Tab 6: About + settings
│   ├── PreviewForm.cs           # Preview dialog (merge result)
│   ├── VNCharacterSchemeForm.cs # VN character/route scheme dialog
│   ├── SchemeSelectionForm.cs   # Replace-scheme selection dialog
│   ├── SchemeSelectionFormBase.cs # Shared base of the two scheme dialogs
│   └── IThemedTab.cs            # Dispatched by MainForm.ApplyToAllTabs
│
├── Services/                    # UI-adjacent infrastructure (exactly 4 files)
│   ├── ThemeManager.cs          # Semantic color palette
│   ├── ControlsHelper.cs        # Shared UI factories + ApplyTheme walker
│   ├── ThemedFlatButton.cs      # Disabled-state button fix
│   └── IStatusSource.cs         # Status/error event interface for tabs
│
├── TextTool.Core/               # Pure logic, no UI deps
│   ├── EncodingDetector.cs      # BOM → UTF-8 → GBK detection
│   ├── LineMerger.cs            # Threshold merge algorithm
│   ├── FileJoiner.cs            # Directory file concatenation
│   ├── CjkParagraphMerger.cs    # CJK truncation fix
│   ├── PunctTruncationMerger.cs # Punctuation truncation fix
│   ├── PunctuationReplacer.cs   # Find-&-replace engine + RuleStore
│   ├── ProcessingPipeline.cs    # Single-pass orchestration
│   ├── VNReformatterService.cs  # VN lines → natural paragraphs
│   ├── PunctFixerService.cs     # Dialogue punctuation completion
│   ├── VNCharacterScheme.cs     # VN preset scheme model & store
│   ├── ReplaceScheme.cs         # Replace preset scheme model & store
│   ├── DialogueLine.cs          # Shared dialogue regex
│   ├── BackupHelper.cs          # Auto-backup with rotation
│   ├── AtomicFile.cs            # Atomic output write
│   ├── JsonFileStore.cs         # Generic JSON persistence
│   ├── PathHelper.cs            # Path utilities
│   ├── TextUtils.cs             # String extension methods
│   ├── LintRule.cs              # AI-tone rule model + store (per-id merge with external file)
│   ├── LintReport.cs            # Lint report model and JSON contract
│   ├── AiToneLintService.cs     # AI-tone check engine (data rules + algorithmic detectors)
│   ├── LintTextFormatter.cs     # Human-readable report rendering (invisible chars escaped)
│   ├── LintRunner.cs            # Shared CLI/GUI lint orchestration (validate + scan + filter)
│   ├── RegexGuard.cs            # Regex construction with ReDoS timeout
│   ├── UpdateChecker.cs         # GitHub latest-release check
│   ├── UpdateClient.cs          # Update-only HttpClient (pinned roots, no redirects)
│   ├── ReleaseVerifier.cs       # Release signature verification (ECDsa P-256)
│   ├── ReleaseSigningPublicKey.cs # Publisher public key constant
│   ├── PinnedRoots.cs           # Public root CA allow-list
│   ├── LastKnownVersion.cs      # Downgrade-replay detection
│   ├── SelfUpdater.cs           # CLI self-update: download, verify, staged swap
│   └── default_schemes.json / default_vn_schemes.json / default_lint_rules.json / pinned_roots.txt  # Embedded resources
│
├── TextTool.Cli/                # texttool.exe entry (merge/replace/vn/join/lint/update)
│
├── Localization/                # i18n
│   ├── Strings.cs               # Loc singleton
│   ├── zh_CN.json
│   ├── zh_TW.json
│   └── en_US.json
│
└── doc/                         # Documentation
    ├── ArchitectureGuide.md     # ← This file
    ├── Publish.md               # Release checklist
    ├── UpdateSecurity.md        # Update trust model (TLS pinning, signing)
    ├── ReplaceSchemesDesign.md  # Replace-scheme design note
    ├── TECH-DEBT.md             # Open optimizations and known debt
    ├── adr/                     # Architecture Decision Records
    └── specs/                   # In-flight design specs
```

**Split rule:** UI-adjacent code lives in `Services/` (or `Controls/`); anything
that can be tested without a UI lives in `TextTool.Core/`. New text-processing
logic goes to `TextTool.Core/`.

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
