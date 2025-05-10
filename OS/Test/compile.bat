@echo on

nasm -f elf32 call_cpp.asm -o call_cpp.o

g++ -c print_text.cpp -o print_text.o

g++ call_cpp.o print_text.o -o out.exe

pause
