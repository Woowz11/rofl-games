; Analyzed from Woowava-Analyze 0.1 | Converted from Woowava-to-nasm 0.1
org 0x7C00

__start__:
	call start
	jmp __end__

%include "woowava.asm"

char_w db 'w'
char_o db 'o'
char_z db 'z'
char_1 db '1'

start:
	mov al, [char_w]
	call base_print_char
	mov al, [char_o]
	call base_print_char
	mov al, [char_o]
	call base_print_char
	mov al, [char_w]
	call base_print_char
	mov al, [char_z]
	call base_print_char
	mov al, [char_1]
	call base_print_char
	mov al, [char_1]
	call base_print_char
	mov al, ' '
	call base_print_char
	; start();
	ret

__end__:

times 510-($-$$) db 0
dw 0XAA55