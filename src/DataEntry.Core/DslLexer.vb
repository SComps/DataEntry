' DslLexer — breaks DSL source text into a flat list of tokens.
' Keeps it simple: no regex, just a single-pass character scan.
Imports System.Collections.Generic


    Public Enum TokenType
        Keyword         ' DATA-SECTION, SCREEN-SECTION, RECORD, FIELD, FILE, etc.
        Identifier      ' any bareword not matched as keyword
        StringLit       ' "quoted string"
        Number          ' integer literal
        Equals          ' =
        Dot             ' .
        Newline         ' end of logical line (blank lines are collapsed)
        EOF
    End Enum

    Public Class Token
        Public Property Type As TokenType
        Public Property Value As String = ""
        Public Property Line As Integer
        Public Property Col As Integer

        Public Overrides Function ToString() As String
            Return $"[{Type} ""{Value}"" L{Line}]"
        End Function
    End Class

    Public Class DslLexer

        ' All keywords, upper-cased for comparison.
        Private Shared ReadOnly Keywords As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "DATA-SECTION", "SCREEN-SECTION", "RECORD", "FIELD", "FILE",
            "APPEND", "NOAPPEND", "LRECL", "LEND", "CRLF", "LF", "CR", "NONE",
            "FORMAT", "START", "LEN", "ROW", "COL", "INTO", "VALIDATE", "WITH",
            "SCREEN", "COLOR", "FG", "BG", "NORMAL", "FOCUS", "ERROR",
            "PROMPT", "LABEL", "PROMPT_ROW", "PROMPT_COL", "LABEL_ROW", "LABEL_COL",
            "FIELD_ROW", "FIELD_COL"
        }

        Private ReadOnly _src As String
        Private _pos As Integer = 0
        Private _line As Integer = 1
        Private _col As Integer = 1

        Public Sub New(source As String)
            _src = source
        End Sub

        ' ── Public entry point ────────────────────────────────────────────────

        Public Function Tokenize() As List(Of Token)
            Dim tokens As New List(Of Token)
            Dim lastWasNewline As Boolean = True  ' suppress leading blank lines

            Do While _pos < _src.Length
                Dim ch = _src(_pos)

                ' Skip spaces and tabs (not newlines)
                If ch = " "c OrElse ch = Chr(9) Then
                    Advance()

                ' Comments — * or // to end of line
                ElseIf ch = "*"c OrElse (ch = "/"c AndAlso Peek(1) = "/"c) Then
                    SkipToEndOfLine()

                ' Newlines — collapse multiple blank lines into one token
                ElseIf ch = Chr(13) OrElse ch = Chr(10) Then
                    SkipNewline()
                    If Not lastWasNewline Then
                        tokens.Add(MakeTok(TokenType.Newline, "", _line, _col))
                        lastWasNewline = True
                    End If
                    Continue Do

                ' String literal
                ElseIf ch = """"c Then
                    tokens.Add(ReadString())
                    lastWasNewline = False

                ' Number
                ElseIf Char.IsDigit(ch) Then
                    tokens.Add(ReadNumber())
                    lastWasNewline = False

                ' Equals sign
                ElseIf ch = "="c Then
                    tokens.Add(MakeTok(TokenType.Equals, "=", _line, _col))
                    Advance()
                    lastWasNewline = False

                ' Dot (FORMAT terminator)
                ElseIf ch = "."c Then
                    tokens.Add(MakeTok(TokenType.Dot, ".", _line, _col))
                    Advance()
                    lastWasNewline = False

                ' Word (keyword or identifier)
                ElseIf Char.IsLetter(ch) OrElse ch = "-"c OrElse ch = "_"c Then
                    tokens.Add(ReadWord())
                    lastWasNewline = False

                Else
                    ' Skip unrecognised characters
                    Advance()
                End If
            Loop

            tokens.Add(MakeTok(TokenType.EOF, "", _line, _col))
            Return tokens
        End Function

        ' ── Private helpers ───────────────────────────────────────────────────

        Private Function ReadWord() As Token
            Dim startLine = _line, startCol = _col
            Dim sb As New System.Text.StringBuilder
            ' Words can contain letters, digits, hyphens, underscores
            Do While _pos < _src.Length
                Dim c = _src(_pos)
                If Char.IsLetterOrDigit(c) OrElse c = "-"c OrElse c = "_"c Then
                    sb.Append(c)
                    Advance()
                Else
                    Exit Do
                End If
            Loop
            Dim word = sb.ToString()
            Dim tt = If(Keywords.Contains(word), TokenType.Keyword, TokenType.Identifier)
            ' Keywords are stored upper-case for easy matching; identifiers keep original case.
            Dim stored = If(tt = TokenType.Keyword, word.ToUpperInvariant(), word)
            Return MakeTok(tt, stored, startLine, startCol)
        End Function

        Private Function ReadNumber() As Token
            Dim startLine = _line, startCol = _col
            Dim sb As New System.Text.StringBuilder
            Do While _pos < _src.Length AndAlso Char.IsDigit(_src(_pos))
                sb.Append(_src(_pos))
                Advance()
            Loop
            Return MakeTok(TokenType.Number, sb.ToString(), startLine, startCol)
        End Function

        Private Function ReadString() As Token
            Dim startLine = _line, startCol = _col
            Advance()  ' skip opening "
            Dim sb As New System.Text.StringBuilder
            Do While _pos < _src.Length AndAlso _src(_pos) <> """"c
                ' Allow escaped quote \"
                If _src(_pos) = "\"c AndAlso Peek(1) = """"c Then
                    sb.Append(""""c)
                    Advance() : Advance()
                Else
                    sb.Append(_src(_pos))
                    Advance()
                End If
            Loop
            If _pos < _src.Length Then Advance()  ' skip closing "
            Return MakeTok(TokenType.StringLit, sb.ToString(), startLine, startCol)
        End Function

        Private Sub SkipToEndOfLine()
            Do While _pos < _src.Length AndAlso _src(_pos) <> Chr(13) AndAlso _src(_pos) <> Chr(10)
                Advance()
            Loop
        End Sub

        Private Sub SkipNewline()
            If _pos < _src.Length AndAlso _src(_pos) = Chr(13) Then Advance()
            If _pos < _src.Length AndAlso _src(_pos) = Chr(10) Then Advance()
            _line += 1
            _col = 1
        End Sub

        Private Sub Advance()
            _pos += 1
            _col += 1
        End Sub

        Private Function Peek(offset As Integer) As Char
            Dim idx = _pos + offset
            If idx < _src.Length Then Return _src(idx)
            Return Chr(0)
        End Function

        Private Function MakeTok(tt As TokenType, value As String, line As Integer, col As Integer) As Token
            Return New Token With {.Type = tt, .Value = value, .Line = line, .Col = col}
        End Function

    End Class

