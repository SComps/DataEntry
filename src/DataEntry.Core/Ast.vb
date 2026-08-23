' AST — plain data-holder classes.  No logic here.
Imports System.Collections.Generic


    ' ── Color ────────────────────────────────────────────────────────────────

    ''' <summary>A foreground/background color pair resolved from the DSL.</summary>
    Public Class ColorSpec
        Public Property Fg As String = "Gray"
        Public Property Bg As String = "DarkBlue"
    End Class

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

    ''' <summary>One FIELD entry inside a SCREEN definition.</summary>
    Public Class ScreenField
        Public Property Label As String = ""
        Public Property Row As Integer = 1
        Public Property Col As Integer = 1
        Public Property Len As Integer = 0
        Public Property IntoRecord As String = ""   ' record name from INTO RECORD.FIELD
        Public Property IntoField As String = ""    ' field name from INTO RECORD.FIELD
        Public Property ValidateFunc As String = "" ' function name from VALIDATE WITH <func>
        Public Property NormalColor As ColorSpec = Nothing  ' Nothing = inherit screen default
        Public Property FocusColor As ColorSpec = Nothing
        Public Property ErrorColor As ColorSpec = Nothing
        Public Property Line As Integer
    End Class

    ''' <summary>A SCREEN block with its list of fields.</summary>
    Public Class ScreenSection
        Public Property Name As String = ""
        Public Property DefaultColor As ColorSpec = New ColorSpec()
        Public Property Fields As New List(Of ScreenField)
        Public Property Line As Integer
    End Class

    ' ── Top-level Document ───────────────────────────────────────────────────

    ''' <summary>The root of the parsed DSL document.</summary>
    Public Class DslDocument
        Public Property Data As DataSection = New DataSection()
        Public Property Screens As New List(Of ScreenSection)
    End Class

