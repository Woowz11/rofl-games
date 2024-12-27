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
static bool withsound = false;
std::map<int, std::string> prikols = {
        {0 , "Fake Yellow (Red & Green)"},
        {1 , "Random Colors"},
        {2 , "Blue-violet Striped Gradient"},
		{3 , "Green Pixels"},
		{4 , "Yellow with Rare Purple"},
		{5 , "Cool Three Color Gradient"},
		{6 , "Red-Green Stripes"},
		{7 , "Metal Wall"},
		{8 , "Green-Purple Gradient"},
		{9 , "Color TV Strips"},
		{10, "Orange-Blue Long Stripes"},
		{11, "Linear Noise"},
		{12, "Wood"}
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
	int number2;
	do {
        std::cout << "===- SO SVUKOM? -===" << std::endl;
		std::cout << "0 - NO, 1 - YES" << std::endl;
        std::cin >> number2;
    } while (number2 < 0 || number2 > 1);
	withsound = (number2==1);
	
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
		static double u = 0;
		static int s = 100; /* 37 - 32767 */
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
				s = 400;
				break;
			case 1:
				for(int i = 0; i < 100; i++){
					int pos = Rand32()%(framesize);
					frame.pixels[pos] = Rand32();
				}
				s = std::round(Rand01()*1000);
				break;
			case 2:
				for(int i = 0; i < 100; i++){
					int pos = (p++)%(framesize);
					double d = (double)pos/(double)framesize;
					double d2 = (double)i/100;
					frame.pixels[pos] = ToColor(d,0,d2);
				}
				s+=10;
				if(s>1000){
					s = 100;
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
			case 5:
				for(int i = 0; i < 100; i++){
					if(Rand01() > 0.5){
						p -= 25;
					}else{
						p += 100;
					}
					int pos = p%(framesize);
					double d = (double)p/(double)framesize;
					double d2 = (double)pos/(double)framesize;
					frame.pixels[pos] = ToColor(d,std::abs(d-0.5),d2);
				}
				break;
			case 6:
				for(int i = 0; i < 100; i++){
					double r = 0;
					double g = 0;
					if(Rand01() > 0.5){
						p -= 100;
						r = 1;
					}else{
						p += 10000;
						g = 1;
					}
					int pos = p%(framesize);
					frame.pixels[pos] = ToColor(r,g,0);
				}
				break;
			case 7:
				for(int i = 0; i < 100; i++){
					if(Rand01() > 0.99){
						p++;
					}else{
						p *= 2;
						u += 0.1;
						if(u > 1){
							u = 0;
						}
					}
					int pos = p%(framesize);
					double d = (double)pos/(double)framesize;
					frame.pixels[pos] = ToColor(u,d,d);
				}
				break;
			case 8:
				for(int i = 0; i < 100; i++){
					int pos = Rand32()%(framesize);
					if(pos%8){
						u += 0.1;
					}else{
						u -= 0.1;
					}
					if(u > 1){
						u = 0;
					}
					if(u < 0){
						u = 1;
					}
					double d = (double)pos/(double)framesize;
					frame.pixels[pos] = ToColor(d,u,d);
				}
				break;
			case 9:
				for(int i = 0; i < 100; i++){
					if(Rand01() > 0.99){
						p = Rand32();
						u *= 1.1;
					}else{
						p++;
						u += 0.1;
					}
					int pos = p%(framesize);
					if(u>1){
						u = 0;
					}
					double d = (double)pos/(double)framesize;
					frame.pixels[pos] = ToColor(u,d,(double)pos/(double)height);
				}
				break;
			case 10:
				for(int i = 0; i < framesize; i++){
					if(Rand01() > 0.5){
						u += 0.01;
					}else{
						u -= 0.01;
					}
					if(u > 1){
						u = 0;
					}
					if(u < 0){
						u = 1;
					}
					frame.pixels[i] = ToColor(u,std::abs(u-0.5),std::abs(u-1));
				}
				break;
			case 11:
				for(int i = 0; i < framesize; i++){
					if(Rand01() > 0.5){
						u += 0.001;
					}else{
						u -= 0.001;
					}
					if(u > 1){
						u = 1;
					}
					if(u < 0){
						u = 0;
					}
					frame.pixels[i] = ToColor(u,u,u);
				}
				break;
			case 12:
				for(int i = 0; i < framesize; i++){
					int pos = i;
					double g = 0;
					double b = 0;
					double rand_ = Rand01();
					if(rand_ > 0.75){
						pos++;
						u += 0.01;
					}else if(rand_ > 0.5){
						pos--;
						b = 1;
					}
					else if(rand_ > 0.25){
						pos *= 2;
						g = 1;
					}
					else{
						pos = Rand32()%(framesize);
						g = 0.5;
						b = 0.5;
					}
					if(u > 1){
						u = Rand01();
					}
					frame.pixels[i] = ToColor(u,g,b);
				}
				break;
			default:
				break;
		}

		if(s>32767){s = 0;}
		if(s<37){s = 32767;}
		if(withsound){
			Beep( s, 200 );
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