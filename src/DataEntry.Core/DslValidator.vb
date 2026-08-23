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

        Private ReadOnly _doc As DslDocument
        Private ReadOnly _errors As New List(Of ValidationError)

        Public Sub New(doc As DslDocument)
            _doc = doc
        End Sub

        Public Function Validate() As List(Of ValidationError)
            _errors.Clear()
            CheckDataSection()
            CheckScreenSections()
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
            Dim pos = 1   ' current byte position for implicit start tracking

            For Each fld In rec.Fields
                If Not fieldNames.Add(fld.Name) Then
                    AddError(fld.Line, 0, $"Duplicate field name '{fld.Name}' in record '{rec.Name}'.")
                End If

                If fld.Len <= 0 Then
                    AddError(fld.Line, 0, $"Field '{fld.Name}' in record '{rec.Name}' has no LEN specified.")
                    Continue For
                End If

                ' Resolve start position
                Dim startPos = If(fld.Start > 0, fld.Start, pos)

                ' Check boundary
                If startPos + fld.Len - 1 > lrecl Then
                    AddError(fld.Line, 0,
                        $"Field '{fld.Name}' in record '{rec.Name}' " &
                        $"(START={startPos} LEN={fld.Len}) exceeds LRECL={lrecl}.")
                End If

                ' Check FORMAT mask length matches LEN
                Dim fmt = fld.Format
                If Not String.IsNullOrEmpty(fmt.Raw) AndAlso fmt.Tokens.Count <> fld.Len Then
                    AddWarning(fld.Line, 0,
                        $"Field '{fld.Name}': FORMAT mask length ({fmt.Tokens.Count}) " &
                        $"does not match LEN={fld.Len}.")
                End If

                pos = startPos + fld.Len   ' advance implicit position
            Next
        End Sub

        ' ── SCREEN-SECTION checks ─────────────────────────────────────────────

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

                For Each sfld In scr.Fields
                    ' INTO cross-reference
                    If Not String.IsNullOrEmpty(sfld.IntoRecord) Then
                        If Not recordMap.ContainsKey(sfld.IntoRecord) Then
                            AddError(sfld.Line, 0,
                                $"Field '{sfld.Label}' references unknown record '{sfld.IntoRecord}'.")
                        ElseIf Not recordMap(sfld.IntoRecord).Contains(sfld.IntoField) Then
                            AddError(sfld.Line, 0,
                                $"Field '{sfld.Label}' references unknown field " &
                                $"'{sfld.IntoRecord}.{sfld.IntoField}'.")
                        End If
                    End If

                    ' VALIDATE WITH — warn only, the function is user-supplied
                    If Not String.IsNullOrEmpty(sfld.ValidateFunc) Then
                        AddWarning(sfld.Line, 0,
                            $"VALIDATE WITH '{sfld.ValidateFunc}' — ensure this function is defined " &
                            "in the generated code.", "Warning")
                    End If

                    ' Color name validation
                    CheckColorSpec(sfld.Line, "NORMAL", sfld.NormalColor)
                    CheckColorSpec(sfld.Line, "FOCUS", sfld.FocusColor)
                    CheckColorSpec(sfld.Line, "ERROR", sfld.ErrorColor)
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

        Private Shared ReadOnly ValidColors As New HashSet(Of String)(StringComparer.OrdinalIgnoreCase) From {
            "Black", "DarkRed", "DarkGreen", "DarkYellow", "DarkBlue",
            "DarkMagenta", "DarkCyan", "Gray", "DarkGray",
            "Red", "Green", "Yellow", "Blue", "Magenta", "Cyan", "White"
        }

        Private Shared Function IsValidColorName(name As String) As Boolean
            If String.IsNullOrEmpty(name) Then Return True  ' empty = use default
            Return ValidColors.Contains(name)
        End Function

        ' ── Helpers ───────────────────────────────────────────────────────────

        Private Sub AddError(line As Integer, col As Integer, msg As String)
            _errors.Add(New ValidationError With {
                .Line = line, .Col = col, .Message = msg, .Severity = "Error"
            })
        End Sub

        Private Sub AddWarning(line As Integer, col As Integer, msg As String,
                               Optional severity As String = "Warning")
            _errors.Add(New ValidationError With {
                .Line = line, .Col = col, .Message = msg, .Severity = severity
            })
        End Sub

    End Class

