// Name: Pixel Sorting
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Pixel Sorting
// Version: 3.7
// Desc: Высокооптимизированный пиксельный сортировщик с исправленной раздельной обработкой RGBA
// Keywords: pixel,sort,sorting,glitch,vhs,channels,rgb,alpha,chromatic,random,seed,fast
// URL:
// Help:

#region UICode
AngleControl Angle = 0;                   // [0,360] Направление сортировки
ListBoxControl SortBy = 0;                // Критерий|Яркость (Luminance)|Насыщенность (Saturation)|Цветовой тон (Hue)|Красный (Red)|Зеленый (Green)|Синий (Blue)|Альфа (Alpha)|Соответствующий канал (Auto)|Случайно (Random)
ListBoxControl ThresholdMode = 0;         // Порог по|Яркости|Насыщенности|Цветовому тону|Красному|Зеленому|Синему|Градиенту (Рёбра)|Случайному шуму
IntSliderControl MinThreshold = 40;       // [0,255] Мин. порог
IntSliderControl MaxThreshold = 220;      // [0,255] Макс. порог
CheckboxControl InvertThreshold = false;  // Инвертировать порог
IntSliderControl MinLength = 5;           // [1,500] Мин. длина сегмента
IntSliderControl MaxLength = 400;         // [1,1000] Макс. длина сегмента
IntSliderControl RandomBreak = 0;         // [0,100] Шанс разрыва полосы (%)
CheckboxControl ReverseSort = false;      // Инвертировать порядок
CheckboxControl SplitChannels = false;    // Раздельная сортировка RGBA
IntSliderControl RedShift = 0;            // [-100,100] Сдвиг R канала (%)
IntSliderControl GreenShift = 0;          // [-100,100] Сдвиг G канала (%)
IntSliderControl BlueShift = 0;           // [-100,100] Сдвиг B канала (%)
IntSliderControl AlphaShift = 0;          // [-100,100] Сдвиг Alpha канала (%)
IntSliderControl BlendOpacity = 100;      // [0,100] Смешение с оригиналом (%)
IntSliderControl CustomSeed = 12345;      // [0,999999] Сид (Seed)
ReseedButtonControl Reseed = 0;           // Случайный сид
#endregion

private Surface scratchBuffer;

struct FastPoint
{
    public ushort X;
    public ushort Y;
}

private FastPoint[] flatPoints;
private double[] flatU;
private int[] lineStarts;
private int[] lineCounts;
private int[] lineCursor;

private double[] keyBuf;
private int[] idxBuf;
private byte[] valBufR, valBufG, valBufB, valBufA;
private ColorBgra[] origColBuf;

private void EnsureBuffers(int totalPixels, int maxDim)
{
    if (flatPoints == null || flatPoints.Length < totalPixels)
    {
        flatPoints = new FastPoint[totalPixels];
        flatU = new double[totalPixels];
    }

    int maxSeg = maxDim + 1000;
    if (keyBuf == null || keyBuf.Length < maxSeg)
    {
        keyBuf = new double[maxSeg];
        idxBuf = new int[maxSeg];
        valBufR = new byte[maxSeg];
        valBufG = new byte[maxSeg];
        valBufB = new byte[maxSeg];
        valBufA = new byte[maxSeg];
        origColBuf = new ColorBgra[maxSeg];
    }
}

void PreRender(Surface dst, Surface src)
{
    int width = src.Width;
    int height = src.Height;
    int totalPixels = width * height;

    if (scratchBuffer == null || scratchBuffer.Width != width || scratchBuffer.Height != height)
    {
        scratchBuffer = new Surface(width, height);
    }
    scratchBuffer.CopySurface(src);

    EnsureBuffers(totalPixels, Math.Max(width, height));

    // Корректная ориентация угла (0 - вправо, 90 - вверх)
    double rad = -Angle * Math.PI / 180.0;
    double cosA = Math.Cos(rad);
    double sinA = Math.Sin(rad);

    double[] cX = { 0, width - 1, 0, width - 1 };
    double[] cY = { 0, 0, height - 1, height - 1 };
    double vMin = double.MaxValue, vMax = double.MinValue;

    for (int i = 0; i < 4; i++)
    {
        double vVal = cX[i] * sinA + cY[i] * cosA;
        if (vVal < vMin) vMin = vVal;
        if (vVal > vMax) vMax = vVal;
    }

    int minLine = (int)Math.Floor(vMin);
    int totalLines = (int)Math.Ceiling(vMax) - minLine + 1;

    if (lineCounts == null || lineCounts.Length < totalLines)
    {
        lineCounts = new int[totalLines];
        lineStarts = new int[totalLines];
        lineCursor = new int[totalLines];
    }
    else
    {
        Array.Clear(lineCounts, 0, totalLines);
    }

    // Проход 1: Подсчет размеров линий
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            double v = x * sinA + y * cosA;
            int lineIdx = (int)Math.Round(v) - minLine;
            lineCounts[lineIdx]++;
        }
    }

    // Проход 2: Индексы смещения
    int accum = 0;
    for (int i = 0; i < totalLines; i++)
    {
        lineStarts[i] = accum;
        lineCursor[i] = accum;
        accum += lineCounts[i];
    }

    // Проход 3: Заполнение буфера точек
    for (int y = 0; y < height; y++)
    {
        for (int x = 0; x < width; x++)
        {
            double u = x * cosA - y * sinA;
            double v = x * sinA + y * cosA;
            int lineIdx = (int)Math.Round(v) - minLine;
            int pos = lineCursor[lineIdx]++;
            flatPoints[pos] = new FastPoint { X = (ushort)x, Y = (ushort)y };
            flatU[pos] = u;
        }
    }

    int seed = CustomSeed + Reseed * 7919;

    // Проход 4: Быстрая сортировка
    for (int l = 0; l < totalLines; l++)
    {
        int startIdx = lineStarts[l];
        int count = lineCounts[l];
        if (count <= 1) continue;

        Array.Sort(flatU, flatPoints, startIdx, count);

        Random rngBreak = new Random(seed + l * 31);
        int i = 0;
        while (i < count)
        {
            while (i < count)
            {
                FastPoint pt = flatPoints[startIdx + i];
                double val = GetThresholdVal(src, pt.X, pt.Y, ThresholdMode, seed);
                bool pass = (val >= MinThreshold && val <= MaxThreshold);
                if (InvertThreshold) pass = !pass;

                if (pass) break;
                i++;
            }

            if (i >= count) break;

            int segStart = i;

            while (i < count)
            {
                FastPoint pt = flatPoints[startIdx + i];
                double val = GetThresholdVal(src, pt.X, pt.Y, ThresholdMode, seed);
                bool pass = (val >= MinThreshold && val <= MaxThreshold);
                if (InvertThreshold) pass = !pass;

                if (!pass) break;

                int currentLen = i - segStart + 1;

                if (RandomBreak > 0 && currentLen >= MinLength && rngBreak.Next(100) < RandomBreak)
                {
                    i++;
                    break;
                }

                if (currentLen >= MaxLength)
                {
                    i++;
                    break;
                }
                i++;
            }

            int segEnd = i;
            int segLen = segEnd - segStart;

            if (segLen >= MinLength)
            {
                SortSegmentFast(src, scratchBuffer, startIdx + segStart, segLen, seed);
            }
        }
    }
}

private void SortSegmentFast(Surface src, Surface dst, int flatStart, int len, int seed)
{
    if (len <= 1) return;

    if (!SplitChannels)
    {
        for (int k = 0; k < len; k++)
        {
            FastPoint pt = flatPoints[flatStart + k];
            ColorBgra col = src[pt.X, pt.Y];
            origColBuf[k] = col;
            idxBuf[k] = k;
            keyBuf[k] = GetSortKey(col, SortBy, 0, pt.X, pt.Y, seed) + (k * 1e-9);
        }

        Array.Sort(keyBuf, idxBuf, 0, len);
        if (ReverseSort) Array.Reverse(idxBuf, 0, len);

        for (int k = 0; k < len; k++)
        {
            FastPoint pt = flatPoints[flatStart + k];
            dst[pt.X, pt.Y] = origColBuf[idxBuf[k]];
        }
    }
    else
    {
        for (int k = 0; k < len; k++)
        {
            FastPoint pt = flatPoints[flatStart + k];
            ColorBgra col = src[pt.X, pt.Y];

            valBufR[k] = col.R;
            valBufG[k] = col.G;
            valBufB[k] = col.B;
            valBufA[k] = col.A;
        }

        // Ключи сортировки заполняются СТРОГО из оригинального Surface (src)
        FillKeysAndSort(src, valBufR, SortBy, 0, flatStart, len, seed);
        FillKeysAndSort(src, valBufG, SortBy, 1, flatStart, len, seed);
        FillKeysAndSort(src, valBufB, SortBy, 2, flatStart, len, seed);
        FillKeysAndSort(src, valBufA, SortBy, 3, flatStart, len, seed);

        int rOff = (int)Math.Round((RedShift / 100.0) * len);
        int gOff = (int)Math.Round((GreenShift / 100.0) * len);
        int bOff = (int)Math.Round((BlueShift / 100.0) * len);
        int aOff = (int)Math.Round((AlphaShift / 100.0) * len);

        for (int k = 0; k < len; k++)
        {
            FastPoint pt = flatPoints[flatStart + k];

            int rIdx = ((k + rOff) % len + len) % len;
            int gIdx = ((k + gOff) % len + len) % len;
            int bIdx = ((k + bOff) % len + len) % len;
            int aIdx = ((k + aOff) % len + len) % len;

            dst[pt.X, pt.Y] = ColorBgra.FromBgra(valBufB[bIdx], valBufG[gIdx], valBufR[rIdx], valBufA[aIdx]);
        }
    }
}

private void FillKeysAndSort(Surface src, byte[] valBuf, int sortBy, int channelIdx, int flatStart, int len, int seed)
{
    for (int k = 0; k < len; k++)
    {
        FastPoint pt = flatPoints[flatStart + k];
        ColorBgra origCol = src[pt.X, pt.Y]; // Берутся чистые оригинальные цвета
        keyBuf[k] = GetSortKey(origCol, sortBy, channelIdx, pt.X, pt.Y, seed) + (k * 1e-9);
    }

    Array.Sort(keyBuf, valBuf, 0, len);
    if (ReverseSort) Array.Reverse(valBuf, 0, len);
}

private double GetThresholdVal(Surface src, int x, int y, int mode, int seed)
{
    ColorBgra c = src[x, y];
    switch (mode)
    {
        case 0: return GetLuminance(c.R, c.G, c.B);
        case 1:
            ColorToHSV(c.R, c.G, c.B, out _, out double s, out _);
            return s * 255.0;
        case 2:
            ColorToHSV(c.R, c.G, c.B, out double h, out _, out _);
            return h * 255.0;
        case 3: return c.R;
        case 4: return c.G;
        case 5: return c.B;
        case 6: // Градиент (Контуры)
            int x2 = Math.Min(src.Width - 1, x + 1);
            int y2 = Math.Min(src.Height - 1, y + 1);
            ColorBgra c2 = src[x2, y2];
            int dr = c.R - c2.R, dg = c.G - c2.G, db = c.B - c2.B;
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        case 7: return Hash(x, y, seed) * 255.0;
        default: return GetLuminance(c.R, c.G, c.B);
    }
}

private double GetSortKey(ColorBgra c, int criteria, int channelIdx, int x, int y, int seed)
{
    switch (criteria)
    {
        case 0: return GetLuminance(c.R, c.G, c.B);
        case 1:
            ColorToHSV(c.R, c.G, c.B, out _, out double s, out _);
            return s;
        case 2:
            ColorToHSV(c.R, c.G, c.B, out double h, out _, out _);
            return h;
        case 3: return c.R;
        case 4: return c.G;
        case 5: return c.B;
        case 6: return c.A;
        case 7: // Соответствующий канал (Auto)
            if (channelIdx == 0) return c.R;
            if (channelIdx == 1) return c.G;
            if (channelIdx == 2) return c.B;
            return c.A;
        case 8: // Случайно
            return Hash(x, y, seed + channelIdx * 10007);
        default: return GetLuminance(c.R, c.G, c.B);
    }
}

private static double GetLuminance(byte r, byte g, byte b)
{
    return 0.2126 * r + 0.7152 * g + 0.0722 * b;
}

private static void ColorToHSV(byte r, byte g, byte b, out double h, out double s, out double v)
{
    double rd = r / 255.0, gd = g / 255.0, bd = b / 255.0;
    double max = Math.Max(rd, Math.Max(gd, bd));
    double min = Math.Min(rd, Math.Min(gd, bd));
    double delta = max - min;

    v = max;
    s = max == 0 ? 0 : delta / max;

    if (delta == 0) h = 0;
    else if (max == rd) h = (gd - bd) / delta + (gd < bd ? 6 : 0);
    else if (max == gd) h = (bd - rd) / delta + 2;
    else h = (rd - gd) / delta + 4;
    h /= 6.0;
}

private static double Hash(int x, int y, int seed)
{
    unchecked
    {
        int h = x * 374761393 + y * 668265263 + seed * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7fffffff) * (1.0 / 0x7fffffff);
    }
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    if (BlendOpacity >= 100)
    {
        dst.CopySurface(scratchBuffer, rect.Location, rect);
        return;
    }

    byte opacityByte = (byte)(BlendOpacity * 255 / 100);

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            ColorBgra sortedCol = scratchBuffer[x, y];
            ColorBgra origCol = src[x, y];
            dst[x, y] = ColorBgra.Blend(origCol, sortedCol, opacityByte);
        }
    }
}