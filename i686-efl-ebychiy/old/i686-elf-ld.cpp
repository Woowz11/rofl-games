#include <iostream>
#include <fstream>
#include <vector>
#include <cstring>

struct Elf32_Ehdr {
    unsigned char e_ident[16];
    uint16_t e_type, e_machine;
    uint32_t e_version;
    uint32_t e_entry, e_phoff, e_shoff;
    uint32_t e_flags;
    uint16_t e_ehsize, e_phentsize, e_phnum;
    uint16_t e_shentsize, e_shnum, e_shstrndx;
};

struct Elf32_Phdr {
    uint32_t p_type, p_offset;
    uint32_t p_vaddr, p_paddr;
    uint32_t p_filesz, p_memsz;
    uint32_t p_flags, p_align;
};

int main(int argc, char* argv[]) {
    if (argc < 4 || std::string(argv[2]) != "-o") {
        std::cerr << "Usage: i686-elf-ld input.o -o output.elf\n";
        return 1;
    }

    std::ifstream in(argv[1], std::ios::binary);
    if (!in) {
        std::cerr << "Cannot open input object file.\n";
        return 1;
    }

    std::ofstream out(argv[3], std::ios::binary);

    // Загрузка .text
    in.seekg(0, std::ios::end);
    size_t size = in.tellg();
    in.seekg(0);
    std::vector<char> text(size);
    in.read(text.data(), size);

    // ELF header
    Elf32_Ehdr ehdr = {};
    memcpy(ehdr.e_ident, "\x7f""ELF\x01\x01\x01", 7);
    ehdr.e_type = 2;
    ehdr.e_machine = 3; // x86
    ehdr.e_version = 1;
    ehdr.e_entry = 0x100000;
    ehdr.e_phoff = sizeof(Elf32_Ehdr);
    ehdr.e_ehsize = sizeof(Elf32_Ehdr);
    ehdr.e_phentsize = sizeof(Elf32_Phdr);
    ehdr.e_phnum = 1;

    // Program header
    Elf32_Phdr phdr = {};
    phdr.p_type = 1;
    phdr.p_offset = 0x1000;
    phdr.p_vaddr = 0x100000;
    phdr.p_paddr = 0x100000;
    phdr.p_filesz = size;
    phdr.p_memsz = size;
    phdr.p_flags = 5;
    phdr.p_align = 0x1000;

    // Пишем ELF
    out.write((char*)&ehdr, sizeof(ehdr));
    out.write((char*)&phdr, sizeof(phdr));

    // Паддинг до 0x1000
    std::vector<char> pad(0x1000 - out.tellp(), 0);
    out.write(pad.data(), pad.size());

    // .text
    out.write(text.data(), size);

    return 0;
}
