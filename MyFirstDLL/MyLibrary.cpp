// MyLibrary.cpp
#include "pch.h" // �������� ��� ������
#include <windows.h>

extern "C" __declspec(dllexport) void MyFunction() {
    MessageBox(NULL, L"Hello from DLL!", L"Message", MB_OK);
}
