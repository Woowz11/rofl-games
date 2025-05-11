#include <iostream>
#include <fstream>
#include <string>

int main(int argc, char* argv[]) {
    if (argc < 3) {
        std::cerr << "Usage: i686-elf-gcc <input.c> -o <output.s>\n";
        return 1;
    }

    std::ifstream in(argv[1]);
    if (!in) {
        std::cerr << "Can't open input file.\n";
        return 1;
    }

    std::string line;
    bool return_found = false;
    int value = 0;

    while (std::getline(in, line)) {
        if (line.find("return") != std::string::npos) {
            size_t pos = line.find("return") + 6;
            value = std::stoi(line.substr(pos));
            return_found = true;
        }
    }

    if (!return_found) {
        std::cerr << "No return statement found.\n";
        return 1;
    }

    std::ofstream out(argv[3]);
    out << "section .text\n"
           "global _start\n"
           "_start:\n"
           "    mov eax, " << value << "\n"
           "    ret\n";

    return 0;
}
