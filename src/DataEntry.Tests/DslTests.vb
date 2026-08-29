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

        ''' <summary>Load a .def sample file from the Samples output directory.</summary>
        Public Function LoadSample(name As String) As String
            ' .def files are copied to the output directory under Samples\
            Dim exeDir   = IO.Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
            Dim filePath = IO.Path.Combine(exeDir, "Samples", name)
            If Not File.Exists(filePath) Then
                Throw New InvalidOperationException(
                    $"Sample file not found: {filePath}. " &
                    "Ensure Samples\*.def has CopyToOutputDirectory=PreserveNewest in the test project.")
            End If
            Return File.ReadAllText(filePath)
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
            Dim fld = result.Doc.Screens(0).Fields.Find(Function(f) f.IntoField = "LNAME")
            Assert.NotNull(fld)
            Assert.Equal("CUSTOMER", fld.IntoRecord)
            Assert.Equal("LNAME", fld.IntoField)
        End Sub

        <Fact>
        Public Sub Parse_ScreenField_WithExplicitColors()
            Dim result = ParseDsl(_src)
            Dim fld = result.Doc.Screens(0).Fields.Find(Function(f) f.IntoField = "CUSTID")
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
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_customer_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Assert.True(File.Exists(IO.Path.Combine(outDir, "Program.vb")))
                Assert.True(File.Exists(IO.Path.Combine(outDir, "MainForm.vb")))
                Assert.True(File.Exists(IO.Path.Combine(outDir, "DataFile.vb")))
                Assert.True(File.Exists(IO.Path.Combine(outDir, "FormatHelper.vb")))
                Assert.True(File.Exists(IO.Path.Combine(outDir, "ColorHelper.vb")))

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
            Dim skuFld = scr.Fields.Find(Function(f) f.IntoField = "SKU")
            Assert.NotNull(skuFld)
            Assert.Equal("CHECKSKU", skuFld.ValidateFunc)
            Dim qtyFld = scr.Fields.Find(Function(f) f.IntoField = "QTY")
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
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_inv_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Assert.True(File.Exists(IO.Path.Combine(outDir, "ValidationFunctions.vb")),
                    "ValidationFunctions.vb should be generated when VALIDATE WITH is used")
                ' Stub file should contain the three function names
                Dim content = File.ReadAllText(IO.Path.Combine(outDir, "ValidationFunctions.vb"))
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
            ' 99\/99\/9999 → 2 digit + literal(/) + 2 digit + literal(/) + 4 digit = 10 tokens, LEN=10
            ' Stored value includes embedded slashes: MM/DD/YYYY = 10 chars.
            Assert.Equal(10, fld.Len)
            Assert.Equal(10, fld.Format.Tokens.Count)
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
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_ts_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim content = File.ReadAllText(IO.Path.Combine(outDir, "MainForm.vb"))
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
        <InlineData("999",      True)>    ' pure digit
        <InlineData("ZZZ",      True)>    ' pure zero-fill
        <InlineData("9Z9",      True)>    ' mixed numeric
        <InlineData("999\-9999",True)>    ' digits + escaped literal — still numeric
        <InlineData("ZZ\.99",   True)>    ' zero-fill + escaped dot — still numeric
        <InlineData("XXX",      False)>   ' alphanumeric — not numeric
        <InlineData("UU",       False)>   ' uppercase — not numeric
        <InlineData("",         False)>   ' empty
        Public Sub IsNumericMask_ReturnsExpected(mask As String, expected As Boolean)
            Dim tokens = DslParser.ParseFormatMask(mask)
            Assert.Equal(expected, FormatMask.IsNumericMask(tokens))
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' FormatHintTests — verify the auto-generated display hint strings
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class FormatHintTests

        Private Shared Function Hint(maskRaw As String) As String
            Return FormatMask.FormatHint(DslParser.ParseFormatMask(maskRaw))
        End Function

        <Fact>
        Public Sub Phone_EscapedHyphens_ProducesReadableHint()
            ' 999\-999\-9999 → ###-###-####
            Assert.Equal("###-###-####", Hint("999\-999\-9999"))
        End Sub

        <Fact>
        Public Sub Date_EscapedSlashes_ProducesReadableHint()
            ' 99\/99\/9999 → ##/##/####
            Assert.Equal("##/##/####", Hint("99\/99\/9999"))
        End Sub

        <Fact>
        Public Sub Decimal_UnescapedDot_ProducesHint()
            ' ZZ.99 — the dot is an unescaped literal → ##.##
            Assert.Equal("##.##", Hint("ZZ.99"))
        End Sub

        <Fact>
        Public Sub PureDigits_NoLiteral_ReturnsEmpty()
            ' 999999 — no literals, hint not needed
            Assert.Equal("", Hint("999999"))
        End Sub

        <Fact>
        Public Sub PureAlpha_NoLiteral_ReturnsEmpty()
            ' XXXXXXXXX — no literals
            Assert.Equal("", Hint("XXXXXXXXX"))
        End Sub

        <Fact>
        Public Sub UpperCase_NoLiteral_ReturnsEmpty()
            Assert.Equal("", Hint("UU"))
        End Sub

        <Fact>
        Public Sub Mixed_UpperWithLiteral_ProducesHint()
            ' UU\/UU → ^^/^^
            Assert.Equal("^^/^^", Hint("UU\/UU"))
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' ApplyMaskTests — verify that FormatMask.ApplyMask produces the correct
    ' fixed-length record field value for a given raw input and FORMAT mask.
    ' These tests directly exercise the same logic that ends up in the
    ' generated FormatHelper.vb of every compiled form.
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class ApplyMaskTests

        ''' <summary>Helper: parse mask raw string then apply it.</summary>
        Private Shared Function Apply(raw As String, maskRaw As String, fieldLen As Integer) As String
            Dim tokens = DslParser.ParseFormatMask(maskRaw)
            Return FormatMask.ApplyMask(raw, tokens, fieldLen)
        End Function

        ' ── Escaped-literal masks (the bug that was reported) ─────────────────

        <Fact>
        Public Sub Phone_EscapedHyphens_InsertsHyphens()
            ' FORMAT=999\-999\-9999.  LEN=12
            ' Input: 3156176379 → expected: 315-617-6379
            Assert.Equal("315-617-6379", Apply("3156176379", "999\-999\-9999", 12))
        End Sub

        <Fact>
        Public Sub Phone_ShortInput_SpacePadsLeft()
            ' Numeric mask with escaped literals — right-justified (numeric padding)
            ' Input: 617 (only 3 digits) → 3 digits filled, rest space-padded then left-padded
            Dim result = Apply("617", "999\-999\-9999", 12)
            Assert.Equal(12, result.Length)
            ' The three digits land at positions 0-2; check digit at index 2 is '7'
            Assert.Equal("7"c, result(2))
        End Sub

        <Fact>
        Public Sub Date_EscapedSlashes_InsertsSlashes()
            ' FORMAT=99\/99\/9999.  LEN=10
            ' Input: 12252025 → expected: 12/25/2025
            Assert.Equal("12/25/2025", Apply("12252025", "99\/99\/9999", 10))
        End Sub

        <Fact>
        Public Sub Phone_PreFormatted_HyphensAlreadyPresent()
            ' User typed the formatted value 800-867-5309 — hyphens should be stripped
            ' and the digits re-formatted correctly.
            Assert.Equal("800-867-5309", Apply("800-867-5309", "999\-999\-9999", 12))
        End Sub

        <Fact>
        Public Sub Phone_PreFormatted_ParensAndHyphens()
            ' User typed (800)867-5309 — both parens, closing paren, and hyphens
            ' should all be stripped leaving only the digits.
            Assert.Equal("800-867-5309", Apply("(800)867-5309", "999\-999\-9999", 12))
        End Sub

        <Fact>
        Public Sub Date_PreFormatted_SlashesAlreadyPresent()
            ' User typed 12/25/2025 — slashes already in place.
            Assert.Equal("12/25/2025", Apply("12/25/2025", "99\/99\/9999", 10))
        End Sub

        ' ── No FORMAT mask — verbatim pass-through ────────────────────────────

        <Fact>
        Public Sub NoMask_PlainText_PassedThroughVerbatim()
            ' A field with no FORMAT at all (empty token list) must store the
            ' user's text exactly as typed — spaces, punctuation and all.
            Assert.Equal("123 ANYSTREET     ", Apply("123 ANYSTREET", "", 18))
        End Sub

        <Fact>
        Public Sub NoMask_ExactLength_NoChange()
            Assert.Equal("HELLO", Apply("HELLO", "", 5))
        End Sub

        <Fact>
        Public Sub NoMask_TooLong_Truncated()
            Assert.Equal("ABCDE", Apply("ABCDEFGH", "", 5))
        End Sub

        ' ── Plain digit / zero-fill masks ─────────────────────────────────────

        <Fact>
        Public Sub Digits_ExactLength_NoChange()
            ' FORMAT=999999.  LEN=6
            Assert.Equal("123456", Apply("123456", "999999", 6))
        End Sub

        <Fact>
        Public Sub Digits_Short_SpacePadsWithSpaces()
            ' 9 placeholder: missing input char → space; result left-justified because
            ' trailing spaces make it look non-numeric for padding — but length already = LEN
            Assert.Equal("42   ", Apply("42", "99999", 5))
        End Sub

        <Fact>
        Public Sub ZeroFill_ShortInput_ZeroFillsMissingPositions()
            ' Z placeholder: missing input char → '0'; result length = LEN, no pad step fires
            Assert.Equal("700", Apply("7", "ZZZ", 3))
        End Sub

        <Fact>
        Public Sub ZeroFill_NonDigit_WritesZero()
            ' Z placeholder: non-digit input char → '0'
            Assert.Equal("000", Apply("A B", "ZZZ", 3))
        End Sub

        <Fact>
        Public Sub ZeroFill_WithDecimalLiteral_CorrectOutput()
            ' FORMAT=ZZ.99.  LEN=5  (hours.minutes, e.g. 08.50)
            ' Input: "850" → Z='8', Z='5', .='.', 9=raw(2)='0', 9=missing→' ' → "85.0 "
            Assert.Equal("85.0 ", Apply("850", "ZZ.99", 5))
        End Sub

        <Fact>
        Public Sub ZeroFill_WithDecimalLiteral_FourDigits_CorrectOutput()
            ' Input: 0850 → Z='0', Z='8', .='.', 9='5', 9='0' → "08.50"
            Assert.Equal("08.50", Apply("0850", "ZZ.99", 5))
        End Sub

        ' ── Alpha masks ───────────────────────────────────────────────────────

        <Fact>
        Public Sub Alpha_X_CopiesAsIs()
            Assert.Equal("Hello", Apply("Hello", "XXXXX", 5))
        End Sub

        <Fact>
        Public Sub Alpha_X_SpacesInAddressPreserved()
            ' "123 ANY STREET" typed into a 30-X field — spaces must survive intact.
            Assert.Equal("123 ANY STREET                ", Apply("123 ANY STREET", "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", 30))
        End Sub

        <Fact>
        Public Sub Alpha_X_LetterXInAddressNotStripped()
            ' "100 X STREET" — the letter X in the data must not be treated as a mask char.
            Assert.Equal("100 X STREET                  ", Apply("100 X STREET", "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX", 30))
        End Sub

        <Fact>
        Public Sub Alpha_X_PunctuationPreserved()
            ' Punctuation (apostrophe, period, comma) must pass through an X mask unchanged.
            Assert.Equal("O'BRIEN, J.               ", Apply("O'BRIEN, J.", "XXXXXXXXXXXXXXXXXXXXXXXXXX", 26))
        End Sub

        <Fact>
        Public Sub Alpha_U_ForcesUpperCase()
            Assert.Equal("HELLO", Apply("hello", "UUUUU", 5))
        End Sub

        <Fact>
        Public Sub Alpha_L_ForcesLowerCase()
            Assert.Equal("hello", Apply("HELLO", "LLLLL", 5))
        End Sub

        <Fact>
        Public Sub Alpha_Short_SpacePadsRight()
            ' Non-numeric mask, input shorter than LEN → left-justified (space right-padded)
            Assert.Equal("AB   ", Apply("AB", "XXXXX", 5))
        End Sub

        <Fact>
        Public Sub Alpha_Long_Truncates()
            Assert.Equal("Hello", Apply("HelloWorld", "XXXXX", 5))
        End Sub

        ' ── Digit validation ──────────────────────────────────────────────────

        <Fact>
        Public Sub Digit_NonDigitInput_WritesSpace()
            ' 9 placeholder: non-digit → space
            Assert.Equal("1 3", Apply("1A3", "999", 3))
        End Sub

        ' ── Field value round-trip through DSL pipeline ───────────────────────

        <Fact>
        Public Sub SampleDef_PhoneField_RoundTrip()
            ' Verify the full pipeline: parse sample.def (in Samples/), find CPHONE field,
            ' apply its mask to the digits that were typed, assert correct record bytes.
            Dim src = LoadSample("sample.def")   ' copied from repo root via .vbproj
            Dim result = ParseDsl(src)
            Dim phoneField = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "CPHONE")
            Assert.NotNull(phoneField)
            Assert.Equal(12, phoneField.Len)

            Dim output = FormatMask.ApplyMask("3156176379", phoneField.Format.Tokens, phoneField.Len)
            Assert.Equal("315-617-6379", output)
        End Sub

        <Fact>
        Public Sub CustomerDef_PhoneField_RoundTrip()
            Dim src = LoadSample("customer.def")
            Dim result = ParseDsl(src)
            Dim phoneField = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "PHONE")
            Assert.NotNull(phoneField)
            Assert.Equal(12, phoneField.Len)

            Dim output = FormatMask.ApplyMask("3156176379", phoneField.Format.Tokens, phoneField.Len)
            Assert.Equal("315-617-6379", output)
        End Sub

        <Fact>
        Public Sub TimesheetDef_WeekendField_RoundTrip()
            ' FORMAT=99\/99\/9999  LEN=10  — date with escaped slashes
            Dim src = LoadSample("timesheet.def")
            Dim result = ParseDsl(src)
            Dim fld = result.Doc.Data.Records(0).Fields.Find(Function(f) f.Name = "WEEKEND")
            Assert.NotNull(fld)
            Assert.Equal(10, fld.Len)

            Dim output = FormatMask.ApplyMask("12252025", fld.Format.Tokens, fld.Len)
            Assert.Equal("12/25/2025", output)
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
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_df_crlf_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim dfContent = File.ReadAllText(IO.Path.Combine(outDir, "DataFile.vb"))
                Dim progContent = File.ReadAllText(IO.Path.Combine(outDir, "Program.vb"))

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
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_df_lf_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim dfContent = File.ReadAllText(IO.Path.Combine(outDir, "DataFile.vb"))

                Assert.Contains("Private Const FilePath As String = ""out.dat""", dfContent)
                Assert.Contains("Private Const Lrecl   As Integer = 80", dfContent)
                Assert.Contains("Private Const RecSize As Integer = 81", dfContent)
                Assert.Contains("sw.Write(vbLf)", dfContent)
                Assert.Contains("If File.Exists(FilePath) Then File.Delete(FilePath)", dfContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub CodeGen_GeneratesSuperViewAdvanceFocusAndInsertionPointReset()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat APPEND LRECL=20 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=10" & vbCrLf &
                      "    FORMAT=XXXXXXXXXX." & vbCrLf &
                      "    F2 LEN=10" & vbCrLf &
                      "    FORMAT=XXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    FIELD ""F1"" ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "    FIELD ""F2"" ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim result = ParseDsl(dsl)
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_advf_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim mfContent = File.ReadAllText(IO.Path.Combine(outDir, "MainForm.vb"))

                Assert.Contains("fld.InsertionPoint = 0", mfContent)
                Assert.Contains("fld.SuperView?.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop)", mfContent)
                Assert.Contains("DataFile.SaveRecordAtIndex(_recordIndex, rec.ToString())", mfContent)
                Assert.Contains("If e = Key.F1 Then", mfContent)
                Assert.Contains("ShowHelp()", mfContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub
    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' KeyHandlingTests — verify KeyDown handlers attached to TextFields
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class KeyHandlingTests

        <Fact>
        Public Sub TextField_CtrlS_TriggersSaveAndClear()
            Dim tf1 As New Terminal.Gui.Views.TextField() With {.Text = "SKU001"}
            Dim tf2 As New Terminal.Gui.Views.TextField() With {.Text = "Widget"}
            Dim allFields As New List(Of Terminal.Gui.Views.TextField) From {tf1, tf2}

            Dim saveCount = 0
            Dim onKeyDownAction = Sub(s As Object, e As Terminal.Gui.Input.Key)
                                      If e.IsCtrl AndAlso (e.NoCtrl = Terminal.Gui.Input.Key.S OrElse e.NoCtrl = Terminal.Gui.Input.Key.s) Then
                                          saveCount += 1
                                          For Each f In allFields
                                              f.Text = ""
                                          Next
                                          If allFields.Count > 0 Then allFields(0).SetFocus()
                                          e.Handled = True
                                      End If
                                  End Sub

            AddHandler tf1.KeyDown, onKeyDownAction
            AddHandler tf2.KeyDown, onKeyDownAction

            ' Simulate Ctrl+S while focused on field 2
            tf2.HasFocus = True
            Dim ctrlS = Terminal.Gui.Input.Key.S.WithCtrl
            tf2.NewKeyDownEvent(ctrlS)

            Assert.Equal(1, saveCount)
            Assert.Equal("", tf1.Text)
            Assert.Equal("", tf2.Text)
            Assert.True(tf1.HasFocus)
        End Sub

        <Fact>
        Public Sub LastField_EnterKey_TriggersSaveAndClear()
            Dim tf1 As New Terminal.Gui.Views.TextField() With {.Text = "ITEM01"}
            Dim tf2 As New Terminal.Gui.Views.TextField() With {.Text = "100"}
            Dim allFields As New List(Of Terminal.Gui.Views.TextField) From {tf1, tf2}

            Dim saveCount = 0
            AddHandler tf2.KeyDown, Sub(s As Object, e As Terminal.Gui.Input.Key)
                                        If e = Terminal.Gui.Input.Key.Enter Then
                                            saveCount += 1
                                            For Each f In allFields
                                                f.Text = ""
                                            Next
                                            If allFields.Count > 0 Then allFields(0).SetFocus()
                                            e.Handled = True
                                        End If
                                    End Sub

            tf2.HasFocus = True
            tf2.NewKeyDownEvent(Terminal.Gui.Input.Key.Enter)

            Assert.Equal(1, saveCount)
            Assert.Equal("", tf1.Text)
            Assert.Equal("", tf2.Text)
            Assert.True(tf1.HasFocus)
        End Sub

    End Class

    ' ─────────────────────────────────────────────────────────────────────────
    ' ScreenElementTests — standalone prompts, independent coordinates & validation
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class ScreenElementTests

        <Fact>
        Public Sub StandalonePrompt_ParsesCorrectly()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat APPEND LRECL=10 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=10" & vbCrLf &
                      "    FORMAT=XXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    PROMPT ""Enter Name:"" ROW=2 COL=2" & vbCrLf &
                      "    FIELD ROW=2 COL=20 LEN=10 INTO R.F1"
            Dim res = ParseAndValidate(dsl)
            Assert.Empty(res.ParseErrs)
            Assert.Empty(HardErrors(res.ValidErrs))
            Assert.Single(res.Doc.Screens(0).Prompts)
            Assert.Equal("Enter Name:", res.Doc.Screens(0).Prompts(0).Text)
            Assert.Equal(2, res.Doc.Screens(0).Prompts(0).Row)
            Assert.Equal(2, res.Doc.Screens(0).Prompts(0).Col)
        End Sub

        <Fact>
        Public Sub Field_ExplicitPromptRowCol_ParsesCorrectly()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat APPEND LRECL=10 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=10" & vbCrLf &
                      "    FORMAT=XXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    FIELD ""Name"" PROMPT_ROW=2 PROMPT_COL=2 ROW=2 COL=20 LEN=10 INTO R.F1"
            Dim res = ParseAndValidate(dsl)
            Assert.Empty(res.ParseErrs)
            Assert.Empty(HardErrors(res.ValidErrs))
            Dim fld = res.Doc.Screens(0).Fields(0)
            Assert.Equal("Name", fld.Label)
            Assert.Equal(2, fld.PromptRow)
            Assert.Equal(2, fld.PromptCol)
            Assert.Equal(2, fld.Row)
            Assert.Equal(20, fld.Col)
        End Sub

        <Fact>
        Public Sub Validator_DetectsOverlapError()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat APPEND LRECL=30 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=20" & vbCrLf &
                      "    FORMAT=XXXXXXXXXXXXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    PROMPT ""Very Long Label Text"" ROW=2 COL=2" & vbCrLf &
                      "    FIELD ROW=2 COL=10 LEN=20 INTO R.F1"
            Dim res = ParseAndValidate(dsl)
            Dim errs = HardErrors(res.ValidErrs)
            Assert.NotEmpty(errs)
            Assert.Contains(errs, Function(e) e.Message.Contains("overlaps"))
        End Sub

        <Fact>
        Public Sub Validator_DetectsScreenBoundaryError()
            Dim dsl = "DATA-SECTION" & vbCrLf &
                      "    FILE out.dat APPEND LRECL=30 LEND=CRLF" & vbCrLf &
                      "RECORD R" & vbCrLf &
                      "    F1 LEN=30" & vbCrLf &
                      "    FORMAT=XXXXXXXXXXXXXXXXXXXXXXXXXXXXXX." & vbCrLf &
                      "SCREEN-SECTION" & vbCrLf &
                      "SCREEN S" & vbCrLf &
                      "    FIELD ROW=2 COL=60 LEN=30 INTO R.F1"
            Dim res = ParseAndValidate(dsl)
            Dim errs = HardErrors(res.ValidErrs)
            Assert.NotEmpty(errs)
            Assert.Contains(errs, Function(e) e.Message.Contains("exceeds screen boundaries"))
        End Sub

    End Class


    ' ─────────────────────────────────────────────────────────────────────────
    ' FullBehaviorTests — FULL= attribute parsing, defaults, and codegen output
    ' ─────────────────────────────────────────────────────────────────────────
    Public Class FullBehaviorTests

        Private Shared ReadOnly BaseDsl As String =
            "DATA-SECTION" & vbCrLf &
            "    FILE out.dat APPEND LRECL=20 LEND=CRLF" & vbCrLf &
            "RECORD R" & vbCrLf &
            "    F1 LEN=10" & vbCrLf &
            "    FORMAT=XXXXXXXXXX." & vbCrLf &
            "    F2 LEN=10" & vbCrLf &
            "    FORMAT=XXXXXXXXXX." & vbCrLf &
            "SCREEN-SECTION" & vbCrLf &
            "SCREEN S" & vbCrLf

        <Fact>
        Public Sub Field_FullStay_ParsesCorrectly()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "        FULL=STAY" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim res = ParseAndValidate(dsl)
            Assert.Empty(res.ParseErrs)
            Assert.Empty(HardErrors(res.ValidErrs))
            Assert.Equal(FullBehavior.Stay, res.Doc.Screens(0).Fields(0).Full)
        End Sub

        <Fact>
        Public Sub Field_FullAdvance_ParsesCorrectly()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "        FULL=ADVANCE" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim res = ParseAndValidate(dsl)
            Assert.Empty(res.ParseErrs)
            Assert.Empty(HardErrors(res.ValidErrs))
            Assert.Equal(FullBehavior.Advance, res.Doc.Screens(0).Fields(0).Full)
        End Sub

        <Fact>
        Public Sub Field_FullOmitted_DefaultsToAdvance()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim res = ParseAndValidate(dsl)
            Assert.Empty(res.ParseErrs)
            Assert.Equal(FullBehavior.Advance, res.Doc.Screens(0).Fields(0).Full)
            Assert.Equal(FullBehavior.Advance, res.Doc.Screens(0).Fields(1).Full)
        End Sub

        <Fact>
        Public Sub Field_FullInvalidValue_ReturnsParseError()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "        FULL=BOGUS" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim res = ParseAndValidate(dsl)
            ' Parser should emit an error and default to Advance
            Assert.NotEmpty(res.ParseErrs)
            Assert.Contains(res.ParseErrs, Function(e) e.Message.Contains("FULL"))
            Assert.Equal(FullBehavior.Advance, res.Doc.Screens(0).Fields(0).Full)
        End Sub

        <Fact>
        Public Sub CodeGen_FullStay_DoesNotContainAdvanceFocusInTextChanged()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "        FULL=STAY" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim result = ParseDsl(dsl)
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_fullstay_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim mfContent = File.ReadAllText(IO.Path.Combine(outDir, "MainForm.vb"))

                ' The STAY field's TextChanged handler should pin InsertionPoint but NOT advance focus
                Assert.Contains("pin cursor at end, no advance", mfContent)
                ' AdvanceFocus should still exist (for the second field's Enter handler), but
                ' the TextChanged block for the STAY field must not call it
                Dim stayBlock = mfContent.Substring(0, mfContent.IndexOf("pin cursor at end, no advance"))
                Assert.DoesNotContain("AdvanceFocus", stayBlock.Substring(stayBlock.LastIndexOf("TextChanged")))
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub CodeGen_FullAdvance_ContainsAdvanceFocusInTextChanged()
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "        FULL=ADVANCE" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim result = ParseDsl(dsl)
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_fulladvance_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim mfContent = File.ReadAllText(IO.Path.Combine(outDir, "MainForm.vb"))
                Assert.Contains("AdvanceFocus", mfContent)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

        <Fact>
        Public Sub CodeGen_TextChanging_CancelsEditWhenFull_NotSubstring()
            ' The TextChanging handler must cancel the edit (set to current text) not truncate,
            ' to prevent Terminal.Gui's ScrollOffset from advancing.
            Dim dsl = BaseDsl &
                      "    FIELD ROW=1 COL=1 LEN=10 INTO R.F1" & vbCrLf &
                      "    FIELD ROW=2 COL=1 LEN=10 INTO R.F2"
            Dim result = ParseDsl(dsl)
            Dim outDir = IO.Path.Combine(IO.Path.GetTempPath(), $"cg_textchanging_{Guid.NewGuid():N}")
            Try
                Dim gen As New CodeGenerator()
                gen.GenerateProject(result.Doc, outDir)
                Dim mfContent = File.ReadAllText(IO.Path.Combine(outDir, "MainForm.vb"))

                ' The TextChanging handler must use the cancel-pattern (DirectCast + current text),
                ' not truncate via Substring, to prevent Terminal.Gui ScrollOffset advancing.
                Assert.Contains("DirectCast(sender, TextField).Text  ' cancel", mfContent)
                ' Confirm the TextChanging block itself does not use Substring —
                ' isolate just that block (between TextChanging and the closing End Sub)
                Dim tcStart = mfContent.IndexOf("AddHandler _S_0.TextChanging")
                Dim tcEnd   = mfContent.IndexOf("End Sub", tcStart)
                Dim tcBlock = mfContent.Substring(tcStart, tcEnd - tcStart)
                Assert.DoesNotContain("ev.Result.Substring", tcBlock)
            Finally
                If Directory.Exists(outDir) Then Directory.Delete(outDir, True)
            End Try
        End Sub

    End Class


End Namespace
