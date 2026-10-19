#Requires AutoHotkey v2.0
#SingleInstance Force
CoordMode "Mouse", "Screen"
CoordMode "Pixel", "Screen"
CoordMode "ToolTip", "Screen"

previewGui := Gui("-Caption +AlwaysOnTop +ToolWindow +E0x20 -DPIScale")
previewGui.BackColor := "000000"
previewGui.Show("w40 h40 NoActivate")  ; показываем один раз

lastHex := ""

SetTimer ShowInfo, 50

ShowInfo() {
    global lastHex
    MouseGetPos &x, &y
    color := PixelGetColor(x, y)
    hex := Format("{:06X}", color)

    previewGui.Move(x + 20, y + 20)  ; только двигаем, не показываем заново

    if (hex != lastHex) {            ; меняем цвет только если он реально другой
        previewGui.BackColor := hex
        lastHex := hex
    }

    ToolTip "X: " x " Y: " y " Color: 0x" hex, x + 20, y - 20
}

Esc:: {
    previewGui.Destroy()
    ExitApp()
}