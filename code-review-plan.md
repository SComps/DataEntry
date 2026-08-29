# DataEntry DSL Compiler — Code Review & Issue Remediation Plan

## Top-Level Overview

This is a VB.NET/.NET 10 DSL compiler that parses `.def` form-definition files, previews
them in a Terminal.Gui TUI, and generates complete standalone VB.NET applications. A full
code evaluation has been performed against all source files and all sample `.def` files.
The project is feature-complete with 50+ passing unit tests and clean architecture;
however several issues ranging from high-severity (code-injection risk in the generator)
down to sample-file cosmetic problems were found and are addressed here.

Issues are grouped into independent, ordered sub-tasks so each fix can be reviewed in
isolation.

---

## Sub-Task 1 — Fix String Escaping in CodeGenerator (Generated Code Corruption)

**Status:** [ ] pending

### Intent
`EscapeString` (CodeGenerator.vb:623-625) escapes backslashes and double-quotes but does
**not** escape embedded newlines (`\r`, `\n`). If a DSL field label, file path, or record
name contains a newline character the generated `.vb` file will be syntactically broken
(the string literal will span multiple lines, causing a compile error in the generated
project). This is the highest-severity correctness bug.

`ValidateFunc` names are embedded directly as VB.NET function names (line 607) without
passing through `MakeSafeName`. A name like `CHECK SKU` would emit invalid VB.NET
(though the VB compiler would reject it rather than execute anything dangerous).

### Expected Outcomes
- `EscapeString` strips CR and LF characters so string literals in generated code are
  always single-line.
- `ValidateFunc` values are sanitized through `MakeSafeName` before being emitted as
  function identifiers.
- Existing tests continue to pass; add a test covering a label with an embedded newline
  and a validate-func name with whitespace.

### Todo List
1. In `EscapeString` (CodeGenerator.vb:623), add `.Replace(vbCr, "").Replace(vbLf, "")` after the existing replacements.
2. In `WriteValidationStubs` (CodeGenerator.vb:605-610), wrap `fn` with `MakeSafeName(fn)` before emitting it as a function name and the TODO comment.
3. Add a test case in `DslTests.vb` that supplies a label containing `vbCrLf` and verifies the generated code does not contain a bare newline inside a string literal.

### Relevant Context
- [`CodeGenerator.EscapeString`](src/DataEntry.Core/CodeGenerator.vb:623)
- [`CodeGenerator.WriteValidationStubs`](src/DataEntry.Core/CodeGenerator.vb:586)
- [`CodeGenerator.MakeSafeName`](src/DataEntry.Core/CodeGenerator.vb:627)
- [`DslTests.vb`](src/DataEntry.Tests/DslTests.vb)

---

## Sub-Task 2 — Add try/catch Around All File I/O Paths

**Status:** [ ] pending

### Intent
Three locations read or create files without any exception handling. If a file is locked,
permissions are denied, or the path is invalid, the process will crash with an unhandled
exception and no user-friendly message:

1. `Program.vb:48` — `File.ReadAllText(defFile)` (main entry point).
2. `PreviewUi.MenuOpen` — `File.ReadAllText(path)` when opening a new file from dialog.
3. `CodeGenerator.GenerateProject` — `Directory.CreateDirectory(outputDir)` can fail on
   permission denied.

### Expected Outcomes
- An `IOException` / `UnauthorizedAccessException` in any of these paths produces a
  clear, human-readable error rather than a stack trace.
- In `--build` mode (non-interactive), errors are written to `Console.Error` and the
  process exits with code 1.
- In interactive (preview) mode, errors are shown in a `MessageBox.Query` dialog.
- No changes to the happy path.

### Todo List
1. Wrap `File.ReadAllText(defFile)` in `Program.vb:48` in a `Try/Catch ex As Exception` block; write to `Console.Error` and call `Environment.Exit(1)`.
2. Wrap `File.ReadAllText(path)` in `PreviewUi.MenuOpen` in a `Try/Catch`; show a `MessageBox.Query` with the error message.
3. Wrap `Directory.CreateDirectory(outputDir)` in `CodeGenerator.GenerateProject` in a `Try/Catch`; re-throw as a descriptive `InvalidOperationException`.
4. Wrap `proc.Start()` / `proc.WaitForExit()` in `BuildRunner.Build` in a `Try/Catch` to handle "dotnet not found" gracefully (return `BuildResult.Success = False` with the exception message in `Output`).

### Relevant Context
- [`Program.vb:46-49`](src/DataEntry/Program.vb:46)
- [`PreviewUi.MenuOpen`](src/DataEntry.Core/PreviewUi.vb:260)
- [`CodeGenerator.GenerateProject`](src/DataEntry.Core/CodeGenerator.vb:11)
- [`BuildRunner.Build`](src/DataEntry.Core/BuildRunner.vb:55)

---

## Sub-Task 3 — Add Timeout to BuildRunner.WaitForExit

**Status:** [ ] pending

### Intent
`BuildRunner.Build` calls `proc.WaitForExit()` with no timeout (BuildRunner.vb:79). If
`dotnet publish` hangs (e.g. a NuGet restore waiting for a network resource that never
responds) the DataEntry compiler hangs indefinitely with no way to cancel.

### Expected Outcomes
- `WaitForExit` is replaced with a timeout-based call (5 minutes).
- If the timeout is exceeded, the spawned process is killed and `BuildResult` is returned
  with `Success = False` and an appropriate message in `Output`.
- Streaming-output mode (`stream = True`) still works correctly.

### Todo List
1. Replace `proc.WaitForExit()` with `proc.WaitForExit(300_000)` (300 seconds).
2. After the call, check the return value; if `False`, call `proc.Kill(entireProcessTree:=True)` and set a timeout message.
3. Return the `BuildResult` with `Success = False` and a "Build timed out after 5 minutes" message.

### Relevant Context
- [`BuildRunner.Build`](src/DataEntry.Core/BuildRunner.vb:55)

---

## Sub-Task 4 — Validate File Path Safety in DSL (Path Traversal)

**Status:** [ ] pending

### Intent
The DSL `FILE` directive accepts any file path, including `../../../sensitive.txt`. The
validator never checks whether the path is safe. When the generated application runs, it
will write to that exact path. The audience is programmers who may intentionally use
relative paths, so this is a **Warning** only — not an error.

### Expected Outcomes
- `DslValidator.CheckDataSection` emits a **Warning** when the `FILE` path contains `..`
  segments or is an absolute (rooted) path.
- Tests in `DslTests.vb` cover both a safe path (no warning) and a traversal path
  (warning emitted).

### Todo List
1. In `DslValidator.CheckDataSection`, after the `FilePath` null check, add: if `ds.FilePath.Contains("..")` or `Path.IsPathRooted(ds.FilePath)`, emit a Warning describing the risk.
2. Add two test cases in `ErrorDetectionTests` (or a new class): one with `FILE data.dat` (no warning) and one with `FILE ../../out.dat` (warning expected).

### Relevant Context
- [`DslValidator.CheckDataSection`](src/DataEntry.Core/DslValidator.vb:32)
- [`DslTests.vb — ErrorDetectionTests`](src/DataEntry.Tests/DslTests.vb)

---

## Sub-Task 5 — Expand ValidColors Set in DslValidator to Match ColorHelper

**Status:** [ ] pending

### Intent
`DslValidator.ValidColors` (DslValidator.vb:239-243) does not include the `Bright*`
color variants: `BrightRed`, `BrightGreen`, `BrightYellow`, `BrightBlue`,
`BrightMagenta`, `BrightCyan`. However `ColorHelper.ToColor16` already maps all of these
correctly to `ColorName16` values. The result is that perfectly valid `.def` files (all
three sample forms use `BrightYellow`) produce spurious "Unknown color" warnings during
validation.

### Expected Outcomes
- `ValidColors` contains exactly the same set of names that `ColorHelper.ToColor16` can
  resolve (minus the fallback aliases `"red"`, `"green"` etc. — those are aliases, not
  primary names, so they needn't be in `ValidColors`).
- No spurious color warnings are emitted for `BrightYellow`, `BrightRed`, `BrightGreen`,
  `BrightBlue`, `BrightMagenta`, or `BrightCyan`.
- Existing color validation tests pass; update any test that previously asserted a warning
  for a `Bright*` color.

### Todo List
1. Add `"BrightRed"`, `"BrightGreen"`, `"BrightYellow"`, `"BrightBlue"`, `"BrightMagenta"`, `"BrightCyan"` to the `ValidColors` `HashSet` in `DslValidator.vb:239`.
2. Review `DslTests.vb — ColorHelperTests` to confirm no test asserts a warning for these names and update if needed.

### Relevant Context
- [`DslValidator.ValidColors`](src/DataEntry.Core/DslValidator.vb:239)
- [`ColorHelper.ToColor16`](src/DataEntry.Core/ColorHelper.vb:9)

---

## Sub-Task 6 — Replace Magic Numbers 80/24 with Named Constants

**Status:** [ ] pending

### Intent
The terminal size `80` (columns) and `24` (rows) are hard-coded as raw integer literals
in six separate places in `DslValidator.vb`. This makes the code hard to maintain.
The fix is minimal: extract two module-level constants.

### Expected Outcomes
- Two `Private Const` values `MaxCols = 80` and `MaxRows = 24` replace all inline
  literals in the screen-bounds checks.
- No behaviour change; existing tests continue to pass.

### Todo List
1. Add `Private Const MaxCols As Integer = 80` and `Private Const MaxRows As Integer = 24` at the top of the `DslValidator` class body.
2. Replace all occurrences of the literals `80` and `24` used in boundary checks with these constants.

### Relevant Context
- [`DslValidator.CheckScreenSections`](src/DataEntry.Core/DslValidator.vb:100)

---

## Sub-Task 7 — Remove Dead Parameter from AddWarning

**Status:** [ ] pending

### Intent
`AddWarning` in `DslValidator` has an `Optional severity As String = "Warning"`
parameter (DslValidator.vb:258) that is never passed by any caller — every call uses
the default. The parameter is dead code and misleading (it implies you can vary the
severity, but none of the callers do).

### Expected Outcomes
- The `Optional severity As String = "Warning"` parameter is removed from `AddWarning`.
- All existing callers compile without change (they never pass it).
- Add a one-line comment in `ColorHelper.vb` near the `Case Else` fallback noting that
  the validator already warns about unknown colors upstream.

### Todo List
1. Remove the `Optional severity As String = "Warning"` parameter from `DslValidator.AddWarning`.
2. Remove the corresponding `.Severity = severity` in the method body and hard-code `.Severity = "Warning"`.
3. Add a comment in `ColorHelper.vb` near line 27 (`Case Else : Return ColorName16.Gray`) explaining the fallback.

### Relevant Context
- [`DslValidator.AddWarning`](src/DataEntry.Core/DslValidator.vb:258)
- [`ColorHelper.vb`](src/DataEntry.Core/ColorHelper.vb:27)

---

## Sub-Task 8 — Promote Over-Long FORMAT Mask Mismatch to Error

**Status:** [ ] pending

### Intent
When `FORMAT` mask token count ≠ `LEN`, the validator emits only a Warning. The design
intent (confirmed) is that a **shorter mask than LEN is valid** — the author deliberately
defines a wider field so the data-entry operator types fewer characters than the full
field width. `FormatHelper.ApplyMask` handles this correctly: the `fieldLen` parameter
pads/trims to the correct record width.

A mask **longer** than LEN is always wrong: `ApplyMask` will apply mask characters
beyond the field boundary, placing data outside the intended record slice — genuine
data corruption. This must be an **Error**.

The confirmed fix: **Error** when `fmt.Tokens.Count > fld.Len`; keep **Warning** when
`fmt.Tokens.Count < fld.Len`.

### Expected Outcomes
- Mask-longer-than-field produces a validation `Error` (not Warning), blocking codegen.
- Mask-shorter-than-field remains a Warning (intentional partial-fill pattern).
- Existing tests that assert a Warning for an over-long mask are updated to assert Error.

### Todo List
1. In `DslValidator.CheckRecord` at the format-check block (DslValidator.vb:75-83), split the `<>` condition: if `fmt.Tokens.Count > fld.Len` call `AddError`; if `fmt.Tokens.Count < fld.Len` call `AddWarning`.
2. Review `DslTests.vb — FormatMaskTests` for any test asserting Warning for an over-long mask and update it to assert Error.

### Relevant Context
- [`DslValidator.CheckRecord — format check`](src/DataEntry.Core/DslValidator.vb:75)
- [`DslTests.vb — FormatMaskTests`](src/DataEntry.Tests/DslTests.vb)

---

## Sub-Task 9 — Document and Test Implicit Field Position Tracking

**Status:** [ ] pending

### Intent
`DslValidator.CheckRecord` tracks field positions implicitly when `START` is omitted.
The tracking variable `pos` advances by `startPos + fld.Len` after each field. This
means a field with an explicit `START` followed by one with no START will be placed
at the position after the explicitly-placed field. This is intentional (COBOL-like),
but it is undocumented and untested for the gap scenario.

### Expected Outcomes
- A code comment in `CheckRecord` explains the implicit tracking logic and the gap
  behaviour.
- A test case exercises mixed explicit/implicit START values without triggering a
  spurious boundary error.

### Todo List
1. Add an explanatory comment block above the position-tracking logic in `CheckRecord`.
2. Add a test in `DslTests.vb` with fields using mixed explicit/implicit START values, asserting no validation errors.

### Relevant Context
- [`DslValidator.CheckRecord`](src/DataEntry.Core/DslValidator.vb:55)
- [`DslTests.vb`](src/DataEntry.Tests/DslTests.vb)

---

## Sub-Task 10 — Overhaul Sample .def Files: Fix Issues and Add Feature-Coverage Samples

**Status:** [ ] pending

### Intent
The sample `.def` files serve a dual purpose: they are the end-user's primary learning
resource and the compiler's regression-test fixtures. This sub-task has two goals:

**Goal A — Fix known defects** in the four existing samples (layout overflow, FORMAT/LRECL
mismatch, box border misalignment, cosmetic issues) so every existing sample is
defect-free and production-quality.

**Goal B — Add new sample files** to ensure every DSL feature has at least one clear,
commented demonstration. The following features are currently undemonstrated:

| Feature | Status |
|---------|--------|
| `LEND=NONE` (continuous-stream file) | ❌ not shown |
| `LEND=CR` (bare carriage-return) | ❌ not shown |
| `FORMAT L` (lower-case mask) | ❌ not shown |
| `FORMAT \\` (literal backslash in mask) | ❌ not shown |
| `FIELD "label" ROW= COL=` inline label syntax | ❌ not shown (all samples use separate PROMPT+FIELD) |
| `PROMPT_ROW= PROMPT_COL=` separate label placement | ❌ not shown |
| `LABEL` keyword (synonym for PROMPT) | ❌ not shown |
| Default colors — no COLOR/FG/BG specified at all | ❌ every sample overrides colors |
| Minimal "getting started" form (mirrors worked example in MANUAL) | ❌ not shown |

---

### Part A — Fix Defects in Existing Samples

#### sample.def defects
1. **LRECL off-by-one**: `LRECL=130` but fields total 129 bytes. Fix: change to `LRECL=129`.
2. **Column overflow**: Section border at ROW=6 COL=2 is 80 chars → right edge = col 81.
   Fix: shorten by one character or move to COL=3 so right edge ≤ col 80.
3. **Asymmetric layout**: Title banner is 57 chars at COL=13 (right edge=69); section box
   is ~77 chars at COL=2 (right edge=78). Unify them — title banner should span the same
   columns as the section box below it.
4. **Unclear continuation label**: `PROMPT "        "` (8 spaces) for CADDR2 at ROW=10.
   Replace with `PROMPT "       2:"` to give the second address line a visible identity
   that aligns with `"Address:"` on row 9.
5. **Box bottom**: Verify section bottom border exactly matches section top border width.

#### customer.def defects
1. **Section bottom borders are 2 chars shorter than their tops** in all four sections
   (Identification, Name, Address, Contact). Each bottom `└──...┘` = 69 chars but each
   top `┌─ ... ┐` = 71 chars. Fix all four bottoms to 71 chars.

#### inventory.def defects
1. **Section bottom borders are 2-3 chars shorter than their tops** in all three sections.
   Fix each bottom to match its top width, with all right edges at col 75 (top COL=4,
   width=72 chars → right edge col 75).

#### timesheet.def defects
1. **TOTALS FORMAT mask too long**: `FORMAT=ZZZ.99.` = 6 tokens but `LEN=5`. After
   Sub-Task 8 this becomes a hard Error. Fix: `FORMAT=ZZ.99.` (5 tokens, max `99.99`).
2. **All section bottom borders short**: Screen 1 Employee/Week&Job tops=72 chars,
   bottoms=70. Screen 2 DailyHours/Summary tops=74 chars, bottoms=70. Fix all four.

---

### Part B — New Sample Files

All new files go in `src/DataEntry.Tests/Samples/` and are also referenced/embedded in
the test project so they are available as embedded resources for future tests. A copy (or
symlink) of the most illustrative new sample should also appear at the workspace root
alongside `sample.def`.

---

#### New file: `minimal.def`
**Purpose**: Mirror the worked example in MANUAL.md — the simplest possible valid form.
Demonstrates: inline `FIELD "label"` syntax (no separate PROMPT), default colors
(no COLOR/FG/BG at all — shows what Terminal.Gui's built-in defaults look like),
`LEND=CRLF` and `APPEND`.

**Record layout** (LRECL=82):
- `CNAME` START=1 LEN=30 `FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.`
- `CADDR1` LEN=30 `FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.`
- `CCITY` LEN=20 `FORMAT=XXXXXXXXXXXXXXXXXXXX.`
- `CSTATE` LEN=2 `FORMAT=UU.`

**Screen**: One screen, no decorative prompts, no color overrides — pure
`FIELD "Name" ROW= COL= LEN= INTO` pattern. Comment block at top explaining that this
is the minimal/default-color demonstration.

---

#### New file: `formats.def`
**Purpose**: Exhaustively demonstrate every FORMAT mask character and the `L` lower-case
and `\\` literal-backslash features never shown elsewhere. Also demonstrates `LEND=NONE`
(continuous stream) and using `LABEL` as a synonym for `PROMPT`.

**Record layout** (LRECL=72, LEND=NONE):
- `UPPER`  LEN=10 `FORMAT=UUUUUUUUUU.`   — forces uppercase
- `LOWER`  LEN=10 `FORMAT=LLLLLLLLLL.`   — forces lowercase (new: L mask)
- `MIXED`  LEN=10 `FORMAT=XXXXXXXXXX.`   — any character
- `DIGITS` LEN=6  `FORMAT=999999.`       — digit, space-padded
- `ZFILL`  LEN=6  `FORMAT=ZZZZZZ.`       — digit, zero-filled
- `PHONE`  LEN=12 `FORMAT=999\-999\-9999.` — literal hyphen
- `AMOUNT` LEN=9  `FORMAT=ZZZZZZ.99.`   — currency-style
- `PATH`   LEN=9  `FORMAT=XXX\\XXX\\XX.` — literal backslash (new: `\\`)

**Screen**: One screen using `LABEL` keyword (instead of `PROMPT`) for section headers
to show the synonym. Inline `FIELD "label"` for some fields and separate PROMPT+FIELD
for others — demonstrating both styles side-by-side with comments explaining the
difference. `FG=White BG=DarkMagenta` color scheme to show a different palette.

---

#### New file: `contacts.def`
**Purpose**: Demonstrate `PROMPT_ROW=` / `PROMPT_COL=` (separate label placement),
`LEND=LF` (Unix line endings), and `NOAPPEND` mode. Also demonstrates the `ERROR` color
state in conjunction with `VALIDATE WITH` so the red-field behavior is clearly shown,
and multiple `VALIDATE WITH` functions on the same form.

**Record layout** (LRECL=80, LEND=LF, NOAPPEND):
- `CONTID`  START=1 LEN=6  `FORMAT=999999.`
- `FNAME`   LEN=15 `FORMAT=XXXXXXXXXXXXXXX.`
- `LNAME`   LEN=20 `FORMAT=XXXXXXXXXXXXXXXXXXXX.`
- `EMAIL`   LEN=30 `FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.`
- `MOBILE`  LEN=9  `FORMAT=999\-9999.`  — 8-digit local number with dash

**Screen**: One screen. For the Name fields, use `PROMPT_ROW= PROMPT_COL=` to place
labels in a banner-row above the input boxes (stacked layout rather than side-by-side),
demonstrating that the label and its TextField can be on different rows. Both `FNAME`
and `LNAME` fields include `VALIDATE WITH` functions. Rich commented header explaining
each feature being demonstrated.

---

### Expected Outcomes
- All existing `.def` files pass `dotnet test` and the DataEntry validator with zero
  errors and zero warnings.
- Three new `.def` files exist in `src/DataEntry.Tests/Samples/` and `sample.def`-level
  root copies where appropriate.
- `formats.def` and `contacts.def` are embedded as test resources in
  `DataEntry.Tests.vbproj`.
- The new files are syntactically valid and would generate and build cleanly.
- Together the full sample set covers every documented DSL feature.

### Todo List

#### A1 — Fix sample.def
1. Change `LRECL=130` → `LRECL=129`.
2. Widen and recentre the title banner to match the section box column span (both at COL=2, width=77 chars).
3. Replace the blank-space prompt for CADDR2 with `"       2:"`.
4. Confirm section top and bottom borders are the same width.

#### A2 — Fix customer.def
1. Fix all four section bottom borders from 69 chars to 71 chars (add 2 `─` characters to each).

#### A3 — Fix inventory.def
1. Fix all three section bottom borders to 72 chars at COL=4 (matching the tops, right edge=col 75).

#### A4 — Fix timesheet.def
1. Change `TOTALS` FORMAT from `ZZZ.99.` to `ZZ.99.`.
2. Fix all four section bottom borders (2 in screen 1, 2 in screen 2) to match their top widths.

#### B1 — Create minimal.def
1. Write `src/DataEntry.Tests/Samples/minimal.def` demonstrating the inline `FIELD "label"` syntax and default colors.
2. Add it to `DataEntry.Tests.vbproj` as an embedded resource.
3. Add a basic parse/validate test in `DslTests.vb` confirming it compiles cleanly.

#### B2 — Create formats.def
1. Write `src/DataEntry.Tests/Samples/formats.def` demonstrating `L` mask, `\\` literal, `LEND=NONE`, `LABEL` keyword, and all FORMAT types in one form.
2. Add it to `DataEntry.Tests.vbproj` as an embedded resource.
3. Add a test confirming it parses and validates with zero errors (and that specifically the `L` and `\\` mask tokens appear in the AST).

#### B3 — Create contacts.def
1. Write `src/DataEntry.Tests/Samples/contacts.def` demonstrating `PROMPT_ROW=`/`PROMPT_COL=`, `LEND=LF`, `NOAPPEND`, and multiple `VALIDATE WITH` functions.
2. Add it to `DataEntry.Tests.vbproj` as an embedded resource.
3. Add a test confirming it parses cleanly and that `PromptRow`/`PromptCol` are set on the expected `ScreenField` AST nodes.

### Relevant Context
- [`sample.def`](sample.def)
- [`src/DataEntry.Tests/Samples/customer.def`](src/DataEntry.Tests/Samples/customer.def)
- [`src/DataEntry.Tests/Samples/inventory.def`](src/DataEntry.Tests/Samples/inventory.def)
- [`src/DataEntry.Tests/Samples/timesheet.def`](src/DataEntry.Tests/Samples/timesheet.def)
- [`src/DataEntry.Tests/DataEntry.Tests.vbproj`](src/DataEntry.Tests/DataEntry.Tests.vbproj)
- [`src/DataEntry.Tests/DslTests.vb`](src/DataEntry.Tests/DslTests.vb)
- [`MANUAL.md`](MANUAL.md)

---

## Sub-Task 11 — FULL= Field Attribute: Scroll-Shift Fix + Full-Field Behavior Control

**Status:** [ ] pending

### Intent
Two related problems are resolved together because they live in the same handler blocks:

**Problem 1 — Scroll-shift bug (existing defect):**
When a user types the last character of a fixed-length field the visible text shifts left,
hiding the first character, while the cursor lands one position past the end with a blank
space. Root cause: Terminal.Gui `TextField` advances its internal `ScrollOffset` the
moment `InsertionPoint` reaches the widget's right edge — *before* the `TextChanging`
event fires. The current handler truncates `ev.Result` after the fact, fixing the stored
value but not the already-advanced viewport. The fix is to **cancel** the incoming edit
entirely when the field is already full (`ev.Result = currentText`), so Terminal.Gui
never advances the scroll offset.

**Problem 2 — New FULL= attribute (new feature):**
Add a per-field DSL attribute that controls what happens the moment a field reaches
capacity:

| Value | Behavior |
|-------|----------|
| `FULL=ADVANCE` | When the last character is typed, automatically move focus to the next field (or save if last field). This is the current behavior and remains the **default**. |
| `FULL=STAY` | When the last character is typed, hold the cursor at the end of the field and inhibit further entry. The user must press Tab/Enter to move on. |

Both values apply the scroll-shift fix. The difference is only what `TextChanged` does.

**DSL syntax** — on the field's continuation line alongside colors:
```
FIELD ROW=12 COL=47 LEN=2 INTO CUST.CSTATE
    NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed FULL=STAY

FIELD ROW=12 COL=62 LEN=5 INTO CUST.CZIP
    NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed FULL=ADVANCE
```

`FULL=` is optional. Omitting it is identical to `FULL=ADVANCE` — all existing `.def`
files remain valid without change.

**Files touched:** AST, Lexer, Parser, Validator (warning on bad value), CodeGenerator,
PreviewUi, MANUAL.md.

### Expected Outcomes
- **Scroll-shift**: typing the last character of any field (regardless of `FULL=` value)
  fills it cleanly — no leftward scroll, no extra blank character, first character always
  visible.
- **`FULL=ADVANCE`** (or omitted): identical to current auto-advance/auto-save behavior.
- **`FULL=STAY`**: cursor locks at end of full field; no focus change; further keystrokes
  are silently discarded until the user explicitly Tabs or presses Enter.
- All existing `.def` files and tests continue to pass unchanged.
- The new `FullBehavior` enum and `ScreenField.Full` property appear in the AST.
- The validator emits a Warning for an unrecognised `FULL=` value.
- New tests cover: parsing `FULL=STAY`, parsing `FULL=ADVANCE`, omitted `FULL=` defaults
  to Advance, bad value warns, generated `MainForm.vb` contains correct handler logic for
  both values.

### Todo List

#### 1 — AST (`Ast.vb`)
1. Add a `Public Enum FullBehavior` with values `Advance` and `Stay`.
2. Add `Public Property Full As FullBehavior = FullBehavior.Advance` to `ScreenField`.

#### 2 — Lexer (`DslLexer.vb`)
1. Add `"FULL"`, `"ADVANCE"`, `"STAY"` to the `Keywords` HashSet.

#### 3 — Parser (`DslParser.vb`)
1. In `ParseFieldColorLine`, add an `ElseIf IsKeyword("FULL")` branch:
   consume `FULL`, expect `=`, consume the value token, set `fld.Full` to
   `FullBehavior.Advance` or `FullBehavior.Stay` based on the value.
   Store unrecognised values as `Advance` (safe default) and record a parse warning.

#### 4 — Validator (`DslValidator.vb`)
1. In `CheckScreenSections`, after the color checks for each field, add a check:
   if the raw `FULL` token value was not `ADVANCE` or `STAY`, emit a Warning
   (this is already handled in the parser, but a belt-and-suspenders note is fine).
   *In practice the parser already defaults to Advance, so no separate validator
   action is needed — the parser warning is sufficient.*

#### 5 — CodeGenerator (`CodeGenerator.vb`)
1. **Fix scroll-shift** in the `TextChanging` handler (lines 404-408):
   Change `ev.Result = ev.Result.Substring(0, {sfld.Len})` to
   `ev.Result = DirectCast(sender, TextField).Text` — cancels the edit when full.
2. **`FULL=ADVANCE`** (current behavior, `isLastField` branch):
   `TextChanged` calls `SaveRecord()` + `ClearFields()` when `Text.Length = maxLen`.
   No change needed here — already correct.
3. **`FULL=ADVANCE`** (intermediate field branch):
   `TextChanged` calls `AdvanceFocus` when `Text.Length = maxLen`. No change needed.
4. **`FULL=STAY`** — emit a *different* `TextChanged` handler body:
   when `Text.Length = maxLen`, do nothing (just ensure `InsertionPoint` stays
   at `maxLen - 1` to keep cursor visible at end of field). No `AdvanceFocus`, no save.
   The Enter/Tab keys still work via the existing `KeyDown` handler.

#### 6 — PreviewUi (`PreviewUi.vb`)
1. **Fix scroll-shift**: same change as CodeGenerator — replace `Substring` with
   `DirectCast(sender, TextField).Text` in the `TextChanging` lambda.
2. For `sfld.Full = FullBehavior.Stay`: emit a `TextChanged` handler that only
   pins `InsertionPoint` to `maxLen - 1` without advancing focus or saving.

#### 7 — Manual (`MANUAL.md`)
1. Add `FULL=` to the FIELD line attribute table.
2. Add a short paragraph explaining both values and the default.

#### 8 — Tests (`DslTests.vb`)
1. Add `FullBehaviorTests` class with tests:
   - `FULL=STAY` parses to `FullBehavior.Stay` on the `ScreenField`.
   - `FULL=ADVANCE` parses to `FullBehavior.Advance`.
   - Omitting `FULL=` defaults to `FullBehavior.Advance`.
   - Generated `MainForm.vb` for a `FULL=STAY` field does **not** contain
     `AdvanceFocus` in the `TextChanged` handler for that field.
   - Generated `MainForm.vb` for a `FULL=ADVANCE` field **does** contain
     `AdvanceFocus`.
   - `TextChanging` handler in generated code sets `ev.Result` to current
     text (not a `Substring`) when over-length.

### Relevant Context
- [`Ast.vb — ScreenField`](src/DataEntry.Core/Ast.vb:86)
- [`DslLexer.vb — Keywords`](src/DataEntry.Core/DslLexer.vb:31)
- [`DslParser.vb — ParseFieldColorLine`](src/DataEntry.Core/DslParser.vb:397)
- [`CodeGenerator.vb — TextChanging handler`](src/DataEntry.Core/CodeGenerator.vb:403)
- [`CodeGenerator.vb — TextChanged handler`](src/DataEntry.Core/CodeGenerator.vb:409)
- [`PreviewUi.vb — BuildFields`](src/DataEntry.Core/PreviewUi.vb:130)
- [`MANUAL.md`](MANUAL.md)
- [`DslTests.vb`](src/DataEntry.Tests/DslTests.vb)

---

## Notes for Implementation

- Run `dotnet test` after each sub-task to confirm no regressions before proceeding.
- **Sub-task 11 is the highest-priority user-visible fix** and should be implemented
  first, before any of the code-quality sub-tasks.
- Sub-tasks 1 and 2 are the next highest-priority correctness fixes.
- Sub-task 5 (expand ValidColors) must be completed before Sub-task 10 (fix .def files)
  otherwise the validator will still warn about BrightYellow colors.
- Sub-task 8 (format/len error promotion) must be completed before Sub-task 10
  otherwise the TOTALS field fix in timesheet.def will not be validated correctly.
- Sub-tasks 6 and 7 are pure cleanup with zero behaviour change risk.
- Sub-task 10 is the most demanding: fix existing files first (A1–A4), then add new files
  one at a time (B1, B2, B3), validating each with `dotnet test` before moving on.
- When writing the new .def files, carefully verify: COL + len(text) - 1 ≤ 80 for every
  PROMPT, COL + LEN - 1 ≤ 80 for every FIELD, and FORMAT token count ≤ LEN for every
  record field.
