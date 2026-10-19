// Name: SchizoGlitch
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Schizo Glitch
// Version: 3.0
// Desc: Уникальная система цифрового безумия: имитация ошибок видеокодека (motion ghost), битовый обвал памяти, аналоговая видео-обратная связь (feedback vortex) и процедурные окна-кода, связанные общим хаос-полем и зонами коррупции
// Keywords: glitch,schizo,corruption,bitplane,motion compensation,video feedback,code bleed,datamosh,memory corruption,analog feedback,шизо,хаос,коррупция,обратная связь,битовый обвал,код
// URL:
// Help:

#region UICode
ListBoxControl StylePreset = 0;                // Пресет безумия|Пользовательский (Custom)|Цифровой труп (Digital Corpse)|Разум без центра (Mindless Core)|Хакнутая реальность (Hacked Reality)|Биомеханический бред (Biomech Delirium)|Тотальный коллапс (Total Collapse)
IntSliderControl CustomSeed = 12345;           // [0,999999] Случайное зерно (Seed)
ReseedButtonControl Reseed = 0;                // Перемешать
DoubleSliderControl Madness = 0.3;             // [0.0,1.0] Безумие (глобально связывает силу и поведение всех эффектов)
IntSliderControl FieldScale = 90;              // [10,300] Масштаб общего хаос-поля
DoubleSliderControl CorruptionZones = 0.0;     // [0.0,1.0] Зоны коррупции (усиливают остальные эффекты внутри себя)
IntSliderControl ZoneSize = 40;                // [8,200] Размер зон коррупции и блоков искажения
DoubleSliderControl MotionGhost = 0.0;         // [0.0,1.0] Ошибка видеокодека: блоки крадут содержимое похожих участков
DoubleSliderControl BitplaneCorruption = 0.0;  // [0.0,1.0] Битовый обвал памяти (резкие скачки цвета по битам)
DoubleSliderControl FeedbackVortex = 0.0;      // [0.0,1.0] Аналоговая видео-обратная связь (спираль-эхо)
DoubleSliderControl VortexHueDrift = 15.0;     // [0.0,60.0] Сдвиг оттенка за один виток спирали
DoubleSliderControl CodeBleed = 0.0;           // [0.0,1.0] Процедурные окна отладчика/псевдо-кода
ColorWheelControl CodeBleedColor = ColorBgra.FromBgra(80, 255, 110, 255); // [Lime?] Цвет псевдо-текста кода
DoubleSliderControl VeinGrowth = 0.0;          // [0.0,1.0] Рост вен/трещин вдоль границ зон коррупции
ColorWheelControl VeinColor = ColorBgra.FromBgra(20, 10, 140, 255); // [DarkRed?] Цвет вен/трещин
DoubleSliderControl FleshMelt = 0.0;           // [0.0,1.0] Органичное плавление (усилено в зонах коррупции)
DoubleSliderControl Posterize = 0.0;           // [0.0,1.0] Постеризация цвета
DoubleSliderControl Saturation = 1.0;          // [0.0,3.0] Насыщенность (Прожарка)
DoubleSliderControl Contrast = 1.0;            // [0.0,3.0] Контраст (Выжигание)
DoubleSliderControl EdgeOutline = 0.0;         // [0.0,1.0] Контурные линии (Edge Detect)
ColorWheelControl OutlineColor = ColorBgra.FromBgra(0, 0, 255, 255); // [Red?] Цвет контурных линий
DoubleSliderControl Vignette = 0.0;            // [0.0,1.0] Выжженная виньетка
ColorWheelControl VignetteColor = ColorBgra.FromBgra(0, 0, 0, 255); // [Black?] Цвет виньетки
CheckboxControl Grayscale = false;             // Принудительный черно-белый режим перед эффектами
DoubleSliderControl FinalMix = 1.0;            // [0.0,1.0] Смешение с оригиналом (Final Mix)
#endregion

// --- Вспомогательные функции ---

private static double Hash21(double x, double y, int seed)
{
    unchecked
    {
        int ix = (int)Math.Floor(x);
        int iy = (int)Math.Floor(y);
        int h = ix * 374761393 + iy * 668265263 + seed * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7fffffff) / (double)0x7fffffff;
    }
}

private static double ValueNoise(double x, double y, int seed)
{
    int ix = (int)Math.Floor(x);
    int iy = (int)Math.Floor(y);

    double fx = x - ix;
    double fy = y - iy;

    double ux = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
    double uy = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);

    double n00 = Hash21(ix, iy, seed);
    double n10 = Hash21(ix + 1, iy, seed);
    double n01 = Hash21(ix, iy + 1, seed);
    double n11 = Hash21(ix + 1, iy + 1, seed);

    double nx0 = n00 + ux * (n10 - n00);
    double nx1 = n01 + ux * (n11 - n01);

    return nx0 + uy * (nx1 - nx0);
}

private static double Fbm(double x, double y, int octaves, double persistence, int seed)
{
    double total = 0.0;
    double frequency = 1.0;
    double amplitude = 1.0;
    double maxValue = 0.0;

    for (int i = 0; i < octaves; i++)
    {
        total += ValueNoise(x * frequency, y * frequency, seed + i * 1013) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= 2.0;
    }

    return total / maxValue;
}

private static double SmoothStep(double edge0, double edge1, double x)
{
    x = Math.Max(0.0, Math.Min(1.0, (x - edge0) / (edge1 - edge0)));
    return x * x * (3.0 - 2.0 * x);
}

private static byte ClampByte(double val)
{
    return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(val)));
}

private static double ClampDouble(double val, double min, double max)
{
    return Math.Max(min, Math.Min(max, val));
}

private static int WrapInt(int v, int max)
{
    if (max <= 0) return 0;
    v = v % max;
    if (v < 0) v += max;
    return v;
}

private static double GetLum(Surface src, int x, int y, int w, int h)
{
    x = Math.Max(0, Math.Min(w - 1, x));
    y = Math.Max(0, Math.Min(h - 1, y));
    ColorBgra c = src[x, y];
    return 0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B;
}

private static void RgbToHsv(double r, double g, double b, out double h, out double s, out double v)
{
    r /= 255.0; g /= 255.0; b /= 255.0;
    double max = Math.Max(r, Math.Max(g, b));
    double min = Math.Min(r, Math.Min(g, b));
    v = max;
    double delta = max - min;
    s = (max <= 0.00001) ? 0.0 : delta / max;

    if (delta <= 0.00001)
    {
        h = 0.0;
    }
    else if (max == r)
    {
        h = 60.0 * ((g - b) / delta);
    }
    else if (max == g)
    {
        h = 60.0 * (((b - r) / delta) + 2.0);
    }
    else
    {
        h = 60.0 * (((r - g) / delta) + 4.0);
    }

    while (h < 0.0) h += 360.0;
    while (h >= 360.0) h -= 360.0;
}

private static void HsvToRgb(double h, double s, double v, out double r, out double g, out double b)
{
    double c = v * s;
    double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
    double m = v - c;
    double rr = 0, gg = 0, bb = 0;

    if (h < 60.0) { rr = c; gg = x; bb = 0.0; }
    else if (h < 120.0) { rr = x; gg = c; bb = 0.0; }
    else if (h < 180.0) { rr = 0.0; gg = c; bb = x; }
    else if (h < 240.0) { rr = 0.0; gg = x; bb = c; }
    else if (h < 300.0) { rr = x; gg = 0.0; bb = c; }
    else { rr = c; gg = 0.0; bb = x; }

    r = (rr + m) * 255.0;
    g = (gg + m) * 255.0;
    b = (bb + m) * 255.0;
}

private static void Worley2D(double x, double y, int seed, out double f1, out double f2, out int cellX, out int cellY)
{
    int ix = (int)Math.Floor(x);
    int iy = (int)Math.Floor(y);
    f1 = double.MaxValue;
    f2 = double.MaxValue;
    cellX = ix;
    cellY = iy;

    for (int oy = -1; oy <= 1; oy++)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            int cx = ix + ox;
            int cy = iy + oy;

            double jx = Hash21(cx, cy, seed);
            double jy = Hash21(cx, cy, seed + 97);

            double px = cx + jx;
            double py = cy + jy;

            double dx = px - x;
            double dy = py - y;
            double d = Math.Sqrt(dx * dx + dy * dy);

            if (d < f1)
            {
                f2 = f1;
                f1 = d;
                cellX = cx;
                cellY = cy;
            }
            else if (d < f2)
            {
                f2 = d;
            }
        }
    }
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    int width = src.Width;
    int height = src.Height;
    int seed = CustomSeed + Reseed * 7919;

    double centerX = width * 0.5;
    double centerY = height * 0.5;
    double maxRadius = Math.Sqrt(centerX * centerX + centerY * centerY);

    double effMadness = Madness;
    double effCorruption = CorruptionZones;
    double effMotionGhost = MotionGhost;
    double effBitplane = BitplaneCorruption;
    double effVortex = FeedbackVortex;
    double effHueDrift = VortexHueDrift;
    double effCodeBleed = CodeBleed;
    double effVein = VeinGrowth;
    double effMelt = FleshMelt;
    double effPosterize = Posterize;
    double effSaturation = Saturation;
    double effContrast = Contrast;
    double effEdge = EdgeOutline;
    double effVignette = Vignette;

    switch (StylePreset)
    {
        case 1: // Цифровой труп (Digital Corpse)
            effMadness = Math.Max(effMadness, 0.6);
            effCorruption = Math.Max(effCorruption, 0.45);
            effMotionGhost = Math.Max(effMotionGhost, 0.55);
            effBitplane = Math.Max(effBitplane, 0.4);
            effVein = Math.Max(effVein, 0.6);
            effMelt = Math.Max(effMelt, 0.5);
            break;

        case 2: // Разум без центра (Mindless Core)
            effMadness = Math.Max(effMadness, 0.7);
            effVortex = Math.Max(effVortex, 0.7);
            effHueDrift = Math.Max(effHueDrift, 25.0);
            effCorruption = Math.Max(effCorruption, 0.3);
            break;

        case 3: // Хакнутая реальность (Hacked Reality)
            effMadness = Math.Max(effMadness, 0.55);
            effCodeBleed = Math.Max(effCodeBleed, 0.6);
            effBitplane = Math.Max(effBitplane, 0.5);
            effMotionGhost = Math.Max(effMotionGhost, 0.4);
            effEdge = Math.Max(effEdge, 0.3);
            break;

        case 4: // Биомеханический бред (Biomech Delirium)
            effMadness = Math.Max(effMadness, 0.6);
            effMelt = Math.Max(effMelt, 0.7);
            effVein = Math.Max(effVein, 0.65);
            effCorruption = Math.Max(effCorruption, 0.5);
            effMotionGhost = Math.Max(effMotionGhost, 0.3);
            break;

        case 5: // Тотальный коллапс (Total Collapse)
            effMadness = 1.0;
            effCorruption = Math.Max(effCorruption, 0.6);
            effMotionGhost = Math.Max(effMotionGhost, 0.5);
            effBitplane = Math.Max(effBitplane, 0.45);
            effVortex = Math.Max(effVortex, 0.5);
            effHueDrift = Math.Max(effHueDrift, 30.0);
            effCodeBleed = Math.Max(effCodeBleed, 0.4);
            effVein = Math.Max(effVein, 0.5);
            effMelt = Math.Max(effMelt, 0.5);
            effPosterize = Math.Max(effPosterize, 0.4);
            effSaturation = Math.Max(effSaturation, 1.8);
            effContrast = Math.Max(effContrast, 1.5);
            effEdge = Math.Max(effEdge, 0.35);
            effVignette = Math.Max(effVignette, 0.5);
            break;
    }

    double fieldFreq = 1.0 / Math.Max(10, FieldScale);
    double zoneScale = 1.0 / Math.Max(8, ZoneSize);
    double veinScale = 1.0 / Math.Max(4, (int)(ZoneSize * 0.35));
    int blockSizeMG = Math.Max(6, ZoneSize / 2);

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            // 1. Общее хаос-поле
            double warpX = Fbm(x * fieldFreq * 0.5, y * fieldFreq * 0.5, 3, 0.5, seed + 500) - 0.5;
            double warpY = Fbm(x * fieldFreq * 0.5 + 50.0, y * fieldFreq * 0.5 + 50.0, 3, 0.5, seed + 510) - 0.5;
            double chaosField = Fbm(x * fieldFreq + warpX * 14.0, y * fieldFreq + warpY * 14.0, 4, 0.55, seed + 520);

            // 2. Зоны коррупции
            double zf1, zf2;
            int zcx, zcy;
            Worley2D(x * zoneScale, y * zoneScale, seed + 600, out zf1, out zf2, out zcx, out zcy);
            double zoneBorder = zf2 - zf1;
            double cellRand = Hash21(zcx, zcy, seed + 650);
            double activation = SmoothStep(1.0 - effCorruption, 1.05 - effCorruption, 1.0 - cellRand);

            // 3. Смещение выборки: разрыв в зонах + плавление плоти
            double sampleX = x;
            double sampleY = y;

            if (activation > 0.001 && effCorruption > 0.0)
            {
                double dispAngle = cellRand * Math.PI * 7.0;
                double dispAmt = Hash21(zcx, zcy, seed + 660);
                double dispStrength = effCorruption * 35.0 * (0.3 + dispAmt) * activation;

                sampleX += Math.Cos(dispAngle) * dispStrength;
                sampleY += Math.Sin(dispAngle) * dispStrength;
            }

            if (effMelt > 0.0)
            {
                double localMeltBoost = 1.0 + activation * 1.1;
                double meltStrength = effMelt * localMeltBoost * (0.5 + effMadness);
                double meltNoise = Fbm(x * 0.02, y * 0.02, 3, 0.5, seed + 700);

                sampleY -= meltStrength * 46.0 * meltNoise;
                sampleX += Math.Sin(y * 0.09 + meltNoise * 9.0) * meltStrength * 9.0;
            }

            // 4. MotionGhost: поиск похожего блока + независимые векторы для каналов
            int mbx = (int)Math.Floor(x / (double)blockSizeMG);
            int mby = (int)Math.Floor(y / (double)blockSizeMG);

            bool mgOk = false;
            int bestOx = 0, bestOy = 0;
            double jR = 0.0, jB = 0.0;

            if (effMotionGhost > 0.0)
            {
                double mgGate = Hash21(mbx, mby, seed + 900);
                double mgThreshold = effMotionGhost * (0.4 + activation * 1.3);

                if (mgGate < mgThreshold)
                {
                    mgOk = true;
                    double bestScore = double.MaxValue;
                    int searchRadius = (int)(15 + effMadness * 70);
                    int refX = mbx * blockSizeMG + blockSizeMG / 2;
                    int refY = mby * blockSizeMG + blockSizeMG / 2;
                    double refLum = GetLum(src, refX, refY, width, height);

                    for (int k = 0; k < 6; k++)
                    {
                        double ang = Hash21(mbx + k * 13, mby + k * 7, seed + 910) * Math.PI * 2.0;
                        double rad = Hash21(mbx + k * 5, mby + k * 11, seed + 920) * searchRadius;
                        int ox = (int)(Math.Cos(ang) * rad);
                        int oy = (int)(Math.Sin(ang) * rad);
                        int cxp = WrapInt(refX + ox, width);
                        int cyp = WrapInt(refY + oy, height);
                        double candLum = GetLum(src, cxp, cyp, width, height);
                        double score = Math.Abs(candLum - refLum);

                        if (score < bestScore)
                        {
                            bestScore = score;
                            bestOx = ox;
                            bestOy = oy;
                        }
                    }

                    jR = (Hash21(mbx, mby, seed + 930) - 0.5) * 30.0 * effMotionGhost * (0.3 + effMadness);
                    jB = (Hash21(mbx, mby, seed + 940) - 0.5) * 30.0 * effMotionGhost * (0.3 + effMadness);
                }
            }

            double r, g, b, a;

            if (mgOk)
            {
                int gx = WrapInt((int)Math.Round(sampleX + bestOx), width);
                int gy = WrapInt((int)Math.Round(sampleY + bestOy), height);
                int rX = WrapInt((int)Math.Round(sampleX + bestOx + jR), width);
                int rY = WrapInt((int)Math.Round(sampleY + bestOy + jR * 0.5), height);
                int bX = WrapInt((int)Math.Round(sampleX + bestOx - jB), width);
                int bY = WrapInt((int)Math.Round(sampleY + bestOy - jB * 0.5), height);

                ColorBgra gC = src[gx, gy];
                ColorBgra rC = src[rX, rY];
                ColorBgra bC = src[bX, bY];

                r = rC.R; g = gC.G; b = bC.B; a = gC.A;
            }
            else
            {
                int baseX = WrapInt((int)Math.Round(sampleX), width);
                int baseY = WrapInt((int)Math.Round(sampleY), height);
                ColorBgra c0 = src[baseX, baseY];
                r = c0.R; g = c0.G; b = c0.B; a = c0.A;
            }

            // 5. Битовый обвал памяти (BitplaneCorruption)
            if (effBitplane > 0.0)
            {
                double bpGate = Hash21(mbx, mby, seed + 1000);
                double bpThreshold = effBitplane * (0.35 + activation * 1.2);

                if (bpGate < bpThreshold)
                {
                    int bitPick = 3 + (int)(Hash21(mbx, mby, seed + 1010) * (3.0 + effMadness * 3.0));
                    bitPick = Math.Max(2, Math.Min(7, bitPick));
                    int mask = 1 << bitPick;

                    int ri = ((int)ClampByte(r)) ^ mask;
                    int gi = ((int)ClampByte(g)) ^ mask;
                    int bi = ((int)ClampByte(b)) ^ mask;

                    r = ri; g = gi; b = bi;
                }
            }

            // 6. Принудительный Ч/Б
            if (Grayscale)
            {
                double lumG = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = lumG; g = lumG; b = lumG;
            }

            // 7. Мутация цвета внутри зоны коррупции + шов по границе
            if (activation > 0.001)
            {
                if (cellRand < 0.33)
                {
                    double invR = 255.0 - r, invG = 255.0 - g, invB = 255.0 - b;
                    r += (invR - r) * activation;
                    g += (invG - g) * activation;
                    b += (invB - b) * activation;
                }
                else if (cellRand < 0.66)
                {
                    double hh, ss, vv;
                    RgbToHsv(r, g, b, out hh, out ss, out vv);
                    hh += (cellRand - 0.5) * 720.0 * effMadness;
                    while (hh < 0.0) hh += 360.0;
                    while (hh >= 360.0) hh -= 360.0;
                    double nr, ng, nb;
                    HsvToRgb(hh, ClampDouble(ss * 1.3, 0.0, 1.0), vv, out nr, out ng, out nb);
                    r += (nr - r) * activation;
                    g += (ng - g) * activation;
                    b += (nb - b) * activation;
                }
                else
                {
                    double lv = Math.Floor((0.2126 * r + 0.7152 * g + 0.0722 * b) / 48.0) * 48.0;
                    r += (lv - r) * activation;
                    g += (lv - g) * activation;
                    b += (lv - b) * activation;
                }
            }

            if (effCorruption > 0.0 && zoneBorder < 0.035)
            {
                double seamFactor = (1.0 - zoneBorder / 0.035) * effCorruption;
                r *= (1.0 - seamFactor * 0.55);
                g *= (1.0 - seamFactor * 0.55);
                b *= (1.0 - seamFactor * 0.55);
            }

            // 8. Вены/трещины по границам зон коррупции
            if (effVein > 0.0)
            {
                double vf1, vf2;
                int vcx, vcy;
                Worley2D(x * veinScale + 300.0, y * veinScale + 300.0, seed + 800, out vf1, out vf2, out vcx, out vcy);
                double veinLine = vf2 - vf1;
                double veinGate = (chaosField > (0.5 - effVein * 0.45)) ? 1.0 : 0.0;
                double veinThresh = 0.03 + effVein * 0.10;

                if (veinGate > 0.5 && veinLine < veinThresh)
                {
                    double veinIntensity = (1.0 - veinLine / veinThresh) * effVein;
                    double vAlpha = VeinColor.A / 255.0;

                    r += (VeinColor.R - r) * veinIntensity * vAlpha;
                    g += (VeinColor.G - g) * veinIntensity * vAlpha;
                    b += (VeinColor.B - b) * veinIntensity * vAlpha;
                }
            }

            // 9. FeedbackVortex: аналоговая видео-обратная связь
            if (effVortex > 0.0)
            {
                int iterations = 3 + (int)(effMadness * 8);
                double vcx2 = centerX + (chaosField - 0.5) * width * 0.4;
                double vcy2 = centerY + (chaosField - 0.5) * height * 0.4;

                double accumR = r, accumG = g, accumB = b, totalW = 1.0;
                double curScale = 1.0;
                double curAngle = chaosField * Math.PI * 2.0;
                double angleStep = 18.0 * Math.PI / 180.0 + chaosField * 0.25;
                double scaleStep = 0.93 - effMadness * 0.1;
                double hueAccum = 0.0;

                for (int i = 1; i <= iterations; i++)
                {
                    curScale *= scaleStep;
                    curAngle += angleStep;

                    double wobble = (Hash21(i, (int)(chaosField * 1000), seed + 1300) - 0.5) * 0.4 * effVortex;
                    double ang = curAngle + wobble;

                    double relX = (x - vcx2) * curScale;
                    double relY = (y - vcy2) * curScale;
                    double rotX = relX * Math.Cos(ang) - relY * Math.Sin(ang);
                    double rotY = relX * Math.Sin(ang) + relY * Math.Cos(ang);

                    int ex = WrapInt((int)(vcx2 + rotX), width);
                    int ey = WrapInt((int)(vcy2 + rotY), height);

                    ColorBgra ec = src[ex, ey];

                    hueAccum += effHueDrift;
                    double eh, es, ev;
                    RgbToHsv(ec.R, ec.G, ec.B, out eh, out es, out ev);
                    eh += hueAccum;
                    while (eh < 0.0) eh += 360.0;
                    while (eh >= 360.0) eh -= 360.0;
                    double nr2, ng2, nb2;
                    HsvToRgb(eh, es, ev, out nr2, out ng2, out nb2);

                    double w = effVortex / (i + 1.0);
                    accumR += nr2 * w; accumG += ng2 * w; accumB += nb2 * w; totalW += w;
                }

                r = accumR / totalW;
                g = accumG / totalW;
                b = accumB / totalW;
            }

            // 10. CodeBleed: процедурные окна псевдо-кода
            if (effCodeBleed > 0.0)
            {
                int numWindows = (int)(effCodeBleed * 7.0);

                for (int wi = 0; wi < numWindows; wi++)
                {
                    double wxr = Hash21(wi, 1, seed + 1200);
                    double wyr = Hash21(wi, 2, seed + 1200);
                    double wwr = Hash21(wi, 3, seed + 1200);
                    double whr = Hash21(wi, 4, seed + 1200);

                    int winW = (int)(width * (0.15 + wwr * 0.35));
                    int winH = (int)(height * (0.08 + whr * 0.25));
                    int winX = (int)(wxr * Math.Max(1, width - winW));
                    int winY = (int)(wyr * Math.Max(1, height - winH));

                    int tearX = (int)(warpX * 10.0 * effMadness);
                    int lx = x - winX - tearX;
                    int ly = y - winY;

                    if (lx >= 0 && lx < winW && ly >= 0 && ly < winH)
                    {
                        int titleH = 16;
                        double ovR, ovG, ovB, ovA;

                        if (ly < titleH)
                        {
                            ovR = CodeBleedColor.R * 0.4;
                            ovG = CodeBleedColor.G * 0.4;
                            ovB = CodeBleedColor.B * 0.4;
                            ovA = 0.85;
                        }
                        else
                        {
                            ovR = 10; ovG = 10; ovB = 14; ovA = 0.75;

                            int lineH = 7;
                            int lineIdx = (ly - titleH) / lineH;
                            double lineRand = Hash21(wi * 100 + lineIdx, 5, seed + 1210);
                            double lineLenFrac = Hash21(wi * 100 + lineIdx, 6, seed + 1220);
                            double lineStartFrac = Hash21(wi * 100 + lineIdx, 7, seed + 1230) * 0.3;

                            if (lineRand < 0.85)
                            {
                                double xFrac = lx / (double)winW;
                                if (xFrac >= lineStartFrac && xFrac < lineStartFrac + lineLenFrac * 0.6)
                                {
                                    int glyphCell = 3;
                                    int gx2 = lx / glyphCell;
                                    int gy2 = (ly - titleH) % lineH;
                                    double glyphOn = Hash21(gx2, lineIdx * 7 + gy2, seed + 1240);

                                    if (glyphOn > 0.45)
                                    {
                                        ovR = CodeBleedColor.R;
                                        ovG = CodeBleedColor.G;
                                        ovB = CodeBleedColor.B;
                                        ovA = 0.9;
                                    }
                                }
                            }

                            if (lx < 2 || lx > winW - 3 || ly > winH - 3)
                            {
                                ovR = 200; ovG = 200; ovB = 200; ovA = 0.5;
                            }
                        }

                        double finalA = ovA * effCodeBleed * (CodeBleedColor.A / 255.0);
                        r += (ovR - r) * finalA;
                        g += (ovG - g) * finalA;
                        b += (ovB - b) * finalA;
                    }
                }
            }

            // 11. Постеризация, насыщенность, контраст
            if (effPosterize > 0.0)
            {
                double levels = Math.Max(2.0, 32.0 - effPosterize * 28.0);
                r = Math.Floor(r / 255.0 * levels) / levels * 255.0;
                g = Math.Floor(g / 255.0 * levels) / levels * 255.0;
                b = Math.Floor(b / 255.0 * levels) / levels * 255.0;
            }

            if (effSaturation != 1.0)
            {
                double lumS = 0.2126 * r + 0.7152 * g + 0.0722 * b;
                r = lumS + (r - lumS) * effSaturation;
                g = lumS + (g - lumS) * effSaturation;
                b = lumS + (b - lumS) * effSaturation;
            }

            if (effContrast != 1.0)
            {
                r = (r - 128.0) * effContrast + 128.0;
                g = (g - 128.0) * effContrast + 128.0;
                b = (b - 128.0) * effContrast + 128.0;
            }

            // 12. Контуры (Sobel Edge Detect по оригиналу)
            if (effEdge > 0.0)
            {
                double gx3 = -GetLum(src, x - 1, y - 1, width, height) - 2.0 * GetLum(src, x - 1, y, width, height) - GetLum(src, x - 1, y + 1, width, height)
                            + GetLum(src, x + 1, y - 1, width, height) + 2.0 * GetLum(src, x + 1, y, width, height) + GetLum(src, x + 1, y + 1, width, height);

                double gy3 = -GetLum(src, x - 1, y - 1, width, height) - 2.0 * GetLum(src, x, y - 1, width, height) - GetLum(src, x + 1, y - 1, width, height)
                            + GetLum(src, x - 1, y + 1, width, height) + 2.0 * GetLum(src, x, y + 1, width, height) + GetLum(src, x + 1, y + 1, width, height);

                double edgeMag = Math.Sqrt(gx3 * gx3 + gy3 * gy3);
                double edgeNorm = ClampDouble(edgeMag / 180.0, 0.0, 1.0);

                if (edgeNorm > 0.12)
                {
                    double outAlpha = OutlineColor.A / 255.0;
                    double factor = ClampDouble(effEdge * edgeNorm * outAlpha, 0.0, 1.0);
                    r += (OutlineColor.R - r) * factor;
                    g += (OutlineColor.G - g) * factor;
                    b += (OutlineColor.B - b) * factor;
                }
            }

            // 13. Выжженная виньетка
            if (effVignette > 0.0)
            {
                double dxv = x - centerX;
                double dyv = y - centerY;
                double dist = Math.Sqrt(dxv * dxv + dyv * dyv) / maxRadius;
                double vigFactor = ClampDouble(Math.Pow(dist, 2.0) * effVignette, 0.0, 1.0);
                double vAlpha2 = VignetteColor.A / 255.0;

                r += (VignetteColor.R - r) * vigFactor * vAlpha2;
                g += (VignetteColor.G - g) * vigFactor * vAlpha2;
                b += (VignetteColor.B - b) * vigFactor * vAlpha2;
            }

            // Финальное смешение с оригиналом
            ColorBgra orig = src[x, y];
            double mix = FinalMix;

            double outR = orig.R + (r - orig.R) * mix;
            double outG = orig.G + (g - orig.G) * mix;
            double outB = orig.B + (b - orig.B) * mix;
            double outA = orig.A + (a - orig.A) * mix;

            dst[x, y] = ColorBgra.FromBgra(
                ClampByte(outB),
                ClampByte(outG),
                ClampByte(outR),
                ClampByte(outA)
            );
        }
    }
}