# DataEntry DSL — User Manual

## Introduction

The **DataEntry** tool lets you define text-oriented data-entry screens in a simple
language (a `.def` file) and then either:

- **Preview** the form interactively to verify its layout and colours, or
- **Compile & Build** it into a standalone executable that writes fixed-length
  records to a data file.

The tool itself runs on Windows, Linux, and macOS in console mode.
The applications it generates also run on all three platforms.

---

## Running the Compiler

```
dataentry                              # opens a file-browse dialog
dataentry myform.def                   # preview the form (or show errors)
dataentry myform.def --build           # generate + compile without UI
dataentry myform.def --build --output ./out    # specify output directory
```

| Flag | Meaning |
|------|---------|
| *(no flags)* | Load the file, preview valid forms or show errors |
| `--build` | Generate a VB.NET project and run `dotnet build` |
| `--output <dir>` | Write generated files to `<dir>` (default: a subfolder named after the `.def` file) |

### Interactive mode

When the form has **no errors** the compiler renders it live so you can check
field positions, colours, and tab order.  No data is written to disk.

| Key | Action |
|-----|--------|
| **Tab / Shift-Tab** | Move between fields |
| **F3** | Cancel (preview message) |
| **Right Ctrl** / **Ctrl+S** | Save record (preview message) |
| **Page Up / Page Down** | Switch between screens in a multi-screen form |
| **Shift-PgUp / Shift-PgDn** | Previous / next record (preview message) |
| **Shift-Home / Shift-End** | First / last record (preview message) |
| **F10** | Compile & Build the form |
| **F → File menu** | Open, Compile & Build, Quit |

When the form has **errors** the compiler shows a scrollable list of every
parse and validation problem with line numbers.  Press **Q** or **ESC** to exit.

---

## Language Reference

A `.def` file has two required sections, in this order:

1. `DATA-SECTION` — describes the output file and its record layout.
2. `SCREEN-SECTION` — describes the data-entry screens.

Lines beginning with `*` or `//` are comments.

---

## DATA-SECTION

```
DATA-SECTION
    FILE <path> [APPEND|NOAPPEND] LRECL=<n> LEND=<mode>
```

### FILE keyword

| Keyword | Required | Meaning |
|---------|----------|---------|
| `FILE <path>` | Yes | Full or relative path to the output data file |
| `APPEND` | No (default) | Add records to the end of an existing file; create if absent |
| `NOAPPEND` | No | Delete the existing file and start fresh |
| `LRECL=<n>` | Yes | Logical record length in bytes (all records are this width) |
| `LEND=<mode>` | No (default CRLF) | Line-ending written after each record |

`LEND` modes:

| Value | Written after each record |
|-------|--------------------------|
| `CRLF` | Carriage-return + line-feed (`\r\n`) |
| `LF` | Line-feed only (`\n`) |
| `CR` | Carriage-return only (`\r`) |
| `NONE` | Nothing — records are written end-to-end |

---

## RECORD Definition

Immediately after the `FILE` line, define one or more records:

```
RECORD <name>
    <fieldname>  [START=<n>]  LEN=<n>
    FORMAT=<mask>.
    <fieldname>  LEN=<n>
    FORMAT=<mask>.
    ...
```

- `<name>` — identifier used in `INTO` clauses on the screen.
- `START=<n>` — byte position of the first character (1-based).  If omitted,
  the field starts immediately after the previous field.
- `LEN=<n>` — number of bytes this field occupies in the record.
- `FORMAT=<mask>.` — input mask (see below).  The mask **must end with a dot**.

### FORMAT Mask Characters

| Character | Meaning |
|-----------|---------|
| `X` | Any alphanumeric character |
| `U` | Letter, forced to upper-case on store |
| `L` | Letter, forced to lower-case on store |
| `9` | Digit (0–9); space-padded if blank |
| `Z` | Digit (0–9); zero-filled if blank |
| `\c` | Literal character `c` stored as-is (e.g. `\-` inserts a hyphen) |
| `\\` | A literal backslash |

**Alignment when storing:**
- Fields whose mask contains only `9` or `Z` are **right-adjusted** (padded on
  the left).
- All other fields are **left-adjusted** (padded on the right with spaces).

**Examples:**

| Mask | LEN | Input | Stored value |
|------|-----|-------|--------------|
| `XXX` | 3 | `Hi` | `Hi ` |
| `999` | 3 | `42` | ` 42` |
| `ZZZ` | 3 | `42` | `042` |
| `UU` | 2 | `tx` | `TX` |
| `999\-9999` | 8 | `5551234` | `555-1234` |

---

## SCREEN-SECTION

```
SCREEN-SECTION
SCREEN <name>  [COLOR=<fg>On<bg>]  [FG=<color>]  [BG=<color>]
    FIELD "<label>"  ROW=<n>  COL=<n>  LEN=<n>  INTO <record>.<field>
                     [VALIDATE WITH <function>]
        [NORMAL=<color>  FOCUS=<color>  ERROR=<color>]
    ...
```

### SCREEN line

| Keyword | Meaning |
|---------|---------|
| `<name>` | Identifier for this screen |
| `COLOR=<fg>On<bg>` | Sets the default foreground and background for the entire screen |
| `FG=<color>` | Default foreground colour only |
| `BG=<color>` | Default background colour only |

### FIELD line

| Keyword | Required | Meaning |
|---------|----------|---------|
| `"<label>"` | Yes | Text shown to the left of the input box |
| `ROW=<n>` | Yes | Screen row (1-based) |
| `COL=<n>` | Yes | Screen column (1-based) |
| `LEN=<n>` | Yes | Width of the input box in characters |
| `INTO <rec>.<fld>` | Yes | Which record field receives this value on save |
| `VALIDATE WITH <fn>` | No | Validation function called on save (see below) |

### Field colour states

On the line(s) following a `FIELD`, you may specify up to three colour states:

```
    NORMAL=<color>  FOCUS=<color>  ERROR=<color>
```

| State | When it applies |
|-------|----------------|
| `NORMAL` | Field is visible but not focused |
| `FOCUS` | Field is currently active (cursor is inside) |
| `ERROR` | Validation function returned `False` |

All three accept either a bare colour name (`White`) or the shorthand
`<fg>On<bg>` (`WhiteOnBlue`).  Omitted states inherit the screen default or
fall back to built-in defaults (see *Colour Names* below).

---

## Colour Names

The 16 standard console colours are supported:

`Black`, `DarkRed` (or `Red`), `DarkGreen` (or `Green`), `DarkYellow` (or `Yellow`),
`DarkBlue` (or `Blue`), `DarkMagenta` (or `Magenta`), `DarkCyan` (or `Cyan`),
`Gray`, `DarkGray`,
`BrightRed`, `BrightGreen`, `BrightYellow`, `BrightBlue`,
`BrightMagenta`, `BrightCyan`, `White`

**Built-in defaults (when not specified in the DSL):**

| Element | Default |
|---------|---------|
| Screen background | `Gray` on `DarkBlue` |
| Field normal | Inherits screen |
| Field focus | `Black` on `Cyan` |
| Field error | `White` on `DarkRed` |

---

## VALIDATE WITH

When a field has `VALIDATE WITH <function>`, that function is called every time
the user attempts to save the record.  The function receives the field value as
a string and must return one of:

| Return value | Meaning |
|--------------|---------|
| `True` | Accept the value as entered |
| `False` | Reject the value; show the field in ERROR colour and keep focus there |
| A string | Replace the field value with this string, then accept |

Validation functions are generated as stubs in `ValidationFunctions.vb` inside
the output project.  Fill in the function bodies with your real logic.

---

## Runtime Hotkeys (Generated Application)

Every application produced by the compiler has these built-in keys.
They cannot be overridden in the DSL.

| Key | Action |
|-----|--------|
| **Tab** | Move to next field |
| **Shift-Tab** | Move to previous field |
| **Home** | Go to start of current field |
| **End** | Go to end of current field |
| **Insert** | Toggle insert / overwrite mode |
| **Backspace** | Delete character to the left |
| **Delete** | Delete character under cursor |
| **F3** | Clear all fields (cancel current entry) |
| **Right Ctrl** / **Ctrl+S** | Save the record and clear fields for next entry |
| **F10** | Quit the application |
| **Page Up / Page Down** | Navigate between screens (multi-screen forms) |
| **Shift-PgUp** | Load previous record for editing |
| **Shift-PgDn** | Load next record for editing |
| **Shift-Home** | Load the first record |
| **Shift-End** | Load the last record |

---

## Worked Example

### 1 — Write the definition file (`cust.def`)

```
DATA-SECTION
    FILE customers.dat APPEND LRECL=80 LEND=CRLF

RECORD CUST
    CNAME   START=1  LEN=30
    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.
    CADDR1  LEN=30
    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.
    CCITY   LEN=20
    FORMAT=XXXXXXXXXXXXXXXXXXXX.
    CSTATE  LEN=2
    FORMAT=UU.
    CZIP    LEN=5
    FORMAT=99999.

SCREEN-SECTION
SCREEN CUST-ENTRY COLOR=WhiteOnBlue
    FIELD "Name"    ROW=2  COL=2  LEN=30  INTO CUST.CNAME
    FIELD "Address" ROW=4  COL=2  LEN=30  INTO CUST.CADDR1
    FIELD "City"    ROW=6  COL=2  LEN=20  INTO CUST.CCITY
    FIELD "State"   ROW=6  COL=28 LEN=2   INTO CUST.CSTATE
    FIELD "Zip"     ROW=6  COL=36 LEN=5   INTO CUST.CZIP
```

### 2 — Preview the form

```
dataentry cust.def
```

The compiler opens a Terminal.Gui window showing the form.  Tab through the
fields to verify placement.  Press **ESC** (via the File menu → Quit) when done.

### 3 — Compile and build

```
dataentry cust.def --build --output ./cust-app
```

The compiler generates the VB.NET project, runs `dotnet build`, and reports
success or any build errors.

### 4 — Run the generated application

```
./cust-app/bin/Debug/net10.0/cust-app
```

Enter customer records.  Press **Right Ctrl** to save each one.  Press **F10** to quit.
The records are written to `customers.dat` as 80-byte fixed-length lines.

---

## Error Messages

| Message | Cause | Fix |
|---------|-------|-----|
| `DATA-SECTION is missing a FILE path` | No `FILE` keyword found | Add `FILE <path> LRECL=<n>` after `DATA-SECTION` |
| `LRECL must be greater than zero` | `LRECL=0` or `LRECL` omitted | Specify `LRECL=<n>` with a positive integer |
| `Duplicate record name '<name>'` | Two `RECORD` blocks share a name | Rename one of the records |
| `Duplicate field name '<name>'` | Two fields in the same record share a name | Rename one of the fields |
| `Field '<f>' exceeds LRECL` | START + LEN goes beyond the record length | Reduce LEN, adjust START, or increase LRECL |
| `FORMAT mask length does not match LEN` | The mask has a different number of positions than LEN | Count mask characters (excluding `\` escapes) and match LEN |
| `Unknown record '<rec>'` in INTO | Screen field references a record not defined in DATA-SECTION | Check the record name spelling |
| `Unknown field '<rec>.<fld>'` in INTO | Screen field references a field not defined in the record | Check the field name spelling |
| `Expected FILE keyword` | First line of DATA-SECTION is not `FILE` | Start the DATA-SECTION body with `FILE <path> ...` |
| `Expected integer for LRECL value` | Non-numeric value after `LRECL=` | Use a plain integer (e.g. `LRECL=80`) |
