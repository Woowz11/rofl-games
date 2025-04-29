#include <windows.h>
#include <iostream>
#include <cmath>

// Структура для представления 3D точки
struct Point3D {
    float x, y, z;
};

// Структура для представления 2D точки
struct Point2D {
    int x, y;
};

// Функция для преобразования 3D точки в 2D проекцию
Point2D Project(const Point3D& point, int width, int height, float scale) {
    Point2D projectedPoint;
    projectedPoint.x = static_cast<int>(width / 2 + scale * point.x / point.z);
    projectedPoint.y = static_cast<int>(height / 2 - scale * point.y / point.z);
    return projectedPoint;
}

// Функция для вращения точки вокруг осей
Point3D Rotate(const Point3D& point, float angleX, float angleY, float angleZ) {
    Point3D rotatedPoint;
    float cosX = cos(angleX);
    float sinX = sin(angleX);
    float cosY = cos(angleY);
    float sinY = sin(angleY);
    float cosZ = cos(angleZ);
    float sinZ = sin(angleZ);

    rotatedPoint.x = point.x * cosY * cosZ + point.y * (cosY * sinZ + sinY * sinX) + point.z * (sinY * sinZ - cosY * sinX);
    rotatedPoint.y = point.x * cosX * sinZ + point.y * (cosX * cosZ - sinX * sinY * sinZ) + point.z * (cosX * sinY * sinZ + cosZ * sinX);
    rotatedPoint.z = point.x * sinX * sinY + point.y * cosX * sinY + point.z * cosX * cosY;

    return rotatedPoint;
}

// Функция для рисования куба
void DrawCube(HDC hdc, int width, int height, float angleX, float angleY, float angleZ, float scale) {
    Point3D vertices[8] = {
        {-1, -1, -1}, {1, -1, -1}, {1, 1, -1}, {-1, 1, -1},
        {-1, -1, 1}, {1, -1, 1}, {1, 1, 1}, {-1, 1, 1}
    };

    int edges[12][2] = {
        {0, 1}, {1, 2}, {2, 3}, {3, 0},
        {4, 5}, {5, 6}, {6, 7}, {7, 4},
        {0, 4}, {1, 5}, {2, 6}, {3, 7}
    };

    HPEN hPen = CreatePen(PS_SOLID, 2, RGB(255, 0, 255));
    SelectObject(hdc, hPen);

    for (int i = 0; i < 8; ++i) {
        vertices[i] = Rotate(vertices[i], angleX, angleY, angleZ);
    }

    for (int i = 0; i < 12; ++i) {
        Point2D p1 = Project(vertices[edges[i][0]], width, height, scale);
        Point2D p2 = Project(vertices[edges[i][1]], width, height, scale);
        MoveToEx(hdc, p1.x, p1.y, NULL);
        LineTo(hdc, p2.x, p2.y);
    }

    DeleteObject(hPen);
}

void Render(HDC hdc, int width, int height, float angleX, float angleY, float angleZ, float scale) {
    DrawCube(hdc, width, height, angleX, angleY, angleZ, scale);
}

void ChangeScreenColor(float angleX, float angleY, float angleZ, float scale) {
    HDC hdcScreen = GetDC(NULL);
    HDC hdcCompatible = CreateCompatibleDC(hdcScreen);
    HBITMAP hBitmap = CreateCompatibleBitmap(hdcScreen, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
    HBITMAP hOldBitmap = (HBITMAP)SelectObject(hdcCompatible, hBitmap);

    Render(hdcCompatible, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), angleX, angleY, angleZ, scale);

    // Используем режим растеризации SRCPAINT для XOR-наложения
    BitBlt(hdcScreen, 0, 0, GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN), hdcCompatible, 0, 0, SRCPAINT);

    SelectObject(hdcCompatible, hOldBitmap);
    DeleteObject(hBitmap);
    DeleteDC(hdcCompatible);
    ReleaseDC(NULL, hdcScreen);
}

typedef void (*MyFunctionPtr)();

int main() {

    HMODULE hModule = LoadLibrary(L"MyFirstDLL.dll");
    if (hModule) {
        MyFunctionPtr MyFunction = (MyFunctionPtr)GetProcAddress(hModule, "MyFunction");
        if (MyFunction) {
            MyFunction();
        }
        else {
            std::cerr << "Failed to get function address: " << GetLastError() << std::endl;
        }
        FreeLibrary(hModule);
    }
    else {
        std::cerr << "Failed to load DLL: " << GetLastError() << std::endl;
    }


    float angleX = 0.0f;
    float angleY = 0.0f;
    float angleZ = 0.0f;
    float scale = 200.0f;

    while (true) {
        angleX += 0.01f;
        angleY += 0.01f;
        angleZ += 0.01f;
        ChangeScreenColor(angleX, angleY, angleZ, scale);
        Sleep(16); // Примерно 60 FPS
    }

    std::cout << "Rendering 3D cube!" << std::endl;
    return 0;
}
