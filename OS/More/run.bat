nasm src/boot.asm -f bin -o root/boot.bin
nasm src/start.asm -f bin -o root/start.bin

genisoimage -o os.iso -b boot.bin -no-emul-boot -boot-load-size 4 root/
qemu-system-x86_64 -cdrom os.iso -drive file=memory.qcow2,format=qcow2 -boot order=d -m 512M
pause