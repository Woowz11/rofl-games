; Первый скрипт системы

org 0x7E00

test_:
	mov si, welcome_message
	call print_string

welcome_message    db 'Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Wee to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!Welcome to Woowz-Test-2 OS boot!'         , 0

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

times 512-($-$$) db 0