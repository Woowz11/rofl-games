; Компактная система "MamaYamiya" на NASM для загрузочного сектора
; Компилировать: nasm -f bin script.asm -o os.flp

[BITS 16]               ; 16-битный режим
[ORG 0x7C00]           ; Загрузчик загружается по адресу 0x7C00

start:
    ; Инициализация
    cli
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7C00
    sti

    ; Очистка экрана
    mov ax, 0x0003     ; 80x25 текстовый режим
    int 0x10

    ; Вывод приветствия
    mov si, welcome
    call print

main_loop:
    ; Вывод меню
    mov si, menu
    call print
    
    ; Ожидание клавиши
    mov ah, 0x00
    int 0x16

    ; Обработка команд
    cmp al, '1'
    je info
    cmp al, '2'
    je memory
    cmp al, '3'
    je halt_system
    jmp main_loop

info:
    mov si, info_msg
    call print
    call wait_key
    jmp main_loop

memory:
    mov si, mem_msg
    call print
    
    ; Простой тест памяти
    mov bx, 0x1000
    mov cx, 100        ; Тестируем 100 байт
test_loop:
    mov byte [bx], 0xAA
    cmp byte [bx], 0xAA
    jne mem_fail
    inc bx
    loop test_loop
    
    mov si, mem_ok
    call print
    call wait_key
    jmp main_loop

mem_fail:
    mov si, mem_fail_msg
    call print
    call wait_key
    jmp main_loop

halt_system:
    mov si, goodbye
    call print
    cli
    hlt

; Функция вывода строки
print:
    push ax
    push si
    mov ah, 0x0E
.loop:
    lodsb
    cmp al, 0
    je .done
    int 0x10
    jmp .loop
.done:
    pop si
    pop ax
    ret

; Ожидание нажатия клавиши
wait_key:
    mov si, press_key
    call print
    mov ah, 0x00
    int 0x16
    ret

; Сообщения (короткие)
welcome     db 13,10,'=== MAMA YAMIYA OS ===',13,10,0
menu        db 13,10,'1-Info 2-Memory 3-Exit',13,10,'> ',0
info_msg    db 13,10,'MamaYamiya v1.0',13,10,'Simple OS',13,10,0
mem_msg     db 13,10,'Testing memory...',13,10,0
mem_ok      db 'Memory OK!',13,10,0
mem_fail_msg db 'Memory FAIL!',13,10,0
press_key   db 'Press key...',13,10,0
goodbye     db 13,10,'Goodbye!',13,10,0

; Заполнение до 510 байт
times 510-($-$$) db 0

; Сигнатура загрузочного сектора
dw 0xAA55