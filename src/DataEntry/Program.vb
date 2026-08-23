' Program.vb — CLI entry point for the DataEntry compiler.
'
' Usage:
'   dataentry                              → file-open dialog, then preview
'   dataentry myform.def                   → parse, show preview or errors
'   dataentry myform.def --build           → generate + dotnet build (no UI)
'   dataentry myform.def --build --output ./out
'
Imports System.IO
Imports DataEntry
Imports Terminal.Gui
Imports Terminal.Gui.App
Imports Terminal.Gui.Views

Namespace DataEntry

    Module Program

        Sub Main(args As String())
            ' ── Parse command-line arguments ──────────────────────────────────
            Dim defFile As String = ""
            Dim doBuild As Boolean = False
            Dim outputDir As String = ""

            Dim i = 0
            Do While i < args.Length
                Select Case args(i).ToLowerInvariant()
                    Case "--build"
                        doBuild = True
                    Case "--output"
                        i += 1
                        If i < args.Length Then outputDir = args(i)
                    Case Else
                        If Not args(i).StartsWith("--") Then defFile = args(i)
                End Select
                i += 1
            Loop

            ' ── If no file given and not in --build mode, show file-open dialog ──
            If String.IsNullOrEmpty(defFile) AndAlso Not doBuild Then
                defFile = ShowOpenDialog()
                If String.IsNullOrEmpty(defFile) Then Return  ' user cancelled
            End If

            ' ── Load and parse the DSL file ───────────────────────────────────
            If Not File.Exists(defFile) Then
                Console.Error.WriteLine($"File not found: {defFile}")
                Environment.Exit(1)
            End If

            Dim src = File.ReadAllText(defFile)
            Dim lexer As New DslLexer(src)
            Dim parser As New DslParser(lexer.Tokenize())
            Dim doc = parser.Parse()

            Dim validator As New DslValidator(doc)
            Dim valErrors = validator.Validate()

            Dim hasErrors = parser.Errors.Count > 0 OrElse
                            valErrors.Exists(Function(e) e.Severity = "Error")

            ' ── --build mode: generate + compile then exit (no interactive UI) ─
            If doBuild Then
                If hasErrors Then
                    For Each e In parser.Errors
                        Console.Error.WriteLine($"[{e.Severity}] Line {e.Line}: {e.Message}")
                    Next
                    For Each e In valErrors
                        Console.Error.WriteLine($"[{e.Severity}] Line {e.Line}: {e.Message}")
                    Next
                    Environment.Exit(1)
                End If

                ' Default output dir: subfolder named after the def file (without extension)
                If String.IsNullOrEmpty(outputDir) Then
                    outputDir = Path.Combine(
                        Path.GetDirectoryName(Path.GetFullPath(defFile)),
                        Path.GetFileNameWithoutExtension(defFile))
                End If

                Console.WriteLine($"Generating project in: {outputDir}")
                Dim gen As New CodeGenerator()
                gen.GenerateProject(doc, outputDir)

                Console.WriteLine("Running dotnet publish (AOT self-contained)…")
                Dim result = BuildRunner.Build(outputDir, stream:=True)
                Environment.Exit(If(result.Success, 0, 1))
            End If

            ' ── Interactive mode: show errors or preview ───────────────────────
            If hasErrors Then
                Dim errUi As New ErrorDisplayUi(parser.Errors, valErrors)
                errUi.Run()
            Else
                If String.IsNullOrEmpty(outputDir) Then
                    outputDir = Path.Combine(
                        Path.GetDirectoryName(Path.GetFullPath(defFile)),
                        Path.GetFileNameWithoutExtension(defFile))
                End If
                Dim prev As New PreviewUi(doc, defFile, outputDir)
                prev.Run()
            End If
        End Sub

        ''' <summary>Show a Terminal.Gui file-open dialog and return the chosen path (or "").</summary>
        Private Function ShowOpenDialog() As String
            Dim chosen As String = ""
            Dim app = Application.Create().Init()
            Dim dlg As New OpenDialog()
            dlg.Title = "Open DSL Definition File"
            app.Run(dlg, Nothing)
            If dlg.FilePaths IsNot Nothing AndAlso dlg.FilePaths.Count > 0 Then
                chosen = dlg.FilePaths(0)
            End If
            app.Dispose()
            Return chosen
        End Function

    End Module

End Namespace
