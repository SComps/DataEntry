' BuildRunner — shells out to "dotnet build" and captures output.
' Works on Windows, Linux, and macOS by calling dotnet directly (no shell wrapper).
Imports System.Diagnostics
Imports System.Text


    Public Class BuildResult
        Public Property Success As Boolean
        Public Property Output As String = ""
    End Class

    Public Class BuildRunner

        ''' <summary>
        ''' Run "dotnet build" on the project in outputDir.
        ''' stream = True  → write each line to Console.Out in real time (--build mode).
        ''' stream = False → collect all output and return it in BuildResult.Output.
        ''' </summary>
        Public Shared Function Build(outputDir As String,
                                     Optional stream As Boolean = False) As BuildResult

            Dim sb As New StringBuilder
            Dim psi As New ProcessStartInfo With {
                .FileName               = "dotnet",
                .Arguments              = $"build ""{outputDir}""",
                .RedirectStandardOutput = True,
                .RedirectStandardError  = True,
                .UseShellExecute        = False,
                .CreateNoWindow         = True
            }

            Using proc = New Process With {.StartInfo = psi}
                ' Wire up async output handlers so stdout and stderr don't deadlock
                AddHandler proc.OutputDataReceived, Sub(s, e)
                    If e.Data Is Nothing Then Return
                    If stream Then
                        Console.WriteLine(e.Data)
                    Else
                        sb.AppendLine(e.Data)
                    End If
                End Sub

                AddHandler proc.ErrorDataReceived, Sub(s, e)
                    If e.Data Is Nothing Then Return
                    If stream Then
                        Console.Error.WriteLine(e.Data)
                    Else
                        sb.AppendLine(e.Data)
                    End If
                End Sub

                proc.Start()
                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()
                proc.WaitForExit()

                Return New BuildResult With {
                    .Success = (proc.ExitCode = 0),
                    .Output  = sb.ToString()
                }
            End Using
        End Function

    End Class

