includelib kernel32.lib

extrn WriteFile: proc
extrn GetStdHandle: proc

.code
text byte "Woowz assembler jopa"

main proc
	sub rsp, 40
	mov rcx, -11
	call GetStdHandle
	mov rcx, rax
	lea rdx, text
	mov r8d, 20 ; тут надо указать кол-во символов в text
	xor r9, r9
	mov qword ptr [rsp + 32], 0
	call WriteFile
	add rsp, 40
	
	ret
	
main endp

end