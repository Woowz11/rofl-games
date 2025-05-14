i686-w64-mingw32-gcc -m32 -c kernel.cpp -o trash/kernel.o
i686-w64-mingw32-objcopy -O binary -j .text trash/kernel.o root/kernel.bin

nasm boot.asm -f bin -o root/boot.bin

genisoimage -o os.iso -b boot.bin -no-emul-boot -boot-load-size 4 root/
qemu-system-x86_64 -cdrom os.iso -boot order=d -m 512M
pause