includelib kernel32.lib   ; подключаем библиотеку kernel32.lib
 
; подключаем функции WriteFile и GetStdHandle
extrn WriteFile: PROC
extrn GetStdHandle: PROC
.code
text byte "Hello METANIT.COM!"    ; выводимая строка
 
main proc
  sub  rsp, 40   ; Для параметров функций WriteFile и GetStdHandle резервируем 40 байт (5 параметров по 8 байт)
  mov  rcx, -11  ; Аргумент для GetStdHandle - STD_OUTPUT
  call GetStdHandle ; вызываем функцию GetStdHandle
  mov  rcx, rax     ; Первый параметр WriteFile - в регистр RCX помещаем дескриптор файла - консоли
  lea  rdx, text    ; Второй параметр WriteFile - загружаем указатель на строку в регистр RDX
  mov  r8d, 18      ; Третий параметр WriteFile - длина строки для записи в регистре R8D 
  xor  r9, r9       ; Четвертый параметр WriteFile - адрес для получения записанных байтов
  mov  qword ptr [rsp + 32], 0  ; Пятый параметр WriteFile
  call WriteFile ; вызываем функцию WriteFile
  add  rsp, 40
  ret
main endp
end