' DslValidator — semantic checks on a parsed DslDocument.
' Returns a flat list of ValidationError; caller decides what to do with them.
Imports System.Collections.Generic


    Public Class ValidationError
        Public Property Line As Integer
        Public Property Col As Integer
        Public Property Message As String = ""
        Public Property Severity As String = "Error"   ' "Error" or "Warning"
    End Class

    Public Class DslValidator

        ' Screen size constants — single source of truth for boundary checks.
        Private Const MaxCols As Integer = 80
        Private Const MaxRows As Integer = 24

        Private ReadOnly _doc As DslDocument
        Private ReadOnly _errors As New List(Of ValidationError)

        Public Sub New(doc As DslDocument)
            _doc = doc
        End Sub

        Public Function Validate() As List(Of ValidationError)
            _errors.Clear()
            CheckDataSection()
            CheckScreenSections()
            CheckValidateSection()
            Return _errors
        End Function

        ' ── DATA-SECTION checks ───────────────────────────────────────────────

        Private Sub CheckDataSection()
            Dim ds = _doc.Data

            If String.IsNullOrWhiteSpace(ds.FilePath) Then
                AddError(ds.Line, 0, "DATA-SECTION is missing a FILE path.")
            End If

            If ds.Lrecl <= 0 Then
                AddError(ds.Line, 0, "LRECL must be greater than zero.")
            End If

            ' Warn if FILE path contains '..' (traversal) or is absolute (rooted).
            ' The audience is programmers who may use relative paths intentionally,
            ' so this is a warning rather than a hard error.
            If Not String.IsNullOrEmpty(ds.FilePath) Then
                If ds.FilePath.Contains("..") OrElse IO.Path.IsPathRooted(ds.FilePath) Then
                    AddWarning(ds.Line, 0,
                        $"FILE path '{ds.FilePath}' contains a path traversal segment or is absolute. " &
                        "Verify the path is intentional — the generated application will write to this location.")
                End If
            End If

            ' Check for duplicate record names
            Dim recNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each rec In ds.Records
                If Not recNames.Add(rec.Name) Then
                    AddError(rec.Line, 0, $"Duplicate record name '{rec.Name}'.")
                End If
                CheckRecord(rec, ds.Lrecl)
            Next
        End Sub

        Private Sub CheckRecord(rec As RecordDef, lrecl As Integer)
            ' Duplicate field names
            Dim fieldNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            Dim pos = 1   ' current byte position for implicit start tracking (1-based)
            ' Track occupied byte ranges [startPos, endPos] for overlap detection.
            ' Each entry is a (start, last) tuple stored as two-element Integer arrays.
            Dim occupied As New List(Of Integer())

            For Each fld In rec.Fields
                If Not fieldNames.Add(fld.Name) Then
                    AddError(fld.Line, 0, $"Duplicate field name '{fld.Name}' in record '{rec.Name}'.")
                End If

                If fld.Len <= 0 Then
                    AddError(fld.Line, 0, $"Field '{fld.Name}' in record '{rec.Name}' has no LEN specified.")
                    Continue For
                End If

                ' Reject START=0 or any negative value — column numbers are 1-based;
                ' column 0 does not exist (cards and mainframe records start at column 1).
                If fld.Start = 0 OrElse fld.Start < -1 Then
                    AddError(fld.Line, 0,
                        $"Field '{fld.Name}' in record '{rec.Name}': START={fld.Start} is invalid. " &
                        $"Column numbers are 1-based; the first column is START=1.")
                    Continue For
                End If

                ' Resolve start position: explicit START= or implicit next-available column.
                Dim startPos = If(fld.Start > 0, fld.Start, pos)
                Dim endPos = startPos + fld.Len - 1

                ' Store resolved 1-based position on the field for downstream consumers.
                fld.ResolvedStart = startPos

                ' Check LRECL boundary.
                If endPos > lrecl Then
                    AddError(fld.Line, 0,
                        $"Field '{fld.Name}' in record '{rec.Name}' " &
                        $"(START={startPos} LEN={fld.Len}) exceeds LRECL={lrecl}.")
                End If

                ' Check for overlap with any already-laid-out field.
                For Each span In occupied
                    If startPos <= span(1) AndAlso endPos >= span(0) Then
                        AddError(fld.Line, 0,
                            $"Field '{fld.Name}' in record '{rec.Name}' " &
                            $"(START={startPos} LEN={fld.Len}) overlaps a previously defined field " &
                            $"(columns {span(0)}–{span(1)}).")
                        Exit For
                    End If
                Next
                occupied.Add(New Integer() {startPos, endPos})

                ' Check FORMAT mask length vs LEN.
                ' Mask longer than LEN will corrupt adjacent record data — hard Error.
                ' Mask shorter than LEN is valid (partial-fill pattern) — Warning only.
                Dim fmt = fld.Format
                If Not String.IsNullOrEmpty(fmt.Raw) Then
                    If fmt.Tokens.Count > fld.Len Then
                        AddError(fld.Line, 0,
                            $"Field '{fld.Name}': FORMAT mask length ({fmt.Tokens.Count}) " &
                            $"exceeds LEN={fld.Len}. Excess mask characters would corrupt adjacent record data.")
                    ElseIf fmt.Tokens.Count < fld.Len Then
                        AddWarning(fld.Line, 0,
                            $"Field '{fld.Name}': FORMAT mask length ({fmt.Tokens.Count}) " &
                            $"is shorter than LEN={fld.Len}. The tail of the field will be space/zero padded.")
                    End If
                End If

                ' Advance implicit cursor to the next available column.
                pos = endPos + 1
            Next
        End Sub

        ' ── SCREEN-SECTION checks ─────────────────────────────────────────────

        Private Class RenderedSpan
            Public Property Name As String = ""
            Public Property Row As Integer
            Public Property StartCol As Integer
            Public Property EndCol As Integer
            Public Property Line As Integer
        End Class

        Private Sub CheckScreenSections()
            ' Build a lookup of all known record/field names for INTO validation
            Dim recordMap As New Dictionary(Of String, HashSet(Of String))(StringComparer.OrdinalIgnoreCase)
            For Each rec In _doc.Data.Records
                Dim names As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
                For Each fld In rec.Fields
                    names.Add(fld.Name)
                Next
                recordMap(rec.Name) = names
            Next

            Dim screenNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each scr In _doc.Screens
                If Not screenNames.Add(scr.Name) Then
                    AddError(scr.Line, 0, $"Duplicate screen name '{scr.Name}'.")
                End If

                Dim spans As New List(Of RenderedSpan)

                ' Check standalone prompts
                For Each pr In scr.Prompts
                    CheckColorSpec(pr.Line, $"prompt '{pr.Text}'", pr.Color)
                    Dim txtLen = Math.Max(1, pr.Text.Length)
                    Dim startCol = pr.Col
                    Dim endCol = pr.Col + txtLen - 1
                    If pr.Row < 1 OrElse pr.Row > MaxRows OrElse pr.Col < 1 OrElse endCol > MaxCols Then
                        AddError(pr.Line, 0,
                            $"Prompt '{pr.Text}' (ROW={pr.Row} COL={startCol}..{endCol}) exceeds screen boundaries ({MaxCols} columns x {MaxRows} rows).")
                    End If
                    spans.Add(New RenderedSpan With {
                        .Name = $"Prompt '{pr.Text}'",
                        .Row = pr.Row,
                        .StartCol = startCol,
                        .EndCol = endCol,
                        .Line = pr.Line
                    })
                Next

                For Each sfld In scr.Fields
                    Dim fieldId = If(Not String.IsNullOrEmpty(sfld.Label),
                                     $"Field '{sfld.Label}'",
                                     $"Field '{sfld.IntoRecord}.{sfld.IntoField}'")

                    ' INTO cross-reference
                    If Not String.IsNullOrEmpty(sfld.IntoRecord) Then
                        If Not recordMap.ContainsKey(sfld.IntoRecord) Then
                            AddError(sfld.Line, 0,
                                $"{fieldId} references unknown record '{sfld.IntoRecord}'.")
                        ElseIf Not recordMap(sfld.IntoRecord).Contains(sfld.IntoField) Then
                            AddError(sfld.Line, 0,
                                $"{fieldId} references unknown field " &
                                $"'{sfld.IntoRecord}.{sfld.IntoField}'.")
                        End If
                    End If

                    ' VALIDATE WITH — warn only if no matching block in VALIDATE-SECTION;
                    ' if a block is defined there the logic is compiled in and no stub is needed.
                    If Not String.IsNullOrEmpty(sfld.ValidateFunc) Then
                        Dim hasBlock = _doc.ValidateBlocks.Exists(
                            Function(b) String.Equals(b.Name, sfld.ValidateFunc, StringComparison.OrdinalIgnoreCase))
                        If Not hasBlock Then
                            AddWarning(sfld.Line, 0,
                                $"VALIDATE WITH '{sfld.ValidateFunc}' — ensure this function is defined " &
                                "in the generated code.")
                        End If
                    End If

                    ' Color name validation
                    CheckColorSpec(sfld.Line, "NORMAL", sfld.NormalColor)
                    CheckColorSpec(sfld.Line, "FOCUS", sfld.FocusColor)
                    CheckColorSpec(sfld.Line, "ERROR", sfld.ErrorColor)

                    ' Calculate spans and screen bounds for field prompt and data box
                    Dim tfRow As Integer, tfCol As Integer
                    If Not String.IsNullOrEmpty(sfld.Label) Then
                        Dim pRow = If(sfld.PromptRow <> -1, sfld.PromptRow, sfld.Row)
                        Dim pCol = If(sfld.PromptCol <> -1, sfld.PromptCol, sfld.Col)
                        Dim pLen = sfld.Label.Length + 1
                        Dim pEnd = pCol + pLen - 1
                        If pRow < 1 OrElse pRow > MaxRows OrElse pCol < 1 OrElse pEnd > MaxCols Then
                            AddError(sfld.Line, 0,
                                $"Prompt '{sfld.Label}' (ROW={pRow} COL={pCol}..{pEnd}) exceeds screen boundaries ({MaxCols} columns x {MaxRows} rows).")
                        End If
                        spans.Add(New RenderedSpan With {
                            .Name = $"Prompt '{sfld.Label}'",
                            .Row = pRow,
                            .StartCol = pCol,
                            .EndCol = pEnd,
                            .Line = sfld.Line
                        })

                        If sfld.PromptRow <> -1 AndAlso sfld.PromptCol <> -1 Then
                            tfRow = sfld.Row
                            tfCol = sfld.Col
                        Else
                            tfRow = sfld.Row
                            tfCol = sfld.Col + sfld.Label.Length + 2
                        End If
                    Else
                        tfRow = sfld.Row
                        tfCol = sfld.Col
                    End If

                    Dim tfEnd = tfCol + Math.Max(1, sfld.Len) - 1
                    If tfRow < 1 OrElse tfRow > MaxRows OrElse tfCol < 1 OrElse tfEnd > MaxCols Then
                        AddError(sfld.Line, 0,
                            $"{fieldId} (ROW={tfRow} COL={tfCol}..{tfEnd}) exceeds screen boundaries ({MaxCols} columns x {MaxRows} rows).")
                    End If
                    spans.Add(New RenderedSpan With {
                        .Name = fieldId,
                        .Row = tfRow,
                        .StartCol = tfCol,
                        .EndCol = tfEnd,
                        .Line = sfld.Line
                    })
                Next

                ' Check for overlaps among all spans on this screen
                For i = 0 To spans.Count - 1
                    For j = i + 1 To spans.Count - 1
                        Dim s1 = spans(i)
                        Dim s2 = spans(j)
                        If s1.Row = s2.Row Then
                            If s1.StartCol <= s2.EndCol AndAlso s1.EndCol >= s2.StartCol Then
                                AddError(Math.Max(s1.Line, s2.Line), 0,
                                    $"{s1.Name} (COL={s1.StartCol}..{s1.EndCol}) overlaps {s2.Name} (COL={s2.StartCol}..{s2.EndCol}) on ROW {s1.Row}.")
                            End If
                        End If
                    Next
                Next

                CheckColorSpec(scr.Line, "screen default", scr.DefaultColor)
            Next
        End Sub

        Private Sub CheckColorSpec(line As Integer, context As String, spec As ColorSpec)
            If spec Is Nothing Then Return
            If Not IsValidColorName(spec.Fg) Then
                AddWarning(line, 0, $"Unknown foreground color '{spec.Fg}' in {context} color spec.")
            End If
            If Not IsValidColorName(spec.Bg) Then
                AddWarning(line, 0, $"Unknown background color '{spec.Bg}' in {context} color spec.")
            End If
        End Sub

        ' Must match the names that ColorHelper.ToColor16 resolves (including Bright* variants).
        ' Aliases like "Red" (= DarkRed) are accepted as colour names here too.
        Private Shared ReadOnly ValidColors As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "Black", "DarkRed", "DarkGreen", "DarkYellow", "DarkBlue",
            "DarkMagenta", "DarkCyan", "Gray", "DarkGray",
            "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White",
            "BrightRed", "BrightGreen", "BrightYellow", "BrightBlue",
            "BrightMagenta", "BrightCyan"
        }

        Private Shared Function IsValidColorName(name As String) As Boolean
            If String.IsNullOrEmpty(name) Then Return True  ' empty = use default
            Return ValidColors.Contains(name)
        End Function

        ' ── VALIDATE-SECTION checks ───────────────────────────────────────────

        Private Sub CheckValidateSection()
            ' Build a flat list of all record field names for expression resolution.
            Dim allFields As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each rec In _doc.Data.Records
                For Each f In rec.Fields
                    allFields.Add(f.Name)
                Next
            Next

            Dim blockNames As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase)
            For Each blk In _doc.ValidateBlocks
                ' Duplicate block names.
                If Not blockNames.Add(blk.Name) Then
                    AddError(blk.Line, 0, $"Duplicate VALIDATE block name '{blk.Name}'.")
                End If

                For Each rule In blk.Rules
                    If rule.Kind = RuleKind.Assign Then
                        ' Target field must be a known record field or VALUE.
                        If Not String.Equals(rule.TargetField, "VALUE", StringComparison.OrdinalIgnoreCase) AndAlso
                           Not allFields.Contains(rule.TargetField) Then
                            AddError(rule.Line, 0,
                                $"VALIDATE '{blk.Name}': target field '{rule.TargetField}' is not defined in any RECORD.")
                        End If
                        ' Field names in the expression must be known or VALUE.
                        For Each tok In rule.Expression
                            If tok.Kind = ExprToken.ExprTokenKind.FieldName AndAlso
                               Not String.Equals(tok.Value, "VALUE", StringComparison.OrdinalIgnoreCase) AndAlso
                               Not allFields.Contains(tok.Value) Then
                                AddError(rule.Line, 0,
                                    $"VALIDATE '{blk.Name}': expression references unknown field '{tok.Value}'.")
                            End If
                        Next

                        ' Warn if the target field appears on a screen without PROTECTED.
                        ' A user could overwrite a calculated value if the field is editable.
                        If Not String.Equals(rule.TargetField, "VALUE", StringComparison.OrdinalIgnoreCase) Then
                            For Each scr In _doc.Screens
                                For Each sfld In scr.Fields
                                    If String.Equals(sfld.IntoField, rule.TargetField, StringComparison.OrdinalIgnoreCase) AndAlso
                                       Not sfld.IsProtected Then
                                        AddWarning(rule.Line, 0,
                                            $"VALIDATE '{blk.Name}': target field '{rule.TargetField}' is on the screen " &
                                            $"but not declared PROTECTED — the user can overwrite the calculated value.")
                                    End If
                                Next
                            Next
                        End If
                    End If
                Next
            Next
        End Sub

        ' ── Helpers ───────────────────────────────────────────────────────────

        Private Sub AddError(line As Integer, col As Integer, msg As String)
            _errors.Add(New ValidationError With {
                .Line = line, .Col = col, .Message = msg, .Severity = "Error"
            })
        End Sub

        Private Sub AddWarning(line As Integer, col As Integer, msg As String)
            _errors.Add(New ValidationError With {
                .Line = line, .Col = col, .Message = msg, .Severity = "Warning"
            })
        End Sub

    End Class

