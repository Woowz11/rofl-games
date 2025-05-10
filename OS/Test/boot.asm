org 0x7c00
start:

	mov ah, 0x00
	int 0x16
	mov [pressedkey], al
	
	cmp al, 13
	je pressed_enter
	
	mov ah, 0x0E
	mov al, [pressedkey]
	int 0x10

jmp start

pressed_enter:
	call newline
	jmp start

pressedkey db 0
hellostring db 'PRINT TEXT NIGGA:', 0

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