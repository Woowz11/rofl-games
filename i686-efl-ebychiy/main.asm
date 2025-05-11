	.file	"main.c"
	.def	___main;	.scl	2;	.type	32;	.endef
section .text
global _main
	.def	_main;	.scl	2;	.type	32;	.endef
_main:
LFB0:
	.cfi_startproc
	push	ebp
	.cfi_def_cfa_offset 8
	.cfi_offset 5, -8
	mov	esp, ebp
	.cfi_def_cfa_register 5
	andl	$-16, esp
	sub	$16, esp
	call	___main
	mov	$2, 12(esp)
	mov	$-100, 8(esp)
	mov	8(esp), eax
	imul	12(esp), eax
	mov	12(esp), edx
	mov	edx, ecx
	sub	8(esp), ecx
	cltd
	idivl	ecx
	mov	eax, edx
	mov	12(esp), eax
	add	edx, eax
	leave
	.cfi_restore 5
	.cfi_def_cfa 4, 4
	ret
	.cfi_endproc
LFE0:
	.ident	"GCC: (MinGW.org GCC-6.3.0-1) 6.3.0"
