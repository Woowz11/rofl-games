; Цвет консоли
console_color db 0x0F

; Написать символ [al] в консоли
print_char:
	call print_char_nomove
	inc dl
	call set_input_position
	ret

; Написать символ [al] в консоли, не двигая инпут
print_char_nomove:
	mov ah, 0x09
	mov bl, [console_color]
	mov cx, 0x01
	int 0x10
	ret

print_nl:
	mov al, 0x0D
	call print_char
	mov al, 0x0A
	call print_char
	ret

; Написать строку [si] в консоли
print_string:
	lodsb
	or al, al
	jz .done
	call print_char
	jmp print_string
.done:
	ret

; Изменить позицию инпута по x [dl] по y [dh]
set_input_position:
	mov ah, 0x02
	mov bh, 0x00
	int 0x10
	ret
	
; Возвращает позицию инпута по x (dl) по y (dh)
get_input_position:
	mov ah, 0x03
	mov bh, 0x00
	int 0x10
	ret

; Детект нажатия клавиши клавиатуры
keypress:
	mov ah, 0x00
	int 0x16
	ret

; Обработка нажатия в консоли
console_keypress:
	push dx

	call get_input_position

	call keypress
	
	; Клавиша очистки
	cmp al, 0x08
	je .press_backspace
	
	; Перенос на новую строку
	cmp al, 0x0D
	je .press_enter
	
	; Движение инпута
	cmp ah, 0x48
	je .press_up
	cmp ah, 0x50
	je .press_down
	cmp ah, 0x4B
	je .press_left
	cmp ah, 0x4D
	je .press_right
	
	; Доп. клавиши
	cmp ah, 0x3B
	je .press_f1
	cmp ah, 0x3C
	je .press_f2
	cmp ah, 0x3D
	je .press_f3
	cmp ah, 0x3E
	je .press_f4
	cmp ah, 0x3F
	je .press_f5
	
	call print_char_nomove
	call .right
	
	jmp .done
.press_backspace:
	call .left
	mov al, ' '
	call print_char_nomove
	jmp .done
.press_enter:
	call .next
	jmp .done
.press_left:
	call .left
	jmp .done
.press_right:
	call .right
	jmp .done
.press_up:
	call .up
	jmp .done
.press_down:
	call .down
	jmp .done
.press_f1:
	call clear_console
	jmp .done
.press_f2:
	dec byte [console_color]
	call change_screen_color
	jmp .done
.press_f3:
	inc byte [console_color]
	call change_screen_color
	jmp .done
.press_f4:
	dec byte [console_color]
	push dx
	
	mov al, 1
	mov dh, 1
	mov dl, 0
	call set_input_position
	call print_char_nomove
	mov dl, 79
	call set_input_position
	call print_char_nomove
	
	pop dx
	call set_input_position
	jmp .done
.press_f5:
	inc byte [console_color]
	push dx
	
	mov al, 1
	mov dh, 1
	mov dl, 0
	call set_input_position
	call print_char_nomove
	mov dl, 79
	call set_input_position
	call print_char_nomove
	
	pop dx
	call set_input_position
	jmp .done
.next:
	mov dl, 0
	call .down
	ret
.back:
	mov dl, 79
	call .up
	ret
.left:
	cmp dl, 0
	je .back
	dec dl
	call set_input_position
	ret
.right:
	cmp dl, 79
	je .next
	inc dl
	call set_input_position
	ret
.up:
	cmp dh, 2
	je .return
	dec dh
	call set_input_position
	ret
.down:
	cmp dh, 24
	je .return
	inc dh
	call set_input_position
	ret
.done:
	pop dx
	ret
.return:
	ret

; Очищает консоль
string_startmessage db 'Welcome to Woowz-Test OS', 0
clear_console:
	call clear_screen

	mov cx, 28
	call .space
	mov si, string_startmessage
	call print_string
	mov cx, 28
	call .space
	
	mov al, 2
	call print_char
	mov cx, 78
.line:
	push cx
	mov al, '='
	call print_char
	pop cx
	dec cx
	cmp cx, 0
	jne .line
	
	mov al, 2
	call print_char
	
	mov dl, 0
	mov dh, 2
	call set_input_position
	
	ret
.space:
	push cx
	mov al, ' '
	call print_char
	pop cx
	dec cx
	cmp cx, 0
	jne .space
	
	ret

; Очищает экран
clear_screen:
	mov dl, 0
	mov dh, 0
	call set_input_position

	mov ah, 0x00
	mov al, 0x03
	int 0x10
	ret

; Изменить цвет одного символа, x [dl], y [dh]
change_char_color:
	call set_input_position
	mov ah, 0x08
	int 0x10
	call print_char_nomove
	ret

; Изменить цвет всех символов на экране
change_screen_color:
	push dx

	mov dl, -1
	mov dh, -1
	call set_input_position
.cycle:
	inc dl
	call change_char_color
	cmp dl, 79
	jne .cycle
	
	mov dl, -1
	inc dh
	cmp dh, 25
	jne .cycle
	
	pop dx
	call set_input_position
	
	ret