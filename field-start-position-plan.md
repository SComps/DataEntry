# Plan: Honour Explicit START= on All Record Fields

## Overview

The `START=` option is currently recognised by the parser and validator for every field,
but the **code generator ignores it** when building the `SaveRecord` (write) path.
`PopulateFields` (read path) does honour `START=` but only as an override of a running
sequential `pos` counter — meaning that if a field with an explicit START leaves a gap,
the next implicit field still starts at the wrong position.

The goal is to ensure that:
1. `START=` is accepted (and optional) on every field in the RECORD block — **this already works**.
2. The **validator** resolves each field's effective start position correctly and detects
   overlapping fields.
3. The **code generator** emits correct, position-aware `Substring` (read) and
   `Mid`-style placement (write) calls that respect explicit START positions and the
   implicit "next available column" rule for fields that omit START=.

---

## Sub-Tasks

---

### Sub-Task 1 — Validator: detect overlapping fields and store resolved start

**Intent**  
The validator already resolves `startPos` per field and advances `pos`.  
However, it does not detect when an explicit `START=` would cause a field to *overlap*
a previously laid-out field (e.g. two fields both wanting position 1).  
It also does not persist the resolved start back onto the `FieldDef` so the code generator
can rely on it — the generator currently has to re-derive the position itself (and does so
incorrectly for the write path).

Storing the resolved start on `FieldDef` gives every downstream consumer a single source
of truth and eliminates the duplicated position-arithmetic scattered across
`DslValidator.vb` and `CodeGenerator.vb`.

**Expected Outcomes**
- `FieldDef` gains a new property `ResolvedStart As Integer` (0-based or 1-based, choose
  one consistently — keep 1-based to match `START=` values in the .def file; code gen
  converts to 0-based at emit time).
- `DslValidator.CheckRecord` populates `fld.ResolvedStart` for every field after
  position resolution.
- A new validation error is emitted when an explicit `START=` would place a field so
  that it overlaps the byte range of any already-resolved field.
- Existing tests still pass; new tests cover the overlap error case.

**Todo List**
1. Add `Public Property ResolvedStart As Integer = 0` to `FieldDef` in `Ast.vb`.
2. In `DslValidator.CheckRecord`, reject `START=0` with a hard error — column numbers
   are 1-based; column 0 does not exist.
3. After computing `startPos`, assign `fld.ResolvedStart = startPos`.
4. Add overlap detection: track occupied byte ranges and emit an error if the new
   field's range intersects any already-occupied range.
5. Add a unit test that verifies `START=0` produces a validation error.
6. Add a unit test that triggers the overlap error (two fields whose START/LEN ranges
   collide).

**Relevant Context**
- `src\DataEntry.Core\Ast.vb` — `FieldDef` class, line 61
- `src\DataEntry.Core\DslValidator.vb` — `CheckRecord`, lines 67–114
- `src\DataEntry.Tests\DslTests.vb` — existing error tests in `ErrorTests` class

**Status** — `[x] done`

---

### Sub-Task 2 — Code Generator: fix SaveRecord (write path) to use resolved positions

**Intent**  
`SaveRecord()` currently builds the record string by sequentially `Append`-ing each
field's formatted value.  This is correct only when all fields are contiguous and in
declaration order.  Once any field carries an explicit `START=` that leaves gaps
(dead space), the append approach writes fields at wrong byte offsets.

The fix is to pre-allocate a fixed-width buffer of `LRECL` spaces, then splice each
field's value into the buffer at `fld.ResolvedStart - 1` (0-based).  This naturally
handles gaps (they remain spaces) and ensures explicit START positions are honoured.

**Expected Outcomes**
- `SaveRecord()` emits code that initialises a `Char` array (or `StringBuilder` filled
  with spaces) of length `LRECL`, then uses `Array.Copy` / direct index assignment to
  place each field value at its resolved position.
- Fields without `START=` are placed at the implicit next-available position
  (their `ResolvedStart` already holds the correct value after Sub-Task 1).
- Round-trip integration test: write a record with a deliberate gap, read it back, and
  assert the gap bytes are spaces and the field bytes are correct.

**Todo List**
1. Replace the `rec As New System.Text.StringBuilder` + `rec.Append(...)` pattern in
   the generated `SaveRecord()` with a fixed-width char-buffer approach:
   ```
   Dim buf(Lrecl - 1) As Char
   Array.Fill(buf, " "c)
   ' per field:
   Dim fieldStr = FormatHelper.ApplyMask(...)
   fieldStr.CopyTo(0, buf, <ResolvedStart - 1>, fieldStr.Length)
   DataFile.SaveRecordAtIndex(_recordIndex, New String(buf))
   ```
2. The `<ResolvedStart - 1>` value is emitted as a compile-time constant from the
   code generator (it reads `fld.ResolvedStart - 1`).
3. Remove the now-redundant sequential `pos` variable from the write codegen block.

**Relevant Context**
- `src\DataEntry.Core\CodeGenerator.vb` — `SaveRecord` codegen, lines 572–609
- `src\DataEntry.Core\Ast.vb` — `FieldDef.ResolvedStart` (added in Sub-Task 1)

**Status** — `[x] done`

---

### Sub-Task 3 — Code Generator: fix PopulateFields (read path) to use resolved positions

**Intent**  
`PopulateFields()` partially handles explicit `START=` fields (line 688) but still
maintains a separate sequential `pos` counter.  When an explicit-START field is
encountered, `fStart` is overridden but `pos` keeps advancing from the old value,
meaning the *next* implicit field after an explicit-START field will start at the
wrong offset.

With `ResolvedStart` available on every `FieldDef`, the code generator no longer needs
its own position arithmetic — it just emits `fld.ResolvedStart - 1` as the Substring
offset for every field.

**Expected Outcomes**
- `PopulateFields()` emits `raw.Substring(<ResolvedStart - 1>, fLen)` for every field,
  using the constant resolved value baked in at code-gen time.
- The local `pos` variable and the inner record-lookup loop are removed from the
  `PopulateFields` codegen block (or simplified to just a lookup of `ResolvedStart`).
- Existing round-trip tests still pass.

**Todo List**
1. In the `PopulateFields` codegen loop, replace the `fStart` / `pos` logic with a
   direct lookup of `f.ResolvedStart - 1` (0-based offset) baked into the emitted
   `Substring` call.
2. Remove the now-dead `pos` variable and the `If f.Start > 0 Then fStart = f.Start - 1`
   branch.
3. Verify existing customer / inventory / timesheet tests still produce correct
   field positions.

**Relevant Context**
- `src\DataEntry.Core\CodeGenerator.vb` — `PopulateFields` codegen, lines 674–698
- Sub-Task 1 must be complete so `ResolvedStart` is populated.

**Status** — `[x] done`

---

### Sub-Task 4 — Tests: cover dead-space (gap) round-trip scenarios

**Intent**  
Add a focused test that exercises a RECORD definition that intentionally contains
dead space — a field with `START=` that skips bytes — to prove that both the
validator and the generated code handle the gap correctly end-to-end.

**Expected Outcomes**
- A new `DeadSpaceTests` class (or section) in `DslTests.vb` with at least:
  - A parse test confirming `ResolvedStart` is correct for all fields.
  - A validator test confirming no errors are raised for a valid gap layout.
  - A validator test confirming an overlap error is raised when two fields collide.
- Optional: a generator-level test that checks the emitted `SaveRecord` and
  `PopulateFields` source strings contain the expected offset literals.

**Relevant Context**
- `src\DataEntry.Tests\DslTests.vb` — existing test classes for reference patterns
- Sub-Tasks 1–3 must be complete.

**Status** — `[x] done`
