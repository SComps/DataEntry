' AST — plain data-holder classes.  No logic here.
Imports System.Collections.Generic


    ' ── Color ────────────────────────────────────────────────────────────────

    ''' <summary>A foreground/background color pair resolved from the DSL.</summary>
    Public Class ColorSpec
        Public Property Fg As String = "Gray"
        Public Property Bg As String = "DarkBlue"
    End Class

    ' ── Field full-behaviour ─────────────────────────────────────────────────

    ''' <summary>
    ''' Controls what happens the instant a field reaches its LEN capacity.
    ''' Advance (default) — automatically move focus to the next field (or save if last).
    ''' Stay             — lock the cursor at the end; inhibit further entry until Tab/Enter.
    ''' </summary>
    Public Enum FullBehavior
        Advance     ' FULL=ADVANCE  (default when FULL= is omitted)
        Stay        ' FULL=STAY
    End Enum

    ' ── Data Section ─────────────────────────────────────────────────────────

    Public Enum AppendMode
        Append
        NoAppend
    End Enum

    Public Enum LineEnding
        CRLF
        LF
        CR
        None
    End Enum

    ''' <summary>A single format mask token.</summary>
    Public Class MaskToken
        Public Enum TokenKind
            Alphanumeric    ' X
            UpperCase       ' U
            LowerCase       ' L
            Digit           ' 9
            ZeroFill        ' Z
            Literal         ' escaped or non-format char stored as-is
        End Enum

        Public Property Kind As TokenKind
        Public Property LiteralChar As Char   ' only used when Kind = Literal
    End Class

    ''' <summary>The FORMAT= specification for a record field.</summary>
    Public Class FormatSpec
        Public Property Raw As String = ""
        Public Property Tokens As New List(Of MaskToken)
    End Class

    ''' <summary>One field inside a RECORD definition.</summary>
    Public Class FieldDef
        Public Property Name As String = ""
        Public Property Start As Integer = -1   ' -1 = not specified (implicit)
        Public Property Len As Integer = 0
        Public Property Format As FormatSpec = New FormatSpec()
        Public Property Line As Integer         ' source line for error reporting
        ''' <summary>
        ''' Resolved 1-based column position, set by DslValidator.
        ''' Equals the explicit START= value when supplied, otherwise the
        ''' implicit next-available column derived from the preceding field.
        ''' Zero means the field has not been validated yet.
        ''' </summary>
        Public Property ResolvedStart As Integer = 0
    End Class

    ''' <summary>A RECORD block with its list of fields.</summary>
    Public Class RecordDef
        Public Property Name As String = ""
        Public Property Fields As New List(Of FieldDef)
        Public Property Line As Integer
    End Class

    ''' <summary>The DATA-SECTION block.</summary>
    Public Class DataSection
        Public Property FilePath As String = ""
        Public Property Mode As AppendMode = AppendMode.Append
        Public Property Lrecl As Integer = 80
        Public Property Ending As LineEnding = LineEnding.CRLF
        Public Property Records As New List(Of RecordDef)
        Public Property Line As Integer
    End Class

    ' ── Screen Section ───────────────────────────────────────────────────────

    ''' <summary>A standalone PROMPT or LABEL text element on a screen.</summary>
    Public Class ScreenPrompt
        Public Property Text As String = ""
        Public Property Row As Integer = 1
        Public Property Col As Integer = 1
        Public Property Color As ColorSpec = Nothing
        Public Property Line As Integer
    End Class

    ''' <summary>One FIELD entry inside a SCREEN definition.</summary>
    Public Class ScreenField
        Public Property Label As String = ""
        Public Property Row As Integer = 1
        Public Property Col As Integer = 1
        Public Property PromptRow As Integer = -1  ' -1 = implicit offset position
        Public Property PromptCol As Integer = -1  ' -1 = implicit offset position
        Public Property Len As Integer = 0
        Public Property IntoRecord As String = ""   ' record name from INTO RECORD.FIELD
        Public Property IntoField As String = ""    ' field name from INTO RECORD.FIELD
        Public Property ValidateFunc As String = "" ' function name from VALIDATE WITH <func>
        Public Property NormalColor As ColorSpec = Nothing  ' Nothing = inherit screen default
        Public Property FocusColor As ColorSpec = Nothing
        Public Property ErrorColor As ColorSpec = Nothing
        Public Property Full As FullBehavior = FullBehavior.Advance  ' FULL= attribute
        ''' <summary>
        ''' When True the field is display-only — rendered read-only with no tab-stop.
        ''' The user can see but not type into it (3270 protected field semantics).
        ''' </summary>
        Public Property IsProtected As Boolean = False
        Public Property Line As Integer
    End Class

    ''' <summary>A SCREEN block with its prompts and fields.</summary>
    Public Class ScreenSection
        Public Property Name As String = ""
        Public Property DefaultColor As ColorSpec = New ColorSpec()
        Public Property Prompts As New List(Of ScreenPrompt)
        Public Property Fields As New List(Of ScreenField)
        Public Property Line As Integer
    End Class

    ' ── Validate Section ─────────────────────────────────────────────────────

    ''' <summary>The kind of a single rule inside a VALIDATE block.</summary>
    Public Enum RuleKind
        NotEmpty    ' NOT EMPTY
        Between     ' VALUE IS BETWEEN n AND m
        Assign      ' <target> IS <expr>  (arithmetic assignment / calculation)
    End Enum

    ''' <summary>One token in a flat arithmetic expression (operand or operator).</summary>
    Public Class ExprToken
        Public Enum ExprTokenKind
            FieldName   ' a record field name or VALUE
            Number      ' a numeric literal
            Op          ' + - * /
        End Enum
        Public Property Kind As ExprTokenKind
        Public Property Value As String = ""   ' field name, literal text, or operator char
    End Class

    ''' <summary>One rule statement inside a VALIDATE block.</summary>
    Public Class ValidateRule
        Public Property Kind As RuleKind
        Public Property Message As String = ""          ' optional MESSAGE "text"

        ' For Between: low and high bound as strings (parsed to Double at runtime).
        Public Property LowBound As String = ""
        Public Property HighBound As String = ""

        ' For Assign: the target field name and the right-hand expression tokens.
        Public Property TargetField As String = ""
        Public Property Expression As New List(Of ExprToken)

        Public Property Line As Integer
    End Class

    ''' <summary>A named VALIDATE block in the VALIDATE-SECTION.</summary>
    Public Class ValidateBlock
        Public Property Name As String = ""
        Public Property Rules As New List(Of ValidateRule)
        Public Property Line As Integer
    End Class

    ' ── Top-level Document ───────────────────────────────────────────────────

    ''' <summary>The root of the parsed DSL document.</summary>
    Public Class DslDocument
        Public Property Data As DataSection = New DataSection()
        Public Property Screens As New List(Of ScreenSection)
        Public Property ValidateBlocks As New List(Of ValidateBlock)
    End Class

