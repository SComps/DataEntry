# DataEntry DSL Compiler — Plan

## Top-Level Overview

Build a VB.NET console application ("the compiler") that:
1. Accepts a DSL specification file describing a data-entry form (fields, records, screens).
2. Parses and validates that DSL.
3. Either displays parse/validation errors **or** renders the described form live in Terminal.Gui for preview/testing (no file I/O).
4. On demand (F10 hotkey or `--build` CLI flag) generates a complete VB.NET + Terminal.Gui project and shells out to `dotnet build` to produce a standalone data-entry executable.

**Non-goals:** C code generation, COBOL strict formatting, over-engineering.

**Tech stack:** VB.NET, .NET 10 SDK, Terminal.Gui NuGet package, `dotnet build` subprocess.

---

## Architecture Overview

```
┌─────────────────────────────────────────────┐
│           DataEntry Compiler (Tool A)        │
│                                              │
│  CLI Entry Point                             │
│    --build flag?  ──► CodeGen + dotnet build │
│    file arg or file-open dialog              │
│           │                                  │
│      DSL Lexer/Parser                        │
│           │                                  │
│      AST (DataSection, RecordDef, Screen)    │
│           │                                  │
│      Validator                               │
│           │                                  │
│    Errors? ──► Error Display UI              │
│    Valid?  ──► Preview UI (Terminal.Gui)     │
│                   │                          │
│               F10 / --build                  │
│                   │                          │
│           Code Generator                    │
│                   │                          │
│           dotnet build subprocess            │
└─────────────────────────────────────────────┘
```

---

## Sub-Tasks

---

### Sub-Task 1 — Project Scaffold

**Intent**
Create the VB.NET solution and project structure for Tool A (the compiler). Establishes the build system, NuGet references, and entry point so all subsequent sub-tasks have a home.

**Expected Outcomes**
- `DataEntry.sln` and `src/DataEntry/DataEntry.vbproj` exist and build cleanly with `dotnet build`.
- Terminal.Gui NuGet package is referenced.
- `Program.vb` contains a minimal `Main` that prints "OK" and exits.

**Todo List**
1. Run `dotnet new sln -n DataEntry` at workspace root.
2. Run `dotnet new console -lang vb -n DataEntry -o src/DataEntry --framework net10.0`.
3. Run `dotnet sln add src/DataEntry/DataEntry.vbproj`.
4. Add Terminal.Gui NuGet reference to the project (`dotnet add package Terminal.Gui`).
5. Confirm `dotnet build` succeeds.

**Relevant Context**
- Workspace root: `/home/scott/DataEntry`
- Target framework: `net10.0`
- NuGet package: `Terminal.Gui` (gui-cs project)

**Status:** [ ] pending

---

### Sub-Task 2 — DSL Lexer & Parser → AST

**Intent**
Implement a hand-written lexer and recursive-descent parser that reads a DSL file and produces an in-memory AST. This is the foundation for validation, preview, and code generation.

**Expected Outcomes**
- `DslLexer.vb` tokenises DSL source into a flat token stream.
- `DslParser.vb` consumes tokens and builds an AST rooted at `DslDocument`.
- All constructs from the spec are handled: `DATA-SECTION`, `FILE`, `APPEND`/`NOAPPEND`, `LRECL`, `LEND`, `RECORD`, field definitions (`NAME START= LEN=`), `FORMAT=`, `SCREEN-SECTION`, field labels, `INTO`, `VALIDATE WITH`.
- Unit-testable — parser can be called with a string and returns an AST or error list without any UI dependency.

**DSL AST Node Types (to define in `Ast.vb`)**

| Node | Key Properties |
|------|---------------|
| `DslDocument` | `DataSection`, `Screens` |
| `DataSection` | `FilePath`, `AppendMode`, `Lrecl`, `LineEnding`, `Records` |
| `RecordDef` | `Name`, `Fields` |
| `FieldDef` | `Name`, `Start` (optional), `Len`, `Format` |
| `FormatSpec` | raw format string, parsed mask tokens |
| `ColorSpec` | `Fg` (ColorName), `Bg` (ColorName) — resolved from COLOR= shorthand or FG=/BG= pair |
| `ScreenSection` | `Name`, `DefaultColor`, `Items` |
| `ScreenField` | `Label`, `Row`, `Col`, `Len`, `IntoRecord`, `IntoField`, `ValidateFunc`, `NormalColor`, `FocusColor`, `ErrorColor` |

**Canonical SCREEN-SECTION Syntax**

```
SCREEN-SECTION
SCREEN CUST-ENTRY COLOR=WhiteOnBlue FG=White BG=Blue
    FIELD "Customer Name"  ROW=3  COL=2  LEN=30  INTO CUST.CNAME   VALIDATE WITH CHECK-NAME
    FIELD "Address Line 1" ROW=4  COL=2  LEN=30  INTO CUST.CADDR1
    FIELD "State"          ROW=7  COL=2  LEN=2   INTO CUST.CSTATE
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
```

- `COLOR=FgOnBg` is shorthand for `FG=Fg BG=Bg`.
- `FG=` / `BG=` override individual components.
- Screen-level color is the default for all fields; field-level color overrides the screen default.
- `NORMAL=`, `FOCUS=`, `ERROR=` each accept `ColorName` or `FgOnBg` shorthand and apply to the three field states.

**Supported Color Names (16 classic console colors)**

`Black`, `DarkRed`, `DarkGreen`, `DarkYellow`, `DarkBlue`, `DarkMagenta`, `DarkCyan`, `Gray`,
`DarkGray`, `Red`, `Green`, `Yellow`, `Blue`, `Magenta`, `Cyan`, `White`

**Todo List**
1. Create `src/DataEntry/Ast.vb` — define all AST node classes including `ColorSpec`, updated `ScreenSection` and `ScreenField`.
2. Create `src/DataEntry/DslLexer.vb` — tokenise DSL text into typed tokens (keyword, identifier, equals, string, number, dot, newline).
3. Create `src/DataEntry/DslParser.vb` — recursive-descent parser; returns `(DslDocument, List(Of ParseError))`.
4. Parse `FORMAT=` mask strings into a list of mask tokens (literal char, `X`, `U`, `L`, `9`, `Z`, escaped `\c`).
5. Parse `COLOR=FgOnBg` shorthand by splitting on `"On"` (case-insensitive) into `Fg` and `Bg` components.
6. Parse `FG=`, `BG=`, `NORMAL=`, `FOCUS=`, `ERROR=` color attributes on both SCREEN and FIELD lines.
7. Confirm parser correctly handles the example in the spec (CUST record with 7 fields).

**Relevant Context**
- Spec example: `Specifications.txt` lines 33–50
- Format characters: `X` alphanumeric, `U` uppercase, `L` lowercase, `9` digit, `Z` zero-fill digit, `\c` escaped literal, `\\` literal backslash.
- `START=` is optional on fields after the first (implicit continuation).
- Color defaults if omitted: screen defaults to `Gray` on `DarkBlue`; fields inherit screen color; focus defaults to `Black` on `Cyan`; error defaults to `White` on `DarkRed`.

**Status:** [ ] pending

---

### Sub-Task 3 — DSL Validator

**Intent**
After parsing, run semantic checks over the AST to catch errors that are syntactically valid but logically wrong. Returns a list of typed `ValidationError` objects with line numbers and messages.

**Expected Outcomes**
- `DslValidator.vb` accepts a `DslDocument` and returns `List(Of ValidationError)`.
- Catches: duplicate record names, duplicate field names within a record, field START + LEN exceeding LRECL, `INTO` references pointing to undefined record/field, `VALIDATE WITH` references to undefined functions (warn, not error), FORMAT mask length mismatching field LEN.
- Zero errors on the spec example.

**Todo List**
1. Create `src/DataEntry/DslValidator.vb`.
2. Implement record/field duplicate checks.
3. Implement LRECL boundary checks (sum of field LENs must not exceed LRECL).
4. Implement INTO cross-reference checks.
5. Implement FORMAT mask vs LEN consistency check.
6. Return `List(Of ValidationError)` with `Line`, `Column`, `Message`, `Severity` (Error / Warning).

**Relevant Context**
- `DslDocument` AST from Sub-Task 2.
- LRECL is defined on the DATA-SECTION FILE line.

**Status:** [ ] pending

---

### Sub-Task 4 — CLI Entry Point & File Loading

**Intent**
Wire up `Program.vb` to handle command-line arguments, load the DSL file, and route to either the error display UI, the preview UI, or the `--build` pipeline.

**Expected Outcomes**
- `dataentry myform.def` loads and parses the file.
- `dataentry myform.def --build` triggers code generation + build (no interactive UI needed).
- `dataentry myform.def --output ./out` overrides the output directory.
- `dataentry` (no args) launches Terminal.Gui with a file-open dialog.
- Clear console error message if the file path does not exist.

**Todo List**
1. Rewrite `Program.vb` with a `Main(args As String())` entry point.
2. Parse CLI args: positional file path, `--build` flag, `--output <dir>` option.
3. If no file path given, launch Terminal.Gui file-open dialog (`OpenDialog`).
4. Load and parse the DSL file using `DslParser`.
5. Run `DslValidator`.
6. If `--build` flag: call `CodeGenerator` then `BuildRunner` and exit (no UI).
7. If errors: launch `ErrorDisplayUi`.
8. If valid: launch `PreviewUi`.

**Relevant Context**
- `DslParser`, `DslValidator` from Sub-Tasks 2–3.
- `CodeGenerator`, `BuildRunner` defined in Sub-Task 6.
- `ErrorDisplayUi`, `PreviewUi` defined in Sub-Tasks 5a and 5b.
- Terminal.Gui `OpenDialog` for file browsing.

**Status:** [ ] pending

---

### Sub-Task 5a — Error Display UI

**Intent**
When the DSL has validation or parse errors, display them clearly in a Terminal.Gui window so the user knows exactly what to fix.

**Expected Outcomes**
- `ErrorDisplayUi.vb` shows a scrollable list of all errors with line number, severity, and message.
- ESC or Q closes the app.
- Clean Terminal.Gui `Application.Init` / `Application.Run` / `Application.Shutdown` lifecycle.

**Todo List**
1. Create `src/DataEntry/ErrorDisplayUi.vb`.
2. Use a `Window` with a `ListView` (or `TableView`) bound to the error list.
3. Show severity (Error / Warning) colour-coded if Terminal.Gui supports it.
4. Bind ESC and Q to `Application.RequestStop()`.
5. Call `Application.Init`, `Application.Run(window)`, `Application.Shutdown`.

**Relevant Context**
- `ValidationError` type from Sub-Task 3.
- Terminal.Gui v2 lifecycle: `Application.Init()` → `Application.Run()` → `Application.Shutdown()`.

**Status:** [ ] pending

---

### Sub-Task 5b — Form Preview UI

**Intent**
When the DSL is valid, render the described data-entry form inside Terminal.Gui exactly as the generated application would look. This is a live preview — no file I/O occurs. The user can tab between fields, type test data, and verify the layout. F10 triggers Compile & Build.

**Expected Outcomes**
- `PreviewUi.vb` renders all screen fields at their specified row/col positions with correct lengths.
- Labels are shown next to each field.
- Screen background and field colors (NORMAL / FOCUS / ERROR states) are applied from the AST color specs; defaults used when not specified.
- Tab / Shift-Tab navigate between fields; active field switches to its FOCUS color.
- Standard editing keys work (Insert, Delete, Backspace, Home, End, arrow keys) — Terminal.Gui `TextField` handles this natively.
- Default hotkeys are active: Page Up / Page Down (multi-screen navigation), F3 = cancel, Right-Ctrl = "save" (preview: shows a message, no actual write), Shift-PgUp/PgDn/Home/End for record navigation (preview: shows a message).
- F10 calls `CodeGenerator` + `BuildRunner` and displays a build log dialog.
- A menu bar shows: File > (Open, Compile & Build, Quit).

**Todo List**
1. Create `src/DataEntry/PreviewUi.vb`.
2. Build a `Window` with a `MenuBar` (File menu with Open, Compile & Build, Quit).
3. Apply screen-level `DefaultColor` to the window background.
4. For each `ScreenField` in the AST, place a `Label` and `TextField` at the specified row/col.
5. Constrain `TextField` to field `Len` (set `Width`).
6. Apply `NormalColor` to each `TextField` at rest; switch to `FocusColor` on Enter/focus, back on Leave.
7. When a VALIDATE WITH function flags an error, apply `ErrorColor` to that field.
8. Bind F3 → cancel confirmation dialog, Right-Ctrl → "save" preview message.
9. Bind Shift-PgUp/PgDn/Home/End → "record navigation preview" message.
10. Bind F10 → trigger `CodeGenerator` + `BuildRunner`; show output in a scrollable dialog.
11. Handle multi-screen forms: Page Up / Page Down switch between screen definitions.

**Relevant Context**
- `ScreenSection`, `ScreenField`, `ColorSpec` AST nodes from Sub-Task 2.
- Terminal.Gui `TextField`, `Label`, `Window`, `MenuBar`, `MessageBox`, `Dialog`.
- Terminal.Gui color is set via `ColorScheme` — build a `ColorScheme` from `ColorSpec` values.
- Color defaults: screen → `Gray` on `DarkBlue`; field normal → inherits screen; focus → `Black` on `Cyan`; error → `White` on `DarkRed`.
- Hotkey defaults are defined here — not in the DSL.

**Status:** [ ] pending

---

### Sub-Task 6 — Code Generator

**Intent**
Given a valid `DslDocument` AST, emit a complete, compilable VB.NET + Terminal.Gui project that is the actual data-entry application. This is the core output of Tool A.

**Expected Outcomes**
- `CodeGenerator.vb` accepts a `DslDocument` + output directory and writes:
  - `<name>.vbproj` — project file referencing Terminal.Gui and targeting net10.0.
  - `Program.vb` — entry point.
  - `DataFile.vb` — fixed-length record read/write logic using the DATA-SECTION spec.
  - `MainForm.vb` — Terminal.Gui form(s) matching the SCREEN-SECTION spec.
  - `FormatHelper.vb` — field formatting/masking logic (X, U, L, 9, Z, escaped literals).
  - `ValidationRunner.vb` — calls VALIDATE WITH functions if defined.
- The generated `Program.vb` wires up the same hotkeys as the preview (F3, Right-Ctrl, Shift-PgUp etc.).
- Generated code must be readable and simple — no over-engineering.

**Todo List**
1. Create `src/DataEntry/CodeGenerator.vb`.
2. Implement `GenerateProject(doc As DslDocument, outputDir As String)` method.
3. Generate `.vbproj` from a string template with correct NuGet reference.
4. Generate `Program.vb` entry point (init + run + shutdown lifecycle).
5. Generate `DataFile.vb`: open/close/append/read/write fixed-length records; apply LEND line endings; right-adjust numbers, left-adjust text per spec.
6. Generate `MainForm.vb`: place Label + TextField per ScreenField; apply `ColorScheme` per field state (NORMAL/FOCUS/ERROR); wire hotkeys; on Right-Ctrl save, collect field values → format → write record.
7. Generate `FormatHelper.vb`: mask application and validation functions (FORMAT mask → pad/trim → formatted string).
8. Generate `ColorHelper.vb`: map DSL color names to `Terminal.Gui.Color` values; build `ColorScheme` from `ColorSpec`.
9. If VALIDATE WITH functions are referenced, emit stub function signatures with a TODO comment in the generated code.
9. Write all files to `outputDir`.

**Relevant Context**
- AST from Sub-Task 2, FORMAT mask tokens.
- Data alignment rules: numbers right-adjusted (pad left with spaces; Z fills with zeros), text left-adjusted (pad right with spaces).
- LEND options: CRLF, LF, CR, NONE.
- `INTO <record>.<field>` maps screen fields to record fields.

**Status:** [ ] pending

---

### Sub-Task 7 — Build Runner

**Intent**
Shell out to `dotnet build` on the generated project and capture output so it can be shown to the user (in the UI dialog or streamed to stdout in `--build` mode).

**Expected Outcomes**
- `BuildRunner.vb` accepts the output directory, runs `dotnet build`, and returns `(Success As Boolean, Output As String)`.
- Works on Windows, Linux, and macOS.
- In `--build` mode, streams output lines to stdout in real time.
- In UI mode, collects all output and returns it for display in a dialog.

**Todo List**
1. Create `src/DataEntry/BuildRunner.vb`.
2. Use `System.Diagnostics.Process` to invoke `dotnet build <outputDir>`.
3. Redirect stdout and stderr.
4. Accept a callback/flag to choose streaming vs. buffered output.
5. Return exit code mapped to `Success As Boolean` plus full output string.

**Relevant Context**
- `dotnet` must be on the PATH (it is, per spec — .NET 10 SDK is installed).
- Cross-platform: no OS-specific shell invocation — call `dotnet` directly as the executable.

**Status:** [ ] pending

---

### Sub-Task 8 — Integration & Smoke Test

**Intent**
Wire all sub-tasks together, run the full pipeline end-to-end with the example from the spec, and confirm the generated application builds and runs correctly.

**Expected Outcomes**
- Compiler tool builds with zero errors/warnings.
- `dataentry sample.def` launches the preview UI showing the CUST form.
- `dataentry sample.def --build --output ./out` generates a project in `./out`, runs `dotnet build`, and produces an executable.
- The generated executable launches a Terminal.Gui data-entry form matching the spec example.
- All 7 CUST fields are present with correct lengths and FORMAT masks.

**Todo List**
1. Create `sample.def` in the workspace root using the exact example from `Specifications.txt`.
2. Run `dotnet run --project src/DataEntry -- sample.def` and verify preview UI.
3. Run `dotnet run --project src/DataEntry -- sample.def --build --output ./out` and verify build output.
4. Run the generated executable and manually verify the form layout.
5. Fix any integration issues found.

**Relevant Context**
- `Specifications.txt` lines 33–50 for the sample DSL.
- All sub-tasks 1–7 must be complete before this sub-task.

**Status:** [ ] pending

---

### Sub-Task 9 — User Manual

**Intent**
Write a comprehensive user manual (`MANUAL.md`) that documents the DSL language for end users — people who will write `.def` files to define their own data-entry forms. This document is the authoritative reference for the language.

**Expected Outcomes**
- `MANUAL.md` exists at the workspace root.
- Covers all DSL constructs: `DATA-SECTION`, `FILE`, `APPEND`/`NOAPPEND`, `LRECL`, `LEND`, `RECORD`, field definitions, `FORMAT=` mask syntax, `SCREEN-SECTION`, screen field layout, `INTO`, `VALIDATE WITH`.
- Covers all FORMAT mask characters: `X`, `U`, `L`, `9`, `Z`, `\c` escapes, alignment rules (right-adjust numbers, left-adjust text).
- Covers the compiler tool's CLI usage: positional file arg, `--build`, `--output`.
- Covers the runtime hotkeys baked into every generated application: Tab/Shift-Tab, F3, Right-Ctrl (save), Page Up/Down, Shift-PgUp/PgDn/Home/End.
- Includes at least one complete worked example (the CUST record from the spec).
- Written in plain, clear language — not aimed at programmers.

**Todo List**
1. Create `MANUAL.md` at the workspace root.
2. Write an Introduction section explaining what the tool does and who it is for.
3. Write a "Running the Compiler" section covering CLI usage and flags.
4. Write the "DATA-SECTION" reference with all keywords and an example.
5. Write the "RECORD Definition" reference: field syntax (`NAME START= LEN=`), FORMAT masks with a table of all mask characters and examples.
6. Write the "SCREEN-SECTION" reference: field placement, `INTO`, `VALIDATE WITH`.
7. Write a "Runtime Hotkeys" section listing all default keys in the generated application.
8. Write a complete worked example from scratch — define a form, compile it, run it.
9. Write an "Error Messages" reference covering common parse/validation errors and how to fix them.

**Relevant Context**
- `Specifications.txt` — primary source of truth for all language rules.
- FORMAT alignment: numbers right-adjusted (space-padded or Z zero-filled), text left-adjusted (space-padded).
- LEND options: `CRLF`, `LF`, `CR`, `NONE`.
- This sub-task can be done at any point — no dependency on other sub-tasks.

**Status:** [ ] pending
