# Plan: PROTECTED Field Keyword

## Overview

Add a `PROTECTED` keyword to the `FIELD` declaration in the `SCREEN-SECTION`.
A protected field is visible on the screen but cannot be typed into by the user —
exactly like a protected field on a 3270 green-screen terminal.

Typical use-case: a calculated field whose value is set by a `VALIDATE` block
`Assign` rule (e.g. `GROSS IS HOURS * RATE`).  The field shows the result but
the user cannot edit it.

---

## Language Design

### Syntax

```
FIELD ROW=10 COL=20 LEN=9 INTO WORK.GROSS PROTECTED
    NORMAL=WhiteOnBlue
```

`PROTECTED` is optional and positional — it can appear anywhere on the `FIELD`
attribute line alongside `ROW=`, `COL=`, `LEN=`, `INTO`, `VALIDATE WITH`.

### Runtime behaviour of a protected field

| Aspect | Behaviour |
|--------|-----------|
| Rendering | Rendered as a read-only `TextField` with `ReadOnly = True` |
| Focus / Tab | Excluded from Tab-stop navigation (`TabStop = False`) |
| Keyboard | No keystrokes accepted (Terminal.Gui enforces this via ReadOnly) |
| Value updates | Populated programmatically from calculation results or record load |
| Color | Uses `NORMAL=` colour only (no `FOCUS=` since it never receives focus) |
| Record save | Included in `SaveRecord` at its resolved position — same as any other field |
| Record load | Included in `PopulateFields` — displays existing record data |

### Validator check

If a `VALIDATE` block has an `Assign` rule targeting a field name (e.g. `GROSS`),
and that field appears on the screen **without** `PROTECTED`, emit a **warning**:
`"Field 'GROSS' is the target of an Assign rule in VALIDATE 'CALCGROSS' but is not
declared PROTECTED — the user can overwrite the calculated value."`

---

## Sub-Tasks

---

### Sub-Task 1 — AST: add `IsProtected` property to `ScreenField`

**Intent**
Add the single boolean flag that everything downstream will read.

**Expected Outcomes**
- `ScreenField` gains `Public Property IsProtected As Boolean = False`.
- No behaviour change.

**Todo List**
1. Add `Public Property IsProtected As Boolean = False` to `ScreenField` in `Ast.vb`.

**Relevant Context**
- `src\DataEntry.Core\Ast.vb` — `ScreenField` class, line 104

**Status** — `[ ] pending`

---

### Sub-Task 2 — Lexer: add `PROTECTED` keyword

**Intent**
The lexer must emit `PROTECTED` as a `Keyword` token.

**Expected Outcomes**
- `"PROTECTED"` added to the `Keywords` HashSet in `DslLexer.vb`.

**Todo List**
1. Add `"PROTECTED"` to the keywords set in `DslLexer.vb`.

**Relevant Context**
- `src\DataEntry.Core\DslLexer.vb` lines 31–40 — keyword set

**Status** — `[ ] pending`

---

### Sub-Task 3 — Parser: recognise `PROTECTED` on the `FIELD` attribute line

**Intent**
The parser's `ParseScreenField` loop already handles all field attributes.
Add `PROTECTED` as a flag-style keyword (no `=` value, just presence).

**Expected Outcomes**
- `IsKeyword("PROTECTED")` branch added to the attribute loop in `ParseScreenField`.
- Sets `fld.IsProtected = True` and consumes the token.
- No parse error if `PROTECTED` is absent.

**Todo List**
1. Add `ElseIf IsKeyword("PROTECTED")` branch in `ParseScreenField` attribute loop
   in `DslParser.vb` — consumes the keyword and sets `fld.IsProtected = True`.

**Relevant Context**
- `src\DataEntry.Core\DslParser.vb` lines 344–376 — `ParseScreenField` attribute loop

**Status** — `[ ] pending`

---

### Sub-Task 4 — Validator: warn when Assign target is not PROTECTED

**Intent**
When a `VALIDATE` block `Assign` rule names a target field that appears on the
screen, check whether that field is declared `PROTECTED`.  If not, emit a warning
so the author is aware the user can overwrite the calculated value.

**Expected Outcomes**
- New check in `CheckValidateSection` (or a companion helper) in `DslValidator.vb`.
- For each `Assign` rule, look up `TargetField` in all screen fields.
- If found and `IsProtected = False` → warning.
- If found and `IsProtected = True` → no warning (correct usage).
- If not on screen at all → no warning (silent record-only write is valid).

**Todo List**
1. After the existing field-reference checks in `CheckValidateSection`, add a loop
   over `_doc.Screens` to find whether `rule.TargetField` is a screen field.
2. If found and `Not sfld.IsProtected` → `AddWarning(...)`.

**Relevant Context**
- `src\DataEntry.Core\DslValidator.vb` — `CheckValidateSection`, lines 308–355

**Status** — `[ ] pending`

---

### Sub-Task 5 — Code Generator: render protected fields as read-only

**Intent**
In `WriteMainForm`, when `sfld.IsProtected = True`:
- Emit `{vn}.ReadOnly = True` immediately after the TextField is created.
- Emit `{vn}.TabStop = False` so Tab navigation skips it entirely.
- Skip emitting the `TextChanging`, `TextChanged`, and `KeyDown` auto-advance
  event handlers (a protected field never advances focus or triggers saves).
- The `Leave` validation handler is also skipped (no user input to validate).
- `PopulateFields` and `SaveRecord` are unchanged — protected fields are included
  in both (they hold data; the user just can't edit it).

**Expected Outcomes**
- Generated `BuildForm()` code for a protected field includes `ReadOnly = True`
  and `TabStop = False`.
- No `TextChanging` / `TextChanged` / `KeyDown` handlers emitted for it.
- No `Leave` validation handler emitted for it.
- `PopulateFields` still reads the field's value from the raw record.
- `SaveRecord` still writes the field's value to the output buffer.

**Todo List**
1. In the `WriteMainForm` field-emission loop, after `sb.AppendLine($"Me.Add({vn})")`,
   if `sfld.IsProtected` emit:
   ```
   {vn}.ReadOnly = True
   {vn}.TabStop  = False
   ```
2. Wrap the `TextChanging` / `TextChanged` / `KeyDown` / `Leave` handler emission
   in `If Not sfld.IsProtected Then ... End If`.

**Relevant Context**
- `src\DataEntry.Core\CodeGenerator.vb` — field emit loop, lines 469–665

**Status** — `[ ] pending`

---

### Sub-Task 6 — Tests

**Intent**
Cover the new feature at the parser, validator, and code-gen layers.

**Expected Outcomes**
- Parser test: `PROTECTED` sets `IsProtected = True`; absent leaves it `False`.
- Validator test: Assign target on screen without PROTECTED → warning.
- Validator test: Assign target on screen with PROTECTED → no warning.
- Code-gen test: generated `BuildForm` contains `ReadOnly = True` and
  `TabStop = False` for a protected field.
- Code-gen test: no `TextChanging` handler emitted for a protected field.

**Todo List**
1. Add `ProtectedFieldTests` class to `DslTests.vb`.
2. Parser test — `PROTECTED` flag parsed correctly.
3. Validator test — Assign target without PROTECTED emits warning.
4. Validator test — Assign target with PROTECTED emits no warning.
5. Code-gen test — `ReadOnly = True` and `TabStop = False` present.
6. Code-gen test — no `TextChanging` handler for protected field.

**Relevant Context**
- `src\DataEntry.Tests\DslTests.vb` — existing test class patterns

**Status** — `[ ] pending`

---

## Implementation Order

```
Sub-Task 1 (AST)
    → Sub-Task 2 (Lexer)
        → Sub-Task 3 (Parser)
            → Sub-Task 4 (Validator)
                → Sub-Task 5 (CodeGen)
                    → Sub-Task 6 (Tests)
```

Small, sequential — each step is reviewable before the next begins.
