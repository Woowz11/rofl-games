nasm -fwin32 woowzlib.asm -o woowzlib.obj
nasm -fwin32 test.asm -o test.obj
gcc test.obj woowzlib.obj -o test.exe
pause