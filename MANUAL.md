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

## Package Contents

When you unzip (Windows) or untar (Linux/macOS) a DataEntry release you get:

```
DataEntry.exe       (Windows) or dataentry (Linux/macOS)
libonigwrap.dll     (Windows) or libonigwrap.so / .dylib  — must stay beside the exe
INSTALL.md          installation and SDK setup guide
MANUAL.md           language reference and user guide
Samples\
    sample.def      simple customer-entry starter form
    customer.def    full customer master with address and contact sections
    inventory.def   inventory item entry with validation stubs
    timesheet.def   multi-screen weekly timesheet (two screens, date/time masks)
    errors.def      intentionally invalid form — demonstrates error reporting
```

`libonigwrap` is a native dependency of Terminal.Gui and must remain in the same
directory as the `DataEntry` executable.

---

## Running the Compiler

```
dataentry                              # opens a file-browse dialog
dataentry myform.def                   # preview the form (or show errors)
dataentry myform.def --build           # generate + build without UI
dataentry myform.def --build --output ./out    # specify output directory
```

| Flag | Meaning |
|------|---------|
| *(no flags)* | Load the file, preview valid forms or show errors |
| `--build` | Generate a VB.NET project, compile it, and publish a self-contained executable |
| `--output <dir>` | Write generated files to `<dir>` (default: a subfolder named after the `.def` file, beside it) |

### Interactive preview mode

When the form has **no errors** the compiler renders it live so you can tab through
fields, check positions, colours, and format hints before building.
No data is written to disk during preview.

| Key | Action |
|-----|--------|
| **Tab / Enter** | Move to next field |
| **Shift-Tab** | Move to previous field |
| **Ctrl+S** | Simulate a save (preview message only — no file written) |
| **F1** | Help dialog |
| **F3** | Clear fields |
| **Page Up / Page Down** | Switch screens in a multi-screen form |
| **Shift-PgUp / Shift-PgDn** | Previous / next record (preview message only) |
| **Shift-Home / Shift-End** | First / last record (preview message only) |
| **F10** | Compile & Build the form |
| **File menu** | Open a different `.def` file, Compile & Build, Quit |

When the form has **errors** the compiler shows a scrollable list of every parse
and validation problem with line numbers.  Press **Q** or **Esc** to exit.

### Build output

`--build` generates a complete VB.NET project, compiles it with `dotnet publish`,
and produces a self-contained single-file executable:

```
<output-dir>\
    <name>.vbproj
    Program.vb
    MainForm.vb
    DataFile.vb
    FormatHelper.vb
    ColorHelper.vb
    ValidationFunctions.vb   (only when VALIDATE WITH is used)
    publish\
        <name>.exe            (Windows) or <name> (Linux/macOS)
        libonigwrap.dll/.so   native dependency — must stay beside the exe
```

Run the generated application directly from the `publish\` subfolder:

```
publish\sample.exe          (Windows)
./publish/sample            (Linux/macOS)
```

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
| `FILE <path>` | Yes | Path to the output data file (relative paths are relative to the generated app's working directory) |
| `APPEND` | No (default) | Add records to the end of an existing file; create if absent |
| `NOAPPEND` | No | Delete the existing file at startup and start fresh |
| `LRECL=<n>` | Yes | Logical record length in bytes — all records are exactly this wide |
| `LEND=<mode>` | No (default `CRLF`) | Line-ending written after each record |

`LEND` modes:

| Value | Written after each record |
|-------|--------------------------|
| `CRLF` | Carriage-return + line-feed (`\r\n`) — standard Windows |
| `LF` | Line-feed only (`\n`) — standard Unix/Linux |
| `CR` | Carriage-return only (`\r`) |
| `NONE` | Nothing — records are written end-to-end |

---

## RECORD Definition

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
| `X` | Any character, stored as-is |
| `U` | Letter, forced to upper-case on store |
| `L` | Letter, forced to lower-case on store |
| `9` | Digit (0–9); space if blank |
| `Z` | Digit (0–9); zero-filled if blank |
| `\c` | Literal character `c` inserted automatically on store (e.g. `\-` inserts a hyphen) |

**Alignment when storing:**
- Fields whose mask contains only `9` or `Z` placeholders (plus any `\c` literals)
  are **right-adjusted** (padded on the left with spaces).
- All other fields are **left-adjusted** (padded on the right with spaces).

**Format hints:**  
When a mask contains embedded literals the compiler automatically places a muted
grey hint label to the right of the field showing the expected input pattern:

| Mask | Hint shown | Notes |
|------|-----------|-------|
| `999\-999\-9999` | `###-###-####` | Type 10 digits; hyphens inserted on save |
| `99\/99\/9999` | `##/##/####` | Type 8 digits; slashes inserted on save |
| `ZZ.99` | `##.##` | Type 4 digits; dot inserted on save |
| `999999` | *(none)* | Pure digits — self-explanatory |

In the hint, `#` represents a digit position, `@` represents any character,
and `^` represents a letter.

**Mask examples:**

| Mask | LEN | Input typed | Stored value |
|------|-----|-------------|--------------|
| `XXX` | 3 | `Hi` | `Hi ` |
| `999` | 3 | `42` | ` 42` |
| `ZZZ` | 3 | `42` | `042` |
| `UU` | 2 | `tx` | `TX` |
| `999\-9999` | 8 | `5551234` | `555-1234` |
| `99\/99\/9999` | 10 | `12252025` | `12/25/2025` |

---

## SCREEN-SECTION

A form may have **one or more screens**.  In a multi-screen form the user presses
**Page Down** to advance and **Page Up** to go back.

```
SCREEN-SECTION

SCREEN <name>  [COLOR=<fg>On<bg>]  [FG=<color>]  [BG=<color>]
    PROMPT "<text>"  ROW=<n>  COL=<n>  [COLOR=<color>]
    FIELD  ROW=<n>  COL=<n>  LEN=<n>  INTO <record>.<field>
           [VALIDATE WITH <function>]
        [NORMAL=<color>  FOCUS=<color>  ERROR=<color>  FULL=<ADVANCE|STAY>]
    ...

SCREEN <name2>  ...
```

### SCREEN line

| Keyword | Meaning |
|---------|---------|
| `<name>` | Identifier for this screen |
| `COLOR=<fg>On<bg>` | Default foreground and background for the entire screen |
| `FG=<color>` | Default foreground colour only |
| `BG=<color>` | Default background colour only |

### PROMPT / LABEL

Places a static text label on the screen.  Used for titles, field labels,
box-drawing borders, and hints.

```
PROMPT "<text>"  ROW=<n>  COL=<n>
    COLOR=<color>
```

- `LABEL` is an alias for `PROMPT`.
- The `COLOR=` line is optional; if omitted the screen default colour is used.
- Box-drawing characters (`╔ ═ ╗ ║ ╚ ╝ ┌ ─ ┐ │ └ ┘`) work directly in
  quoted strings for panel borders and title banners.

### FIELD line

| Keyword | Required | Meaning |
|---------|----------|---------|
| `ROW=<n>` | Yes | Screen row (1-based) |
| `COL=<n>` | Yes | Screen column (1-based) |
| `LEN=<n>` | Yes | Width of the input box in characters — must match the FORMAT mask length |
| `INTO <rec>.<fld>` | Yes | Which record field receives this value on save |
| `VALIDATE WITH <fn>` | No | Validation function called on save |

An optional inline label can precede the position keywords:

```
FIELD "Phone" ROW=8 COL=2 LEN=12 INTO CUST.CPHONE
```

### Field colour states and behaviour

On the line(s) following a `FIELD`, specify colour states and full-field behaviour:

```
    NORMAL=<color>  FOCUS=<color>  ERROR=<color>  FULL=<ADVANCE|STAY>
```

| Attribute | When it applies |
|-----------|----------------|
| `NORMAL=<color>` | Field is visible but not focused |
| `FOCUS=<color>` | Field is currently active |
| `ERROR=<color>` | Validation returned `False` |
| `FULL=ADVANCE` | When the field fills, automatically move to the next field (or save if last). **This is the default.** |
| `FULL=STAY` | When the field fills, hold the cursor and wait for Tab or Enter. Useful for fixed codes where the operator should review before advancing. |

All colour attributes accept either a bare colour name (`White`) or the
`<fg>On<bg>` shorthand (`WhiteOnBlue`).

---

## Colour Names

The 16 standard console colours:

`Black`, `DarkRed` (or `Red`), `DarkGreen` (or `Green`), `DarkYellow` (or `Yellow`),
`DarkBlue` (or `Blue`), `DarkMagenta` (or `Magenta`), `DarkCyan` (or `Cyan`),
`Gray`, `DarkGray`,
`BrightRed`, `BrightGreen`, `BrightYellow`, `BrightBlue`,
`BrightMagenta`, `BrightCyan`, `White`

**Built-in defaults (when not specified in the DSL):**

| Element | Default |
|---------|---------|
| Screen background | `Gray` on `DarkBlue` |
| Field normal | Inherits screen default |
| Field focus | `Black` on `Cyan` |
| Field error | `White` on `DarkRed` |

---

## VALIDATE WITH

When a field has `VALIDATE WITH <function>`, that function is called every time
the user saves a record.  It receives the field value as a string and must return:

| Return value | Meaning |
|--------------|---------|
| `True` | Accept the value |
| `False` | Reject; show the field in ERROR colour and keep focus there |
| A string | Replace the field value with this string, then accept |

The compiler generates stub functions in `ValidationFunctions.vb` inside the output
project.  Fill in the function bodies with your real validation logic.

---

## Runtime Hotkeys (Generated Application)

Every application produced by the compiler has these built-in keys.

| Key | Action |
|-----|--------|
| **Tab / Enter** | Move to next field |
| **Shift-Tab** | Move to previous field |
| **Home** | Go to start of current field |
| **End** | Go to end of current field |
| **Insert** | Toggle insert / overwrite mode |
| **Backspace** | Delete character to the left |
| **Delete** | Delete character under cursor |
| **Last field full / Enter** | Automatically saves the record and clears fields for next entry |
| **Ctrl+S** | Save record manually and clear fields |
| **F3** | Clear all fields (cancel current entry) |
| **F10** | Quit the application |
| **Page Up / Page Down** | Switch between screens (multi-screen forms) |
| **Shift-PgUp** | Load previous record for editing |
| **Shift-PgDn** | Load next record for editing |
| **Shift-Home** | Load the first record |
| **Shift-End** | Load the last record |

---

## Worked Example

### 1 — Write the definition file (`cust.def`)

```
DATA-SECTION
    FILE customers.dat APPEND LRECL=87 LEND=CRLF

RECORD CUST
    CNAME   START=1  LEN=30
    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.
    CADDR   LEN=30
    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX.
    CSTATE  LEN=2
    FORMAT=UU.
    CZIP    LEN=5
    FORMAT=99999.
    CPHONE  LEN=12
    FORMAT=999\-999\-9999.
    CEMAIL  LEN=8
    FORMAT=XXXXXXXX.

SCREEN-SECTION
SCREEN CUST-ENTRY COLOR=WhiteOnBlue
    PROMPT "╔══════════════════════════════════╗" ROW=1 COL=5
        COLOR=WhiteOnBlue
    PROMPT "║       CUSTOMER ENTRY             ║" ROW=2 COL=5
        COLOR=BrightYellowOnBlue
    PROMPT "╚══════════════════════════════════╝" ROW=3 COL=5
        COLOR=WhiteOnBlue
    PROMPT "Name   :" ROW=5 COL=5
    FIELD ROW=5 COL=14 LEN=30 INTO CUST.CNAME
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
    PROMPT "Address:" ROW=6 COL=5
    FIELD ROW=6 COL=14 LEN=30 INTO CUST.CADDR
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
    PROMPT "State  :" ROW=7 COL=5
    FIELD ROW=7 COL=14 LEN=2 INTO CUST.CSTATE
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed FULL=STAY
    PROMPT "Zip    :" ROW=7 COL=19
    FIELD ROW=7 COL=28 LEN=5 INTO CUST.CZIP
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
    PROMPT "Phone  :" ROW=8 COL=5
    FIELD ROW=8 COL=14 LEN=12 INTO CUST.CPHONE
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
    PROMPT "Email  :" ROW=9 COL=5
    FIELD ROW=9 COL=14 LEN=8 INTO CUST.CEMAIL
        NORMAL=WhiteOnBlue FOCUS=BlackOnCyan ERROR=WhiteOnRed
```

Notes on this form:
- `CPHONE` has a `FORMAT=999\-999\-9999.` mask.  The compiler automatically
  shows a `###-###-####` hint to the right of the field.  Type 10 digits;
  the hyphens are inserted automatically when the record is saved.
- `CSTATE` uses `FULL=STAY` so the cursor waits after the 2nd character.
- `LRECL=87` = 30+30+2+5+12+8 = 87 bytes per record.

### 2 — Preview the form

```
dataentry cust.def
```

The compiler opens a Terminal.Gui window.  Tab through the fields to verify
placement.  Notice the grey `###-###-####` hint beside the phone field.
Press **Esc** (File → Quit) when done.

### 3 — Compile and build

```
dataentry cust.def --build
```

The compiler generates the VB.NET project in `cust\` beside `cust.def`,
compiles it, and publishes a self-contained executable:

```
cust\publish\cust.exe        (Windows)
cust\publish\cust            (Linux/macOS)
cust\publish\libonigwrap.dll (native dependency — keep beside the exe)
```

### 4 — Run the generated application

```
cust\publish\cust.exe        (Windows)
./cust/publish/cust          (Linux/macOS)
```

Enter customer records.  The phone field shows `###-###-####` — type
10 digits and the saved record will contain `315-617-6379`.  Completing
the last field (or pressing **Ctrl+S**) saves each record and clears the
form for the next entry.  Press **F10** to quit.

Records are written to `customers.dat` as 87-byte fixed-length lines.

---

## Error Messages

| Message | Cause | Fix |
|---------|-------|-----|
| `DATA-SECTION is missing a FILE path` | No `FILE` keyword found | Add `FILE <path> LRECL=<n>` after `DATA-SECTION` |
| `LRECL must be greater than zero` | `LRECL=0` or omitted | Specify `LRECL=<n>` with a positive integer |
| `Duplicate record name '<name>'` | Two `RECORD` blocks share a name | Rename one |
| `Duplicate field name '<name>'` | Two fields in the same record share a name | Rename one |
| `Field '<f>' exceeds LRECL` | `START + LEN` exceeds the record length | Reduce `LEN`, adjust `START`, or increase `LRECL` |
| `FORMAT mask is longer than LEN` | Mask has more positions than `LEN` — data would be truncated | Increase `LEN` to match the mask length (count each `\c` pair as one position) |
| `FORMAT mask is shorter than LEN` *(warning)* | Mask has fewer positions than `LEN` | Acceptable for partial-fill fields; increase mask length to fill completely |
| `Unknown record '<rec>'` | Screen field `INTO` references an undefined record | Check record name spelling |
| `Unknown field '<rec>.<fld>'` | Screen field `INTO` references an undefined field | Check field name spelling |
| `FILE path contains '..' or is absolute` *(warning)* | Path may write outside the working directory | Verify the path is intentional |
