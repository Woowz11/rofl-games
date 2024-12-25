#define UNICODE
#define _UNICODE
#include <cmath>
#include <windows.h>
#include <stdbool.h>
#include <stdint.h>
#include <iostream>
#include <map>
#include <string>

static bool quit = false;
static int prikoltype = 0;
std::map<int, std::string> prikols = {
        {0, "Fake Yellow (Red & Green)"},
        {1, "Random Colors"},
        {2, "Blue-violet Striped Gradient"},
		{3, "Green Pixels"},
		{4, "Yellow with Rare Purple"}
    };

struct {
    int width;
    int height;
    uint32_t *pixels;
} frame = {0};

LRESULT CALLBACK WindowProcessMessage(HWND, UINT, WPARAM, LPARAM);
#if RAND_MAX == 32767
#define Rand32() ((rand() << 16) + (rand() << 1) + (rand() & 1))
#else
#define Rand32() rand()
#endif

double Rand01() {
	return (double)rand() / (double)RAND_MAX;
}

unsigned int ToColor(double red, double green, double blue) {
    int r = static_cast<int>(red * 255);
    int g = static_cast<int>(green * 255);
    int b = static_cast<int>(blue * 255);

    unsigned int color = 0xFF000000 | (r << 16) | (g << 8) | b;
    return color;
}

static BITMAPINFO frame_bitmap_info;
static HBITMAP frame_bitmap = 0;
static HDC frame_device_context = 0;

int WINAPI WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, PSTR pCmdLine, int nCmdShow) {
	int number;
    do {
        std::cout << "===- SELECT PRIKOL -===" << std::endl;
		for(const auto& pair : prikols){
			std::cout << pair.first << ": " << pair.second << std::endl;
		}
        std::cin >> number;
    } while (number < 0 || number > prikols.size() - 1);
	prikoltype = number;
	
    const wchar_t window_class_name[] = L"My Window Class";
    static WNDCLASS window_class = { 0 };
    window_class.lpfnWndProc = WindowProcessMessage;
    window_class.hInstance = hInstance;
    window_class.lpszClassName = window_class_name;
    RegisterClass(&window_class);

    frame_bitmap_info.bmiHeader.biSize = sizeof(frame_bitmap_info.bmiHeader);
    frame_bitmap_info.bmiHeader.biPlanes = 1;
    frame_bitmap_info.bmiHeader.biBitCount = 32;
    frame_bitmap_info.bmiHeader.biCompression = BI_RGB;
    frame_device_context = CreateCompatibleDC(0);

    static HWND window_handle;
    window_handle = CreateWindow(window_class_name, L"TestCPlusPlusGame", WS_OVERLAPPEDWINDOW | WS_VISIBLE, CW_USEDEFAULT, CW_USEDEFAULT, 480, 320, NULL, NULL, hInstance, NULL);
    if(window_handle == NULL) { return -1; }

    while(!quit) {
        static MSG message = { 0 };
        while(PeekMessage(&message, NULL, 0, 0, PM_REMOVE)) { DispatchMessage(&message); }

		static unsigned int p = 0;
		int width = frame.width;
		int height = frame.height;
		int framesize = width*height;
		switch (prikoltype)
		{
			case 0:
				for(int i = 0; i < 100; i++){
					int pos = (p++)%(framesize);
					if(pos%2==0){
						frame.pixels[pos] = ToColor(1,0,0);
					}else{
						frame.pixels[pos] = ToColor(0,1,0);
					}
				}
				break;
			case 1:
				for(int i = 0; i < 100; i++){
					int pos = Rand32()%(framesize);
					frame.pixels[pos] = Rand32();
				}
				break;
			case 2:
				for(int i = 0; i < 100; i++){
					int pos = (p++)%(framesize);
					double d = (double)pos/(double)framesize;
					double d2 = (double)i/100;
					frame.pixels[pos] = ToColor(d,0,d2);
				}
				break;
			case 3:
				for(int i = 0; i < 100; i++){
					int pos = ((p++)*5)%(framesize);
					double d = (double)pos/(double)framesize;
					double d2 = (double)i/100;
					frame.pixels[pos] = ToColor(0,std::abs(d-d2),0);
				}
				break;
			case 4:
				for(int i = 0; i < 100; i++){
					p += 2;
					int pos = p%(framesize);
					if(Rand01() > 0.99){
						frame.pixels[pos] = ToColor(1,0,1);
					}else{
						frame.pixels[pos] = ToColor(1,1,0);
					}
				}
				break;
			default:
				break;
		}

        InvalidateRect(window_handle, NULL, FALSE);
        UpdateWindow(window_handle);
    }

    return 0;
}

LRESULT CALLBACK WindowProcessMessage(HWND window_handle, UINT message, WPARAM wParam, LPARAM lParam) {
    switch(message) {
        case WM_QUIT:
        case WM_DESTROY: {
            quit = true;
        } break;

        case WM_PAINT: {
            static PAINTSTRUCT paint;
            static HDC device_context;
            device_context = BeginPaint(window_handle, &paint);
            BitBlt(device_context,
				paint.rcPaint.left, paint.rcPaint.top,
				paint.rcPaint.right - paint.rcPaint.left, paint.rcPaint.bottom - paint.rcPaint.top,
				frame_device_context,
				paint.rcPaint.left, paint.rcPaint.top,
				SRCCOPY);
            EndPaint(window_handle, &paint);
        } break;

        case WM_SIZE: {
            frame_bitmap_info.bmiHeader.biWidth  = LOWORD(lParam);
            frame_bitmap_info.bmiHeader.biHeight = HIWORD(lParam);

            if(frame_bitmap) DeleteObject(frame_bitmap);
            frame_bitmap = CreateDIBSection(NULL, &frame_bitmap_info, DIB_RGB_COLORS, (void**)&frame.pixels, 0, 0);
            SelectObject(frame_device_context, frame_bitmap);

            frame.width =  LOWORD(lParam);
            frame.height = HIWORD(lParam);
        } break;

        default: {
            return DefWindowProc(window_handle, message, wParam, lParam);
        }
    }
    return 0;
}