' ColorHelper — builds Terminal.Gui Scheme objects from our own DSL ColorSpec.
' We manage all color decisions here; Terminal.Gui just applies whatever Scheme we hand it.
Imports Terminal.Gui.Drawing


    Public Module ColorHelper

        ' ── Map a DSL color name to Terminal.Gui ColorName16 ─────────────────
        Public Function ToColor16(name As String) As ColorName16
            Select Case name.Trim().ToLowerInvariant()
                Case "black"                     : Return ColorName16.Black
                Case "darkred",   "red"          : Return ColorName16.Red
                Case "darkgreen", "green"        : Return ColorName16.Green
                Case "darkyellow","yellow"       : Return ColorName16.Yellow
                Case "darkblue",  "blue"         : Return ColorName16.Blue
                Case "darkmagenta","magenta"     : Return ColorName16.Magenta
                Case "darkcyan",  "cyan"         : Return ColorName16.Cyan
                Case "gray"                      : Return ColorName16.Gray
                Case "darkgray"                  : Return ColorName16.DarkGray
                Case "brightred"                 : Return ColorName16.BrightRed
                Case "brightgreen"               : Return ColorName16.BrightGreen
                Case "brightyellow"              : Return ColorName16.BrightYellow
                Case "brightblue"                : Return ColorName16.BrightBlue
                Case "brightmagenta"             : Return ColorName16.BrightMagenta
                Case "brightcyan"                : Return ColorName16.BrightCyan
                Case "white"                     : Return ColorName16.White
                Case Else                        : Return ColorName16.Gray
            End Select
        End Function

        ' ── Build an Attribute from a ColorSpec ───────────────────────────────
        Public Function MakeAttr(spec As ColorSpec) As Attribute
            Return New Attribute(ToColor16(spec.Fg), ToColor16(spec.Bg))
        End Function

        ' ── Shorthand: build an Attribute from two name strings ───────────────
        Public Function MakeAttr(fg As String, bg As String) As Attribute
            Return New Attribute(ToColor16(fg), ToColor16(bg))
        End Function

        ' ── Build a Scheme for a screen field ────────────────────────────────
        ' Scheme properties are init-only — must use object initializer syntax.
        Public Function MakeFieldScheme(normalSpec As ColorSpec,
                                        focusSpec  As ColorSpec,
                                        errorSpec  As ColorSpec,
                                        screenDefault As ColorSpec) As Scheme

            Dim nAttr = MakeAttr(If(normalSpec, screenDefault))
            Dim fAttr = MakeAttr(If(focusSpec,  New ColorSpec With {.Fg = "Black",  .Bg = "Cyan"}))
            Dim dAttr = MakeAttr("Gray", screenDefault.Bg)

            Return New Scheme With {
                .Normal    = nAttr,
                .Focus     = fAttr,
                .Editable  = fAttr,
                .HotNormal = nAttr,
                .HotFocus  = fAttr,
                .Disabled  = dAttr
            }
        End Function

        ' ── Build a Scheme for the overall screen window ──────────────────────
        Public Function MakeScreenScheme(spec As ColorSpec) As Scheme
            Dim attr  = MakeAttr(spec)
            Dim fAttr = MakeAttr("Black", "Cyan")
            Dim dAttr = MakeAttr("Gray", spec.Bg)

            Return New Scheme With {
                .Normal    = attr,
                .Focus     = fAttr,
                .Editable  = fAttr,
                .HotNormal = attr,
                .HotFocus  = fAttr,
                .Disabled  = dAttr
            }
        End Function

        ' ── Error-state scheme ────────────────────────────────────────────────
        Public Function MakeErrorScheme(errorSpec As ColorSpec) As Scheme
            Dim attr = MakeAttr(If(errorSpec, New ColorSpec With {.Fg = "White", .Bg = "DarkRed"}))

            Return New Scheme With {
                .Normal    = attr,
                .Focus     = attr,
                .Editable  = attr,
                .HotNormal = attr,
                .HotFocus  = attr
            }
        End Function

    End Module

