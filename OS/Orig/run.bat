nasm boot.asm -f bin -o os.flp
qemu-system-x86_64 -drive format=raw,file=os.flp
pause