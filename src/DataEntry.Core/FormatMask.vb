' FormatMask — applies a parsed FORMAT mask to a raw input string.
' This module lives in DataEntry.Core so it can be unit-tested directly.
' The CodeGenerator emits equivalent logic into FormatHelper.vb for generated projects.
Imports System.Text
Imports System.Collections.Generic


    Public Module FormatMask

        ''' <summary>
        ''' Apply a parsed FORMAT mask to a raw input string, returning a string of exactly
        ''' <paramref name="fieldLen"/> characters.
        ''' <para>
        ''' Data placeholders consume one character from <paramref name="raw"/>:
        '''   X  — alphanumeric, copied as-is (space-padded if short)
        '''   U  — forced upper-case
        '''   L  — forced lower-case
        '''   9  — digit only (space if non-digit)
        '''   Z  — zero-fill digit (0 if non-digit)
        ''' Literal tokens are inserted verbatim and do NOT consume a raw character.
        ''' </para>
        ''' <para>
        ''' If the result is shorter than fieldLen it is padded: right-justified for numeric
        ''' masks (all placeholders are 9/Z), left-justified for all others.
        ''' If longer, it is truncated.
        ''' </para>
        ''' </summary>
        Public Function ApplyMask(raw As String,
                                  tokens As List(Of MaskToken),
                                  fieldLen As Integer) As String
            ' No FORMAT mask — accept input verbatim, just pad/truncate to fieldLen.
            If tokens.Count = 0 Then
                If raw.Length < fieldLen Then Return raw.PadRight(fieldLen)
                If raw.Length > fieldLen Then Return raw.Substring(0, fieldLen)
                Return raw
            End If

            Dim sb As New StringBuilder
            Dim ri = 0   ' index into raw input

            ' Normalise pre-formatted input: strip separator/punctuation characters that
            ' cannot be accepted by the mask's placeholder types.  This means both
            ' "800-867-5309" and "(800)867-5309" collapse to raw digits before the mask runs.
            raw = StripLiterals(raw, tokens)

            For Each tok In tokens
                Select Case tok.Kind
                    Case MaskToken.TokenKind.Alphanumeric
                        sb.Append(If(ri < raw.Length, raw(ri), " "c))
                        ri += 1
                    Case MaskToken.TokenKind.UpperCase
                        sb.Append(If(ri < raw.Length, Char.ToUpperInvariant(raw(ri)), " "c))
                        ri += 1
                    Case MaskToken.TokenKind.LowerCase
                        sb.Append(If(ri < raw.Length, Char.ToLowerInvariant(raw(ri)), " "c))
                        ri += 1
                    Case MaskToken.TokenKind.Digit
                        Dim dc = If(ri < raw.Length, raw(ri), " "c)
                        sb.Append(If(Char.IsDigit(dc), dc, " "c))
                        ri += 1
                    Case MaskToken.TokenKind.ZeroFill
                        Dim dc = If(ri < raw.Length, raw(ri), "0"c)
                        sb.Append(If(Char.IsDigit(dc), dc, "0"c))
                        ri += 1
                    Case MaskToken.TokenKind.Literal
                        sb.Append(tok.LiteralChar)   ' never consumes raw input
                End Select
            Next

            Dim result = sb.ToString()
            If result.Length < fieldLen Then
                result = If(IsNumericMask(tokens),
                            result.PadLeft(fieldLen),
                            result.PadRight(fieldLen))
            ElseIf result.Length > fieldLen Then
                result = result.Substring(0, fieldLen)
            End If
            Return result
        End Function

        ''' <summary>
        ''' Derives a human-readable format hint from a mask token list.
        ''' Only returned when the mask contains at least one Literal token — pure
        ''' alpha/digit masks are self-explanatory from context.
        ''' <para>
        ''' Placeholder characters shown in the hint:
        '''   9 / Z  →  #   (digit position)
        '''   X      →  @   (any character)
        '''   U / L  →  ^   (letter, forced case)
        '''   Literal → the literal character itself
        ''' </para>
        ''' Returns an empty string when no hint is warranted.
        ''' </summary>
        Public Function FormatHint(tokens As List(Of MaskToken)) As String
            ' Only emit a hint when there is at least one embedded literal
            Dim hasLiteral = tokens.Exists(Function(t) t.Kind = MaskToken.TokenKind.Literal)
            If Not hasLiteral Then Return ""

            Dim sb As New System.Text.StringBuilder
            For Each tok In tokens
                Select Case tok.Kind
                    Case MaskToken.TokenKind.Digit, MaskToken.TokenKind.ZeroFill
                        sb.Append("#"c)
                    Case MaskToken.TokenKind.Alphanumeric
                        sb.Append("@"c)
                    Case MaskToken.TokenKind.UpperCase, MaskToken.TokenKind.LowerCase
                        sb.Append("^"c)
                    Case MaskToken.TokenKind.Literal
                        sb.Append(tok.LiteralChar)
                End Select
            Next
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Strips non-data characters from <paramref name="raw"/> based on what the mask's
        ''' placeholder types can accept.  This normalises pre-formatted input — e.g. both
        ''' "800-867-5309" and "(800)867-5309" collapse to "8008675309" for a digit-only mask,
        ''' so they format identically to a user who typed raw digits.
        ''' <para>
        ''' Rules (first matching wins):
        '''   X placeholder present  → keep only alphanumeric characters
        '''   U/L placeholder present → keep only letter characters
        '''   9/Z placeholders only  → keep only digit characters
        '''   No data placeholders   → return raw unchanged
        ''' </para>
        ''' </summary>
        Public Function StripLiterals(raw As String, tokens As List(Of MaskToken)) As String
            ' Only normalise input when the mask actually has embedded literal separators.
            ' A mask with no literals doesn't need stripping — the placeholder logic already
            ' handles unexpected characters (9 emits a space for non-digits, etc.).
            Dim hasLiteralToken = tokens.Exists(Function(t) t.Kind = MaskToken.TokenKind.Literal)
            If Not hasLiteralToken Then Return raw

            Dim hasAlphanumeric = tokens.Exists(Function(t) t.Kind = MaskToken.TokenKind.Alphanumeric)
            Dim hasLetter       = tokens.Exists(Function(t) t.Kind = MaskToken.TokenKind.UpperCase OrElse
                                                              t.Kind = MaskToken.TokenKind.LowerCase)
            Dim hasDigit        = tokens.Exists(Function(t) t.Kind = MaskToken.TokenKind.Digit OrElse
                                                              t.Kind = MaskToken.TokenKind.ZeroFill)

            Dim sb As New StringBuilder(raw.Length)
            For Each ch In raw
                If hasAlphanumeric Then
                    ' X accepts any char — keep alphanumerics; strip punctuation/separators
                    If Char.IsLetterOrDigit(ch) Then sb.Append(ch)
                ElseIf hasLetter Then
                    If Char.IsLetter(ch) Then sb.Append(ch)
                ElseIf hasDigit Then
                    If Char.IsDigit(ch) Then sb.Append(ch)
                End If
            Next
            Return sb.ToString()
        End Function

        ''' <summary>
        ''' Returns True when every data-placeholder token in <paramref name="tokens"/> is
        ''' a digit (9) or zero-fill (Z) — i.e. the field holds a pure number.
        ''' Literal tokens are ignored for this classification.
        ''' </summary>
        Public Function IsNumericMask(tokens As List(Of MaskToken)) As Boolean
            Dim hasPlaceholder = False
            For Each tok In tokens
                Select Case tok.Kind
                    Case MaskToken.TokenKind.Digit, MaskToken.TokenKind.ZeroFill
                        hasPlaceholder = True
                    Case MaskToken.TokenKind.Literal
                        ' literals don't affect numeric classification
                    Case Else
                        Return False   ' X / U / L placeholder → not purely numeric
                End Select
            Next
            Return hasPlaceholder
        End Function

    End Module
