// Name: Line Displacement
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Line Displacement
// Version: 3.5
// Desc: VHS-глитч со сдвигом полос, направлением, каналами, волной и Voronoi-искажением
// Keywords: glitch,vhs,displacement,line,shift,rgb,channel,rotate,voronoi,fracture
// URL:
// Help:

#region UICode
IntSliderControl Amount = 50;             // [0,2000] Сила смещения
IntSliderControl LineHeight = 4;          // [1,500] Толщина полосы
IntSliderControl HeightVariance = 0;      // [0,100] Разброс толщины (%)
IntSliderControl Probability = 50;        // [0,100] Шанс сдвига полосы (%)
IntSliderControl NumColumns = 1;          // [1,100] Кол-во колонок
AngleControl Angle = 0;                   // [0,360] Направление сдвига
AngleControl LineAngle = 90;              // [0,360] Поворот полос
CheckboxControl RandomDirection = true;   // Случайная сторона сдвига
CheckboxControl WrapEdges = false;        // Заворачивать края
CheckboxControl InvertAlternateColumns = false; // Инверт через колонку
IntSliderControl ChannelDistR = 100;      // [0,500] Сдвиг R (%)
IntSliderControl ChannelDistG = 100;      // [0,500] Сдвиг G (%)
IntSliderControl ChannelDistB = 100;      // [0,500] Сдвиг B (%)
IntSliderControl ChannelDistA = 100;      // [0,500] Сдвиг Alpha (%)
DoubleSliderControl WaveAmplitude = 0;    // [0,200] Амплитуда волны
DoubleSliderControl WaveFrequency = 1;    // [0.01,20] Частота волны
DoubleSliderControl VoronoiAmount = 0;    // [0,50] Искажение в Voronoi
IntSliderControl VoronoiScale = 50;       // [4,500] Размер ячеек Voronoi
IntSliderControl BlendOpacity = 100;      // [0,100] Смешение с оригиналом
IntSliderControl CustomSeed = 12345;      // [0,999999] Сид паттерна
ReseedButtonControl Reseed = 0;           // Случайный сид
#endregion

private int[,] columnOffsets;
private int numBands;
private double uMin, uMax, vMin, vMax;
private double lineAngleRad;
private int noiseSeed;

void PreRender(Surface dst, Surface src)
{
    int width = src.Width;
    int height = src.Height;

    lineAngleRad = LineAngle * Math.PI / 180.0;
    double cosL = Math.Cos(lineAngleRad);
    double sinL = Math.Sin(lineAngleRad);

    double[] cornersX = { 0, width, 0, width };
    double[] cornersY = { 0, 0, height, height };

    uMin = double.MaxValue; uMax = double.MinValue;
    vMin = double.MaxValue; vMax = double.MinValue;

    for (int i = 0; i < 4; i++)
    {
        double u = cornersX[i] * cosL + cornersY[i] * sinL;
        double v = -cornersX[i] * sinL + cornersY[i] * cosL;

        if (u < uMin) uMin = u;
        if (u > uMax) uMax = u;
        if (v < vMin) vMin = v;
        if (v > vMax) vMax = v;
    }

    numBands = (int)((uMax - uMin) / LineHeight) + 2;

    columnOffsets = new int[NumColumns, numBands];

    int finalSeed = CustomSeed + Reseed * 7919;
    noiseSeed = finalSeed;
    Random rng = new Random(finalSeed);

    for (int col = 0; col < NumColumns; col++)
    {
        int band = 0;

        while (band < numBands)
        {
            int bandSpan = 1;

            if (HeightVariance > 0)
            {
                int variance = Math.Max(1, (int)(HeightVariance / 100.0 * 5));
                bandSpan = Math.Max(1, 1 + rng.Next(-variance, variance + 1) / 2);
            }

            bool displaceThisLine = rng.Next(0, 100) < Probability;
            int offset = 0;

            if (displaceThisLine && Amount > 0)
            {
                offset = rng.Next(1, Amount + 1);

                if (RandomDirection && rng.Next(0, 2) == 0)
                    offset = -offset;
            }

            if (InvertAlternateColumns && col % 2 == 1)
                offset = -offset;

            int endBand = Math.Min(band + bandSpan, numBands);

            for (int b = band; b < endBand; b++)
            {
                columnOffsets[col, b] = offset;
            }

            band = endBand;
        }
    }
}

double Hash(int x, int y, int seed)
{
    unchecked
    {
        int h = x * 374761393 + y * 668265263 + seed * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7fffffff) / (double)0x7fffffff;
    }
}

double VoronoiF1(double px, double py, double cellSize, int seed)
{
    int cx = (int)Math.Floor(px / cellSize);
    int cy = (int)Math.Floor(py / cellSize);

    double minDist = double.MaxValue;

    for (int oy = -1; oy <= 1; oy++)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            int nx = cx + ox;
            int ny = cy + oy;

            double jitterX = Hash(nx, ny, seed);
            double jitterY = Hash(nx, ny, seed + 1);

            double pointX = (nx + jitterX) * cellSize;
            double pointY = (ny + jitterY) * cellSize;

            double dx = pointX - px;
            double dy = pointY - py;
            double dist = Math.Sqrt(dx * dx + dy * dy);

            if (dist < minDist) minDist = dist;
        }
    }

    return minDist / cellSize;
}

ColorBgra SamplePixel(Surface src, int x, int y, bool wrap)
{
    int w = src.Width;
    int h = src.Height;

    if (wrap)
    {
        x = ((x % w) + w) % w;
        y = ((y % h) + h) % h;
    }
    else
    {
        x = Math.Max(0, Math.Min(w - 1, x));
        y = Math.Max(0, Math.Min(h - 1, y));
    }

    return src[x, y];
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    double angleRad = Angle * Math.PI / 180.0;
    double cosA = Math.Cos(angleRad);
    double sinA = Math.Sin(angleRad);

    double cosL = Math.Cos(lineAngleRad);
    double sinL = Math.Sin(lineAngleRad);

    double colWidth = (vMax - vMin) / NumColumns;
    if (colWidth <= 0) colWidth = 1;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            double u = x * cosL + y * sinL;
            double v = -x * sinL + y * cosL;

            if (VoronoiAmount > 0)
            {
                double vNoise = VoronoiF1(x, y, VoronoiScale, noiseSeed);
                double distort = (vNoise - 0.75) * VoronoiAmount * LineHeight;
                u += distort;
            }

            int bandIndex = (int)((u - uMin) / LineHeight);
            bandIndex = ((bandIndex % numBands) + numBands) % numBands;

            int colIndex = (int)((v - vMin) / colWidth);
            colIndex = Math.Max(0, Math.Min(NumColumns - 1, colIndex));

            int baseOffset = columnOffsets[colIndex, bandIndex];

            if (WaveAmplitude > 0 && baseOffset != 0)
            {
                double wave = Math.Sin(u * WaveFrequency * 0.1) * WaveAmplitude;
                baseOffset += (int)Math.Round(wave);
            }

            double dxBase = baseOffset * cosA;
            double dyBase = baseOffset * sinA;

            int rX = (int)Math.Round(x - dxBase * (ChannelDistR / 100.0));
            int rY = (int)Math.Round(y - dyBase * (ChannelDistR / 100.0));
            byte r = SamplePixel(src, rX, rY, WrapEdges).R;

            int gX = (int)Math.Round(x - dxBase * (ChannelDistG / 100.0));
            int gY = (int)Math.Round(y - dyBase * (ChannelDistG / 100.0));
            byte g = SamplePixel(src, gX, gY, WrapEdges).G;

            int bX = (int)Math.Round(x - dxBase * (ChannelDistB / 100.0));
            int bY = (int)Math.Round(y - dyBase * (ChannelDistB / 100.0));
            byte b = SamplePixel(src, bX, bY, WrapEdges).B;

            int aX = (int)Math.Round(x - dxBase * (ChannelDistA / 100.0));
            int aY = (int)Math.Round(y - dyBase * (ChannelDistA / 100.0));
            byte a = SamplePixel(src, aX, aY, WrapEdges).A;

            ColorBgra displaced = ColorBgra.FromBgra(b, g, r, a);

            if (BlendOpacity < 100)
            {
                ColorBgra original = src[x, y];
                displaced = ColorBgra.Blend(original, displaced, (byte)(BlendOpacity * 255 / 100));
            }

            dst[x, y] = displaced;
        }
    }
}