' DslParser — recursive-descent parser.
' Consumes a token list from DslLexer and builds a DslDocument AST.
' Returns both the document and any parse errors found.
Imports System.Collections.Generic


    Public Class ParseError
        Public Property Line As Integer
        Public Property Col As Integer
        Public Property Message As String = ""
        Public Property Severity As String = "Error"   ' "Error" or "Warning"
    End Class

    Public Class DslParser

        Private ReadOnly _tokens As List(Of Token)
        Private _pos As Integer = 0
        Public ReadOnly Errors As New List(Of ParseError)

        Public Sub New(tokens As List(Of Token))
            _tokens = tokens
        End Sub

        ' ── Entry point ───────────────────────────────────────────────────────

        Public Function Parse() As DslDocument
            Dim doc As New DslDocument()

            SkipNewlines()

            Do While Not AtEof()
                Dim tok = Current()

                If tok.Type = TokenType.Keyword AndAlso tok.Value = "DATA-SECTION" Then
                    Consume()
                    doc.Data = ParseDataSection()

                ElseIf IsKeyword("RECORD") Then
                    ' RECORD blocks may appear at the top level (after DATA-SECTION)
                    doc.Data.Records.Add(ParseRecord())

                ElseIf tok.Type = TokenType.Keyword AndAlso tok.Value = "SCREEN-SECTION" Then
                    Consume()
                    SkipNewlines()
                    ' Parse one or more SCREEN blocks
                    Do While IsKeyword("SCREEN")
                        doc.Screens.Add(ParseScreen())
                        SkipNewlines()
                    Loop

                Else
                    AddError($"Unexpected token '{tok.Value}' at top level.", tok)
                    Consume()
                End If

                SkipNewlines()
            Loop

            Return doc
        End Function

        ' ── DATA-SECTION ──────────────────────────────────────────────────────

        Private Function ParseDataSection() As DataSection
            Dim ds As New DataSection With {.Line = Current().Line}
            SkipNewlines()

            ' FILE <path> [APPEND|NOAPPEND] LRECL=<n> LEND=<x>
            If IsKeyword("FILE") Then
                Consume()
                ds.FilePath = ConsumeFilePath()
                ds.Mode = AppendMode.Append   ' default

                ' Consume optional keywords on same logical line
                Do While Not AtLineEnd() AndAlso Not AtEof()
                    Dim t = Current()
                    If IsKeyword("APPEND") Then
                        ds.Mode = AppendMode.Append
                        Consume()
                    ElseIf IsKeyword("NOAPPEND") Then
                        ds.Mode = AppendMode.NoAppend
                        Consume()
                    ElseIf IsKeyword("LRECL") Then
                        Consume()
                        Expect(TokenType.Equals)
                        ds.Lrecl = ConsumeInt("LRECL value")
                    ElseIf IsKeyword("LEND") Then
                        Consume()
                        Expect(TokenType.Equals)
                        ds.Ending = ParseLineEnding()
                    Else
                        Exit Do
                    End If
                Loop
                SkipNewlines()
            Else
                AddError("Expected FILE keyword in DATA-SECTION.", Current())
            End If

            ' RECORD blocks may also appear directly inside DATA-SECTION
            Do While IsKeyword("RECORD")
                ds.Records.Add(ParseRecord())
                SkipNewlines()
            Loop

            Return ds
        End Function

        ''' <summary>
        ''' Read a file path — joins tokens with dots to handle names like "output.dat".
        ''' Stops at the first token that is not an identifier/keyword or dot-followed-by-identifier.
        ''' </summary>
        Private Function ConsumeFilePath() As String
            Dim sb As New System.Text.StringBuilder
            ' First segment
            If AtLineEnd() OrElse AtEof() Then
                AddError("Expected file path after FILE.", Current())
                Return ""
            End If
            sb.Append(Current().Value)
            Consume()
            ' Keep appending  .segment  pairs
            Do While Current().Type = TokenType.Dot
                Dim savedPos = _pos
                Consume()  ' eat the dot
                If Current().Type = TokenType.Identifier OrElse Current().Type = TokenType.Keyword OrElse Current().Type = TokenType.Number Then
                    sb.Append("."c)
                    sb.Append(Current().Value)
                    Consume()
                Else
                    ' Dot wasn't part of path — put position back and stop
                    _pos = savedPos
                    Exit Do
                End If
            Loop
            Return sb.ToString()
        End Function

        Private Function ParseLineEnding() As LineEnding
            Dim t = Current()
            If t.Type = TokenType.Keyword OrElse t.Type = TokenType.Identifier Then
                Select Case t.Value.ToUpperInvariant()
                    Case "CRLF" : Consume() : Return LineEnding.CRLF
                    Case "LF"   : Consume() : Return LineEnding.LF
                    Case "CR"   : Consume() : Return LineEnding.CR
                    Case "NONE" : Consume() : Return LineEnding.None
                End Select
            End If
            AddError($"Expected CRLF/LF/CR/NONE after LEND=, got '{t.Value}'.", t)
            Return LineEnding.CRLF
        End Function

        ' ── RECORD ───────────────────────────────────────────────────────────

        Private Function ParseRecord() As RecordDef
            Dim rec As New RecordDef With {.Line = Current().Line}
            Consume()  ' eat RECORD
            rec.Name = ConsumeIdentifier("record name")
            SkipNewlines()

            ' Fields: each starts with an identifier (field name)
            Dim lastField As FieldDef = Nothing
            Do While Not AtEof() AndAlso Not IsKeyword("RECORD") AndAlso
                     Not IsKeyword("SCREEN-SECTION") AndAlso Not IsKeyword("DATA-SECTION")

                ' FORMAT= line (continuation of previous field)
                If IsKeyword("FORMAT") Then
                    Consume()
                    Expect(TokenType.Equals)
                    Dim fmt = ReadFormatString()
                    If lastField IsNot Nothing Then
                        lastField.Format = fmt
                    Else
                        AddError("FORMAT without a preceding field.", Current())
                    End If
                    SkipNewlines()
                    Continue Do
                End If

                ' Blank / newline
                If Current().Type = TokenType.Newline Then
                    SkipNewlines()
                    Continue Do
                End If

                ' Field name (identifier)
                If Current().Type <> TokenType.Identifier AndAlso Current().Type <> TokenType.Keyword Then
                    Exit Do
                End If

                Dim fld As New FieldDef With {.Line = Current().Line}
                fld.Name = ConsumeIdentifier("field name")

                ' Optional START= and LEN= on same line
                Do While Not AtLineEnd() AndAlso Not AtEof()
                    If IsKeyword("START") Then
                        Consume()
                        Expect(TokenType.Equals)
                        fld.Start = ConsumeInt("START value")
                    ElseIf IsKeyword("LEN") Then
                        Consume()
                        Expect(TokenType.Equals)
                        fld.Len = ConsumeInt("LEN value")
                    Else
                        Exit Do
                    End If
                Loop

                rec.Fields.Add(fld)
                lastField = fld
                SkipNewlines()
            Loop

            Return rec
        End Function

        ''' <summary>
        ''' Read a FORMAT mask string.
        ''' The mask is everything on the current line up to and including the terminating dot.
        ''' We collect all tokens to end-of-line (including embedded dots like in ZZZZZ.99),
        ''' then strip the trailing '.' terminator.
        ''' </summary>
        Private Function ReadFormatString() As FormatSpec
            Dim spec As New FormatSpec()
            Dim sb As New System.Text.StringBuilder

            ' Collect all tokens on this line, dots and all
            Do While Not AtEof() AndAlso Not AtLineEnd()
                Dim t = Current()
                If t.Type = TokenType.Dot Then
                    sb.Append("."c)
                Else
                    sb.Append(t.Value)
                End If
                Consume()
            Loop

            ' The trailing '.' is the terminator — strip it
            Dim raw = sb.ToString().Trim()
            If raw.EndsWith(".") Then raw = raw.Substring(0, raw.Length - 1)

            spec.Raw = raw
            spec.Tokens = ParseFormatMask(spec.Raw)
            Return spec
        End Function

        ''' <summary>Convert a raw format mask string into MaskToken list.</summary>
        Public Shared Function ParseFormatMask(raw As String) As List(Of MaskToken)
            Dim list As New List(Of MaskToken)
            Dim i = 0
            Do While i < raw.Length
                Dim c = raw(i)
                If c = "\"c AndAlso i + 1 < raw.Length Then
                    ' Escaped literal: \\ → \, \x → x
                    list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.Literal, .LiteralChar = raw(i + 1)})
                    i += 2
                Else
                    Select Case Char.ToUpperInvariant(c)
                        Case "X"c
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.Alphanumeric})
                        Case "U"c
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.UpperCase})
                        Case "L"c
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.LowerCase})
                        Case "9"c
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.Digit})
                        Case "Z"c
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.ZeroFill})
                        Case Else
                            list.Add(New MaskToken With {.Kind = MaskToken.TokenKind.Literal, .LiteralChar = c})
                    End Select
                    i += 1
                End If
            Loop
            Return list
        End Function

        ' ── SCREEN-SECTION ────────────────────────────────────────────────────

        Private Function ParseScreen() As ScreenSection
            Dim scr As New ScreenSection With {.Line = Current().Line}
            Consume()  ' eat SCREEN
            scr.Name = ConsumeIdentifier("screen name")

            ' Optional COLOR= / FG= / BG= on same line as SCREEN name
            scr.DefaultColor = ParseColorAttribs(New ColorSpec With {.Fg = "Gray", .Bg = "DarkBlue"})

            SkipNewlines()

            ' Fields and Prompts until next SCREEN or end of SCREEN-SECTION or EOF
            Do While Not AtEof() AndAlso Not IsKeyword("SCREEN") AndAlso
                     Not IsKeyword("DATA-SECTION") AndAlso Not IsKeyword("SCREEN-SECTION")

                If Current().Type = TokenType.Newline Then
                    SkipNewlines()
                    Continue Do
                End If

                If IsKeyword("FIELD") Then
                    scr.Fields.Add(ParseScreenField())
                ElseIf IsKeyword("PROMPT") OrElse IsKeyword("LABEL") Then
                    scr.Prompts.Add(ParseScreenPrompt())
                Else
                    Exit Do
                End If
            Loop

            Return scr
        End Function

        Private Function ParseScreenPrompt() As ScreenPrompt
            Dim pr As New ScreenPrompt With {.Line = Current().Line}
            Consume()  ' eat PROMPT or LABEL
            pr.Text = ConsumeString("prompt text")

            Do While Not AtLineEnd() AndAlso Not AtEof()
                If IsKeyword("ROW") Then
                    Consume() : Expect(TokenType.Equals)
                    pr.Row = ConsumeInt("ROW value")
                ElseIf IsKeyword("COL") Then
                    Consume() : Expect(TokenType.Equals)
                    pr.Col = ConsumeInt("COL value")
                Else
                    Exit Do
                End If
            Loop

            SkipNewlines()
            If Not AtEof() AndAlso (IsKeyword("COLOR") OrElse IsKeyword("FG") OrElse IsKeyword("BG") OrElse IsKeyword("NORMAL")) Then
                pr.Color = ParseColorAttribs(Nothing)
                SkipNewlines()
            End If

            Return pr
        End Function

        Private Function ParseScreenField() As ScreenField
            Dim fld As New ScreenField With {.Line = Current().Line}
            Consume()  ' eat FIELD
            If Current().Type = TokenType.StringLit Then
                fld.Label = ConsumeString("field label")
            End If

            ' ROW= COL= PROMPT_ROW= PROMPT_COL= LEN= INTO RECORD.FIELD  VALIDATE WITH <func>  on same line
            Do While Not AtLineEnd() AndAlso Not AtEof()
                If IsKeyword("ROW") OrElse IsKeyword("FIELD_ROW") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.Row = ConsumeInt("ROW value")
                ElseIf IsKeyword("COL") OrElse IsKeyword("FIELD_COL") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.Col = ConsumeInt("COL value")
                ElseIf IsKeyword("PROMPT_ROW") OrElse IsKeyword("LABEL_ROW") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.PromptRow = ConsumeInt("PROMPT_ROW value")
                ElseIf IsKeyword("PROMPT_COL") OrElse IsKeyword("LABEL_COL") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.PromptCol = ConsumeInt("PROMPT_COL value")
                ElseIf IsKeyword("LEN") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.Len = ConsumeInt("LEN value")
                ElseIf IsKeyword("INTO") Then
                    Consume()
                    ' Expect RECORD.FIELD
                    Dim recName = ConsumeIdentifier("record name after INTO")
                    Expect(TokenType.Dot)
                    Dim fldName = ConsumeIdentifier("field name after INTO record.")
                    fld.IntoRecord = recName
                    fld.IntoField = fldName
                ElseIf IsKeyword("VALIDATE") Then
                    Consume()
                    If IsKeyword("WITH") Then Consume()
                    fld.ValidateFunc = ConsumeIdentifier("validate function name")
                Else
                    Exit Do
                End If
            Loop

            ' Optional color attributes on continuation lines (NORMAL= FOCUS= ERROR=)
            SkipNewlines()
            Do While Not AtEof() AndAlso Not IsKeyword("FIELD") AndAlso
                     Not IsKeyword("PROMPT") AndAlso Not IsKeyword("LABEL") AndAlso
                     Not IsKeyword("SCREEN") AndAlso Not IsKeyword("DATA-SECTION") AndAlso
                     Not IsKeyword("SCREEN-SECTION")

                If IsKeyword("NORMAL") OrElse IsKeyword("FOCUS") OrElse IsKeyword("ERROR") Then
                    ParseFieldColorLine(fld)
                    SkipNewlines()
                Else
                    Exit Do
                End If
            Loop

            Return fld
        End Function

        ''' <summary>Parse a line of  NORMAL=x FOCUS=y ERROR=z  color attributes onto fld.</summary>
        Private Sub ParseFieldColorLine(fld As ScreenField)
            Do While Not AtLineEnd() AndAlso Not AtEof()
                If IsKeyword("NORMAL") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.NormalColor = ParseOneColor()
                ElseIf IsKeyword("FOCUS") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.FocusColor = ParseOneColor()
                ElseIf IsKeyword("ERROR") Then
                    Consume() : Expect(TokenType.Equals)
                    fld.ErrorColor = ParseOneColor()
                Else
                    Exit Do
                End If
            Loop
        End Sub

        ' ── Color helpers ────────────────────────────────────────────────────

        ''' <summary>Consume optional COLOR= FG= BG= tokens from the current line, returning a ColorSpec.</summary>
        Private Function ParseColorAttribs(defaults As ColorSpec) As ColorSpec
            Dim spec As New ColorSpec With {.Fg = If(defaults Is Nothing, "", defaults.Fg), .Bg = If(defaults Is Nothing, "", defaults.Bg)}
            Do While Not AtLineEnd() AndAlso Not AtEof()
                If IsKeyword("COLOR") Then
                    Consume() : Expect(TokenType.Equals)
                    Dim pair = ParseOneColor()
                    spec.Fg = pair.Fg
                    spec.Bg = pair.Bg
                ElseIf IsKeyword("FG") Then
                    Consume() : Expect(TokenType.Equals)
                    spec.Fg = ConsumeColorName()
                ElseIf IsKeyword("BG") Then
                    Consume() : Expect(TokenType.Equals)
                    spec.Bg = ConsumeColorName()
                Else
                    Exit Do
                End If
            Loop
            Return spec
        End Function

        ''' <summary>Read a single color value — either FgOnBg shorthand or a bare color name.</summary>
        Private Function ParseOneColor() As ColorSpec
            Dim raw = ConsumeValue("color value")
            ' Try to split on "On" (case-insensitive)
            Dim idx = raw.IndexOf("On", StringComparison.OrdinalIgnoreCase)
            If idx > 0 Then
                Return New ColorSpec With {
                    .Fg = raw.Substring(0, idx),
                    .Bg = raw.Substring(idx + 2)
                }
            End If
            ' Bare name — foreground only, background stays default
            Return New ColorSpec With {.Fg = raw, .Bg = "DarkBlue"}
        End Function

        ''' <summary>Consume one token as a color name string.</summary>
        Private Function ConsumeColorName() As String
            Return ConsumeValue("color name")
        End Function

        ' ── Token consumption helpers ─────────────────────────────────────────

        Private Function Current() As Token
            If _pos < _tokens.Count Then Return _tokens(_pos)
            Return _tokens(_tokens.Count - 1)   ' EOF
        End Function

        Private Function Peek(offset As Integer) As Token
            Dim idx = _pos + offset
            If idx < _tokens.Count Then Return _tokens(idx)
            Return _tokens(_tokens.Count - 1)
        End Function

        Private Sub Consume()
            If _pos < _tokens.Count - 1 Then _pos += 1
        End Sub

        Private Function AtEof() As Boolean
            Return Current().Type = TokenType.EOF
        End Function

        Private Function AtLineEnd() As Boolean
            Return Current().Type = TokenType.Newline OrElse Current().Type = TokenType.EOF
        End Function

        Private Sub SkipNewlines()
            Do While Current().Type = TokenType.Newline
                Consume()
            Loop
        End Sub

        Private Function IsKeyword(kw As String) As Boolean
            Dim t = Current()
            Return (t.Type = TokenType.Keyword OrElse t.Type = TokenType.Identifier) AndAlso
                   String.Equals(t.Value, kw, StringComparison.OrdinalIgnoreCase)
        End Function

        Private Sub Expect(tt As TokenType)
            If Current().Type = tt Then
                Consume()
            Else
                AddError($"Expected {tt}, got '{Current().Value}'.", Current())
            End If
        End Sub

        ''' <summary>Consume and return a value — identifier, keyword, string, or number.</summary>
        Private Function ConsumeValue(what As String) As String
            Dim t = Current()
            If t.Type = TokenType.EOF OrElse t.Type = TokenType.Newline Then
                AddError($"Expected {what} but got end of line.", t)
                Return ""
            End If
            Consume()
            Return t.Value
        End Function

        Private Function ConsumeIdentifier(what As String) As String
            Dim t = Current()
            If t.Type = TokenType.Identifier OrElse t.Type = TokenType.Keyword Then
                Consume()
                Return t.Value
            End If
            AddError($"Expected {what} (identifier), got '{t.Value}'.", t)
            Return ""
        End Function

        Private Function ConsumeString(what As String) As String
            Dim t = Current()
            If t.Type = TokenType.StringLit Then
                Consume()
                Return t.Value
            End If
            ' Fall back to a bare word
            Return ConsumeIdentifier(what)
        End Function

        Private Function ConsumeInt(what As String) As Integer
            Dim t = Current()
            If t.Type = TokenType.Number Then
                Consume()
                Dim n As Integer
                If Integer.TryParse(t.Value, n) Then Return n
            End If
            AddError($"Expected integer for {what}, got '{t.Value}'.", t)
            Return 0
        End Function

        Private Sub AddError(msg As String, tok As Token, Optional severity As String = "Error")
            Errors.Add(New ParseError With {
                .Line = tok.Line, .Col = tok.Col,
                .Message = msg, .Severity = severity
            })
        End Sub

    End Class

