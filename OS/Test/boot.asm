org 0x7C00

start:
	; графический режим (320x200, 256 цветов)
	mov ax, 0x13
	int 0x10
	
	jmp cycle

cycle:
	call render
	
	; задержка 1 микросекунда
	mov ah, 0x86
    mov cx, 0x00
    mov dx, 0x01
    int 0x15
	
	jmp cycle

render:
	;mov al, 4
	;call drawbackground

	;mov cx, 50
	;mov dx, 50
	;mov al, 5
	;call drawpixel
	
	push cx
	push dx
	
	mov cx, 320
	doLoopX2:
		cmp cx, -1
		je endLoopX2
		
		mov dx, 200
		doLoopY2:
			cmp dx, -1
			je endLoopY2
			
			mov al, cl
			push cx
			push dx
			call drawpixel
			pop dx
			pop cx
			
			dec dx
			jmp doLoopY2
		endLoopY2:
		
		dec cx
		jmp doLoopX2
	endLoopX2:
	
	pop dx
	pop cx
ret

; [рисует задний фон] al - цвет фона
drawbackground:
	push cx
	push dx
	
	mov cx, 320
	doLoopX:
		cmp cx, -1
		je endLoopX
		
		mov dx, 200
		doLoopY:
			cmp dx, -1
			je endLoopY
			
			push cx
			push dx
			call drawpixel
			pop dx
			pop cx
			
			dec dx
			jmp doLoopY
		endLoopY:
		
		dec cx
		jmp doLoopX
	endLoopX:
	
	pop dx
	pop cx
ret

; [рисует пиксель] cx - x координата, dx - y координата, al - цвет пикселя
drawpixel:
	push ax
	push bx

	mov ax, 0xA000
	mov es, ax
	mov di, 0
	
	mov ax, dx
	mov bx, 320
	mul bx
	add ax, cx
	mov di, ax
	
	pop bx
	pop ax
	
	mov [es:di], al
ret

times 510-($-$$) db 0
dw 0XAA55