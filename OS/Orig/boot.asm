org 0x7C00

start_os:
	call clear_screen

	mov al, 1
	call print_char
	mov si, string_startmessage
	call print_string
	mov al, 1
	call print_char
	
	mov si, string_line
	call print_string
	
	jmp cycle_os
	
	jmp end_os
	
cycle_os:
	call console_keypress

	jmp cycle_os
	
%include "asm_library.asm"

string_startmessage db  '                        Welcome to Woowz-Test OS!                             ', 0
string_line db         '================================================================================'

end_os:

times 510-($-$$) db 0
dw 0XAA55