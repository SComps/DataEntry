' BuildRunner — shells out to "dotnet publish" (self-contained single-file) and captures output.
' Works on Windows, Linux, and macOS by calling dotnet directly (no shell wrapper).
Imports System.Diagnostics
Imports System.IO
Imports System.Runtime.InteropServices
Imports System.Text


    Public Class BuildResult
        Public Property Success As Boolean
        Public Property Output As String = ""
        Public Property PublishDir As String = ""  ' where the EXE was written
    End Class

    Public Class BuildRunner

        ''' <summary>
        ''' Determine the dotnet runtime identifier (RID) for the current OS and architecture.
        ''' A RID is required for self-contained publish to produce a native EXE.
        ''' </summary>
        Private Shared Function CurrentRid() As String
            Dim arch = RuntimeInformation.ProcessArchitecture
            Dim archSuffix = If(arch = Architecture.Arm64, "arm64", "x64")

            If RuntimeInformation.IsOSPlatform(OSPlatform.Windows) Then
                Return $"win-{archSuffix}"
            ElseIf RuntimeInformation.IsOSPlatform(OSPlatform.OSX) Then
                Return $"osx-{archSuffix}"
            Else
                Return $"linux-{archSuffix}"
            End If
        End Function

        ''' <summary>
        ''' Run "dotnet publish" (self-contained single-file EXE) on the project in projectDir.
        ''' The finished EXE is written to projectDir\publish\.
        ''' stream = True  → write each line to Console.Out in real time (--build mode).
        ''' stream = False → collect all output and return it in BuildResult.Output.
        ''' </summary>
        ''' <summary>Timeout in milliseconds for dotnet publish (5 minutes).</summary>
        Private Const BuildTimeoutMs As Integer = 300_000

        Public Shared Function Build(projectDir As String,
                                     Optional stream As Boolean = False) As BuildResult

            Dim rid        = CurrentRid()
            Dim publishDir = IO.Path.Combine(projectDir, "publish")
            Dim sb         As New StringBuilder

            Dim psi As New ProcessStartInfo With {
                .FileName               = "dotnet",
                .Arguments              = $"publish ""{projectDir}"" --configuration Release --runtime {rid} --self-contained true -p:PublishSingleFile=true --output ""{publishDir}""",
                .RedirectStandardOutput = True,
                .RedirectStandardError  = True,
                .UseShellExecute        = False,
                .CreateNoWindow         = True
            }

            Dim proc As Process
            Try
                proc = New Process With {.StartInfo = psi}
            Catch ex As Exception
                Return New BuildResult With {
                    .Success    = False,
                    .Output     = $"Failed to create process: {ex.Message}",
                    .PublishDir = publishDir
                }
            End Try

            Using proc
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

                Try
                    proc.Start()
                Catch ex As Exception
                    Return New BuildResult With {
                        .Success    = False,
                        .Output     = $"Could not start 'dotnet': {ex.Message}. Ensure the .NET SDK is installed and on PATH.",
                        .PublishDir = publishDir
                    }
                End Try

                proc.BeginOutputReadLine()
                proc.BeginErrorReadLine()

                Dim exited = proc.WaitForExit(BuildTimeoutMs)
                If Not exited Then
                    Try
                        proc.Kill(entireProcessTree:=True)
                    Catch
                        ' Ignore kill errors — process may have already exited
                    End Try
                    Return New BuildResult With {
                        .Success    = False,
                        .Output     = $"Build timed out after {BuildTimeoutMs \ 60_000} minutes and was cancelled.",
                        .PublishDir = publishDir
                    }
                End If

                Return New BuildResult With {
                    .Success    = (proc.ExitCode = 0),
                    .Output     = sb.ToString(),
                    .PublishDir = publishDir
                }
            End Using
        End Function

    End Class

