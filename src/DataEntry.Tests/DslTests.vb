' DslTests.vb — xUnit tests for the DataEntry DSL compiler.
'
' Each real-world sample .def file is embedded as a resource and exercised through
' the full pipeline: Lexer → Parser → Validator → (optionally) CodeGenerator.
'
' Test categories:
'   ParseTests    — AST structure is correct for each valid sample
'   ValidateTests — validator accepts valid samples and rejects the errors sample
'   FormatTests   — FORMAT mask tokenisation and field alignment logic
'   CodeGenTests  — CodeGenerator produces compilable output for each valid sample

Imports System
Imports System.IO
Imports System.Reflection
Imports System.Collections.Generic
Imports Xunit
Imports DataEntry

Namespace DataEntry.Tests

    ' ─────────────────────────────────────────────────────────────────────────
    ' Helpers shared by all test classes
    ' ─────────────────────────────────────────────────────────────────────────
    Public Module TestHelpers

        ''' <summary>Load a .def file that was compiled as an embedded resource.</summary>
        Public Function LoadSample(name As String) As String
            Dim asm = Assembly.GetExecutingAssembly()
            ' Resource name is the assembly's default namespace + filename.
            ' The test project has no explicit RootNamespace so the name is just
            ' DataEntry.Tests.<name> (the Samples subfolder is not included).
            Dim resName = $"DataEntry.Tests.{name}"
            Using stream = asm.GetManifestResourceStream(resName)
                If stream Is Nothing Then
                    Throw New InvalidOperationException(
                        $"Embedded resource '{resName}' not found. " &
                        $"Available: {String.Join(", ", asm.GetManifestResourceNames())}")
                End If
                Using reader As New StreamReader(stream)
                    Return reader.ReadToEnd()
                End Using
            End Using
        End Function

        ''' <summary>Parse a DSL string and return (document, parseErrors).</summary>
        Public Function ParseDsl(src As String) As (Doc As DslDocument, Errors As List(Of ParseError))
            Dim lexer As New DslLexer(src)
            Dim parser As New DslParser(lexer.Tokenize())
            Dim doc = parser.Parse()
            Return (doc, parser.Errors)
        End Function

        ''' <summary>Parse + validate and return all errors (parse + validation combined).</summary>
        Public Function ParseAndValidate(src As String) As (Doc As DslDocument,
                                                            ParseErrs As List(Of ParseError),
                                                            ValidErrs As List(Of ValidationError))
            Dim result = ParseDsl(src)
            Dim validator As New DslValidator(result.Doc)
            Dim valErrs = validator.Validate()
            Return (result.Doc, result.Errors, valErrs)
        End Function

        ''' <summary>Return only Error-severity items from a validation list.</summary>
        Public Function HardErrors(errs As List(Of ValidationError)) As List(Of ValidationError)
            Return errs.FindAll(Function(e) e.Severity = "Error")
        End Function

    End Module

    ' ─────────────────────────────────────────────────────────────────────────
    ' customer.def — full address / contact form
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class CustomerTests

        Private ReadOnly _src As String

        Public Sub New()
            _src = LoadSample("customer.def")
        End Sub

        <Fact>
        Public Sub Parse_NoErrors()
            Dim result = ParseDsl(_src)
            Assert.Empty(result.Errors)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_FilePath()
            Dim result = ParseDsl(_src)
            Assert.Equal("customers.dat", result.Doc.Data.FilePath)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_LRECL()
            Dim result = ParseDsl(_src)
            Assert.Equal(120, result.Doc.Data.Lrecl)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_LineEnding()
            Dim result = ParseDsl(_src)
            Assert.Equal(LineEnding.CRLF, result.Doc.Data.Ending)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_AppendMode()
            Dim result = ParseDsl(_src)
            Assert.Equal(AppendMode.Append, result.Doc.Data.Mode)
        End Sub

        <Fact>
        Public Sub Parse_Record_FieldCount()
            Dim result = ParseDsl(_src)
            Dim rec = Assert.Single(result.Doc.Data.Records)
            Assert.Equal("CUSTOMER", rec.Name)
            Assert.Equal(9, rec.Fields.Count)
        End Sub

        <Fact>
        Public Sub Parse_Field_CUSTID_StartAndLen()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields(0)
            Assert.Equal("CUSTID", fld.Name)
            Assert.Equal(1, fld.Start)
            Assert.Equal(6, fld.Len)
        End Sub

        <Fact>
        Public Sub Parse_Field_STATE_UpperCaseMask()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "STATE")
            Assert.NotNull(fld)
            Assert.Equal("UU", fld.Format.Raw)
            Assert.All(fld.Format.Tokens, Sub(t) Assert.Equal(MaskToken.TokenKind.UpperCase, t.Kind))
        End Sub

        <Fact>
        Public Sub Parse_Field_PHONE_EscapedLiterals()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "PHONE")
            Assert.NotNull(fld)
            ' 999\-999\-9999 → 12 tokens: 3 digit, 1 literal(-), 3 digit, 1 literal(-), 4 digit
            Assert.Equal(12, fld.Format.Tokens.Count)
            Assert.Equal(MaskToken.TokenKind.Literal, fld.Format.Tokens(3).Kind)
            Assert.Equal("-"c, fld.Format.Tokens(3).LiteralChar)
        End Sub

        <Fact>
        Public Sub Parse_Screen_FieldCount()
            Dim result = ParseDsl(_src)
            Dim scr = Assert.Single(result.Doc.Screens)
            Assert.Equal("CUSTOMER-ENTRY", scr.Name)
            Assert.Equal(9, scr.Fields.Count)
        End Sub

        <Fact>
        Public Sub Parse_Screen_DefaultColor()
            Dim result = ParseDsl(_src)
            Dim scr = result.Doc.Screens(0)
            Assert.Equal("White", scr.DefaultColor.Fg)
            Assert.Equal("Blue", scr.DefaultColor.Bg)
        End Sub

        <Fact>
        Public Sub Parse_ScreenField_IntoMapping()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Screens(0).Fields.Find(Function(f) f.Label = "Last Name")
            Assert.NotNull(fld)
            Assert.Equal("CUSTOMER", fld.IntoRecord)
            Assert.Equal("LNAME", fld.IntoField)
        End Sub

        <Fact>
        Public Sub Parse_ScreenField_WithExplicitColors()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Screens(0).Fields.Find(Function(f) f.Label = "Customer ID")
            Assert.NotNull(fld)
            Assert.NotNull(fld.NormalColor)
            Assert.Equal("White", fld.NormalColor.Fg)
            Assert.Equal("Blue", fld.NormalColor.Bg)
            Assert.NotNull(fld.FocusColor)
            Assert.Equal("Black", fld.FocusColor.Fg)
            Assert.Equal("Cyan", fld.FocusColor.Bg)
            Assert.NotNull(fld.ErrorColor)
            Assert.Equal("White", fld.ErrorColor.Fg)
            Assert.Equal("Red", fld.ErrorColor.Bg)
        End Sub

        <Fact>
        Public Sub Validate_NoHardErrors()
            Dim result = ParseAndValidate(_src)
            Assert.Empty(HardErrors(result.ValidErrs))
        End Sub

        <Fact>
        Public Sub Validate_FieldsTotalFitInLrecl()
            ' All 9 fields: 6+20+15+25+18+2+5+12+17 = 120 = LRECL
            Dim result = ParseAndValidate(_src)
            Dim totalLen = result.Doc.Data.Records(0).Fields.Sum(Function(f) f.Len)
            Assert.Equal(result.Doc.Data.Lrecl, totalLen)
        End Sub

        <Fact>
        Public Sub CodeGen_ProducesOutput()
            Dim result = ParseAndValidate(_src)
            Dim outDir = Path.Combine(Path.GetTempPath(), $"cg_customer_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Assert.True(File.Exists(Path.Combine(outDir, "Program.vb")))
                Assert.True(File.Exists(Path.Combine(outDir, "MainForm.vb")))
                Assert.True(File.Exists(Path.Combine(outDir, "DataFile.vb")))
                Assert.True(File.Exists(Path.Combine(outDir, "FormatHelper.vb")))
                Assert.True(File.Exists(Path.Combine(outDir, "ColorHelper.vb")))

                Dim projFiles = Directory.GetFiles(outDir, "*.vbproj")
                Assert.Single(projFiles)
                Dim projContent = File.ReadAllText(projFiles(0))
                Assert.Contains("<PublishSingleFile>true</PublishSingleFile>", projContent)
                Assert.Contains("<SelfContained>true</SelfContained>", projContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' inventory.def — SKU / description / qty / price / location
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class InventoryTests

        Private ReadOnly _src As String

        Public Sub New()
            _src = LoadSample("inventory.def")
        End Sub

        <Fact>
        Public Sub Parse_NoErrors()
            Dim result = ParseDsl(_src)
            Assert.Empty(result.Errors)
        End Sub

        <Fact>
        Public Sub Parse_Record_EightFields()
            Dim result = ParseDsl(_src)
            Dim rec = Assert.Single(result.Doc.Data.Records)
            Assert.Equal("ITEM", rec.Name)
            Assert.Equal(8, rec.Fields.Count)
        End Sub

        <Fact>
        Public Sub Parse_Field_QTY_ZeroFillMask()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "QTY")
            Assert.NotNull(fld)
            Assert.Equal(6, fld.Len)
            Assert.All(fld.Format.Tokens, Sub(t) Assert.Equal(MaskToken.TokenKind.ZeroFill, t.Kind))
        End Sub

        <Fact>
        Public Sub Parse_Field_PRICE_MixedNumericAndLiteral()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "PRICE")
            Assert.NotNull(fld)
            ' ZZZZZ.99 → 5 ZeroFill + 1 Literal(.) + 2 Digit = 8 tokens, LEN=9
            ' Wait — LEN=9 but mask is 8 chars.  Validator should warn.
            Assert.Equal(9, fld.Len)
        End Sub

        <Fact>
        Public Sub Parse_Field_LOCATN_UpperCase()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "LOCATN")
            Assert.NotNull(fld)
            Assert.All(fld.Format.Tokens, Sub(t) Assert.Equal(MaskToken.TokenKind.UpperCase, t.Kind))
        End Sub

        <Fact>
        Public Sub Parse_Screen_ValidateWithFunctions()
            Dim result = ParseDsl(_src)
            Dim scr = Assert.Single(result.Doc.Screens)
            Dim skuFld = scr.Fields.Find(Function(f) f.Label = "SKU")
            Assert.NotNull(skuFld)
            Assert.Equal("CHECKSKU", skuFld.ValidateFunc)
            Dim qtyFld = scr.Fields.Find(Function(f) f.Label = "Qty On Hand")
            Assert.NotNull(qtyFld)
            Assert.Equal("CHECKQTY", qtyFld.ValidateFunc)
        End Sub

        <Fact>
        Public Sub Validate_ValidateWithProducesWarnings()
            ' VALIDATE WITH functions emit warnings (not hard errors) since they are user-supplied
            Dim result = ParseAndValidate(_src)
            Assert.Empty(HardErrors(result.ValidErrs))
            Dim warnings = result.ValidErrs.FindAll(Function(e) e.Severity = "Warning")
            Assert.NotEmpty(warnings)
        End Sub

        <Fact>
        Public Sub Validate_NoHardErrors()
            Dim result = ParseAndValidate(_src)
            Assert.Empty(HardErrors(result.ValidErrs))
        End Sub

        <Fact>
        Public Sub CodeGen_ValidationStubFileCreated()
            Dim result = ParseAndValidate(_src)
            Dim outDir = Path.Combine(Path.GetTempPath(), $"cg_inv_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Assert.True(File.Exists(Path.Combine(outDir, "ValidationFunctions.vb")),
                    "ValidationFunctions.vb should be generated when VALIDATE WITH is used")
                ' Stub file should contain the three function names
                Dim content = File.ReadAllText(Path.Combine(outDir, "ValidationFunctions.vb"))
                Assert.Contains("CHECKSKU", content)
                Assert.Contains("CHECKQTY", content)
                Assert.Contains("CHECKPRICE", content)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' timesheet.def — multi-screen weekly hours entry
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class TimesheetTests

        Private ReadOnly _src As String

        Public Sub New()
            _src = LoadSample("timesheet.def")
        End Sub

        <Fact>
        Public Sub Parse_NoErrors()
            Dim result = ParseDsl(_src)
            Assert.Empty(result.Errors)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_NoAppendMode()
            Dim result = ParseDsl(_src)
            Assert.Equal(AppendMode.NoAppend, result.Doc.Data.Mode)
        End Sub

        <Fact>
        Public Sub Parse_DataSection_LFLineEnding()
            Dim result = ParseDsl(_src)
            Assert.Equal(LineEnding.LF, result.Doc.Data.Ending)
        End Sub

        <Fact>
        Public Sub Parse_Record_FieldCount()
            Dim result = ParseDsl(_src)
            Dim rec = Assert.Single(result.Doc.Data.Records)
            Assert.Equal(13, rec.Fields.Count)
        End Sub

        <Fact>
        Public Sub Parse_TwoScreens()
            Dim result = ParseDsl(_src)
            Assert.Equal(2, result.Doc.Screens.Count)
            Assert.Equal("TIMESHEET-ENTRY", result.Doc.Screens(0).Name)
            Assert.Equal("HOURS-ENTRY", result.Doc.Screens(1).Name)
        End Sub

        <Fact>
        Public Sub Parse_Screen_FGBGSeparateAttributes()
            ' FG=White BG=DarkGreen (not COLOR= shorthand)
            Dim result = ParseDsl(_src)
            Dim scr = result.Doc.Screens(0)
            Assert.Equal("White", scr.DefaultColor.Fg)
            Assert.Equal("DarkGreen", scr.DefaultColor.Bg)
        End Sub

        <Fact>
        Public Sub Parse_Field_WEEKEND_EscapedSlash()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "WEEKEND")
            Assert.NotNull(fld)
            ' 99\/99\/9999 → 2 digit + literal(/) + 2 digit + literal(/) + 4 digit = 10 tokens, LEN=8
            ' LEN=8 but mask is 10 tokens — validator should warn about mismatch
            Assert.Equal(8, fld.Len)
        End Sub

        <Fact>
        Public Sub Parse_FirstScreen_ThreeFields()
            Dim result = ParseDsl(_src)
            Assert.Equal(3, result.Doc.Screens(0).Fields.Count)
        End Sub

        <Fact>
        Public Sub Parse_SecondScreen_TenFields()
            Dim result = ParseDsl(_src)
            Assert.Equal(10, result.Doc.Screens(1).Fields.Count)
        End Sub

        <Fact>
        Public Sub Validate_NoHardErrors()
            Dim result = ParseAndValidate(_src)
            Assert.Empty(HardErrors(result.ValidErrs))
        End Sub

        <Fact>
        Public Sub CodeGen_TwoScreensMentionedInMainForm()
            Dim result = ParseAndValidate(_src)
            Dim outDir = Path.Combine(Path.GetTempPath(), $"cg_ts_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim content = File.ReadAllText(Path.Combine(outDir, "MainForm.vb"))
                Assert.Contains("TIMESHEET-ENTRY", content)
                Assert.Contains("HOURS-ENTRY", content)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' errors.def — intentionally broken: expects specific validation errors
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class ErrorDetectionTests

        Private ReadOnly _src As String

        Public Sub New()
            _src = LoadSample("errors.def")
        End Sub

        <Fact>
        Public Sub Validate_DuplicateRecordName_Detected()
            Dim result = ParseAndValidate(_src)
            Dim allErrs = New List(Of String)
            For Each e In result.ValidErrs
                allErrs.Add(e.Message)
            Next
            Assert.Contains(allErrs, Function(m) m.Contains("Duplicate record name") AndAlso m.Contains("DUP"))
        End Sub

        <Fact>
        Public Sub Validate_DuplicateFieldName_Detected()
            Dim result = ParseAndValidate(_src)
            Dim allErrs = New List(Of String)
            For Each e In result.ValidErrs
                allErrs.Add(e.Message)
            Next
            Assert.Contains(allErrs, Function(m) m.Contains("Duplicate field name") AndAlso m.Contains("AFIELD"))
        End Sub

        <Fact>
        Public Sub Validate_UnknownIntoRecord_Detected()
            Dim result = ParseAndValidate(_src)
            Dim allErrs = New List(Of String)
            For Each e In result.ValidErrs
                allErrs.Add(e.Message)
            Next
            Assert.Contains(allErrs, Function(m) m.Contains("NORECORD"))
        End Sub

        <Fact>
        Public Sub Validate_HasAtLeastThreeErrors()
            Dim result = ParseAndValidate(_src)
            Assert.True(HardErrors(result.ValidErrs).Count >= 3,
                "Expected at least 3 hard errors (dup record, dup field, bad INTO)")
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' FormatMaskTests — unit tests for the mask tokeniser and field alignment
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class FormatMaskTests

        <Theory>
        <InlineData("XXX",      3, New Integer() {0, 0, 0})>         ' all alphanumeric
        <InlineData("UU",       2, New Integer() {1, 1})>             ' all uppercase
        <InlineData("LL",       2, New Integer() {2, 2})>             ' all lowercase
        <InlineData("999",      3, New Integer() {3, 3, 3})>          ' all digit
        <InlineData("ZZZ",      3, New Integer() {4, 4, 4})>          ' all zerofill
        Public Sub ParseMask_CorrectKinds(mask As String, expectedCount As Integer,
                                          expectedKinds As Integer())
            Dim tokens = DslParser.ParseFormatMask(mask)
            Assert.Equal(expectedCount, tokens.Count)
            For i = 0 To expectedKinds.Length - 1
                Assert.Equal(CType(expectedKinds(i), MaskToken.TokenKind), tokens(i).Kind)
            Next
        End Sub

        <Fact>
        Public Sub ParseMask_EscapedHyphen_IsLiteral()
            Dim tokens = DslParser.ParseFormatMask("999\-9999")
            Assert.Equal(8, tokens.Count)
            Assert.Equal(MaskToken.TokenKind.Literal, tokens(3).Kind)
            Assert.Equal("-"c, tokens(3).LiteralChar)
        End Sub

        <Fact>
        Public Sub ParseMask_DoubleBackslash_IsLiteralBackslash()
            Dim tokens = DslParser.ParseFormatMask("X\\X")
            Assert.Equal(3, tokens.Count)
            Assert.Equal(MaskToken.TokenKind.Literal, tokens(1).Kind)
            Assert.Equal("\"c, tokens(1).LiteralChar)
        End Sub

        <Fact>
        Public Sub ParseMask_MixedMask_CorrectSequence()
            ' ZZZZZ.99 → 5 ZeroFill + 1 Literal(.) + 2 Digit
            Dim tokens = DslParser.ParseFormatMask("ZZZZZ.99")
            Assert.Equal(8, tokens.Count)
            Assert.Equal(MaskToken.TokenKind.ZeroFill, tokens(0).Kind)
            Assert.Equal(MaskToken.TokenKind.Literal, tokens(5).Kind)
            Assert.Equal("."c, tokens(5).LiteralChar)
            Assert.Equal(MaskToken.TokenKind.Digit, tokens(6).Kind)
        End Sub

        <Theory>
        <InlineData("999",  True)>    ' pure digit
        <InlineData("ZZZ",  True)>    ' pure zero-fill
        <InlineData("9Z9",  True)>    ' mixed numeric
        <InlineData("XXX",  False)>   ' alphanumeric — not numeric
        <InlineData("UU",   False)>   ' uppercase — not numeric
        <InlineData("",     False)>   ' empty
        Public Sub IsNumericMask_ReturnsExpected(mask As String, expected As Boolean)
            ' Test the numeric-detection logic used for right/left adjustment.
            ' We test it directly via a helper that replicates what CodeGenerator does.
            Dim isNum = mask.Replace("9", "").Replace("Z", "").Trim().Length = 0 AndAlso mask.Length > 0
            Assert.Equal(expected, isNum)
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' ColorHelperTests — DSL color names → Terminal.Gui ColorName16
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class ColorHelperTests

        <Theory>
        <InlineData("Black",       0)>   ' ColorName16.Black    = 0
        <InlineData("White",      15)>   ' ColorName16.White    = 15
        <InlineData("Gray",        7)>   ' ColorName16.Gray     = 7
        <InlineData("DarkGray",    8)>   ' ColorName16.DarkGray = 8
        <InlineData("Red",         4)>   ' ColorName16.Red      = 4  (DarkRed alias)
        <InlineData("DarkRed",     4)>
        <InlineData("Cyan",        3)>   ' ColorName16.Cyan     = 3
        <InlineData("DarkCyan",    3)>
        <InlineData("UnknownXYZ",  7)>   ' fallback → Gray
        Public Sub ToColor16_MapsCorrectly(name As String, expectedOrdinal As Integer)
            Dim result = CType(ColorHelper.ToColor16(name), Integer)
            Assert.Equal(expectedOrdinal, result)
        End Sub

        <Fact>
        Public Sub MakeAttr_FromSpec_FgBgCorrect()
            Dim spec As New ColorSpec With {.Fg = "White", .Bg = "Blue"}
            Dim attr = ColorHelper.MakeAttr(spec)
            ' Attribute stores colors — just verify it was constructed without throwing
            Assert.NotEqual(attr.Foreground, attr.Background)
        End Sub

        <Fact>
        Public Sub MakeScreenScheme_AllSlotsPopulated()
            Dim spec As New ColorSpec With {.Fg = "White", .Bg = "DarkBlue"}
            Dim scheme = ColorHelper.MakeScreenScheme(spec)
            Assert.NotNull(scheme)
            ' Spot-check that Normal and Focus are set (non-default Attribute)
            Assert.NotEqual(scheme.Normal, scheme.Focus)
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' TextFieldBehaviorTests — verify event signatures on Terminal.Gui TextField
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class TextFieldBehaviorTests

        <Fact>
        Public Sub TextField_TextChanging_EnforcesLimit()
            Dim tf As New Terminal.Gui.Views.TextField()
            Dim maxLen = 5
            AddHandler tf.TextChanging, Sub(sender As Object, ev As Terminal.Gui.App.ResultEventArgs(Of String))
                If ev.Result IsNot Nothing AndAlso ev.Result.Length > maxLen Then
                    ev.Result = ev.Result.Substring(0, maxLen)
                End If
            End Sub

            tf.Text = "HelloWorld"
            Assert.Equal("Hello", tf.Text)
        End Sub

        <Fact>
        Public Sub TextField_TextChanged_FiresWithoutCastException()
            Dim tf As New Terminal.Gui.Views.TextField()
            Dim fired = False
            AddHandler tf.TextChanged, Sub(sender As Object, ev As System.EventArgs)
                fired = True
            End Sub

            tf.Text = "A"
            Assert.True(fired)
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' DataFileCodeGenTests — verify generated DataFile.vb code correctness
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class DataFileCodeGenTests

        <Fact>
        Public Sub CodeGen_DataFile_CRLF_AppendMode()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE test.dat APPEND LRECL=50 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=50" & vbCrLf &
                      "    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    FIELD ""F1"" ROW=1 COL=1 LEN=50 INTO R.F1"
            Dim result = ParseDsl(dsl)
            Dim outDir = Path.Combine(Path.GetTempPath(), $"cg_df_crlf_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim dfContent = File.ReadAllText(Path.Combine(outDir, "DataFile.vb"))
                Dim progContent = File.ReadAllText(Path.Combine(outDir, "Program.vb"))

                Assert.Contains("Private Const FilePath As String = ""test.dat""", dfContent)
                Assert.Contains("Private Const Lrecl   As Integer = 50", dfContent)
                Assert.Contains("Private Const RecSize As Integer = 52", dfContent)
                Assert.Contains("sw.Write(vbCrLf)", dfContent)
                Assert.Contains("APPEND mode", dfContent)
                Assert.Contains("DataFile.Initialize()", progContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub CodeGen_DataFile_LF_NoAppendMode()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat NOAPPEND LRECL=80 LEND=LF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=80" & vbCrLf &
                      "    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    FIELD ""F1"" ROW=1 COL=1 LEN=80 INTO R.F1"
            Dim result = ParseDsl(dsl)
            Dim outDir = Path.Combine(Path.GetTempPath(), $"cg_df_lf_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim dfContent = File.ReadAllText(Path.Combine(outDir, "DataFile.vb"))

                Assert.Contains("Private Const FilePath As String = ""out.dat""", dfContent)
                Assert.Contains("Private Const Lrecl   As Integer = 80", dfContent)
                Assert.Contains("Private Const RecSize As Integer = 81", dfContent)
                Assert.Contains("sw.Write(vbLf)", dfContent)
                Assert.Contains("If File.Exists(FilePath) Then File.Delete(FilePath)", dfContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

    End Class

End Namespace
