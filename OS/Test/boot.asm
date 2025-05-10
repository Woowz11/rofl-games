org 0x7c00
start:
	mov cx, 10
	
	forLoop:
	cmp cx, 0
	je endLoop
	
	push cx
	
	push cx
	call hprint
	
	;mov ax, 0xDEAD
	;push ax
	;call hprint
	
	call newline
	
	pop cx
	
	dec cx
	jmp forLoop
	endLoop:
jmp $

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