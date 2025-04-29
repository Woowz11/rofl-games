// MyLibrary.cpp
#include "pch.h" // Добавьте эту строку
#include <windows.h>

extern "C" __declspec(dllexport) void MyFunction() {
    MessageBox(NULL, L"Hello from DLL!", L"Message", MB_OK);
}
