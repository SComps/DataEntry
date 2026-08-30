' PreviewUi — renders the DSL-defined form live in Terminal.Gui for testing.
' No file I/O occurs here.  F10 = Compile & Build, F3 = Cancel, Right-Ctrl = Save (preview).
' Shift-PgUp/PgDn/Home/End simulate record navigation (preview messages only).
Imports Terminal.Gui
Imports Terminal.Gui.Views
Imports Terminal.Gui.Input
Imports Terminal.Gui.App
Imports Terminal.Gui.Drawing
Imports Terminal.Gui.ViewBase
Imports System.Collections.Generic
Imports System.IO
Imports TDim = Terminal.Gui.ViewBase.Dim
Imports TPos = Terminal.Gui.ViewBase.Pos


    Public Class PreviewUi

        Private ReadOnly _doc As DslDocument
        Private ReadOnly _defFile As String
        Private ReadOnly _outputDir As String

        Private _screens As List(Of ScreenSection)
        Private _screenIndex As Integer = 0
        Private _app As IApplication
        Private _menuBar As MenuBar

        Public Sub New(doc As DslDocument, defFile As String, outputDir As String)
            _doc = doc
            _defFile = defFile
            _outputDir = outputDir
            _screens = doc.Screens
        End Sub

        Public Sub Run()
            _app = Application.Create().Init()
            ' Loop instead of recursing — each screen switch calls RequestStop(),
            ' which returns from _app.Run(); we dispose the old window and show the next.
            Dim keepGoing As Boolean = True
            Do While keepGoing
                Dim win As New Window()
                Try
                    ShowCurrentScreen(win)
                    _app.Run(win, Nothing)
                Finally
                    RemoveHandler win.KeyDown, AddressOf OnKeyDown
                    win.Dispose()
                End Try
                ' _nextAction is set by key/menu handlers before RequestStop().
                Select Case _nextAction
                    Case NextAction.PrevScreen
                        _screenIndex -= 1
                        _nextAction = NextAction.None
                    Case NextAction.NextScreen
                        _screenIndex += 1
                        _nextAction = NextAction.None
                    Case Else
                        keepGoing = False
                End Select
            Loop
            _app.Dispose()
        End Sub

        Private Enum NextAction
            None
            PrevScreen
            NextScreen
        End Enum
        Private _nextAction As NextAction = NextAction.None

        ' ── Screen rendering ──────────────────────────────────────────────────

        Private Sub ShowCurrentScreen(win As Window)
            Dim scr = If(_screens.Count > 0, _screens(_screenIndex), Nothing)
            win.Title  = If(scr IsNot Nothing, $"Preview: {scr.Name}", "Preview — no screens defined")
            win.X      = 0
            win.Y      = 0
            win.Width  = TDim.Fill()
            win.Height = TDim.Fill()

            If scr IsNot Nothing Then
                win.SetScheme(ColorHelper.MakeScreenScheme(scr.DefaultColor))
            End If

            ' Menu bar: File > Open | Compile & Build | Quit
            Dim openItem    As New MenuItem("_Open…",           "", AddressOf MenuOpen)
            Dim buildItem   As New MenuItem("_Compile && Build","", AddressOf MenuBuild)
            Dim quitItem    As New MenuItem("_Quit",            "", Sub() _app.RequestStop())
            Dim fileMenu    As New MenuBarItem("_File", New MenuItem() {openItem, buildItem, quitItem})
            _menuBar        =  New MenuBar(New MenuBarItem() {fileMenu})
            win.Add(_menuBar)

            If scr IsNot Nothing Then
                BuildFields(win, scr, _doc)
            End If

            AddHandler win.KeyDown, AddressOf OnKeyDown
        End Sub

        Private _allFields As New List(Of TextField)

        Private Sub BuildFields(win As Window, scr As ScreenSection, doc As DslDocument)
            _allFields.Clear()

            ' Render standalone PROMPT/LABEL elements
            For Each pr In scr.Prompts
                Dim plbl As New Label()
                plbl.Text  = pr.Text
                plbl.X     = TPos.Absolute(pr.Col - 1)
                plbl.Y     = TPos.Absolute(pr.Row)
                plbl.Width = TDim.Auto()
                If pr.Color IsNot Nothing Then
                    plbl.SetScheme(ColorHelper.MakeScreenScheme(pr.Color))
                End If
                win.Add(plbl)
            Next

            ' Render FIELD elements
            For i = 0 To scr.Fields.Count - 1
                Dim sfld = scr.Fields(i)
                Dim isLastField = (i = scr.Fields.Count - 1)

                Dim tf_x As Integer, tf_y As Integer
                If Not String.IsNullOrEmpty(sfld.Label) Then
                    Dim pRow = If(sfld.PromptRow <> -1, sfld.PromptRow, sfld.Row)
                    Dim pCol = If(sfld.PromptCol <> -1, sfld.PromptCol, sfld.Col)
                    Dim lbl As New Label()
                    lbl.Text  = sfld.Label & ":"
                    lbl.X     = TPos.Absolute(pCol - 1)
                    lbl.Y     = TPos.Absolute(pRow)
                    lbl.Width = TDim.Auto()
                    win.Add(lbl)

                    If sfld.PromptRow <> -1 AndAlso sfld.PromptCol <> -1 Then
                        tf_x = sfld.Col - 1
                        tf_y = sfld.Row
                    Else
                        tf_x = sfld.Col - 1 + sfld.Label.Length + 2
                        tf_y = sfld.Row
                    End If
                Else
                    tf_x = sfld.Col - 1
                    tf_y = sfld.Row
                End If

                Dim tf As New TextField()
                tf.X     = TPos.Absolute(tf_x)
                tf.Y     = TPos.Absolute(tf_y)
                tf.Width = TDim.Absolute(sfld.Len)
                tf.Text  = ""
                tf.SetScheme(ColorHelper.MakeFieldScheme(
                    sfld.NormalColor, sfld.FocusColor, sfld.ErrorColor, scr.DefaultColor))

                _allFields.Add(tf)
                AddHandler tf.KeyDown, AddressOf OnKeyDown

                ' Enforce max length — cancel the edit entirely when full so Terminal.Gui
                ' never advances its internal ScrollOffset (which would shift text left).
                Dim maxLen = sfld.Len
                AddHandler tf.TextChanging, Sub(sender As Object, ev As ResultEventArgs(Of String))
                    If ev.Result IsNot Nothing AndAlso ev.Result.Length > maxLen Then
                        ev.Result = DirectCast(sender, TextField).Text  ' cancel — keeps scroll offset stable
                    End If
                End Sub

                If sfld.Full = FullBehavior.Stay Then
                    ' FULL=STAY — hold cursor at end, inhibit auto-advance/auto-save
                    AddHandler tf.TextChanged, Sub(sender As Object, ev As EventArgs)
                        Dim field = DirectCast(sender, TextField)
                        If field.HasFocus AndAlso field.Text IsNot Nothing AndAlso field.Text.Length = maxLen Then
                            field.InsertionPoint = maxLen - 1  ' pin cursor at end, no advance
                        End If
                    End Sub
                    AddHandler tf.KeyDown, Sub(sender As Object, ev As Key)
                        If ev = Key.Enter Then
                            Dim field = DirectCast(sender, TextField)
                            field.SuperView?.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop)
                            ev.Handled = True
                        End If
                    End Sub
                ElseIf isLastField Then
                    ' FULL=ADVANCE, last field — auto-save in preview
                    AddHandler tf.TextChanged, Sub(sender As Object, ev As EventArgs)
                        Dim field = DirectCast(sender, TextField)
                        If field.HasFocus AndAlso field.Text IsNot Nothing AndAlso field.Text.Length = maxLen Then
                            field.InsertionPoint = 0
                            field.InsertionPoint = maxLen - 1
                            MessageBox.Query(_app, "Save",
                                "Record saved. (preview only — no file was written)", "OK")
                            ClearPreviewFields()
                        End If
                    End Sub
                    AddHandler tf.KeyDown, Sub(sender As Object, ev As Key)
                        If ev = Key.Enter Then
                            MessageBox.Query(_app, "Save",
                                "Record saved. (preview only — no file was written)", "OK")
                            ClearPreviewFields()
                            ev.Handled = True
                        End If
                    End Sub
                Else
                    ' FULL=ADVANCE, intermediate field — auto-advance to next field
                    AddHandler tf.TextChanged, Sub(sender As Object, ev As EventArgs)
                        Dim field = DirectCast(sender, TextField)
                        If field.HasFocus AndAlso field.Text IsNot Nothing AndAlso field.Text.Length = maxLen Then
                            field.InsertionPoint = 0
                            field.SuperView?.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop)
                        End If
                    End Sub
                    AddHandler tf.KeyDown, Sub(sender As Object, ev As Key)
                        If ev = Key.Enter Then
                            Dim field = DirectCast(sender, TextField)
                            field.SuperView?.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop)
                            ev.Handled = True
                        End If
                    End Sub
                End If

                win.Add(tf)

                ' Auto format hint — shown to the right of the field in muted colour when the
                ' mask contains embedded literals (e.g. ###-###-#### for a phone mask).
                ' Look up the format tokens from the RECORD field definition.
                Dim hintTokens As New List(Of MaskToken)
                For Each r In doc.Data.Records
                    If String.Equals(r.Name, sfld.IntoRecord, StringComparison.OrdinalIgnoreCase) Then
                        For Each f In r.Fields
                            If String.Equals(f.Name, sfld.IntoField, StringComparison.OrdinalIgnoreCase) Then
                                hintTokens = f.Format.Tokens
                            End If
                        Next
                    End If
                Next
                Dim hint = FormatMask.FormatHint(hintTokens)
                If hint.Length > 0 Then
                    Dim hintLbl As New Label()
                    hintLbl.Text  = hint
                    hintLbl.X     = TPos.Absolute(tf_x + sfld.Len + 1)
                    hintLbl.Y     = TPos.Absolute(tf_y)
                    hintLbl.Width = TDim.Auto()
                    hintLbl.SetScheme(ColorHelper.MakeScreenScheme(
                        New ColorSpec With {.Fg = "DarkGray", .Bg = scr.DefaultColor.Bg}))
                    win.Add(hintLbl)
                End If
            Next
        End Sub

        ' ── Key handling ──────────────────────────────────────────────────────

        Private Sub OnKeyDown(sender As Object, e As Key)
            ' F9 — activate the menu bar (fallback for macOS/QEMU where Alt is not delivered)
            If e = Key.F9 Then
                _menuBar.SetFocus()
                e.Handled = True
                Return
            End If

            If e = Key.F1 Then
                ShowHelp()
                e.Handled = True
                Return
            End If

            If e = Key.F3 Then
                MessageBox.Query(_app, "Cancel",
                    "Cancel data entry? (preview only — no data was written)", "OK")
                ClearPreviewFields()
                e.Handled = True
                Return
            End If

            If e = Key.F10 Then
                MenuBuild()
                e.Handled = True
                Return
            End If

            If e = Key.PageUp AndAlso Not e.IsShift Then
                If _screenIndex > 0 Then
                    _nextAction = NextAction.PrevScreen
                    _app.RequestStop()
                End If
                e.Handled = True
                Return
            End If

            If e = Key.PageDown AndAlso Not e.IsShift Then
                If _screenIndex < _screens.Count - 1 Then
                    _nextAction = NextAction.NextScreen
                    _app.RequestStop()
                End If
                e.Handled = True
                Return
            End If

            ' Ctrl+S = Save (preview message)
            If e.IsCtrl AndAlso (e.NoCtrl = Key.S OrElse e.NoCtrl = Key.s) Then
                MessageBox.Query(_app, "Save",
                    "Record saved. (preview only — no file was written)", "OK")
                ClearPreviewFields()
                e.Handled = True
                Return
            End If

            ' Shift + navigation keys = record navigation
            If e.IsShift Then
                Dim base_ = e.NoShift
                If base_ = Key.PageUp Then
                    MessageBox.Query(_app, "Records", "Previous record (preview — no data file open)", "OK")
                    e.Handled = True
                ElseIf base_ = Key.PageDown Then
                    MessageBox.Query(_app, "Records", "Next record (preview — no data file open)", "OK")
                    e.Handled = True
                ElseIf base_ = Key.Home Then
                    MessageBox.Query(_app, "Records", "First record (preview — no data file open)", "OK")
                    e.Handled = True
                ElseIf base_ = Key.End Then
                    MessageBox.Query(_app, "Records", "Last record (preview — no data file open)", "OK")
                    e.Handled = True
                End If
            End If
        End Sub

        Private Sub ClearPreviewFields()
            For Each tf In _allFields
                tf.Text = ""
            Next
            If _allFields.Count > 0 Then _allFields(0).SetFocus()
        End Sub

        ' ── Menu actions ──────────────────────────────────────────────────────

        Private Sub MenuOpen()
            Dim dlg As New OpenDialog()
            dlg.Title = "Open DSL File"
            dlg.OpenMode = OpenMode.File
            If Not String.IsNullOrEmpty(_defFile) AndAlso File.Exists(_defFile) Then
                dlg.Path = IO.Path.GetDirectoryName(IO.Path.GetFullPath(_defFile))
            Else
                dlg.Path = Directory.GetCurrentDirectory()
            End If
            dlg.AllowedTypes.Add(New AllowedType("DSL Definition Files (*.def)", ".def"))
            dlg.AllowedTypes.Add(New AllowedType("All Files (*.*)", ".*"))
            _app.Run(dlg, Nothing)

            If dlg.FilePaths IsNot Nothing AndAlso dlg.FilePaths.Count > 0 Then
                Dim filePath = dlg.FilePaths(0)
                If File.Exists(filePath) Then
                    Dim src As String
                    Try
                        src = File.ReadAllText(filePath)
                    Catch ex As Exception
                        MessageBox.Query(_app, "Error", $"Cannot read file:{Environment.NewLine}{ex.Message}", "OK")
                        Return
                    End Try
                    Dim lexer As New DslLexer(src)
                    Dim parser As New DslParser(lexer.Tokenize())
                    Dim newDoc = parser.Parse()
                    Dim validator As New DslValidator(newDoc)
                    Dim valErrs = validator.Validate()

                    ' Signal the Run() loop to exit cleanly, then launch the new UI
                    ' after the current app has fully stopped — no nested IApplication.
                    _nextAction = NextAction.None
                    _app.RequestStop()

                    If parser.Errors.Count > 0 OrElse valErrs.Exists(Function(x) x.Severity = "Error") Then
                        Dim errUi As New ErrorDisplayUi(parser.Errors, valErrs)
                        errUi.Run()
                    Else
                        Dim prev As New PreviewUi(newDoc, filePath, _outputDir)
                        prev.Run()
                    End If
                End If
            End If
        End Sub

        Private Sub MenuBuild()
            If String.IsNullOrEmpty(_outputDir) Then
                MessageBox.Query(_app, "Build",
                    "No output directory specified. Re-run with --output <dir> to enable code generation.", "OK")
                Return
            End If

            Dim gen As New CodeGenerator()
            gen.GenerateProject(_doc, _outputDir)

            Dim result = BuildRunner.Build(_outputDir, stream:=False)

            Dim msg As String
            If result.Success Then
                msg = $"Publish succeeded.{Environment.NewLine}{Environment.NewLine}" &
                      $"Self-contained EXE written to:{Environment.NewLine}{result.PublishDir}"
            Else
                msg = $"Publish FAILED — see output below.{Environment.NewLine}{Environment.NewLine}{result.Output}"
            End If

            MessageBox.Query(_app, "Publish Result", msg, "Close")
        End Sub

        Private Sub ShowHelp()
            Dim helpMsg = "Keyboard Shortcuts & Commands (Preview Mode):" & vbCrLf & vbCrLf &
                          "  Enter / Tab      - Move to next field" & vbCrLf &
                          "  Shift+Tab        - Move to previous field" & vbCrLf &
                          "  Ctrl+S           - Save record (preview mode)" & vbCrLf &
                          "  F1               - Show this Help screen" & vbCrLf &
                          "  F3               - Cancel / Clear fields" & vbCrLf &
                          "  F9               - Open File menu (Alt+F on most platforms)" & vbCrLf &
                          "  F10              - Compile & Build application" & vbCrLf &
                          "  PageUp / PageDown- Switch screen section" & vbCrLf &
                          "  Shift + PageUp   - Simulate previous record" & vbCrLf &
                          "  Shift + PageDown - Simulate next record" & vbCrLf &
                          "  Shift + Home     - Simulate first record" & vbCrLf &
                          "  Shift + End      - Simulate last record"
            MessageBox.Query(_app, "Help — Commands & Hotkeys", helpMsg, "OK")
        End Sub

    End Class

