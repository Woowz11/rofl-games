org 0x7c00
start:

	mov si, sexProfessionalLimitWordOpaHopaDopaBypa
	
	call print
	
	call newline
	
	mov si, sexProfessionalLimitWordOpaHopaDopaBypa2
	
	call print
	
	call newline
	
	mov si, sexProfessionalLimitWordOpaHopaDopaBypa
	
	call print
	
	call newline
	
	mov si, sexProfessionalLimitWordOpaHopaDopaBypa3
	
	call print

jmp $

sexProfessionalLimitWordOpaHopaDopaBypa db 'sosat ia novii razrabotchik vindi, call me +7-925-631-74-12', 0
sexProfessionalLimitWordOpaHopaDopaBypa2 db 'ti ymer', 0
sexProfessionalLimitWordOpaHopaDopaBypa3 db 'Вариант 2: установить\n через MSYS2 Установи MSYS2 Запусти MSYS2 MinGW 32-bit или MSYS2 UCRT64', 0

print:
	strLoop:
	cmp byte [si], 0
	je endStrLoop
	
	mov al, byte [si]
	mov ah, 0x0e
	int 0x10
	inc si
	jmp strLoop
	endStrLoop:
ret

hprint:
	push bp
	mov bp, sp
	
	mov bx, 4
	.loop:
	cmp bx, 0
	jz .end
	dec bx
	mov ax, 4
	mov cx, 3
	sub cx, bx
	mul cx
	
	mov cx, ax
	mov ax, word [bp+4]
	
	shl ax, cl
	shr ax, 12
	
	cmp al, 10
	jl .num
	mov ah, 55
	jmp .char
	.num:
	mov ah, 48
	.char:
	add al, ah
	mov ah, 0x0e
	int 0x10
	jmp .loop
	.end:
	
	pop bp
ret 2

newline:
	mov ax, 0x0e0a
	int 0x10
	mov ax, 0x0e0d
	int 0x10
ret

times 510-($-$$) db 0

dw 0xAA55