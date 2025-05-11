#include <iostream>
#include <fstream>
#include <string>
#include <regex>
#include <vector>
#include <utility>

std::string convertLine(const std::string& line) {
    std::string l = line;

    std::vector<std::pair<std::regex, std::string>> rules;
    rules.push_back({std::regex("^\\s*\\.globl\\s+"), "global "});
    rules.push_back({std::regex("^\\s*\\.text"), "section .text"});
    rules.push_back({std::regex("^\\s*\\.data"), "section .data"});
    rules.push_back({std::regex("^\\s*\\.bss"), "section .bss"});
    rules.push_back({std::regex("%eax"), "eax"});
    rules.push_back({std::regex("%ebx"), "ebx"});
    rules.push_back({std::regex("%ecx"), "ecx"});
    rules.push_back({std::regex("%edx"), "edx"});
    rules.push_back({std::regex("%esi"), "esi"});
    rules.push_back({std::regex("%edi"), "edi"});
    rules.push_back({std::regex("%esp"), "esp"});
    rules.push_back({std::regex("%ebp"), "ebp"});
    rules.push_back({std::regex("movl"), "mov"});
    rules.push_back({std::regex("addl"), "add"});
    rules.push_back({std::regex("subl"), "sub"});
    rules.push_back({std::regex("imull"), "imul"});
    rules.push_back({std::regex("pushl"), "push"});
    rules.push_back({std::regex("popl"), "pop"});
    rules.push_back({std::regex("retl?"), "ret"});
    rules.push_back({std::regex("jmp"), "jmp"});
    rules.push_back({std::regex("cmpl"), "cmp"});
    rules.push_back({std::regex("je"), "je"});
    rules.push_back({std::regex("jne"), "jne"});
    rules.push_back({std::regex("jg"), "jg"});
    rules.push_back({std::regex("jge"), "jge"});
    rules.push_back({std::regex("jl"), "jl"});
    rules.push_back({std::regex("jle"), "jle"});

    for (size_t i = 0; i < rules.size(); ++i) {
        l = std::regex_replace(l, rules[i].first, rules[i].second);
    }

    return l;
}

int main(int argc, char* argv[]) {
    if (argc != 3) {
        std::cerr << "Использование: c2nasm <вход.s> <выход.asm>\n";
        return 1;
    }

    std::ifstream fin(argv[1]);
    if (!fin) {
        std::cerr << "Ошибка открытия файла: " << argv[1] << "\n";
        return 1;
    }

    std::ofstream fout(argv[2]);
    if (!fout) {
        std::cerr << "Ошибка создания файла: " << argv[2] << "\n";
        return 1;
    }

    std::string line;
    while (std::getline(fin, line)) {
        fout << convertLine(line) << "\n";
    }

    std::cout << "Конвертация завершена. Результат в " << argv[2] << "\n";
    return 0;
}
