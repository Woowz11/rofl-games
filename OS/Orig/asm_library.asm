; Написать символ [al] в консоли
print_char:
	mov ah, 0x0E
	int 0x10
	ret

; Написать символ [al] в консоли, не двигая инпут
print_char_nomove:
	mov ah, 0x09
    mov bl, 0x07
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
	
; Очищает экран
clear_screen:
	mov ah, 0x00
	mov al, 0x03
	int 0x10
	ret