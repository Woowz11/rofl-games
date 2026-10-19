// Name: Weathered
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Weathered
// Version: 3.2
// Desc: Комплексный генератор старения и износа (реалистичный кракелюр, улучшенные волосы/волокна, цветная ржавчина с альфа-интенсивностью, настраиваемый цвет открытой стены, прозрачность, подтеки, пыль)
// Keywords: old,aged,vintage,sepia,paper,rust,dirt,scratches,dust,vignette,weathered,retro,peeling,wall,hair,cracks,transparent,старина,сепия,ржавчина,подтеки,трещины,волосы,прозрачность,кракелюр
// URL:
// Help:

#region UICode
ListBoxControl AgePreset = 0;              // Пресет старения|Пользовательский (Custom)|Старое фото (Vintage Sepia)|Ветхая бумага (Old Paper)|Ржавый металл (Rust & Grime)|Пыльная пленка (Dusty Film)|Затопленный документ (Water Damage)|Облупившаяся стена (Peeling Wall)
ListBoxControl TintMode = 1;               // Цветность и оттенок|Оригинальные цвета|Сепия (Classic Sepia)|Пожелтение (Vintage Yellow)|Монохромный фототип|Ржаво-коричневый|Гниль и плесень|Пользовательский цвет
ColorWheelControl CustomTint = ColorBgra.FromBgra(30, 80, 140, 255); // Цвет пользовательского оттенка
DoubleSliderControl TintAmount = 0.0;      // [0.0,1.0] Интенсивность оттенка
DoubleSliderControl Fading = 0.0;          // [0.0,1.0] Выцветание (Потеря контраста)
DoubleSliderControl Vignette = 0.0;        // [0.0,1.0] Виньетка (Потемнение краев)
DoubleSliderControl Distortion = 0.0;      // [0.0,1.0] Деформация и искривление
DoubleSliderControl GrainAmount = 0.0;     // [0.0,1.0] Зернистость / Шум
DoubleSliderControl Cracks = 0.0;          // [0.0,1.0] Плотность сети трещин (кракелюр)
DoubleSliderControl CrackWidth = 1.0;      // [0.5,10.0] Толщина трещин
DoubleSliderControl DirtAmount = 0.0;      // [0.0,1.0] Грязь и налет
DoubleSliderControl Scratches = 0.0;       // [0.0,1.0] Мелкие царапины и пыль
DoubleSliderControl HairFibers = 0.0;      // [0.0,1.0] Волосы и волокна
DoubleSliderControl Creases = 0.0;         // [0.0,1.0] Изломы и сгибы
DoubleSliderControl WaterStains = 0.0;     // [0.0,1.0] Подтеки и разводы
DoubleSliderControl RustAmount = 0.0;      // [0.0,1.0] Ржавчина и коррозия
ColorWheelControl RustColor = ColorBgra.FromBgra(45, 82, 160, 255); // [Sienna?] Цвет ржавчины (Alpha = интенсивность)
DoubleSliderControl PeelingPaint = 0.0;    // [0.0,1.0] Отслоение краски / Старая стена
ColorWheelControl WallColor = ColorBgra.FromBgra(105, 105, 105, 255); // [DimGray?] Цвет открывшейся стены (Alpha = сила проявления)
CheckboxControl TransparentWall = false;   // Прозрачная стена под краской
IntSliderControl EffectScale = 100;        // [10,500] Масштаб текстур старения
IntSliderControl CustomSeed = 12345;       // [0,999999] Случайное зерно (Seed)
ReseedButtonControl Reseed = 0;            // Перемешать
#endregion

// --- Вспомогательные математические генераторы ---

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

// Билинейное сэмплирование для деформации
private static ColorBgra SampleBilinear(Surface src, double x, double y, int w, int h)
{
    x = Math.Max(0.0, Math.Min(w - 1.001, x));
    y = Math.Max(0.0, Math.Min(h - 1.001, y));

    int x0 = (int)x;
    int y0 = (int)y;
    int x1 = Math.Min(w - 1, x0 + 1);
    int y1 = Math.Min(h - 1, y0 + 1);

    double fx = x - x0;
    double fy = y - y0;

    ColorBgra c00 = src[x0, y0];
    ColorBgra c10 = src[x1, y0];
    ColorBgra c01 = src[x0, y1];
    ColorBgra c11 = src[x1, y1];

    double w00 = (1.0 - fx) * (1.0 - fy);
    double w10 = fx * (1.0 - fy);
    double w01 = (1.0 - fx) * fy;
    double w11 = fx * fy;

    byte b = ClampByte(c00.B * w00 + c10.B * w10 + c01.B * w01 + c11.B * w11);
    byte g = ClampByte(c00.G * w00 + c10.G * w10 + c01.G * w01 + c11.G * w11);
    byte r = ClampByte(c00.R * w00 + c10.R * w10 + c01.R * w01 + c11.R * w11);
    byte a = ClampByte(c00.A * w00 + c10.A * w10 + c01.A * w01 + c11.A * w11);

    return ColorBgra.FromBgra(b, g, r, a);
}

// Расстояние до кривой волоска (Кривая Безье по 3 точкам)
private static double DistanceToHair(double px, double py, double p0x, double p0y, double p1x, double p1y, double p2x, double p2y)
{
    double minDistSq = double.MaxValue;
    int steps = 18;
    for (int i = 0; i <= steps; i++)
    {
        double t = i / (double)steps;
        double invT = 1.0 - t;
        double bx = invT * invT * p0x + 2.0 * invT * t * p1x + t * t * p2x;
        double by = invT * invT * p0y + 2.0 * invT * t * p1y + t * t * p2y;

        double dx = px - bx;
        double dy = py - by;
        double dSq = dx * dx + dy * dy;
        if (dSq < minDistSq) minDistSq = dSq;
    }
    return Math.Sqrt(minDistSq);
}

// Ворони-шум (F1/F2) для реалистичной сети трещин (кракелюра)
private static void Worley2D(double x, double y, int seed, out double f1, out double f2)
{
    int ix = (int)Math.Floor(x);
    int iy = (int)Math.Floor(y);
    f1 = double.MaxValue;
    f2 = double.MaxValue;

    for (int oy = -1; oy <= 1; oy++)
    {
        for (int ox = -1; ox <= 1; ox++)
        {
            int cellX = ix + ox;
            int cellY = iy + oy;

            double jx = Hash21(cellX, cellY, seed);
            double jy = Hash21(cellX, cellY, seed + 97);

            double px = cellX + jx;
            double py = cellY + jy;

            double dx = px - x;
            double dy = py - y;
            double d = Math.Sqrt(dx * dx + dy * dy);

            if (d < f1)
            {
                f2 = f1;
                f1 = d;
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

    // Параметры в зависимости от выбранного пресета
    double effTintAmount = TintAmount;
    double effFading = Fading;
    double effVignette = Vignette;
    double effDistortion = Distortion;
    double effGrain = GrainAmount;
    double effPeeling = PeelingPaint;
    double effCracks = Cracks;
    double effDirt = DirtAmount;
    double effScratches = Scratches;
    double effHairFibers = HairFibers;
    double effCreases = Creases;
    double effWater = WaterStains;
    double effRust = RustAmount;
    int effTintMode = TintMode;

    switch (AgePreset)
    {
        case 1: // Старое фото (Vintage Sepia)
            effTintMode = (effTintMode == 0) ? 1 : effTintMode;
            effTintAmount = Math.Max(effTintAmount, 0.6);
            effFading = Math.Max(effFading, 0.4);
            effVignette = Math.Max(effVignette, 0.6);
            effScratches = Math.Max(effScratches, 0.35);
            effGrain = Math.Max(effGrain, 0.25);
            effDirt = Math.Max(effDirt, 0.25);
            break;

        case 2: // Ветхая бумага (Old Paper)
            effTintMode = (effTintMode == 0) ? 2 : effTintMode;
            effTintAmount = Math.Max(effTintAmount, 0.6);
            effFading = Math.Max(effFading, 0.5);
            effCreases = Math.Max(effCreases, 0.6);
            effWater = Math.Max(effWater, 0.45);
            effDirt = Math.Max(effDirt, 0.4);
            effDistortion = Math.Max(effDistortion, 0.25);
            break;

        case 3: // Ржавый металл (Rust & Grime)
            effTintMode = (effTintMode == 0) ? 4 : effTintMode;
            effTintAmount = Math.Max(effTintAmount, 0.5);
            effRust = Math.Max(effRust, 0.75);
            effDirt = Math.Max(effDirt, 0.6);
            effFading = Math.Max(effFading, 0.3);
            effVignette = Math.Max(effVignette, 0.5);
            break;

        case 4: // Пыльная пленка (Dusty Film)
            effTintMode = (effTintMode == 0) ? 3 : effTintMode;
            effTintAmount = Math.Max(effTintAmount, 0.5);
            effScratches = Math.Max(effScratches, 0.6);
            effHairFibers = Math.Max(effHairFibers, 0.5);
            effGrain = Math.Max(effGrain, 0.45);
            effVignette = Math.Max(effVignette, 0.5);
            break;

        case 5: // Затопленный документ (Water Damage)
            effTintMode = (effTintMode == 0) ? 5 : effTintMode;
            effTintAmount = Math.Max(effTintAmount, 0.5);
            effWater = Math.Max(effWater, 0.8);
            effDirt = Math.Max(effDirt, 0.5);
            effFading = Math.Max(effFading, 0.4);
            effCreases = Math.Max(effCreases, 0.35);
            effDistortion = Math.Max(effDistortion, 0.3);
            break;

        case 6: // Облупившаяся стена (Peeling Wall)
            effPeeling = Math.Max(effPeeling, 0.75);
            effCracks = Math.Max(effCracks, 0.5);
            effDirt = Math.Max(effDirt, 0.65);
            effCreases = Math.Max(effCreases, 0.4);
            effDistortion = Math.Max(effDistortion, 0.2);
            break;
    }

    double noiseScale = (EffectScale * 0.05);

    // Точный цвет настраиваемой ржавчины (Alpha = интенсивность эффекта)
    double rustBaseR = RustColor.R;
    double rustBaseG = RustColor.G;
    double rustBaseB = RustColor.B;
    double rustAlphaMul = RustColor.A / 255.0;

    // Точный цвет открывшейся стены (Alpha = сила проявления)
    double wallBaseR = WallColor.R;
    double wallBaseG = WallColor.G;
    double wallBaseB = WallColor.B;
    double wallAlphaMul = WallColor.A / 255.0;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            double sampleX = x;
            double sampleY = y;

            // 1. Деформация картинки (Distortion)
            if (effDistortion > 0.0)
            {
                double dDx = (Fbm(x * 0.008, y * 0.008, 3, 0.5, seed + 800) - 0.5) * effDistortion * 70.0;
                double dDy = (Fbm(x * 0.008 + 10.0, y * 0.008 + 10.0, 3, 0.5, seed + 810) - 0.5) * effDistortion * 70.0;
                double wave = Math.Sin((x + y) * 0.01) * effDistortion * 12.0;

                sampleX += dDx + wave;
                sampleY += dDy + wave;
            }

            ColorBgra srcPixel = SampleBilinear(src, sampleX, sampleY, width, height);

            double scaleX = x / noiseScale;
            double scaleY = y / noiseScale;

            double r = srcPixel.R;
            double g = srcPixel.G;
            double b = srcPixel.B;
            double alpha = srcPixel.A;

            // 2. Выцветание и потеря контраста (Fading)
            double lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;

            if (effFading > 0.0)
            {
                r = r + (lum - r) * effFading;
                g = g + (lum - g) * effFading;
                b = b + (lum - b) * effFading;

                r = 15.0 * effFading + r * (1.0 - 0.2 * effFading);
                g = 15.0 * effFading + g * (1.0 - 0.2 * effFading);
                b = 10.0 * effFading + b * (1.0 - 0.2 * effFading);
            }

            // 3. Цветность и оттенок (Tinting)
            if (effTintMode > 0 && effTintAmount > 0.0)
            {
                double targetR = lum;
                double targetG = lum;
                double targetB = lum;

                switch (effTintMode)
                {
                    case 1: targetR = lum * 1.18; targetG = lum * 0.92; targetB = lum * 0.70; break; // Сепия
                    case 2: targetR = lum * 1.12; targetG = lum * 1.05; targetB = lum * 0.78; break; // Пожелтение
                    case 3: targetR = lum; targetG = lum; targetB = lum; break;                     // Монохром
                    case 4: targetR = lum * 1.25; targetG = lum * 0.75; targetB = lum * 0.45; break; // Ржавый
                    case 5: targetR = lum * 0.85; targetG = lum * 0.98; targetB = lum * 0.75; break; // Плесневелый
                    case 6: // Пользовательский
                        targetR = (CustomTint.R / 255.0) * lum * 1.3;
                        targetG = (CustomTint.G / 255.0) * lum * 1.3;
                        targetB = (CustomTint.B / 255.0) * lum * 1.3;
                        break;
                }

                r = r + (targetR - r) * effTintAmount;
                g = g + (targetG - g) * effTintAmount;
                b = b + (targetB - b) * effTintAmount;
            }

            // 4. Реалистичная сеть трещин (кракелюр) на основе Ворони-ячеек
            if (effCracks > 0.0)
            {
                double warpAmt = 1.1;
                double wCx = scaleX * 0.22 + (Fbm(scaleX * 0.05, scaleY * 0.05, 3, 0.5, seed + 1150) - 0.5) * warpAmt;
                double wCy = scaleY * 0.22 + (Fbm(scaleX * 0.05 + 7.0, scaleY * 0.05 + 7.0, 3, 0.5, seed + 1160) - 0.5) * warpAmt;

                double f1a, f2a;
                Worley2D(wCx, wCy, seed + 1170, out f1a, out f2a);
                double cellDiffA = f2a - f1a;

                double f1b, f2b;
                Worley2D(wCx * 2.6 + 40.0, wCy * 2.6 + 40.0, seed + 1180, out f1b, out f2b);
                double cellDiffB = f2b - f1b;

                double threshWidth = 0.018 + CrackWidth * 0.016;
                double crackA = SmoothStep(threshWidth, 0.0, cellDiffA);
                double crackB = SmoothStep(threshWidth * 0.55, 0.0, cellDiffB) * 0.5;

                double coverageNoise = Fbm(scaleX * 0.06 + 300.0, scaleY * 0.06 + 300.0, 3, 0.5, seed + 1190);
                double coverageMask = SmoothStep(1.0 - effCracks, 1.05 - effCracks, coverageNoise);

                double totalCrack = Math.Max(crackA, crackB) * coverageMask;

                if (totalCrack > 0.001)
                {
                    double crackDepth = totalCrack * Math.Min(1.0, 0.35 + effCracks * 1.4);
                    r = Math.Max(0.0, r - crackDepth * 215.0);
                    g = Math.Max(0.0, g - crackDepth * 198.0);
                    b = Math.Max(0.0, b - crackDepth * 178.0);
                }
            }

            // 5. Изломы и сгибы (Creases) — вытянутые вдоль случайного направления "сгибы бумаги"
            if (effCreases > 0.0)
            {
                double creaseAngle = Hash21(0, 0, seed + 5) * Math.PI;
                double ca = Math.Cos(creaseAngle);
                double sa = Math.Sin(creaseAngle);
                double rotX = (scaleX * ca - scaleY * sa) * 0.12;
                double rotY = (scaleX * sa + scaleY * ca) * 0.35;

                double warpX = rotX + (Fbm(rotX * 0.4, rotY * 0.4, 3, 0.5, seed + 10) - 0.5) * 2.0;
                double warpY = rotY + (Fbm(rotX * 0.4 + 5.0, rotY * 0.4 + 5.0, 3, 0.5, seed + 20) - 0.5) * 2.0;

                double ridge = Math.Abs(Fbm(warpX, warpY, 3, 0.5, seed + 30) - 0.5) * 2.0;
                double creaseLine = 1.0 - Math.Pow(ridge, 0.22);

                if (creaseLine > 0.05)
                {
                    double creaseVal = (creaseLine - 0.5) * effCreases * 1.6;
                    r += creaseVal * 120.0;
                    g += creaseVal * 100.0;
                    b += creaseVal * 70.0;
                }
            }

            // 6. Водные подтёки и разводы (Water Stains)
            if (effWater > 0.0)
            {
                double wNoise = Fbm(scaleX * 0.12, scaleY * 0.12, 4, 0.55, seed + 100);

                double ring1 = Math.Max(0.0, 1.0 - Math.Abs(wNoise - 0.42) * 22.0);
                double ring2 = Math.Max(0.0, 1.0 - Math.Abs(wNoise - 0.58) * 25.0);
                double stainInterior = (wNoise > 0.42 && wNoise < 0.58) ? 0.12 : 0.0;

                double totalStain = (ring1 * 0.7 + ring2 * 0.6 + stainInterior) * effWater;

                if (totalStain > 0.0)
                {
                    r -= totalStain * 60.0;
                    g -= totalStain * 50.0;
                    b -= totalStain * 30.0;
                }
            }

            // Расстояние до центра для виньетки и краевой грязи
            double dx = x - centerX;
            double dy = y - centerY;
            double distFromCenter = Math.Sqrt(dx * dx + dy * dy) / maxRadius;

            // 7. Грязь и налёт (Dirt & Grime)
            if (effDirt > 0.0)
            {
                double dNoise = Fbm(scaleX * 0.35, scaleY * 0.35, 4, 0.6, seed + 200);
                double edgeFactor = Math.Pow(distFromCenter, 2.2) * 0.6;
                double dirtVal = Math.Pow(dNoise, 1.7) * (effDirt + edgeFactor);

                if (dirtVal > 0.05)
                {
                    r -= dirtVal * 90.0;
                    g -= dirtVal * 85.0;
                    b -= dirtVal * 70.0;
                }
            }

            // 8. Ржавчина с настраиваемым цветом (Rust Corrosion & Custom Color, Alpha = интенсивность)
            if (effRust > 0.0)
            {
                double rNoise = Fbm(scaleX * 0.25, scaleY * 0.25, 5, 0.6, seed + 300);
                double rustMask = SmoothStep(0.38, 0.72, rNoise) * effRust * rustAlphaMul;

                if (rustMask > 0.0)
                {
                    double rustGrain = Hash21(x, y, seed + 350) * 0.4 + 0.8;
                    double finalRustR = rustBaseR * rustGrain;
                    double finalRustG = rustBaseG * rustGrain;
                    double finalRustB = rustBaseB * rustGrain;

                    r = r + (finalRustR - r) * rustMask;
                    g = g + (finalRustG - g) * rustMask;
                    b = b + (finalRustB - b) * rustMask;
                }
            }

            // 9. Мелкие царапины и пылинки (Scratches & Dust)
            if (effScratches > 0.0)
            {
                double dustHash = Hash21(x, y, seed + 500);
                double dustThreshold = effScratches * 0.003;

                if (dustHash < dustThreshold) // Темные точечные пылинки
                {
                    r *= 0.2; g *= 0.2; b *= 0.2;
                }
                else if (dustHash > (1.0 - dustThreshold * 0.5)) // Светлые микро-пылинки
                {
                    r += 120.0; g += 120.0; b += 110.0;
                }
            }

            // 10. Реалистичные волосы и волокна (разной толщины, тона и формы)
            if (effHairFibers > 0.0)
            {
                int gridSize = Math.Max(50, (int)(150 * (EffectScale / 100.0)));
                int gridCellX = (int)Math.Floor(x / (double)gridSize);
                int gridCellY = (int)Math.Floor(y / (double)gridSize);

                for (int hcy = -1; hcy <= 1; hcy++)
                {
                    for (int hcx = -1; hcx <= 1; hcx++)
                    {
                        int cx = gridCellX + hcx;
                        int cy = gridCellY + hcy;

                        double hExist = Hash21(cx, cy, seed + 700);
                        if (hExist < effHairFibers * 0.4)
                        {
                            double p0x = (cx + Hash21(cx, cy, seed + 710)) * gridSize;
                            double p0y = (cy + Hash21(cx, cy, seed + 720)) * gridSize;

                            double curveLen = gridSize * (0.6 + Hash21(cx, cy, seed + 722) * 0.6);
                            double angle0 = Hash21(cx, cy, seed + 725) * Math.PI * 2.0;

                            double p1x = p0x + Math.Cos(angle0) * curveLen * 0.5;
                            double p1y = p0y + Math.Sin(angle0) * curveLen * 0.5;

                            double angle1 = angle0 + (Hash21(cx, cy, seed + 745) - 0.5) * 2.4;
                            double p2x = p1x + Math.Cos(angle1) * curveLen * 0.5;
                            double p2y = p1y + Math.Sin(angle1) * curveLen * 0.5;

                            double hDist = DistanceToHair(x, y, p0x, p0y, p1x, p1y, p2x, p2y);
                            double hairWidth = 0.5 + Hash21(cx, cy, seed + 750) * 1.4;

                            if (hDist < hairWidth)
                            {
                                double hairEdge = 1.0 - hDist / hairWidth;
                                hairEdge = hairEdge * hairEdge * (3.0 - 2.0 * hairEdge);

                                double tone = Hash21(cx, cy, seed + 755);
                                if (tone < 0.78)
                                {
                                    double darkFactor = 1.0 - hairEdge * 0.82;
                                    r *= darkFactor;
                                    g *= darkFactor;
                                    b *= darkFactor;
                                }
                                else
                                {
                                    double lightAdd = hairEdge * 85.0;
                                    r += lightAdd;
                                    g += lightAdd;
                                    b += lightAdd;
                                }
                            }
                        }
                    }
                }
            }

            // 11. Виньетка (Vignette)
            if (effVignette > 0.0)
            {
                double vigFactor = 1.0 - Math.Pow(Math.Max(0.0, distFromCenter - 0.15), 1.8) * effVignette * 1.1;
                vigFactor = ClampDouble(vigFactor, 0.0, 1.0);

                r *= vigFactor;
                g *= vigFactor;
                b *= vigFactor;
            }

            // 12. Зернистость (Film Grain)
            if (effGrain > 0.0)
            {
                double grainNoise = (Hash21(x, y, seed + 600) - 0.5) * effGrain * 65.0;
                r += grainNoise;
                g += grainNoise;
                b += grainNoise;
            }

            // 13. ПОСЛЕДНИЙ ЭФФЕКТ: Отслоение краски / Старая стена (Peeling Paint & Transparent Wall)
            if (effPeeling > 0.0)
            {
                double peelNoise = Fbm(scaleX * 0.18, scaleY * 0.18, 4, 0.65, seed + 900);
                double peelThresh = 1.0 - effPeeling * 0.6;
                double shadowMargin = 0.04;

                if (peelNoise > peelThresh)
                {
                    // Облупившийся участок
                    if (TransparentWall)
                    {
                        alpha = 0; // Полная прозрачность дыры
                    }
                    else
                    {
                        double wallGrain = Hash21(x, y, seed + 950);
                        double wR = wallBaseR + (wallGrain - 0.5) * 30.0;
                        double wG = wallBaseG + (wallGrain - 0.5) * 25.0;
                        double wB = wallBaseB + (wallGrain - 0.5) * 20.0;

                        r = r + (wR - r) * wallAlphaMul;
                        g = g + (wG - g) * wallAlphaMul;
                        b = b + (wB - b) * wallAlphaMul;
                    }
                }
                else if (peelNoise > (peelThresh - shadowMargin))
                {
                    // Край оставшейся краски - 3D-тень от края отслоения!
                    double shadowT = (peelThresh - peelNoise) / shadowMargin;
                    double shadowFactor = 0.35 + 0.65 * Math.Pow(shadowT, 0.5);

                    r *= shadowFactor;
                    g *= shadowFactor;
                    b *= shadowFactor;
                }
            }

            // Выходной пиксель
            dst[x, y] = ColorBgra.FromBgra(
                ClampByte(b),
                ClampByte(g),
                ClampByte(r),
                ClampByte(alpha)
            );
        }
    }
}