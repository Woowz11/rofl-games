#Requires AutoHotkey v2.0
#SingleInstance Force

; ============================================================
;   Roblox Auto-Macro — 2 режима
; ============================================================
;
;   Режим 1 (F1): E → 1 → по кругу
;   Режим 2 (F2): драг (665,716) → (665,1008) → "1" → "e"
;
;   Esc — выход
;
; ============================================================

CoordMode "Mouse", "Screen"
CoordMode "Pixel", "Screen"

SendMode "Event"
SetKeyDelay -1, 50

running1 := false
running2 := false

; === GUI-индикатор ===
statusGui := Gui("-Caption +AlwaysOnTop +ToolWindow +E0x20")
statusGui.BackColor := "1e1e1e"
statusGui.SetFont("s10 cWhite", "Consolas")

titleText := statusGui.AddText("x10 y10 w340 cYellow", "Auto-Macro — 2 режима")
descText := statusGui.AddText("x10 y32 w340 cSilver",
    "Режим 1: E → 1 → по кругу`n"
    . "Режим 2: драг (665,716)→(665,1008) → 1 → E")

statusText := statusGui.AddText("x10 y78 w340 cWhite", "Статус: остановлен")
actionText := statusGui.AddText("x10 y101 w340 cWhite", "Действие: —")
hintText := statusGui.AddText("x10 y128 w340 cGray", "F1 — режим 1    F2 — режим 2    Esc — выход")

screenW := A_ScreenWidth
screenH := A_ScreenHeight
statusGui.Show("x" (screenW - 360) " y" (screenH - 190) " w340 h160 NoActivate")

UpdateStatus(text) {
    global statusText
    statusText.Text := "Статус: " text
}

UpdateAction(text) {
    global actionText
    actionText.Text := "Действие: " text
}

; F1 — старт/стоп режима 1
F1:: {
    global running1, running2
    if running1 {
        running1 := false
        UpdateStatus("остановлен")
        UpdateAction("—")
        return
    }
    running2 := false
    Sleep 50
    running1 := true
    UpdateStatus("работает (режим 1)")
    MainLoop1()
}

; F2 — старт/стоп режима 2
F2:: {
    global running1, running2
    if running2 {
        running2 := false
        UpdateStatus("остановлен")
        UpdateAction("—")
        return
    }
    running1 := false
    Sleep 50
    running2 := true
    UpdateStatus("работает (режим 2)")
    MainLoop2()
}

; ============================================================
;   Режим 1: E → 1 → по кругу
; ============================================================
MainLoop1() {
    global running1

    while running1 {

        UpdateAction("нажатие E")
        Send "{e down}"
        Sleep RandRange(120, 220)
        Send "{e up}"
        Sleep RandRange(150, 300)
        if !running1
            break

        UpdateAction("нажатие 1")
        Send "{1 down}"
        Sleep RandRange(100, 180)
        Send "{1 up}"
        Sleep RandRange(150, 300)
    }
    UpdateAction("—")
}

; ============================================================
;   Режим 2: драг (665,716) → (665,1008) → "1" → "e"
; ============================================================
MainLoop2() {
    global running2

    while running2 {

        UpdateAction("перемещение в точку старта драга")
        MoveMouseSmooth(665, 716, RandRange(30, 50))
        Sleep RandRange(50, 100)
        if !running2
            break

        UpdateAction("зажатие ЛКМ")
        MouseClick "Left", 665, 716, 1, 0, "D"
        Sleep RandRange(50, 100)
        if !running2
            break

        UpdateAction("перемещение (драг)")
        MoveMouseSmooth(665, 1008, RandRange(40, 50))
        Sleep RandRange(50, 100)
        if !running2
            break

        UpdateAction("отпускание ЛКМ")
        MouseClick "Left", 665, 1008, 1, 0, "U"
        Sleep RandRange(150, 300)
        if !running2
            break

        UpdateAction("нажатие 1")
        Send "{1 down}"
        Sleep RandRange(100, 180)
        Send "{1 up}"
        Sleep RandRange(150, 250)
        if !running2
            break

        UpdateAction("нажатие E")
        Send "{e down}"
        Sleep RandRange(100, 180)
        Send "{e up}"
        Sleep RandRange(150, 300)
    }
    UpdateAction("—")
}

; === быстрое плавное движение мыши (ease in-out) ===
MoveMouseSmooth(targetX, targetY, duration := 100) {
    MouseGetPos &startX, &startY
    steps := 10
    Loop steps {
        t := A_Index / steps
        easedT := t < 0.5 ? 2*t*t : 1 - ((-2*t + 2)**2) / 2
        newX := startX + (targetX - startX) * easedT
        newY := startY + (targetY - startY) * easedT
        MouseMove Round(newX), Round(newY), 0
        Sleep duration / steps
    }
}

RandRange(min, max) {
    return Random(min, max)
}

Esc:: {
    global running1, running2
    running1 := false
    running2 := false
    statusGui.Destroy()
    ExitApp()
}