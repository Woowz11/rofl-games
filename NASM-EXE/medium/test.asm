extern wl_print
extern wl_exit
extern wl_clearscreen

global _main

msg: db "kaka", 0
msg2: db "mega popo", 0

section .text
_main:
    call wl_clearscreen
	
	push msg2
    call wl_print
	
	call wl_exit