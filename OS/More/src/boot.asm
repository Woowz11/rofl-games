; Запускаемый файл

org 0x7C00

start_os:
	; Установка сегментных регистров
    xor ax, ax
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 0x7C00

	; Очистка фона
	mov ah, 0x00
	mov al, 0x03
	int 0x10

	; Приветственное сообщение
	mov si, welcome_message
	call print_string
	call print_nl

	; Загрузка диска
	call load_disk
	
	jmp end_os

; Загрузка диска
load_disk:
	mov ah, 0x02
	mov al, 1
	mov ch, 0
	mov cl, 2
	mov dh, 0
	mov dl, 0
	mov bx, 0x7E00
	int 0x13
	
	jc disk_error
	
	jmp 0x7E00
	
	ret

; Обработка ошибки загрузки диска
disk_error:
	mov si, disk_error_message
	call print_string
	jmp end_os

welcome_message    db 'Welcome to Woowz-Test-2 OS boot!'         , 0
disk_error_message db 'An error occurred while loading the disk!', 0

print_char:
	mov ah, 0x0E
	int 0x10
	ret

print_nl:
	mov al, 0x0D
	call print_char
	mov al, 0x0A
	call print_char
	ret

print_string:
	lodsb
	or al, al
	jz .done
	call print_char
	jmp print_string
.done:
	ret
	
end_os:

times 510-($-$$) db 0
dw 0XAA55