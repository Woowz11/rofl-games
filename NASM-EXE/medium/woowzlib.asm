extern _system
extern _exit
extern _printf

section .text
global wl_exit
global wl_print
global wl_clearscreen

cls_command db "cls", 0

; Завершает работу приложения
wl_exit:
	push 0
	call _exit
	ret

; Выводит текст в консоль | push (eax или db) -> переменная, сообщение
wl_print:
	push ebp
    mov ebp, esp
    push dword [ebp + 8]
    call _printf
    add esp, 4
    mov esp, ebp
    pop ebp
	ret

wl_clearscreen:
	push cls_command
	call _system
	add esp, 4
	ret