org 0x7C00

start_os:
	call clear_console
	
	call change_screen_color
	
	jmp cycle_os
	
	jmp end_os
	
cycle_os:
	call console_keypress

	jmp cycle_os
	
%include "asm_library.asm"

end_os:

times 510-($-$$) db 0
dw 0XAA55