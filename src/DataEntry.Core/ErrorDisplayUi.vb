' ErrorDisplayUi — shows parse/validation errors in a Terminal.Gui window.
' ESC or Q closes the application.
Imports Terminal.Gui
Imports Terminal.Gui.Views
Imports Terminal.Gui.Input
Imports Terminal.Gui.App
Imports System.Collections.Generic
Imports System.Collections.ObjectModel
Imports TDim = Terminal.Gui.ViewBase.Dim
Imports TPos = Terminal.Gui.ViewBase.Pos


    Public Class ErrorDisplayUi

        Private ReadOnly _parseErrors As List(Of ParseError)
        Private ReadOnly _validErrors As List(Of ValidationError)

        Public Sub New(parseErrors As List(Of ParseError),
                       validErrors As List(Of ValidationError))
            _parseErrors = parseErrors
            _validErrors = validErrors
        End Sub

        Public Sub Run()
            Dim app = Application.Create().Init()

            Dim win As New Window()
            win.Title  = "DataEntry Compiler — Errors"
            win.X      = 0
            win.Y      = 0
            win.Width  = TDim.Fill()
            win.Height = TDim.Fill()

            ' Build the error list as strings
            Dim items As New ObservableCollection(Of String)
            For Each e In _parseErrors
                items.Add($"[{e.Severity,-7}] Line {e.Line}: {e.Message}")
            Next
            For Each e In _validErrors
                items.Add($"[{e.Severity,-7}] Line {e.Line}: {e.Message}")
            Next

            Dim header As New Label()
            header.Text  = $"{items.Count} issue(s) found.  Press Q or ESC to exit."
            header.X     = TPos.Absolute(1)
            header.Y     = TPos.Absolute(0)
            header.Width = TDim.Fill()

            Dim listView As New ListView()
            listView.X        = TPos.Absolute(1)
            listView.Y        = TPos.Absolute(2)
            listView.Width    = TDim.Fill(TDim.Absolute(1))
            listView.Height   = TDim.Fill(TDim.Absolute(1))
            listView.CanFocus = True
            listView.Source   = New ListWrapper(Of String)(items)

            win.Add(header, listView)

            ' Q or ESC quits
            AddHandler win.KeyDown, Sub(sender As Object, e As Key)
                If e = Key.Esc OrElse e = Key.Q Then
                    app.RequestStop()
                End If
            End Sub

            app.Run(win, Nothing)
            app.Dispose()
        End Sub

    End Class

