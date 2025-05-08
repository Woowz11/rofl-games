#include <SDL3/SDL.h>
#include <SDL3_image/SDL_image.h>
#include <stdio.h>
#include <string.h>
#include <limits.h>

#include <Windows.h>

int main(int argc, char* argv[])
{
    if (SDL_Init(SDL_INIT_VIDEO) < 0) {
        printf("SDL_Init Error: %s\n", SDL_GetError());
    }

    SDL_Window* window = SDL_CreateWindow("Ayy", 640, 480, SDL_WINDOW_RESIZABLE);
    if (window == NULL) {
        printf("SDL_CreateWindow Error: %s\n", SDL_GetError());
        SDL_Quit();
        return 1;
    }

    SDL_Renderer* renderer = SDL_CreateRenderer(window, NULL);
    if (renderer == NULL) {
        printf("SDL_CreateRenderer Error: %s\n", SDL_GetError());
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }

   /* if (!SDL_SetRenderVSync(renderer, SDL_RENDERER_VSYNC_ADAPTIVE)) {
        printf("SDL_SetRenderVSync Error: %s\n", SDL_GetError());
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }*/

    char fullPath[MAX_PATH];
    if (_fullpath(fullPath, argv[0], MAX_PATH) == NULL) {
        printf("Failed to get full path of the executable.\n");
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }

    char dirPath[MAX_PATH];
    strcpy_s(dirPath, sizeof(dirPath), fullPath);
    char* lastSlash = strrchr(dirPath, '\\');
    if (lastSlash != NULL) {
        *lastSlash = '\0';
    }
    else {
        printf("Failed to extract directory path.\n");
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }

    printf("Директория запускаемого файла: %s\n", dirPath);

    char imagePath[MAX_PATH];
    snprintf(imagePath, sizeof(imagePath), "%s\\img.png", dirPath);

    SDL_Surface* imageSurface = IMG_Load(imagePath);
    if (imageSurface == NULL) {
        printf("IMG_Load Error: %s\n", SDL_GetError());
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }

    SDL_Texture* imageTexture = SDL_CreateTextureFromSurface(renderer, imageSurface);
    if (imageTexture == NULL) {
        printf("SDL_CreateTextureFromSurface Error: %s\n", SDL_GetError());
        SDL_DestroySurface(imageSurface);
        SDL_DestroyRenderer(renderer);
        SDL_DestroyWindow(window);
        SDL_Quit();
        return 1;
    }

    SDL_DestroySurface(imageSurface);

    int quit = 0;
    SDL_Event e;
    while (!quit) {
        while (SDL_PollEvent(&e) != 0) {
            if (e.type == SDL_EVENT_QUIT) {
                quit = 1;
            }
        }

        // Очистка рендерера
        SDL_RenderClear(renderer);

        // Рендеринг текстуры
        SDL_RenderTexture(renderer, imageTexture, NULL, NULL);

        // Обновление экрана
        SDL_RenderPresent(renderer);
    }

    SDL_DestroyTexture(imageTexture);
    SDL_DestroyRenderer(renderer);
    SDL_DestroyWindow(window);
    SDL_Quit();

    return 0;
}