# Plan: Inline Validation Rules in the .def File (VALIDATE-SECTION)

## Overview

Add a `VALIDATE-SECTION` to the `.def` language that lets authors write field-validation
rules directly in the definition file using a simple COBOL-like syntax, instead of
hand-writing VB stubs.  The compiler translates those rules into real VB.NET code in the
generated `ValidationFunctions.vb`.

Existing `VALIDATE WITH <name>` references that have **no matching block** in the
`VALIDATE-SECTION` continue to produce VB stubs as today — fully backwards compatible.

Validation fires **when the user leaves a field** (focus-leave), not at save time.
The record block is unchanged — it remains purely structural.

---

## Language Design (settled)

### VALIDATE-SECTION location
Top-level section, alongside `DATA-SECTION` and `SCREEN-SECTION`.

### Named validate block
```
VALIDATE-SECTION

VALIDATE CHECKHOURS
    NOT EMPTY                        MESSAGE "Hours cannot be blank"
    VALUE IS BETWEEN 0 AND 80        MESSAGE "Hours must be 0–80"

VALIDATE CHECKGROSS
    GROSS IS HOURS * RATE
```

### Rule statements (first version)

| Statement | Meaning | Returns |
|-----------|---------|---------|
| `NOT EMPTY` | Field must not be blank | FALSE + error colour if blank |
| `VALUE IS BETWEEN n AND m` | Numeric range check | FALSE + error colour if out of range |
| `<target> IS <expr>` | Arithmetic assignment — compute and replace target field | Replacement string |

### Arithmetic expressions
Flat only (no parentheses): field names and numeric literals joined by `+ - * /`.
`VALUE` refers to the current field. Other names resolve to field names from the
RECORD block (short name if unambiguous, `RECORD.FIELD` if needed).

### Error display
`MESSAGE "text"` is optional on any rule. When present the text is shown in a
status label at the bottom of the screen. When absent a generic message is shown.

### FIELD reference
```
FIELD ROW=8 COL=20 LEN=5 INTO TIMESHEET.HOURS VALIDATE WITH CHECKHOURS
```
`VALIDATE WITH <name>` — unchanged syntax. Now resolved against the VALIDATE-SECTION
first; falls back to stub generation if no block is found.

---

## Sub-Tasks

---

### Sub-Task 1 — AST: add ValidateBlock and ValidateRule nodes

**Intent**
Define the data structures that will hold parsed validation blocks and their rules.
Everything else depends on these shapes being stable.

**Expected Outcomes**
- `ValidateRule` class: holds rule kind (NotEmpty / Between / Assign), operands,
  optional message string, source line.
- `ValidateBlock` class: holds name, list of rules, source line.
- `DslDocument` gains a `ValidateBlocks As New List(Of ValidateBlock)`.
- No behaviour change — just new inert data structures.

**Todo List**
1. Add `RuleKind` enum to `Ast.vb`: `NotEmpty`, `Between`, `Assign`.
2. Add `ValidateRule` class: `Kind`, `Message`, `LowBound`/`HighBound` (for Between),
   `TargetField`, `Expression` (for Assign), `Line`.
3. Add `ValidateBlock` class: `Name`, `Rules As New List(Of ValidateRule)`, `Line`.
4. Add `ValidateBlocks As New List(Of ValidateBlock)` to `DslDocument`.

**Relevant Context**
- `src\DataEntry.Core\Ast.vb` — all existing AST nodes

**Status** — `[ ] pending`

---

### Sub-Task 2 — Lexer: add VALIDATE-SECTION keywords

**Intent**
The lexer must recognise the new keywords so the parser sees `Keyword` tokens rather
than `Identifier` tokens for them.

**Expected Outcomes**
- The keywords `VALIDATE-SECTION`, `VALIDATE`, `NOT`, `EMPTY`, `VALUE`, `IS`,
  `BETWEEN`, `AND`, `MESSAGE` are added to the keyword set.
- All existing tokens unaffected.
- No new tests needed beyond confirming the parser sub-tasks work.

**Todo List**
1. Add the eight new keywords to the `Keywords` HashSet in `DslLexer.vb`.

**Relevant Context**
- `src\DataEntry.Core\DslLexer.vb` lines 31–39 — keyword set

**Status** — `[ ] pending`

---

### Sub-Task 3 — Parser: parse VALIDATE-SECTION and validate blocks

**Intent**
Extend `DslParser.Parse()` to recognise `VALIDATE-SECTION` at the top level and parse
each named `VALIDATE <name>` block and its rule statements into the new AST nodes.

**Expected Outcomes**
- `Parse()` dispatches to `ParseValidateSection()` when it sees `VALIDATE-SECTION`.
- Each `VALIDATE <name>` block is parsed into a `ValidateBlock`.
- Each rule line is parsed into a `ValidateRule` of the correct kind.
- Unrecognised rule syntax emits a parse error and skips to next line (resilient).
- Parse errors do not throw — consistent with existing error-accumulation pattern.

**Todo List**
1. Add `ParseValidateSection()` method: loops consuming `VALIDATE <name>` blocks until
   the next top-level keyword or EOF.
2. Add `ParseValidateBlock()` method: reads rule lines until the next `VALIDATE` or
   top-level keyword.
3. Add `ParseValidateRule()` method: pattern-matches the three rule kinds:
   - `NOT EMPTY [MESSAGE "..."]`
   - `VALUE IS BETWEEN <n> AND <m> [MESSAGE "..."]`
   - `<ident> IS <expr> [MESSAGE "..."]`  where `<expr>` is a flat arithmetic sequence
4. Hook `ParseValidateSection()` into `Parse()` dispatch alongside `DATA-SECTION` and
   `SCREEN-SECTION`.

**Relevant Context**
- `src\DataEntry.Core\DslParser.vb` lines 26–60 — `Parse()` dispatcher
- Sub-Tasks 1 and 2 must be complete.

**Status** — `[ ] pending`

---

### Sub-Task 4 — Validator: semantic checks on validate blocks

**Intent**
Check that validate blocks are semantically correct — names are unique, field references
exist in the record, numeric literals are valid, and each `VALIDATE WITH` reference on a
screen field resolves to either a block or a stub (warn not error for stubs).

**Expected Outcomes**
- Duplicate `VALIDATE` block names → hard error.
- Field name in an `Assign` expression that cannot be resolved → hard error.
- `VALIDATE WITH <name>` that matches a block → no warning (it's defined).
- `VALIDATE WITH <name>` that has no block → warning (stub will be generated) — same
  as today.
- All existing validator tests still pass.

**Todo List**
1. Add `CheckValidateSection()` method to `DslValidator`.
2. Check for duplicate block names (case-insensitive HashSet).
3. For each `Assign` rule, resolve field name references against all record fields;
   emit error if unresolved.
4. Update `CheckScreenSections()` to suppress the "ensure this function is defined"
   warning when the name matches a defined validate block.
5. Call `CheckValidateSection()` from `Validate()`.

**Relevant Context**
- `src\DataEntry.Core\DslValidator.vb` lines 26–31 — `Validate()` dispatcher
- `src\DataEntry.Core\DslValidator.vb` lines 206–211 — existing VALIDATE WITH warning

**Status** — `[ ] pending`

---

### Sub-Task 5 — Code Generator: emit real VB from validate blocks

**Intent**
`WriteValidationStubs` currently always emits a `Return True` stub body.
Extend it so that blocks defined in the `VALIDATE-SECTION` get real generated code,
while referenced names with no block still get the stub.

Each generated function must:
- Accept `value As String` and a field-values lookup (dictionary or individual params
  for cross-field access).
- Return `Object` — `True`, `False`, or a replacement string.
- Implement each rule in order; first failing rule returns `False` (or the replacement).

**Expected Outcomes**
- `NOT EMPTY` → `If String.IsNullOrWhiteSpace(value) Then Return False`
- `VALUE IS BETWEEN n AND m` → numeric parse + range check → `Return False` if out
- `<target> IS <expr>` → evaluate arithmetic expression → `Return "<result>"` string
- `MESSAGE` text is emitted as a comment above the check (displayed at runtime via
  a separate mechanism — see Sub-Task 6).
- Blocks with no rules defined get `Return True` stub.
- Existing stub-only behaviour unchanged for unreferenced names.

**Todo List**
1. In `WriteValidationStubs`, after collecting func names, also build a lookup of
   `ValidateBlock` by name (case-insensitive).
2. For each function name: if a matching block exists, call new `EmitBlockBody()`;
   otherwise emit the existing `Return True` stub.
3. Write `EmitBlockBody(sb, block, doc)`:
   - For `NotEmpty`: emit whitespace check.
   - For `Between`: emit `Dim n As Double`, `TryParse`, range check.
   - For `Assign`: emit arithmetic expression evaluation using `Double.Parse` of
     referenced field values, format result as string, `Return` it.
4. Cross-field `Assign` rules need access to other field values — emit the function
   signature as `(value As String, fields As Dictionary(Of String, String)) As Object`
   for blocks that contain `Assign` rules; keep `(value As String) As Object` for
   blocks that do not.

**Relevant Context**
- `src\DataEntry.Core\CodeGenerator.vb` lines 719–749 — `WriteValidationStubs`
- Sub-Tasks 1–4 must be complete.

**Status** — `[ ] pending`

---

### Sub-Task 6 — Code Generator: wire validation call into MainForm (focus-leave)

**Intent**
The generated `MainForm.vb` must call the validation function when the user leaves a
field (focus-leave / `Leave` event). This is the runtime behaviour glue.

On `False` → field stays in ERROR colour, focus returns to the field, optional message
shown in a status label at screen bottom.
On a string result → field value is replaced with the returned string, field shown in
NORMAL colour, focus advances.
On `True` → normal colour, focus advances.

**Expected Outcomes**
- Each field that has `VALIDATE WITH` gets a `Leave` event handler wired in the
  generated constructor.
- The handler calls the appropriate function (passing the dictionary for cross-field
  blocks).
- A `_statusLabel` is added to the generated form for displaying MESSAGE text.
- Existing fields without `VALIDATE WITH` are unaffected.
- Existing save / navigation tests still pass.

**Todo List**
1. In `WriteMainForm`, add a `_statusLabel As Label` field and initialise it at the
   bottom row of the screen.
2. For each screen field with `ValidateFunc` set, emit a `Leave` event handler that:
   a. Calls `ValidationFunctions.<name>(value [, fields])`.
   b. Casts result and branches on `True` / `False` / `String`.
   c. On `False`: sets ERROR colour scheme, sets focus back, sets status label text.
   d. On string: replaces field `.Text`, sets NORMAL colour scheme, clears status.
   e. On `True`: sets NORMAL colour scheme, clears status.
3. Build the `fields` dictionary from the current text values of all named screen
   fields (keyed by short field name, case-insensitive) for cross-field blocks.

**Relevant Context**
- `src\DataEntry.Core\CodeGenerator.vb` — `WriteMainForm` section
- Sub-Task 5 must be complete.

**Status** — `[ ] pending`

---

### Sub-Task 7 — Tests

**Intent**
Cover the new feature end-to-end with focused tests at each layer, consistent with the
existing test patterns in `DslTests.vb`.

**Expected Outcomes**
- Parser tests: `VALIDATE-SECTION` parses correctly; each rule kind produces the right
  AST node; unrecognised syntax produces a parse error.
- Validator tests: duplicate block name → error; unknown field reference → error;
  `VALIDATE WITH` matching a block → no warning.
- Code-gen tests: generated `ValidationFunctions.vb` contains real logic (not just
  `Return True`) when a block is defined; stub still generated for unreferenced names.
- At least one sample `.def` file (new `timesheet-validate.def` or extend
  `timesheet.def`) exercises `NOT EMPTY`, `BETWEEN`, and `Assign` rules.

**Todo List**
1. Add `ValidateSectionParseTests` class to `DslTests.vb`.
2. Add `ValidateSectionValidatorTests` class.
3. Add `ValidateSectionCodeGenTests` class.
4. Add or extend a sample `.def` file with a `VALIDATE-SECTION`.

**Relevant Context**
- `src\DataEntry.Tests\DslTests.vb` — existing test class patterns
- Sub-Tasks 1–6 must be complete.

**Status** — `[ ] pending`

---

## Implementation Order

```
Sub-Task 1 (AST)
    → Sub-Task 2 (Lexer)
        → Sub-Task 3 (Parser)
            → Sub-Task 4 (Validator)
                → Sub-Task 5 (CodeGen — function bodies)
                    → Sub-Task 6 (CodeGen — runtime wiring)
                        → Sub-Task 7 (Tests)
```

Each sub-task is a complete, reviewable unit before the next begins.
