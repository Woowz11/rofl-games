extern _printf
global _main

section .data
msg: db "HELLO!!!!!!! welocome to nasm experince", 10, 0

section .text
_main:
    push msg
    call _printf
    add esp,4   
    ret