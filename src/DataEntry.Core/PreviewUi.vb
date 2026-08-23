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

        Public Sub New(doc As DslDocument, defFile As String, outputDir As String)
            _doc = doc
            _defFile = defFile
            _outputDir = outputDir
            _screens = doc.Screens
        End Sub

        Public Sub Run()
            _app = Application.Create().Init()
            ShowCurrentScreen()
            _app.Dispose()
        End Sub

        ' ── Screen rendering ──────────────────────────────────────────────────

        Private Sub ShowCurrentScreen()
            Dim scr = If(_screens.Count > 0, _screens(_screenIndex), Nothing)
            Dim title = If(scr IsNot Nothing, $"Preview: {scr.Name}", "Preview — no screens defined")

            Dim win As New Window()
            win.Title  = title
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
            Dim menuBar     As New MenuBar(New MenuBarItem() {fileMenu})
            win.Add(menuBar)

            If scr IsNot Nothing Then
                BuildFields(win, scr)
            End If

            AddHandler win.KeyDown, AddressOf OnKeyDown

            _app.Run(win, Nothing)
        End Sub

        Private Sub BuildFields(win As Window, scr As ScreenSection)
            For Each sfld In scr.Fields
                Dim lbl As New Label()
                lbl.Text  = sfld.Label & ":"
                lbl.X     = TPos.Absolute(sfld.Col - 1)
                lbl.Y     = TPos.Absolute(sfld.Row)   ' row 1-based; +0 because row 1 = Y=1 (menubar at Y=0)
                lbl.Width = TDim.Absolute(sfld.Label.Length + 1)

                Dim tf As New TextField()
                tf.X     = TPos.Absolute(sfld.Col - 1 + sfld.Label.Length + 2)
                tf.Y     = TPos.Absolute(sfld.Row)
                tf.Width = TDim.Absolute(sfld.Len)
                tf.Text  = ""
                tf.SetScheme(ColorHelper.MakeFieldScheme(
                    sfld.NormalColor, sfld.FocusColor, sfld.ErrorColor, scr.DefaultColor))

                ' Enforce max length and auto-advance to next field
                Dim maxLen = sfld.Len
                AddHandler tf.TextChanging, Sub(sender As Object, ev As ResultEventArgs(Of String))
                    If ev.Result IsNot Nothing AndAlso ev.Result.Length > maxLen Then
                        ev.Result = ev.Result.Substring(0, maxLen)
                    End If
                End Sub

                AddHandler tf.TextChanged, Sub(sender As Object, ev As EventArgs)
                    Dim field = DirectCast(sender, TextField)
                    If field.HasFocus AndAlso field.Text IsNot Nothing AndAlso field.Text.Length = maxLen Then
                        field.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop)
                    End If
                End Sub

                win.Add(lbl, tf)
            Next
        End Sub

        ' ── Key handling ──────────────────────────────────────────────────────

        Private Sub OnKeyDown(sender As Object, e As Key)
            If e = Key.F3 Then
                MessageBox.Query(_app, "Cancel",
                    "Cancel data entry? (preview only — no data was written)", "OK")
                Return
            End If

            If e = Key.F10 Then
                MenuBuild()
                Return
            End If

            If e = Key.PageUp AndAlso Not e.IsShift Then
                If _screenIndex > 0 Then
                    _screenIndex -= 1
                    _app.RequestStop()
                    ShowCurrentScreen()
                End If
                Return
            End If

            If e = Key.PageDown AndAlso Not e.IsShift Then
                If _screenIndex < _screens.Count - 1 Then
                    _screenIndex += 1
                    _app.RequestStop()
                    ShowCurrentScreen()
                End If
                Return
            End If

            ' Right Ctrl (alone) or Ctrl+S = Save (preview only — no file is written)
            If e.IsCtrl AndAlso (e.NoCtrl = Key.Empty OrElse e.NoCtrl = Key.S) Then
                MessageBox.Query(_app, "Save",
                    "Record saved. (preview only — no file was written)", "OK")
                Return
            End If

            ' Shift + navigation keys = record navigation
            If e.IsShift Then
                Dim base_ = e.NoShift
                If base_ = Key.PageUp Then
                    MessageBox.Query(_app, "Records", "Previous record (preview — no data file open)", "OK")
                ElseIf base_ = Key.PageDown Then
                    MessageBox.Query(_app, "Records", "Next record (preview — no data file open)", "OK")
                ElseIf base_ = Key.Home Then
                    MessageBox.Query(_app, "Records", "First record (preview — no data file open)", "OK")
                ElseIf base_ = Key.End Then
                    MessageBox.Query(_app, "Records", "Last record (preview — no data file open)", "OK")
                End If
            End If
        End Sub

        ' ── Menu actions ──────────────────────────────────────────────────────

        Private Sub MenuOpen()
            Dim dlg As New OpenDialog()
            dlg.Title = "Open DSL File"
            _app.Run(dlg, Nothing)

            If dlg.FilePaths IsNot Nothing AndAlso dlg.FilePaths.Count > 0 Then
                Dim path = dlg.FilePaths(0)
                If File.Exists(path) Then
                    Dim src = File.ReadAllText(path)
                    Dim lexer As New DslLexer(src)
                    Dim parser As New DslParser(lexer.Tokenize())
                    Dim newDoc = parser.Parse()
                    Dim validator As New DslValidator(newDoc)
                    Dim valErrs = validator.Validate()

                    _app.RequestStop()

                    If parser.Errors.Count > 0 OrElse valErrs.Exists(Function(x) x.Severity = "Error") Then
                        Dim errUi As New ErrorDisplayUi(parser.Errors, valErrs)
                        errUi.Run()
                    Else
                        Dim prev As New PreviewUi(newDoc, path, _outputDir)
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

    End Class

