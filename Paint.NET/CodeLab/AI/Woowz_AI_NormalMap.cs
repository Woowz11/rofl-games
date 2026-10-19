// Name: Normal Map
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Normal Map
// Version: 1.5
// Desc: Генератор и конвертер карт нормалей с исправленной математикой высот (Черный=Низко, Белый=Высоко), OpenGL, DirectX, SSBump и точным 3D-светом
// Keywords: normal,map,bump,heightmap,opengl,directx,vulkan,ssbump,3d,light,sobel,seamless,converter,ambient
// URL:
// Help:

#region UICode
ListBoxControl FormatMode = 0;            // Выходной формат|OpenGL (Y+)|DirectX / Vulkan (Y-)|SSBump (Valve Source)|Пользовательские цвета|Карта высот (Heightmap)
ListBoxControl HeightSource = 0;          // Источник высоты|Яркость (Luminance)|Красный (Red)|Зеленый (Green)|Синий (Blue)|Альфа (Alpha)
ListBoxControl FilterMethod = 0;          // Фильтр градиентов|Sobel (3x3)|Scharr (High-Precision)|Prewitt (3x3)|Central Difference
DoubleSliderControl HeightStrength = 3.0; // [0.1,50.0] Сила высоты (Strength)
IntSliderControl PreBlur = 0;             // [0,10] Сглаживание карты высот
AngleControl NormalAngle = 0;             // [0,360] Поворот нормалей
CheckboxControl Seamless = false;         // Бесшовная нормаль (Seamless / Wrap)
CheckboxControl InvertHeight = false;     // Инвертировать высоту (По умолчанию: Черный = Низко, Белый = Высоко)
CheckboxControl InvertX = false;        // Инвертировать X (Красный)
CheckboxControl InvertY = false;        // Инвертировать Y (Зеленый)
CheckboxControl InvertZ = false;        // Инвертировать Z (Синий)

// --- Работа с готовой картой нормалей ---
CheckboxControl UseExistingNormalMap = false; // Использовать холст как готовую карту нормалей
ListBoxControl InputNormalFormat = 0;         // Формат входной нормали|OpenGL (Y+)|DirectX / Vulkan (Y-)

// --- Пользовательские цвета (для формата "Пользовательские цвета") ---
ColorWheelControl CustomXColor = ColorBgra.FromBgra(0, 0, 255, 255); // Цвет оси X (Red)
ColorWheelControl CustomYColor = ColorBgra.FromBgra(0, 255, 0, 255); // Цвет оси Y (Green)
ColorWheelControl CustomZColor = ColorBgra.FromBgra(255, 0, 0, 255); // Цвет оси Z (Blue)

// --- 3D-Отладка освещения ---
CheckboxControl EnableLightPreview = false; // Включить 3D-источник света
AngleControl LightAngle = 90;               // [0,360] Направление света (Azimuth)
IntSliderControl LightElevation = 45;       // [0,90] Высота света (Elevation)
ColorWheelControl LightColor = ColorBgra.FromBgra(255, 255, 255, 255); // Цвет источника света
DoubleSliderControl AmbientIntensity = 0.15; // [0.0,1.0] Сила фонового света (Ambient)
DoubleSliderControl LightIntensity = 1.0;   // [0.0,3.0] Яркость света (Diffuse)
DoubleSliderControl SpecularIntensity = 0.5; // [0.0,2.0] Интенсивность бликов (Specular)
CheckboxControl LightOnly = false;          // Отладка: Только свет и тени (без цвета текстуры)
#endregion

private float[] heightMap;
private int mapWidth;
private int mapHeight;

void PreRender(Surface dst, Surface src)
{
    mapWidth = src.Width;
    mapHeight = src.Height;
    int totalPixels = mapWidth * mapHeight;

    if (!UseExistingNormalMap)
    {
        if (heightMap == null || heightMap.Length < totalPixels)
        {
            heightMap = new float[totalPixels];
        }

        // 1. Извлечение карты высот (0.0 = Низко/Темно, 1.0 = Высоко/Светло)
        for (int y = 0; y < mapHeight; y++)
        {
            int rowIdx = y * mapWidth;
            for (int x = 0; x < mapWidth; x++)
            {
                ColorBgra c = src[x, y];
                float h = 0f;

                switch (HeightSource)
                {
                    case 0: // Luminance
                        h = (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
                        break;
                    case 1: h = c.R / 255f; break; // Red
                    case 2: h = c.G / 255f; break; // Green
                    case 3: h = c.B / 255f; break; // Blue
                    case 4: h = c.A / 255f; break; // Alpha
                }

                if (InvertHeight) h = 1.0f - h;
                heightMap[rowIdx + x] = h;
            }
        }

        // 2. Предварительное сглаживание карты высот
        if (PreBlur > 0)
        {
            heightMap = BoxBlur(heightMap, mapWidth, mapHeight, PreBlur, Seamless);
        }
    }
}

private float[] BoxBlur(float[] input, int w, int h, int r, bool wrap)
{
    float[] temp = new float[w * h];
    float[] output = new float[w * h];

    for (int y = 0; y < h; y++)
    {
        int row = y * w;
        for (int x = 0; x < w; x++)
        {
            float sum = 0;
            int count = 0;
            for (int k = -r; k <= r; k++)
            {
                int px = x + k;
                if (wrap) px = ((px % w) + w) % w;
                else px = Math.Max(0, Math.Min(w - 1, px));

                sum += input[row + px];
                count++;
            }
            temp[row + x] = sum / count;
        }
    }

    for (int x = 0; x < w; x++)
    {
        for (int y = 0; y < h; y++)
        {
            float sum = 0;
            int count = 0;
            for (int k = -r; k <= r; k++)
            {
                int py = y + k;
                if (wrap) py = ((py % h) + h) % h;
                else py = Math.Max(0, Math.Min(h - 1, py));

                sum += temp[py * w + x];
                count++;
            }
            output[y * w + x] = sum / count;
        }
    }

    return output;
}

private float GetHeightSample(int x, int y)
{
    if (Seamless)
    {
        x = ((x % mapWidth) + mapWidth) % mapWidth;
        y = ((y % mapHeight) + mapHeight) % mapHeight;
    }
    else
    {
        x = Math.Max(0, Math.Min(mapWidth - 1, x));
        y = Math.Max(0, Math.Min(mapHeight - 1, y));
    }
    return heightMap[y * mapWidth + x];
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    float strength = (float)HeightStrength;

    // Вектор направления света в пространстве OpenGL (0° = Вправо +X, 90° = Вверх +Y)
    float lRadAzimuth = (float)(LightAngle * Math.PI / 180.0);
    float lRadElev = (float)(LightElevation * Math.PI / 180.0);

    float lx = (float)(Math.Cos(lRadElev) * Math.Cos(lRadAzimuth));
    float ly = (float)(Math.Cos(lRadElev) * Math.Sin(lRadAzimuth));
    float lz = (float)Math.Sin(lRadElev);

    // Цвет источника света (нормализован 0.0 - 1.0)
    float lColorR = LightColor.R / 255f;
    float lColorG = LightColor.G / 255f;
    float lColorB = LightColor.B / 255f;

    // Вектор Блинна-Фонга (Half Vector)
    float hx = lx, hy = ly, hz = lz + 1.0f;
    float hLen = (float)Math.Sqrt(hx * hx + hy * hy + hz * hz);
    if (hLen > 1e-5f) { hx /= hLen; hy /= hLen; hz /= hLen; }

    // Матрица поворота нормалей
    float normRad = (float)(-NormalAngle * Math.PI / 180.0);
    float cosN = (float)Math.Cos(normRad);
    float sinN = (float)Math.Sin(normRad);

    // Константы векторов для SSBump (Valve Source Engine)
    float ssb0_x = 0.81649658f, ssb0_y = 0.0f,          ssb0_z = 0.57735027f;
    float ssb1_x = -0.40824829f, ssb1_y = 0.70710678f,  ssb1_z = 0.57735027f;
    float ssb2_x = -0.40824829f, ssb2_y = -0.70710678f, ssb2_z = 0.57735027f;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            float nx = 0f, ny = 0f, nz = 1f;

            if (UseExistingNormalMap)
            {
                // Извлечение нормали напрямую из готового изображения
                ColorBgra c = src[x, y];
                nx = (c.R / 255.0f) * 2.0f - 1.0f;
                ny = (c.G / 255.0f) * 2.0f - 1.0f;
                nz = (c.B / 255.0f) * 2.0f - 1.0f;

                // Если входная нормаль в DirectX, инвертируем Y к OpenGL
                if (InputNormalFormat == 1) 
                {
                    ny = -ny;
                }

                if (InvertX) nx = -nx;
                if (InvertY) ny = -ny;
                if (InvertZ) nz = -nz;

                // Поворот нормали
                if (NormalAngle != 0)
                {
                    float rDx = nx * cosN - ny * sinN;
                    float rDy = nx * sinN + ny * cosN;
                    nx = rDx;
                    ny = rDy;
                }

                // Нормализация
                float lenIn = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (lenIn > 1e-5f)
                {
                    nx /= lenIn;
                    ny /= lenIn;
                    nz /= lenIn;
                }
            }
            else
            {
                // Вывод чистой карты высот
                if (FormatMode == 4)
                {
                    byte hByte = (byte)Math.Max(0, Math.Min(255, heightMap[y * mapWidth + x] * 255f));
                    dst[x, y] = ColorBgra.FromBgra(hByte, hByte, hByte, src[x, y].A);
                    continue;
                }

                // Вычисление градиентов из карты высот
                float dx = 0f, dy = 0f;

                switch (FilterMethod)
                {
                    case 0: // Sobel (3x3)
                        float s00 = GetHeightSample(x - 1, y - 1), s10 = GetHeightSample(x, y - 1), s20 = GetHeightSample(x + 1, y - 1);
                        float s01 = GetHeightSample(x - 1, y),     s21 = GetHeightSample(x + 1, y);
                        float s02 = GetHeightSample(x - 1, y + 1), s12 = GetHeightSample(x, y + 1), s22 = GetHeightSample(x + 1, y + 1);

                        dx = (s20 + 2f * s21 + s22) - (s00 + 2f * s01 + s02);
                        dy = (s02 + 2f * s12 + s22) - (s00 + 2f * s10 + s20);
                        break;

                    case 1: // Scharr (High-Precision)
                        float sc00 = GetHeightSample(x - 1, y - 1), sc10 = GetHeightSample(x, y - 1), sc20 = GetHeightSample(x + 1, y - 1);
                        float sc01 = GetHeightSample(x - 1, y),      sc21 = GetHeightSample(x + 1, y);
                        float sc02 = GetHeightSample(x - 1, y + 1), sc12 = GetHeightSample(x, y + 1), sc22 = GetHeightSample(x + 1, y + 1);

                        dx = (3f * sc20 + 10f * sc21 + 3f * sc22) - (3f * sc00 + 10f * sc01 + 3f * sc02);
                        dy = (3f * sc02 + 10f * sc12 + 3f * sc22) - (3f * sc00 + 10f * sc10 + 3f * sc20);
                        break;

                    case 2: // Prewitt (3x3)
                        float p00 = GetHeightSample(x - 1, y - 1), p10 = GetHeightSample(x, y - 1), p20 = GetHeightSample(x + 1, y - 1);
                        float p01 = GetHeightSample(x - 1, y),     p21 = GetHeightSample(x + 1, y);
                        float p02 = GetHeightSample(x - 1, y + 1), p12 = GetHeightSample(x, y + 1), p22 = GetHeightSample(x + 1, y + 1);

                        dx = (p20 + p21 + p22) - (p00 + p01 + p02);
                        dy = (p02 + p12 + p22) - (p00 + p10 + p20);
                        break;

                    case 3: // Central Difference
                        dx = (GetHeightSample(x + 1, y) - GetHeightSample(x - 1, y)) * 2f;
                        dy = (GetHeightSample(x, y + 1) - GetHeightSample(x, y - 1)) * 2f;
                        break;
                }

                dx *= strength;
                dy *= strength;

                // Поворот нормалей
                if (NormalAngle != 0)
                {
                    float rDx = dx * cosN - dy * sinN;
                    float rDy = dx * sinN + dy * cosN;
                    dx = rDx;
                    dy = rDy;
                }

                // Математически точная ориентация нормалей OpenGL Space:
                nx = -dx;
                ny = dy;
                nz = 1.0f;

                // Если выбран выходной формат DirectX Y-, инвертируем Y
                if (FormatMode == 1)
                {
                    ny = -ny;
                }

                if (InvertX) nx = -nx;
                if (InvertY) ny = -ny;
                if (InvertZ) nz = -nz;

                float len = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 1e-5f)
                {
                    nx /= len;
                    ny /= len;
                    nz /= len;
                }
            }

            // --- 3D-ОТЛАДКА ИСТОЧНИКА СВЕТА ---
            if (EnableLightPreview)
            {
                // Рассеивание (Lambertian Diffuse N * L)
                float NdotL = Math.Max(0f, nx * lx + ny * ly + nz * lz);

                // Зеркальные блики Блинна-Фонга (Specular N * H)
                float NdotH = Math.Max(0f, nx * hx + ny * hy + nz * hz);
                float specFactor = (NdotL > 0f) ? (float)Math.Pow(NdotH, 32) * (float)SpecularIntensity : 0f;

                float ambient = (float)AmbientIntensity;
                float diffFactor = NdotL * (float)LightIntensity;

                float baseR, baseG, baseB;

                // ИСПРАВЛЕНИЕ БАГА: Если включен LightOnly или используется готовая карта нормалей,
                // берем нейтральный серый материал (0.75), чтобы вектор цвета карты нормалей не искажал цвет света!
                if (LightOnly || UseExistingNormalMap)
                {
                    baseR = 0.75f;
                    baseG = 0.75f;
                    baseB = 0.75f;
                }
                else
                {
                    ColorBgra srcCol = src[x, y];
                    baseR = srcCol.R / 255f;
                    baseG = srcCol.G / 255f;
                    baseB = srcCol.B / 255f;
                }

                // Точная формулировка освещения: Ambient + (Diffuse * LightColor) + (Specular * LightColor)
                float finalR = baseR * (ambient + diffFactor * lColorR) + specFactor * lColorR;
                float finalG = baseG * (ambient + diffFactor * lColorG) + specFactor * lColorG;
                float finalB = baseB * (ambient + diffFactor * lColorB) + specFactor * lColorB;

                byte rShaded = (byte)Math.Max(0, Math.Min(255, (int)(finalR * 255f)));
                byte gShaded = (byte)Math.Max(0, Math.Min(255, (int)(finalG * 255f)));
                byte bShaded = (byte)Math.Max(0, Math.Min(255, (int)(finalB * 255f)));

                dst[x, y] = ColorBgra.FromBgra(bShaded, gShaded, rShaded, src[x, y].A);
            }
            else
            {
                // --- СТАНДАРТНЫЙ ВЫВОД КАРТЫ НОРМАЛЕЙ ---
                ColorBgra finalColor;

                if (FormatMode == 2) // SSBump (Valve Source Engine)
                {
                    float dot0 = Math.Max(0f, nx * ssb0_x + ny * ssb0_y + nz * ssb0_z);
                    float dot1 = Math.Max(0f, nx * ssb1_x + ny * ssb1_y + nz * ssb1_z);
                    float dot2 = Math.Max(0f, nx * ssb2_x + ny * ssb2_y + nz * ssb2_z);

                    byte rSS = (byte)Math.Max(0, Math.Min(255, (int)(dot0 * 255f)));
                    byte gSS = (byte)Math.Max(0, Math.Min(255, (int)(dot1 * 255f)));
                    byte bSS = (byte)Math.Max(0, Math.Min(255, (int)(dot2 * 255f)));

                    finalColor = ColorBgra.FromBgra(bSS, gSS, rSS, src[x, y].A);
                }
                else if (FormatMode == 3) // Пользовательские цвета
                {
                    float wX = Math.Max(0f, nx);
                    float wY = Math.Max(0f, ny);
                    float wZ = Math.Max(0f, nz);

                    float rC = (CustomXColor.R * wX + CustomYColor.R * wY + CustomZColor.R * wZ);
                    float gC = (CustomXColor.G * wX + CustomYColor.G * wY + CustomZColor.G * wZ);
                    float bC = (CustomXColor.B * wX + CustomYColor.B * wY + CustomZColor.B * wZ);

                    byte rByte = (byte)Math.Max(0, Math.Min(255, (int)rC));
                    byte gByte = (byte)Math.Max(0, Math.Min(255, (int)gC));
                    byte bByte = (byte)Math.Max(0, Math.Min(255, (int)bC));

                    finalColor = ColorBgra.FromBgra(bByte, gByte, rByte, src[x, y].A);
                }
                else // Стандартный вывод (OpenGL / DirectX)
                {
                    byte rByte = (byte)Math.Max(0, Math.Min(255, (int)((nx * 0.5f + 0.5f) * 255f)));
                    byte gByte = (byte)Math.Max(0, Math.Min(255, (int)((ny * 0.5f + 0.5f) * 255f)));
                    byte bByte = (byte)Math.Max(0, Math.Min(255, (int)((nz * 0.5f + 0.5f) * 255f)));

                    finalColor = ColorBgra.FromBgra(bByte, gByte, rByte, src[x, y].A);
                }

                dst[x, y] = finalColor;
            }
        }
    }
}