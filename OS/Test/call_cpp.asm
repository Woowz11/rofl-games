section .data
    ; Здесь можно определить данные, если необходимо

section .text
    global _mainASM
    extern _main  ; Объявляем внешнюю функцию main из C++

_mainASM:
    call _main   ; Вызываем функцию main из C++
    mov eax, 1   ; Системный вызов для выхода
    int 0x80     ; Вызов прерывания
