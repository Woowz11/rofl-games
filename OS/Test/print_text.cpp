#include <iostream>

// Функция main, которая будет вызвана из ассемблерного кода
extern "C" int main() {
    std::cout << "Omg what is at???? runnig cpp from nasm!!!!!" << std::endl;
    return 0;
}
