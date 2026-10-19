// Name: Seamless
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Seamless
// Version: 7.0
// Desc: Делает текстуру бесшовной: зеркало, растягивание, кроссфейд или случайные кусочки (с пресетами и UV-отладкой)
// Keywords: seamless,tile,texture,wrap,mirror,blend,edge,stretch,patch
// URL:
// Help:

#region UICode
ListBoxControl Method = 0;               // Метод|Зеркало|Растягивание|Кроссфейд|Случайные кусочки
ListBoxControl Preset = 0;               // Пресет|Свои настройки|Плавный шов|Чёткий шов|Кирпичная кладка|Органические пятна|Растягивание линий
CheckboxControl FixHorizontal = true;    // Чинить шов по горизонтали
CheckboxControl FixVertical = true;      // Чинить шов по вертикали
CheckboxControl FlipDirectionX = true;   // Обрабатывать противоположную сторону от шва по X
CheckboxControl FlipDirectionY = true;   // Обрабатывать противоположную сторону от шва по Y
IntSliderControl SeamWidth = 128;        // [1,5000] Ширина шва
DoubleSliderControl SeamBlur = 1.0;      // [0.1,10] Размытость шва (меньше = чётче)
DoubleSliderControl SeamPositionX = 0;   // [0,1] Позиция шва по X
DoubleSliderControl SeamPositionY = 0;   // [0,1] Позиция шва по Y
DoubleSliderControl NoiseAmount = 0;     // [0,100] Сила шума на шве
IntSliderControl NoiseScale = 32;        // [2,500] Масштаб шума
IntSliderControl NoiseSeed = 12345;      // [0,999999] Сид шума
ReseedButtonControl Reseed = 0;          // Перегенерировать шум и кусочки
CheckboxControl ShowDebug = false;       // Отладка: UV-визуализация зон/кусочков
CheckboxControl ShowSeamOnly = false;    // Показать только шов (альфа-маска)
// --- Настройки только для метода "Случайные кусочки" ---
ListBoxControl PatchShape = 0;              // Форма кусочков|Прямоугольники|Круги|Произвольные
IntSliderControl PatchSizeMin = 8;          // [2,500] Мин. размер кусочка
IntSliderControl PatchSizeMax = 32;         // [2,500] Макс. размер кусочка
IntSliderControl PatchDistanceMin = 0;      // [0,500] Мин. расстояние кусочка от шва
IntSliderControl PatchDistanceMax = 32;     // [0,500] Макс. расстояние кусочка от шва
IntSliderControl PatchCount = 12;           // [0,300] Доп. количество кусочков вокруг шва
CheckboxControl PatchRandomRotation = true; // Случайный поворот кусочков
CheckboxControl PatchRandomMirror = true;   // Случайное отзеркаливание кусочков
#endregion

private int width, height;
private int finalNoiseSeed;
private int seamX, seamY;

// Базовая лента кусочков (всегда покрывает сам шов, без дыр)
private int[] ribIdH, ribIdV, ribRotH, ribRotV;
private double[] ribCenterH, ribCenterV, ribHalfAlongH, ribHalfAlongV, ribHalfPerpH, ribHalfPerpV, ribSourcePerpH, ribSourcePerpV;
private bool[] ribFlipAH, ribFlipAV, ribFlipPH, ribFlipPV;

// Доп. случайные кусочки на расстоянии от шва
private int scatterCountH, scatterCountV;
private int[] scIdH, scIdV, scRotH, scRotV;
private double[] scCenterH, scCenterV, scHalfAlongH, scHalfAlongV, scHalfPerpH, scHalfPerpV, scPerpCenterH, scPerpCenterV, scSourcePerpH, scSourcePerpV;
private bool[] scFlipAH, scFlipAV, scFlipPH, scFlipPV;
private List<int>[] scBucketH, scBucketV;

void ApplyPreset()
{
    // Пресеты переопределяют часть параметров ТОЛЬКО для расчёта эффекта.
    // Ползунки на экране при этом визуально не двигаются (ограничение CodeLab),
    // но сам результат будет соответствовать выбранному пресету.
    switch (Preset)
    {
        case 1: // Плавный шов
            Method = 2;
            SeamWidth = 96;
            SeamBlur = 3.0;
            NoiseAmount = 15;
            NoiseScale = 24;
            break;

        case 2: // Чёткий шов
            Method = 0;
            SeamBlur = 0.3;
            NoiseAmount = 0;
            break;

        case 3: // Кирпичная кладка
            Method = 3;
            PatchShape = 0;
            PatchSizeMin = 16; PatchSizeMax = 48;
            PatchDistanceMin = 0; PatchDistanceMax = 24;
            PatchCount = 20;
            PatchRandomRotation = false;
            PatchRandomMirror = true;
            NoiseAmount = 0;
            break;

        case 4: // Органические пятна
            Method = 3;
            PatchShape = 2;
            PatchSizeMin = 12; PatchSizeMax = 40;
            PatchDistanceMin = 4; PatchDistanceMax = 40;
            PatchCount = 30;
            PatchRandomRotation = true;
            PatchRandomMirror = true;
            NoiseAmount = 20;
            NoiseScale = 20;
            break;

        case 5: // Растягивание линий
            Method = 1;
            SeamWidth = 64;
            SeamBlur = 1.0;
            NoiseAmount = 0;
            break;

        default: // Свои настройки — ничего не меняем
            break;
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

double Lerp(double a, double b, double t) => a + (b - a) * t;

double SmoothNoise(double x, double y, double scale, int seed)
{
    x /= scale; y /= scale;
    int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
    int x1 = x0 + 1, y1 = y0 + 1;
    double sx = x - x0, sy = y - y0;

    double n00 = Hash(x0, y0, seed);
    double n10 = Hash(x1, y0, seed);
    double n01 = Hash(x0, y1, seed);
    double n11 = Hash(x1, y1, seed);

    double ix0 = Lerp(n00, n10, sx);
    double ix1 = Lerp(n01, n11, sx);
    return Lerp(ix0, ix1, sy);
}

int Clamp(int v, int max) => Math.Max(0, Math.Min(max - 1, v));
int WrapHard(int v, int max) => ((v % max) + max) % max;

int SignedOffset(int coord, int seam, int size)
{
    int raw = WrapHard(coord - seam, size);
    if (raw > size / 2) raw -= size;
    return raw;
}

double CircularSignedDelta(double pos, double center, int length)
{
    double d = pos - center;
    d = ((d % length) + length) % length;
    if (d > length / 2.0) d -= length;
    return d;
}

double EdgeWeight(double dist, double zoneWidth, double blur)
{
    if (zoneWidth <= 0) return 0;
    double t = 1.0 - Math.Min(1.0, dist / zoneWidth);
    t = Math.Max(0.0, t);
    double s = t * t * (3 - 2 * t);
    double gamma = 1.0 / Math.Max(0.1, blur);
    return Math.Pow(s, gamma);
}

double ApplyNoiseToDistance(double dist, double zoneWidth, double noiseVal)
{
    if (NoiseAmount <= 0 || zoneWidth <= 0) return dist;
    double amplitude = noiseVal * zoneWidth * (NoiseAmount / 100.0);
    return Math.Max(0, dist + amplitude);
}

void InverseRotateFlip(double a0, double p0, int rotation, bool flipAlong, bool flipPerp, out double la, out double lp)
{
    double ra, rp;

    switch (rotation)
    {
        case 90: ra = p0; rp = -a0; break;
        case 180: ra = -a0; rp = -p0; break;
        case 270: ra = -p0; rp = a0; break;
        default: ra = a0; rp = p0; break;
    }

    la = flipAlong ? -ra : ra;
    lp = flipPerp ? -rp : rp;
}

double ShapeDistance(double ca, double cp, double hA, double hP, int shape, int patchId)
{
    hA = Math.Max(0.5, hA);
    hP = Math.Max(0.5, hP);

    if (shape == 1) // Круг
    {
        double rx = ca / hA;
        double ry = cp / hP;
        return Math.Sqrt(rx * rx + ry * ry);
    }
    else if (shape == 2) // Произвольная форма
    {
        double r = Math.Sqrt(ca * ca + cp * cp);
        double baseR = (hA + hP) * 0.5;
        double angle = Math.Atan2(cp, ca);
        double noiseVal = SmoothNoise(Math.Cos(angle) * 3.0, Math.Sin(angle) * 3.0, 1.0, patchId + 90000);
        double wobble = 1.0 + (noiseVal - 0.5) * 0.9;
        double effR = Math.Max(1.0, baseR * wobble);
        return r / effR;
    }
    else // Прямоугольник
    {
        return Math.Max(Math.Abs(ca) / hA, Math.Abs(cp) / hP);
    }
}

double PatchWeight(double d, double blur)
{
    double t = 1.0 - Math.Min(1.0, Math.Max(0.0, d));
    double s = t * t * (3 - 2 * t);
    double gamma = 1.0 / Math.Max(0.1, blur);
    return Math.Pow(s, gamma);
}

// Чёрно-красно-зелёная UV-визуализация кусочка: красный = положение вдоль кусочка,
// зелёный = положение поперёк кусочка — ОБА уже посчитаны в повёрнутых координатах (la,lp),
// поэтому при включённом повороте кусочка градиент на отладке тоже поворачивается.
ColorBgra PatchUVColor(double la, double lp, double halfA, double halfP, double weight)
{
    double u = la / Math.Max(0.5, halfA);
    double v = lp / Math.Max(0.5, halfP);
    double r = Math.Max(0.0, Math.Min(1.0, u * 0.5 + 0.5));
    double g = Math.Max(0.0, Math.Min(1.0, v * 0.5 + 0.5));
    double w = Math.Max(0.0, Math.Min(1.0, weight));
    byte rr = (byte)Math.Round(r * 255 * w);
    byte gg = (byte)Math.Round(g * 255 * w);
    return ColorBgra.FromBgra(0, gg, rr, 255);
}

void BuildRibbonH(int length, Random rng)
{
    ribIdH = new int[length]; ribCenterH = new double[length];
    ribHalfAlongH = new double[length]; ribHalfPerpH = new double[length];
    ribSourcePerpH = new double[length]; ribRotH = new int[length];
    ribFlipAH = new bool[length]; ribFlipPH = new bool[length];

    int sizeMin = Math.Max(2, Math.Min(PatchSizeMin, PatchSizeMax));
    int sizeMax = Math.Max(sizeMin, Math.Max(PatchSizeMin, PatchSizeMax));
    int distMin = Math.Min(PatchDistanceMin, PatchDistanceMax);
    int distMax = Math.Max(PatchDistanceMin, PatchDistanceMax);

    int startPhase = rng.Next(0, length);
    int cum = 0, id = 0;

    while (cum < length)
    {
        int sizeAlong = rng.Next(sizeMin, sizeMax + 1);
        int remaining = length - cum;
        if (sizeAlong > remaining) sizeAlong = remaining;

        double halfAlong = sizeAlong / 2.0;
        double halfPerp = rng.Next(sizeMin, sizeMax + 1) / 2.0;

        double dist = distMin + rng.NextDouble() * (distMax - distMin);
        int side = rng.Next(0, 2) == 0 ? -1 : 1;
        double sourcePerp = side * dist;

        int rotation = PatchRandomRotation ? rng.Next(0, 4) * 90 : 0;
        bool flipAlong = PatchRandomMirror && rng.Next(0, 2) == 0;
        bool flipPerp = PatchRandomMirror && rng.Next(0, 2) == 0;

        double centerIdx = startPhase + cum + halfAlong;

        for (int k = 0; k < sizeAlong; k++)
        {
            int idx = WrapHard(startPhase + cum + k, length);
            ribIdH[idx] = id; ribCenterH[idx] = centerIdx;
            ribHalfAlongH[idx] = halfAlong; ribHalfPerpH[idx] = halfPerp;
            ribSourcePerpH[idx] = sourcePerp; ribRotH[idx] = rotation;
            ribFlipAH[idx] = flipAlong; ribFlipPH[idx] = flipPerp;
        }

        cum += sizeAlong; id++;
    }
}

void BuildRibbonV(int length, Random rng)
{
    ribIdV = new int[length]; ribCenterV = new double[length];
    ribHalfAlongV = new double[length]; ribHalfPerpV = new double[length];
    ribSourcePerpV = new double[length]; ribRotV = new int[length];
    ribFlipAV = new bool[length]; ribFlipPV = new bool[length];

    int sizeMin = Math.Max(2, Math.Min(PatchSizeMin, PatchSizeMax));
    int sizeMax = Math.Max(sizeMin, Math.Max(PatchSizeMin, PatchSizeMax));
    int distMin = Math.Min(PatchDistanceMin, PatchDistanceMax);
    int distMax = Math.Max(PatchDistanceMin, PatchDistanceMax);

    int startPhase = rng.Next(0, length);
    int cum = 0, id = 0;

    while (cum < length)
    {
        int sizeAlong = rng.Next(sizeMin, sizeMax + 1);
        int remaining = length - cum;
        if (sizeAlong > remaining) sizeAlong = remaining;

        double halfAlong = sizeAlong / 2.0;
        double halfPerp = rng.Next(sizeMin, sizeMax + 1) / 2.0;

        double dist = distMin + rng.NextDouble() * (distMax - distMin);
        int side = rng.Next(0, 2) == 0 ? -1 : 1;
        double sourcePerp = side * dist;

        int rotation = PatchRandomRotation ? rng.Next(0, 4) * 90 : 0;
        bool flipAlong = PatchRandomMirror && rng.Next(0, 2) == 0;
        bool flipPerp = PatchRandomMirror && rng.Next(0, 2) == 0;

        double centerIdx = startPhase + cum + halfAlong;

        for (int k = 0; k < sizeAlong; k++)
        {
            int idx = WrapHard(startPhase + cum + k, length);
            ribIdV[idx] = id; ribCenterV[idx] = centerIdx;
            ribHalfAlongV[idx] = halfAlong; ribHalfPerpV[idx] = halfPerp;
            ribSourcePerpV[idx] = sourcePerp; ribRotV[idx] = rotation;
            ribFlipAV[idx] = flipAlong; ribFlipPV[idx] = flipPerp;
        }

        cum += sizeAlong; id++;
    }
}

void BuildScatterH(int length, Random rng)
{
    int count = Math.Max(0, PatchCount);
    scatterCountH = count;

    scIdH = new int[count]; scCenterH = new double[count];
    scHalfAlongH = new double[count]; scHalfPerpH = new double[count];
    scPerpCenterH = new double[count]; scSourcePerpH = new double[count];
    scRotH = new int[count]; scFlipAH = new bool[count]; scFlipPH = new bool[count];
    scBucketH = new List<int>[length];

    int sizeMin = Math.Max(2, Math.Min(PatchSizeMin, PatchSizeMax));
    int sizeMax = Math.Max(sizeMin, Math.Max(PatchSizeMin, PatchSizeMax));
    int distMin = Math.Min(PatchDistanceMin, PatchDistanceMax);
    int distMax = Math.Max(PatchDistanceMin, PatchDistanceMax);

    for (int i = 0; i < count; i++)
    {
        double halfAlong = rng.Next(sizeMin, sizeMax + 1) / 2.0;
        double halfPerp = rng.Next(sizeMin, sizeMax + 1) / 2.0;
        double center = rng.Next(0, length);

        double dist = distMin + rng.NextDouble() * (distMax - distMin);
        int side = rng.Next(0, 2) == 0 ? -1 : 1;
        double perpCenter = side * dist;

        int rotation = PatchRandomRotation ? rng.Next(0, 4) * 90 : 0;
        bool flipAlong = PatchRandomMirror && rng.Next(0, 2) == 0;
        bool flipPerp = PatchRandomMirror && rng.Next(0, 2) == 0;

        scIdH[i] = i + 100000; scCenterH[i] = center;
        scHalfAlongH[i] = halfAlong; scHalfPerpH[i] = halfPerp;
        scPerpCenterH[i] = perpCenter; scSourcePerpH[i] = -perpCenter;
        scRotH[i] = rotation; scFlipAH[i] = flipAlong; scFlipPH[i] = flipPerp;

        int lo = (int)Math.Floor(center - halfAlong) - 1;
        int hi = (int)Math.Ceiling(center + halfAlong) + 1;

        for (int k = lo; k <= hi; k++)
        {
            int idx = WrapHard(k, length);
            if (scBucketH[idx] == null) scBucketH[idx] = new List<int>();
            scBucketH[idx].Add(i);
        }
    }
}

void BuildScatterV(int length, Random rng)
{
    int count = Math.Max(0, PatchCount);
    scatterCountV = count;

    scIdV = new int[count]; scCenterV = new double[count];
    scHalfAlongV = new double[count]; scHalfPerpV = new double[count];
    scPerpCenterV = new double[count]; scSourcePerpV = new double[count];
    scRotV = new int[count]; scFlipAV = new bool[count]; scFlipPV = new bool[count];
    scBucketV = new List<int>[length];

    int sizeMin = Math.Max(2, Math.Min(PatchSizeMin, PatchSizeMax));
    int sizeMax = Math.Max(sizeMin, Math.Max(PatchSizeMin, PatchSizeMax));
    int distMin = Math.Min(PatchDistanceMin, PatchDistanceMax);
    int distMax = Math.Max(PatchDistanceMin, PatchDistanceMax);

    for (int i = 0; i < count; i++)
    {
        double halfAlong = rng.Next(sizeMin, sizeMax + 1) / 2.0;
        double halfPerp = rng.Next(sizeMin, sizeMax + 1) / 2.0;
        double center = rng.Next(0, length);

        double dist = distMin + rng.NextDouble() * (distMax - distMin);
        int side = rng.Next(0, 2) == 0 ? -1 : 1;
        double perpCenter = side * dist;

        int rotation = PatchRandomRotation ? rng.Next(0, 4) * 90 : 0;
        bool flipAlong = PatchRandomMirror && rng.Next(0, 2) == 0;
        bool flipPerp = PatchRandomMirror && rng.Next(0, 2) == 0;

        scIdV[i] = i + 200000; scCenterV[i] = center;
        scHalfAlongV[i] = halfAlong; scHalfPerpV[i] = halfPerp;
        scPerpCenterV[i] = perpCenter; scSourcePerpV[i] = -perpCenter;
        scRotV[i] = rotation; scFlipAV[i] = flipAlong; scFlipPV[i] = flipPerp;

        int lo = (int)Math.Floor(center - halfAlong) - 1;
        int hi = (int)Math.Ceiling(center + halfAlong) + 1;

        for (int k = lo; k <= hi; k++)
        {
            int idx = WrapHard(k, length);
            if (scBucketV[idx] == null) scBucketV[idx] = new List<int>();
            scBucketV[idx].Add(i);
        }
    }
}

void PreRender(Surface dst, Surface src)
{
    ApplyPreset();

    width = src.Width;
    height = src.Height;
    finalNoiseSeed = NoiseSeed + Reseed * 7919;

    seamX = width - 1 - (int)Math.Round(SeamPositionX * (width - 1));
    seamY = height - 1 - (int)Math.Round(SeamPositionY * (height - 1));

    ribIdH = null; ribIdV = null; scIdH = null; scIdV = null; scBucketH = null; scBucketV = null;

    if (Method == 3)
    {
        Random rng = new Random(finalNoiseSeed + 54321);

        if (FixHorizontal) { BuildRibbonH(height, rng); BuildScatterH(height, rng); }
        if (FixVertical) { BuildRibbonV(width, rng); BuildScatterV(width, rng); }
    }
}

void EvaluatePatchH(Surface src, int x, int y, double noiseVal, out double weight, out ColorBgra color, out int debugId)
{
    weight = 0; color = default(ColorBgra); debugId = -1;
    if (ribIdH == null) return;

    int pixelPerp = SignedOffset(x, seamX, width);

    double ra = CircularSignedDelta(y, ribCenterH[y], height);
    double rp = pixelPerp;
    double la, lp;
    InverseRotateFlip(ra, rp, ribRotH[y], ribFlipAH[y], ribFlipPH[y], out la, out lp);

    double d = ShapeDistance(la, lp, ribHalfAlongH[y], ribHalfPerpH[y], PatchShape, ribIdH[y]);
    if (NoiseAmount > 0) d += noiseVal * (NoiseAmount / 100.0) * 0.3;

    double bestWeight = PatchWeight(d, SeamBlur);
    int bestId = ribIdH[y];
    double bestLa = la, bestLp = lp, bestHalfA = ribHalfAlongH[y], bestHalfP = ribHalfPerpH[y];
    int bestSy = WrapHard((int)Math.Round(ribCenterH[y] + la), height);
    int bestSx = WrapHard(seamX + (int)Math.Round(ribSourcePerpH[y] + lp), width);

    List<int> bucket = scBucketH != null ? scBucketH[y] : null;
    if (bucket != null)
    {
        for (int bi = 0; bi < bucket.Count; bi++)
        {
            int i = bucket[bi];
            double sa = CircularSignedDelta(y, scCenterH[i], height);
            double sp = pixelPerp - scPerpCenterH[i];
            double la2, lp2;
            InverseRotateFlip(sa, sp, scRotH[i], scFlipAH[i], scFlipPH[i], out la2, out lp2);

            double sd = ShapeDistance(la2, lp2, scHalfAlongH[i], scHalfPerpH[i], PatchShape, scIdH[i]);
            if (NoiseAmount > 0) sd += noiseVal * (NoiseAmount / 100.0) * 0.3;

            double sw = PatchWeight(sd, SeamBlur);

            if (sw > bestWeight)
            {
                bestWeight = sw;
                bestId = scIdH[i];
                bestLa = la2; bestLp = lp2; bestHalfA = scHalfAlongH[i]; bestHalfP = scHalfPerpH[i];
                bestSy = WrapHard((int)Math.Round(scCenterH[i] + la2), height);
                bestSx = WrapHard(seamX + (int)Math.Round(scSourcePerpH[i] + lp2), width);
            }
        }
    }

    weight = Math.Max(0, bestWeight);
    debugId = bestId;
    color = ShowDebug ? PatchUVColor(bestLa, bestLp, bestHalfA, bestHalfP, bestWeight) : src[bestSx, bestSy];
}

void EvaluatePatchV(Surface src, int x, int y, double noiseVal, out double weight, out ColorBgra color, out int debugId)
{
    weight = 0; color = default(ColorBgra); debugId = -1;
    if (ribIdV == null) return;

    int pixelPerp = SignedOffset(y, seamY, height);

    double ra = CircularSignedDelta(x, ribCenterV[x], width);
    double rp = pixelPerp;
    double la, lp;
    InverseRotateFlip(ra, rp, ribRotV[x], ribFlipAV[x], ribFlipPV[x], out la, out lp);

    double d = ShapeDistance(la, lp, ribHalfAlongV[x], ribHalfPerpV[x], PatchShape, ribIdV[x]);
    if (NoiseAmount > 0) d += noiseVal * (NoiseAmount / 100.0) * 0.3;

    double bestWeight = PatchWeight(d, SeamBlur);
    int bestId = ribIdV[x];
    double bestLa = la, bestLp = lp, bestHalfA = ribHalfAlongV[x], bestHalfP = ribHalfPerpV[x];
    int bestSx = WrapHard((int)Math.Round(ribCenterV[x] + la), width);
    int bestSy = WrapHard(seamY + (int)Math.Round(ribSourcePerpV[x] + lp), height);

    List<int> bucket = scBucketV != null ? scBucketV[x] : null;
    if (bucket != null)
    {
        for (int bi = 0; bi < bucket.Count; bi++)
        {
            int i = bucket[bi];
            double sa = CircularSignedDelta(x, scCenterV[i], width);
            double sp = pixelPerp - scPerpCenterV[i];
            double la2, lp2;
            InverseRotateFlip(sa, sp, scRotV[i], scFlipAV[i], scFlipPV[i], out la2, out lp2);

            double sd = ShapeDistance(la2, lp2, scHalfAlongV[i], scHalfPerpV[i], PatchShape, scIdV[i]);
            if (NoiseAmount > 0) sd += noiseVal * (NoiseAmount / 100.0) * 0.3;

            double sw = PatchWeight(sd, SeamBlur);

            if (sw > bestWeight)
            {
                bestWeight = sw;
                bestId = scIdV[i];
                bestLa = la2; bestLp = lp2; bestHalfA = scHalfAlongV[i]; bestHalfP = scHalfPerpV[i];
                bestSx = WrapHard((int)Math.Round(scCenterV[i] + la2), width);
                bestSy = WrapHard(seamY + (int)Math.Round(scSourcePerpV[i] + lp2), height);
            }
        }
    }

    weight = Math.Max(0, bestWeight);
    debugId = bestId;
    color = ShowDebug ? PatchUVColor(bestLa, bestLp, bestHalfA, bestHalfP, bestWeight) : src[bestSx, bestSy];
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    int acrossX = WrapHard(seamX + 1, width);
    int acrossY = WrapHard(seamY + 1, height);

    bool oneSided = (Method == 0 || Method == 1);

    int anchorX = FlipDirectionX ? seamX : acrossX;
    int anchorY = FlipDirectionY ? seamY : acrossY;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            ColorBgra orig = src[x, y];

            double wh = 0, wv = 0;
            ColorBgra hSample = orig, vSample = orig, dSample = orig;

            double nh = 0, nv = 0;
            if (NoiseAmount > 0)
            {
                nh = (SmoothNoise(x, y, NoiseScale, finalNoiseSeed) - 0.5) * 2.0;
                nv = (SmoothNoise(x + 1000, y + 1000, NoiseScale, finalNoiseSeed) - 0.5) * 2.0;
            }

            if (Method == 3) // Случайные кусочки
            {
                if (FixHorizontal) EvaluatePatchH(src, x, y, nh, out wh, out hSample, out int idH);
                if (FixVertical) EvaluatePatchV(src, x, y, nv, out wv, out vSample, out int idV);

                if (ShowDebug)
                {
                    // Прямой показ UV-цвета победившего кусочка, без смешивания с оригиналом
                    dst[x, y] = (wh >= wv) ? hSample : vSample;
                    continue;
                }

                dSample = (wh >= wv) ? hSample : vSample;
            }
            else
            {
                double nearH = WrapHard(seamX - x, width);
                double farH = WrapHard(x - acrossX, width);
                double nearV = WrapHard(seamY - y, height);
                double farV = WrapHard(y - acrossY, height);

                double distH, distV;

                if (oneSided)
                {
                    distH = FlipDirectionX ? farH : nearH;
                    distV = FlipDirectionY ? farV : nearV;
                }
                else
                {
                    distH = Math.Min(nearH, farH);
                    distV = Math.Min(nearV, farV);
                }

                distH = ApplyNoiseToDistance(distH, SeamWidth, nh);
                distV = ApplyNoiseToDistance(distV, SeamWidth, nv);

                wh = FixHorizontal ? EdgeWeight(distH, SeamWidth, SeamBlur) : 0;
                wv = FixVertical ? EdgeWeight(distV, SeamWidth, SeamBlur) : 0;

                if (Method == 1) // Растягивание
                {
                    hSample = src[anchorX, y];
                    vSample = src[x, anchorY];
                    dSample = src[anchorX, anchorY];
                }
                else // Зеркало и Кроссфейд
                {
                    int mirrorX = WrapHard(2 * seamX + 1 - x, width);
                    int mirrorY = WrapHard(2 * seamY + 1 - y, height);

                    hSample = src[Clamp(mirrorX, width), y];
                    vSample = src[x, Clamp(mirrorY, height)];
                    dSample = src[Clamp(mirrorX, width), Clamp(mirrorY, height)];

                    if (Method == 2) // Кроссфейд — всегда ровно половина
                    {
                        wh *= 0.5;
                        wv *= 0.5;
                    }
                }

                if (ShowDebug)
                {
                    byte rr = (byte)Math.Round(Math.Min(1.0, wh) * 255);
                    byte gg = (byte)Math.Round(Math.Min(1.0, wv) * 255);
                    dst[x, y] = ColorBgra.FromBgra(0, gg, rr, 255);
                    continue;
                }
            }

            double wOrig = (1 - wh) * (1 - wv);
            double wH = wh * (1 - wv);
            double wV = (1 - wh) * wv;
            double wD = wh * wv;

            double rF = orig.R * wOrig + hSample.R * wH + vSample.R * wV + dSample.R * wD;
            double gF = orig.G * wOrig + hSample.G * wH + vSample.G * wV + dSample.G * wD;
            double bF = orig.B * wOrig + hSample.B * wH + vSample.B * wV + dSample.B * wD;
            double aF = orig.A * wOrig + hSample.A * wH + vSample.A * wV + dSample.A * wD;

            ColorBgra result = ColorBgra.FromBgra(
                (byte)Math.Round(bF), (byte)Math.Round(gF), (byte)Math.Round(rF), (byte)Math.Round(aF));

            if (ShowSeamOnly)
            {
                double strength = 1.0 - wOrig;
                byte alpha = (byte)Math.Round(Math.Min(1.0, strength) * 255);
                dst[x, y] = ColorBgra.FromBgra(result.B, result.G, result.R, alpha);
            }
            else
            {
                dst[x, y] = result;
            }
        }
    }
}