; Analyzed from Woowava-Analyze 0.1 | Converted from Woowava-to-nasm 0.1
; Ванильная библиотека функций Woowava 0.0.0

; Пишет в консоль один символ [a8]
base_print_char:
	mov ah, 0x0E
	int 0x10
	ret