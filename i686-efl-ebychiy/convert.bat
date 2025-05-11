gcc -m32 -S -o main.s main.c
c2nasm.exe main.s main.asm
nasm -f win32 main.asm -o main.o
ld -m pe-i386 -o main.exe main.o
pause