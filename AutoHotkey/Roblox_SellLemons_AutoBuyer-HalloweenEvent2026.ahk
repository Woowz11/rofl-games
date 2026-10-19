#Requires AutoHotkey v2.0
#SingleInstance Force

; ============================================================
;   Roblox Auto-Buyer — Sell Lemons (Halloween Event 2026)
; ============================================================
;
;   Игра:    Sell Lemons
;   Ссылка:  https://www.roblox.com/games/79268393072444/Sell-Lemons
;   Задача:  автоматическая покупка паков в Sticker Shop
;            во время хэллоуинского ивента 2026
;
;   Режим 1 (F1): проверка цвета в точке (568, 743) ~ 0xF2F2F2
;                 → покупка White Common Pack
;   Режим 2 (F2): проверка цвета в точке (827, 745) ~ 0x00A5F4
;                 → покупка другого пака
;
;   После проверки и первого клика — 3 клика подтверждения
;   в точке (975, 533), затем пауза, и цикл повторяется
;   в том же режиме, пока не остановишь.
;
;   Управление:
;     F1  — запустить режим 1
;     F2  — запустить режим 2
;     Esc — остановить и полностью закрыть скрипт
;
; ============================================================

CoordMode "Mouse", "Screen"
CoordMode "Pixel", "Screen"

running := false

; === GUI-индикатор ===
statusGui := Gui("-Caption +AlwaysOnTop +ToolWindow +E0x20")
statusGui.BackColor := "1e1e1e"
statusGui.SetFont("s10 cWhite", "Consolas")

titleText := statusGui.AddText("x10 y10 w340 cYellow", "Sell Lemons — Auto Buyer (Halloween 2026)")
descText := statusGui.AddText("x10 y32 w340 cSilver",
    "Режим 1: White Common Pack (568,743 / 0xF2F2F2)`n"
    . "Режим 2: другой пак (827,745 / 0x00A5F4)`n"
    . "Проверка цвета → клик → 3 клика подтверждения → пауза")

statusText := statusGui.AddText("x10 y85 w340 cWhite", "Статус: остановлен")
actionText := statusGui.AddText("x10 y108 w340 cWhite", "Действие: —")
hintText := statusGui.AddText("x10 y135 w340 cGray", "F1 — режим 1    F2 — режим 2    Esc — выход")

screenW := A_ScreenWidth
screenH := A_ScreenHeight
statusGui.Show("x" (screenW - 360) " y" (screenH - 190) " w340 h165 NoActivate")

UpdateStatus(text) {
    global statusText
    statusText.Text := "Статус: " text
}

UpdateAction(text) {
    global actionText
    actionText.Text := "Действие: " text
}

; F1 — запустить режим 1
F1:: {
    global running
    if running
        return
    running := true
    UpdateStatus("работает (режим 1)")
    MainLoop(1)
}

; F2 — запустить режим 2
F2:: {
    global running
    if running
        return
    running := true
    UpdateStatus("работает (режим 2)")
    MainLoop(2)
}

MainLoop(mode) {
    global running

    if mode = 1 {
        checkX := 568, checkY := 743, checkColor := 0xF2F2F2
    } else {
        checkX := 872, checkY := 745, checkColor := 0x00A5F4
    }

    while running {

        ; === проверка цвета перед первым кликом ===
        UpdateAction("проверка цвета (режим " mode ")...")
        while !IsColorMatch(checkX, checkY, checkColor, 15) {
            if !running
                break 2
            Sleep 100
        }

        UpdateAction("клик (покупка, режим " mode ")")
        ClickAt(checkX, checkY)
        if !running
            break

        UpdateAction("ожидание...")
        Sleep RandRange(800, 1200)
        if !running
            break

        UpdateAction("клик 1/3 (подтверждение)")
        ClickAt(975, 533)
        if !running
            break
        Sleep RandRange(400, 700)

        UpdateAction("клик 2/3 (подтверждение)")
        ClickAt(975, 533)
        if !running
            break
        Sleep RandRange(400, 700)

        UpdateAction("клик 3/3 (подтверждение)")
        ClickAt(975, 533)
        if !running
            break

        UpdateAction("пауза после цикла...")
        Sleep RandRange(1500, 2500)
    }
    UpdateAction("—")
}

; === плавное движение мыши (ease in-out, без телепорта) ===
MoveMouseSmooth(targetX, targetY, duration := 300) {
    MouseGetPos &startX, &startY
    steps := 30
    Loop steps {
        t := A_Index / steps
        easedT := t < 0.5 ? 2*t*t : 1 - ((-2*t + 2)**2) / 2
        newX := startX + (targetX - startX) * easedT
        newY := startY + (targetY - startY) * easedT
        MouseMove Round(newX), Round(newY), 0
        Sleep duration / steps
    }
}

ClickAt(x, y) {
    MoveMouseSmooth(x, y, RandRange(250, 450))
    Sleep RandRange(100, 250)
    Click
    Sleep RandRange(100, 200)
}

; === проверка цвета с допуском по каждому каналу ===
IsColorMatch(x, y, targetColor, tolerance := 10) {
    color := PixelGetColor(x, y, "RGB")
    targetR := (targetColor >> 16) & 0xFF
    targetG := (targetColor >> 8) & 0xFF
    targetB := targetColor & 0xFF
    r := (color >> 16) & 0xFF
    g := (color >> 8) & 0xFF
    b := color & 0xFF
    return Abs(r - targetR) <= tolerance
        && Abs(g - targetG) <= tolerance
        && Abs(b - targetB) <= tolerance
}

RandRange(min, max) {
    return Random(min, max)
}

Esc:: {
    global running
    running := false
    statusGui.Destroy()
    ExitApp()
}