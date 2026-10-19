// Name: Anisotropic Kuwahara
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Anisotropic Kuwahara
// Version: 1.2
// Desc: Анизотропный фильтр Кувахары — живопись маслом с отладкой потока, палитрой мастихина и детализацией
// Keywords: kuwahara,oil,paint,painting,art,anisotropic,artistic,filter,smooth,debug,tensor
// URL:
// Help:

#region UICode
IntSliderControl Radius = 6;              // [1,20] Размер кисти (Радиус)
DoubleSliderControl Anisotropy = 2.5;     // [0.0,5.0] Сила анизотропии (Вытягивание мазков)
DoubleSliderControl Sharpness = 8.0;      // [1.0,20.0] Четкость краев мазков
IntSliderControl Smoothness = 3;          // [1,10] Сглаживание потока (Tensor Blur)
IntSliderControl FlowAngleOffset = 0;     // [-90,90] Поворот мазков (Градусы)
ListBoxControl SectorsCount = 1;          // Количество секторов|4 Сектора|8 Секторов
DoubleSliderControl Saturation = 1.0;     // [0.0,2.0] Насыщенность красок
IntSliderControl ColorLevels = 0;         // [0,32] Квантование цвета / Слои краски (0 = Выкл)
DoubleSliderControl DetailPreservation = 0.0; // [0.0,1.0] Сохранение мелких деталей
CheckboxControl ShowFlowMap = false;      // Отладка: Показать карту направлений (Flow Map)
IntSliderControl BlendOpacity = 100;      // [0,100] Непрозрачность (Смешение с оригиналом)
#endregion

private float[] angleMap;
private float[] animMap;

void PreRender(Surface dst, Surface src)
{
    int w = src.Width;
    int h = src.Height;
    int total = w * h;

    if (angleMap == null || angleMap.Length < total)
    {
        angleMap = new float[total];
        animMap = new float[total];
    }

    // 1. Вычисление градиентов Собеля и компонентов тензора структуры
    float[] jxx = new float[total];
    float[] jyy = new float[total];
    float[] jxy = new float[total];

    for (int y = 0; y < h; y++)
    {
        int ym1 = Math.Max(0, y - 1);
        int yp1 = Math.Min(h - 1, y + 1);

        for (int x = 0; x < w; x++)
        {
            int xm1 = Math.Max(0, x - 1);
            int xp1 = Math.Min(w - 1, x + 1);

            float l00 = GetLuminance(src[xm1, ym1]);
            float l10 = GetLuminance(src[x,   ym1]);
            float l20 = GetLuminance(src[xp1, ym1]);

            float l01 = GetLuminance(src[xm1, y]);
            float l21 = GetLuminance(src[xp1, y]);

            float l02 = GetLuminance(src[xm1, yp1]);
            float l12 = GetLuminance(src[x,   yp1]);
            float l22 = GetLuminance(src[xp1, yp1]);

            float gx = (l20 + 2f * l21 + l22) - (l00 + 2f * l01 + l02);
            float gy = (l00 + 2f * l10 + l20) - (l02 + 2f * l12 + l22);

            int idx = y * w + x;
            jxx[idx] = gx * gx;
            jyy[idx] = gy * gy;
            jxy[idx] = gx * gy;
        }
    }

    // 2. Сглаживание тензора структуры
    float[] sjxx = BoxBlur(jxx, w, h, Smoothness);
    float[] sjyy = BoxBlur(jyy, w, h, Smoothness);
    float[] sjxy = BoxBlur(jxy, w, h, Smoothness);

    float offsetRad = (float)(FlowAngleOffset * Math.PI / 180.0);

    // 3. Вычисление собственного угла и анизотропии
    for (int i = 0; i < total; i++)
    {
        float xx = sjxx[i];
        float yy = sjyy[i];
        float xy = sjxy[i];

        // Угол потока вдоль контуров + пользовательский сдвиг
        float theta = 0.5f * (float)Math.Atan2(2.0f * xy, xx - yy);
        angleMap[i] = theta + (float)(Math.PI * 0.5) + offsetRad;

        float trace = xx + yy;
        float diff = xx - yy;
        float sqrtTerm = (float)Math.Sqrt(Math.Max(0f, diff * diff + 4f * xy * xy));

        float l1 = 0.5f * (trace + sqrtTerm);
        float l2 = 0.5f * (trace - sqrtTerm);

        float anim = (l1 + l2 > 1e-5f) ? (l1 - l2) / (l1 + l2) : 0f;
        animMap[i] = anim;
    }
}

private static float GetLuminance(ColorBgra c)
{
    return (0.2126f * c.R + 0.7152f * c.G + 0.0722f * c.B) / 255f;
}

private float[] BoxBlur(float[] input, int w, int h, int r)
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
                if (px >= 0 && px < w)
                {
                    sum += input[row + px];
                    count++;
                }
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
                if (py >= 0 && py < h)
                {
                    sum += temp[py * w + x];
                    count++;
                }
            }
            output[y * w + x] = sum / count;
        }
    }

    return output;
}

private static ColorBgra HSVtoRGB(float h, float s, float v, byte alpha)
{
    float r = 0, g = 0, b = 0;
    int i = (int)Math.Floor(h * 6f);
    float f = h * 6f - i;
    float p = v * (1f - s);
    float q = v * (1f - f * s);
    float t = v * (1f - (1f - f) * s);

    switch (i % 6)
    {
        case 0: r = v; g = t; b = p; break;
        case 1: r = q; g = v; b = p; break;
        case 2: r = p; g = v; b = t; break;
        case 3: r = p; g = q; b = v; break;
        case 4: r = t; g = p; b = v; break;
        case 5: r = v; g = p; b = q; break;
    }

    return ColorBgra.FromBgra(
        (byte)Math.Max(0, Math.Min(255, b * 255f)),
        (byte)Math.Max(0, Math.Min(255, g * 255f)),
        (byte)Math.Max(0, Math.Min(255, r * 255f)),
        alpha
    );
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    int w = src.Width;
    int h = src.Height;
    int numSectors = (SectorsCount == 0) ? 4 : 8;
    int r = Radius;
    double sharp = Sharpness;
    float maxAnim = (float)Anisotropy;
    byte opacityByte = (byte)(BlendOpacity * 255 / 100);

    double[] sectorAngles = new double[8];
    for (int k = 0; k < numSectors; k++)
    {
        sectorAngles[k] = k * 2.0 * Math.PI / numSectors;
    }

    // Рабочие буферы потока (выделяются 1 раз на поток)
    double[] sumW = new double[8];
    double[] sumR = new double[8];
    double[] sumG = new double[8];
    double[] sumB = new double[8];
    double[] sumA = new double[8];

    double[] sumR2 = new double[8];
    double[] sumG2 = new double[8];
    double[] sumB2 = new double[8];

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        int rowIdx = y * w;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            int idx = rowIdx + x;
            float angle = angleMap[idx];
            float anim = animMap[idx];

            // --- ОТЛАДОЧНЫЙ РЕЖИМ: Отображение векторной карты потока ---
            if (ShowFlowMap)
            {
                float hVal = (float)((angle % Math.PI + Math.PI) % Math.PI / Math.PI);
                float sVal = 0.85f;
                float vVal = Math.Min(1f, 0.25f + anim * 0.75f);

                ColorBgra flowCol = HSVtoRGB(hVal, sVal, vVal, src[x, y].A);

                if (opacityByte < 255)
                    dst[x, y] = ColorBgra.Blend(src[x, y], flowCol, opacityByte);
                else
                    dst[x, y] = flowCol;

                continue;
            }

            // --- РЕЖИМ ФИЛЬТРАЦИИ КУВАХАРЫ ---
            float cosA = (float)Math.Cos(-angle);
            float sinA = (float)Math.Sin(-angle);

            float scaleX = 1.0f + anim * maxAnim;
            float scaleY = 1.0f / Math.Max(0.1f, scaleX);

            Array.Clear(sumW, 0, numSectors);
            Array.Clear(sumR, 0, numSectors);
            Array.Clear(sumG, 0, numSectors);
            Array.Clear(sumB, 0, numSectors);
            Array.Clear(sumA, 0, numSectors);
            Array.Clear(sumR2, 0, numSectors);
            Array.Clear(sumG2, 0, numSectors);
            Array.Clear(sumB2, 0, numSectors);

            for (int dy = -r; dy <= r; dy++)
            {
                int py = Math.Max(0, Math.Min(h - 1, y + dy));

                for (int dx = -r; dx <= r; dx++)
                {
                    int px = Math.Max(0, Math.Min(w - 1, x + dx));

                    float u = (dx * cosA - dy * sinA) / scaleX;
                    float v = (dx * sinA + dy * cosA) / scaleY;

                    float dist2 = u * u + v * v;
                    if (dist2 > r * r) continue;

                    float dist = (float)Math.Sqrt(dist2);
                    float phi = (float)Math.Atan2(v, u);

                    double wRadial = Math.Exp(-2.0 * (dist / r) * (dist / r));

                    ColorBgra col = src[px, py];

                    for (int k = 0; k < numSectors; k++)
                    {
                        double angleDiff = phi - sectorAngles[k];
                        double cosDiff = Math.Cos(angleDiff);
                        if (cosDiff <= 0) continue;

                        double wAngular = cosDiff * cosDiff;
                        double wSample = wRadial * wAngular;

                        sumW[k] += wSample;
                        sumR[k] += col.R * wSample;
                        sumG[k] += col.G * wSample;
                        sumB[k] += col.B * wSample;
                        sumA[k] += col.A * wSample;

                        sumR2[k] += col.R * col.R * wSample;
                        sumG2[k] += col.G * col.G * wSample;
                        sumB2[k] += col.B * col.B * wSample;
                    }
                }
            }

            double totalR = 0, totalG = 0, totalB = 0, totalA = 0;
            double totalSectorW = 0;

            for (int k = 0; k < numSectors; k++)
            {
                if (sumW[k] <= 1e-6) continue;

                double invW = 1.0 / sumW[k];
                double meanR = sumR[k] * invW;
                double meanG = sumG[k] * invW;
                double meanB = sumB[k] * invW;
                double meanA = sumA[k] * invW;

                double varR = Math.Max(0, (sumR2[k] * invW) - (meanR * meanR));
                double varG = Math.Max(0, (sumG2[k] * invW) - (meanG * meanG));
                double varB = Math.Max(0, (sumB2[k] * invW) - (meanB * meanB));

                double variance = (varR + varG + varB) / 3.0;

                double weight = 1.0 / Math.Pow(variance + 1e-3, sharp * 0.5);

                totalR += meanR * weight;
                totalG += meanG * weight;
                totalB += meanB * weight;
                totalA += meanA * weight;
                totalSectorW += weight;
            }

            ColorBgra resultCol;

            if (totalSectorW > 0)
            {
                float fR = (float)(totalR / totalSectorW);
                float fG = (float)(totalG / totalSectorW);
                float fB = (float)(totalB / totalSectorW);
                float fA = (float)(totalA / totalSectorW);

                // 1. Коррекция насыщенности
                if (Math.Abs(Saturation - 1.0) > 0.01)
                {
                    float lum = 0.2126f * fR + 0.7152f * fG + 0.0722f * fB;
                    fR = Math.Max(0f, Math.Min(255f, lum + (fR - lum) * (float)Saturation));
                    fG = Math.Max(0f, Math.Min(255f, lum + (fG - lum) * (float)Saturation));
                    fB = Math.Max(0f, Math.Min(255f, lum + (fB - lum) * (float)Saturation));
                }

                // 2. Квантование палитры (Palette Knife Effect)
                if (ColorLevels > 1)
                {
                    double step = 255.0 / (ColorLevels - 1);
                    fR = (float)Math.Min(255, Math.Max(0, Math.Round(Math.Round(fR / step) * step)));
                    fG = (float)Math.Min(255, Math.Max(0, Math.Round(Math.Round(fG / step) * step)));
                    fB = (float)Math.Min(255, Math.Max(0, Math.Round(Math.Round(fB / step) * step)));
                }

                // 3. Сохранение мелких деталей по карте анизотропии
                if (DetailPreservation > 0)
                {
                    float edgeWeight = Math.Min(1f, anim * (float)DetailPreservation);
                    ColorBgra orig = src[x, y];
                    fR = fR * (1f - edgeWeight) + orig.R * edgeWeight;
                    fG = fG * (1f - edgeWeight) + orig.G * edgeWeight;
                    fB = fB * (1f - edgeWeight) + orig.B * edgeWeight;
                    fA = fA * (1f - edgeWeight) + orig.A * edgeWeight;
                }

                byte finalR = (byte)Math.Max(0, Math.Min(255, fR));
                byte finalG = (byte)Math.Max(0, Math.Min(255, fG));
                byte finalB = (byte)Math.Max(0, Math.Min(255, fB));
                byte finalA = (byte)Math.Max(0, Math.Min(255, fA));

                resultCol = ColorBgra.FromBgra(finalB, finalG, finalR, finalA);
            }
            else
            {
                resultCol = src[x, y];
            }

            if (opacityByte < 255)
            {
                dst[x, y] = ColorBgra.Blend(src[x, y], resultCol, opacityByte);
            }
            else
            {
                dst[x, y] = resultCol;
            }
        }
    }
}