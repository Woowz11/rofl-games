.global _start
_start:
    xor %rbp, %rbp
    mov %rsp, %rdi
    call main
    mov %rax, %rdi
    mov $60, %rax   # syscall: exit
    syscall