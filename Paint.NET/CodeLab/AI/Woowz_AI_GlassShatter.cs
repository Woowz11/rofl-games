// Name: Glass Shatter
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Glass Shatter (Mirror Impact v3.6)
// Version: 3.6
// Desc: Эффект удара по зеркалу с рваной пробоиной по контурам осколков, выносом центра за края, слайдером прозрачности фона и матовостью
// Keywords: glass,shatter,crack,mirror,impact,hammer,radial,spiderweb,refraction,glow,holes,alpha,void
// URL:
// Help:

#region UICode
DoubleSliderControl ImpactX = 0.0;        // [-3.0,3.0] Эпицентр удара X (Можно за края)
DoubleSliderControl ImpactY = 0.0;        // [-3.0,3.0] Эпицентр удара Y (Можно за края)

// --- Структура разлома и разлета ---
IntSliderControl RadialSpokes = 28;       // [6,80] Радиальные лучи (Radial Rays)
IntSliderControl ConcentricRings = 16;    // [3,50] Концентрические кольца (Rings)
DoubleSliderControl CrackRandomness = 0.4; // [0.0,1.0] Изогнутость трещин (Wobble)
DoubleSliderControl CenterCrushSize = 0.3; // [0.0,1.0] Зона мелкой крошки в центре
DoubleSliderControl CenterHoleRadius = 0.0; // [0.0,0.8] Рваная пробоина в центре (Blow Hole)

// --- Выпавшие осколки & Фон ---
DoubleSliderControl FallenShardChance = 0.15; // [0.0,1.0] Шанс выпадения осколков (Fallen Shards)
DoubleSliderControl ExplosionPush = 10.0;     // [0.0,100.0] Сила разлета осколков (Dislodged Shift)
ColorWheelControl VoidColor = ColorBgra.FromBgra(0, 0, 0, 255); // Цвет пробоины / фона (Void Color)
IntSliderControl VoidOpacity = 0;         // [0,100] Непрозрачность фона (0 = Прозрачный / Альфа)

// --- Оптика и Свойства Стекла ---
DoubleSliderControl RefractionIndex = 25.0; // [0.0,100.0] Сила преломления (Refraction)
DoubleSliderControl Shard3DTilt = 0.4;    // [0.0,1.0] 3D-прогиб осколков (Radial Tilt)
DoubleSliderControl ChromaticDispersion = 2.0; // [0.0,10.0] Хроматическая дисперсия (Prism Split)
DoubleSliderControl GlassFrosting = 0.15; // [0.0,1.0] Матовость / Помутнение (Internal Frost)
DoubleSliderControl DoubleRefraction = 0.3; // [0.0,1.0] Двойное отражение (Glass Thickness)

// --- Грани и Свечение Стекла ---
DoubleSliderControl GlassEdgeGlow = 2.5;  // [0.0,5.0] Блик на срезе стекла (Glass Edge Glow)
ColorWheelControl GlassEdgeColor = ColorBgra.FromBgra(255, 235, 190, 255); // Цвет отражения грани (Cyan/White)
DoubleSliderControl CrackThickness = 1.5; // [0.0,10.0] Ширина щелей (Crack Gap)
ColorWheelControl CrackColor = ColorBgra.FromBgra(5, 5, 8, 255); // Цвет глубины трещин

// --- Качество и рендеринг ---
ListBoxControl WrapMode = 2;              // Заворачивание краев|Повтор (Seamless Repeat)|Зеркало (Reflect)|Зажим (Clamp)|Прозрачность
DoubleSliderControl Smoothness = 0.0;     // [0.0,5.0] Сглаживание (Anti-Aliasing)
IntSliderControl BlendOpacity = 100;      // [0,100] Непрозрачность (Blend Opacity)
IntSliderControl CustomSeed = 0;          // [0,1000] Сид случайности (Random Seed)
#endregion

private struct GlassSite
{
    public double X;
    public double Y;
    public double TiltX;
    public double TiltY;
    public bool IsFallen;
    public double FrostSeed;
}

private struct Offset2D
{
    public double X;
    public double Y;
    public Offset2D(double x, double y) { X = x; Y = y; }
}

private static readonly Offset2D[] SingleOffset = new Offset2D[] { new Offset2D(0.5, 0.5) };

private int selX, selY, selWidth, selHeight;
private double impactPxX, impactPxY;
private GlassSite[] shards;

private List<int>[,] spatialGrid;
private int gridCols, gridRows;
private double cellSize;
private double gridOriginX, gridOriginY;

private static double Hash(int seed, int index, int salt)
{
    int n = seed * 1619 + index * 31337 + salt * 1013;
    n = (n << 13) ^ n;
    return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483648.0;
}

private static double FastNoise(double x, double y, int seed)
{
    int ix = (int)Math.Floor(x);
    int iy = (int)Math.Floor(y);
    return Hash(seed, ix, iy);
}

void PreRender(Surface dst, Surface src)
{
    Rectangle selBounds = EnvironmentParameters.SelectionBounds;
    selX = selBounds.X;
    selY = selBounds.Y;
    selWidth = Math.Max(1, selBounds.Width);
    selHeight = Math.Max(1, selBounds.Height);

    impactPxX = selX + (selWidth * 0.5) + (ImpactX * selWidth * 0.5);
    impactPxY = selY + (selHeight * 0.5) + (ImpactY * selHeight * 0.5);

    List<GlassSite> siteList = new List<GlassSite>();
    double maxRadius = Math.Sqrt(selWidth * selWidth * 4.0 + selHeight * selHeight * 4.0);
    double blowHoleRadiusPx = CenterHoleRadius * Math.Min(selWidth, selHeight) * 0.8;

    // 1. Центральная крошка
    double crushRadius = Math.Max(15.0, Math.Min(selWidth, selHeight) * 0.25 * CenterCrushSize);
    int crushCount = (int)(180 * CenterCrushSize);

    for (int i = 0; i < crushCount; i++)
    {
        double u = Hash(CustomSeed, i, 10);
        double v = Hash(CustomSeed, i, 11);
        double r = Math.Sqrt(u) * crushRadius;
        double a = v * 2.0 * Math.PI;

        GlassSite site = new GlassSite();
        site.X = impactPxX + r * Math.Cos(a);
        site.Y = impactPxY + r * Math.Sin(a);
        site.TiltX = (Hash(CustomSeed, i, 12) - 0.5) * 2.0 * Shard3DTilt;
        site.TiltY = (Hash(CustomSeed, i, 13) - 0.5) * 2.0 * Shard3DTilt;

        double holeJitter = (Hash(CustomSeed, i, 333) - 0.5) * 0.4;
        double effectiveHoleR = blowHoleRadiusPx * (1.0 + holeJitter);
        site.IsFallen = (r < effectiveHoleR) || (Hash(CustomSeed, i, 14) < FallenShardChance);

        site.FrostSeed = Hash(CustomSeed, i, 15);
        siteList.Add(site);
    }

    // 2. Генерация радиальных лучей и колец
    int rings = ConcentricRings;
    int baseSpokes = RadialSpokes;

    for (int ring = 0; ring < rings; ring++)
    {
        double normR = (ring + 1.0) / rings;
        double r = crushRadius + (maxRadius - crushRadius) * Math.Pow(normR, 1.8);

        int spokesInRing = baseSpokes + (int)(ring * 0.7);
        double ringAngleOffset = Hash(CustomSeed, ring, 99) * 2.0 * Math.PI;

        for (int spoke = 0; spoke < spokesInRing; spoke++)
        {
            double stepAngle = (2.0 * Math.PI) / spokesInRing;
            double baseAngle = spoke * stepAngle + ringAngleOffset;

            double jitterA = (Hash(CustomSeed, ring * 100 + spoke, 1) - 0.5) * stepAngle * CrackRandomness * 1.2;
            double jitterR = (Hash(CustomSeed, ring * 100 + spoke, 2) - 0.5) * (r * 0.2) * CrackRandomness;

            double finalAngle = baseAngle + jitterA;
            double finalR = Math.Max(1.0, r + jitterR);

            double radDirX = Math.Cos(finalAngle);
            double radDirY = Math.Sin(finalAngle);

            double pushAmount = ExplosionPush * Hash(CustomSeed, ring * 100 + spoke, 5);

            GlassSite site = new GlassSite();
            site.X = impactPxX + finalR * radDirX + radDirX * pushAmount;
            site.Y = impactPxY + finalR * radDirY + radDirY * pushAmount;

            site.TiltX = radDirX * Shard3DTilt + (Hash(CustomSeed, spoke, 3) - 0.5) * 0.3;
            site.TiltY = radDirY * Shard3DTilt + (Hash(CustomSeed, spoke, 4) - 0.5) * 0.3;

            double holeJitter = (Hash(CustomSeed, ring * 100 + spoke, 333) - 0.5) * 0.5;
            double effectiveHoleR = blowHoleRadiusPx * (1.0 + holeJitter);
            site.IsFallen = (finalR < effectiveHoleR) || (Hash(CustomSeed, ring * 1000 + spoke, 77) < FallenShardChance);

            site.FrostSeed = Hash(CustomSeed, ring * 100 + spoke, 88);

            siteList.Add(site);
        }
    }

    shards = siteList.ToArray();

    // 3. Пространственная сетка (-3.0 ... 3.0)
    gridOriginX = -selWidth * 3.5;
    gridOriginY = -selHeight * 3.5;
    double gridMaxX = selWidth * 4.5 + src.Width;
    double gridMaxY = selHeight * 4.5 + src.Height;

    cellSize = 75.0;
    gridCols = Math.Max(1, (int)Math.Ceiling((gridMaxX - gridOriginX) / cellSize));
    gridRows = Math.Max(1, (int)Math.Ceiling((gridMaxY - gridOriginY) / cellSize));

    spatialGrid = new List<int>[gridCols, gridRows];
    for (int gx = 0; gx < gridCols; gx++)
    {
        for (int gy = 0; gy < gridRows; gy++)
        {
            spatialGrid[gx, gy] = new List<int>();
        }
    }

    for (int i = 0; i < shards.Length; i++)
    {
        int gx = Math.Max(0, Math.Min(gridCols - 1, (int)((shards[i].X - gridOriginX) / cellSize)));
        int gy = Math.Max(0, Math.Min(gridRows - 1, (int)((shards[i].Y - gridOriginY) / cellSize)));
        spatialGrid[gx, gy].Add(i);
    }
}

private ColorBgra SamplePixelRaw(Surface src, int ix, int iy, int wrap, int w, int h)
{
    switch (wrap)
    {
        case 0: // Repeat
            ix = ((ix % w) + w) % w;
            iy = ((iy % h) + h) % h;
            break;

        case 1: // Reflect
            ix = Math.Abs(ix);
            iy = Math.Abs(iy);
            ix = (ix / w % 2 == 0) ? (ix % w) : (w - 1 - (ix % w));
            iy = (iy / h % 2 == 0) ? (iy % h) : (h - 1 - (iy % h));
            break;

        case 2: // Clamp
            ix = Math.Max(0, Math.Min(w - 1, ix));
            iy = Math.Max(0, Math.Min(h - 1, iy));
            break;

        case 3: // Transparent
            if (ix < 0 || ix >= w || iy < 0 || iy >= h)
                return ColorBgra.FromBgra(0, 0, 0, 0);
            break;
    }

    return src[ix, iy];
}

private ColorBgra SamplePixelBilinear(Surface src, double x, double y, int wrap)
{
    int w = src.Width;
    int h = src.Height;

    int x0 = (int)Math.Floor(x);
    int y0 = (int)Math.Floor(y);

    double fx = x - x0;
    double fy = y - y0;

    ColorBgra c00 = SamplePixelRaw(src, x0,     y0,     wrap, w, h);
    ColorBgra c10 = SamplePixelRaw(src, x0 + 1, y0,     wrap, w, h);
    ColorBgra c01 = SamplePixelRaw(src, x0,     y0 + 1, wrap, w, h);
    ColorBgra c11 = SamplePixelRaw(src, x0 + 1, y0 + 1, wrap, w, h);

    double w00 = (1.0 - fx) * (1.0 - fy);
    double w10 = fx * (1.0 - fy);
    double w01 = (1.0 - fx) * fy;
    double w11 = fx * fy;

    double a00 = c00.A;
    double a10 = c10.A;
    double a01 = c01.A;
    double a11 = c11.A;

    double totalA = w00 * a00 + w10 * a10 + w01 * a01 + w11 * a11;

    if (totalA < 0.001)
        return ColorBgra.FromBgra(0, 0, 0, 0);

    double r = (w00 * c00.R * a00 + w10 * c10.R * a10 + w01 * c01.R * a01 + w11 * c11.R * a11) / totalA;
    double g = (w00 * c00.G * a00 + w10 * c10.G * a10 + w01 * c01.G * a01 + w11 * c11.G * a11) / totalA;
    double b = (w00 * c00.B * a00 + w10 * c10.B * a10 + w01 * c01.B * a01 + w11 * c11.B * a11) / totalA;

    return ColorBgra.FromBgra(
        (byte)Math.Max(0, Math.Min(255, (int)(b + 0.5))),
        (byte)Math.Max(0, Math.Min(255, (int)(g + 0.5))),
        (byte)Math.Max(0, Math.Min(255, (int)(r + 0.5))),
        (byte)Math.Max(0, Math.Min(255, (int)(totalA + 0.5)))
    );
}

private void FindClosestShards(double px, double py, out int closestIdx, out int secondIdx, out double d1, out double d2)
{
    closestIdx = 0;
    secondIdx = 0;
    d1 = double.MaxValue;
    d2 = double.MaxValue;

    int centerGx = (int)((px - gridOriginX) / cellSize);
    int centerGy = (int)((py - gridOriginY) / cellSize);

    int searchRadius = 2;
    int minGx = Math.Max(0, centerGx - searchRadius);
    int maxGx = Math.Min(gridCols - 1, centerGx + searchRadius);
    int minGy = Math.Max(0, centerGy - searchRadius);
    int maxGy = Math.Min(gridRows - 1, centerGy + searchRadius);

    for (int gx = minGx; gx <= maxGx; gx++)
    {
        for (int gy = minGy; gy <= maxGy; gy++)
        {
            List<int> cell = spatialGrid[gx, gy];
            int cellCount = cell.Count;
            for (int k = 0; k < cellCount; k++)
            {
                int idx = cell[k];
                double dx = px - shards[idx].X;
                double dy = py - shards[idx].Y;
                double distSq = dx * dx + dy * dy;

                if (distSq < d1)
                {
                    d2 = d1;
                    secondIdx = closestIdx;
                    d1 = distSq;
                    closestIdx = idx;
                }
                else if (distSq < d2)
                {
                    d2 = distSq;
                    secondIdx = idx;
                }
            }
        }
    }

    d1 = Math.Sqrt(d1);
    d2 = Math.Sqrt(d2);
}

private Offset2D[] GetAAOffsets(double smoothness, out int count)
{
    if (smoothness <= 0.001)
    {
        count = 1;
        return SingleOffset;
    }

    double r = smoothness * 0.35;

    if (smoothness <= 2.0)
    {
        count = 5;
        return new Offset2D[]
        {
            new Offset2D(0.5, 0.5),
            new Offset2D(0.5 - r, 0.5 - r),
            new Offset2D(0.5 + r, 0.5 - r),
            new Offset2D(0.5 - r, 0.5 + r),
            new Offset2D(0.5 + r, 0.5 + r)
        };
    }
    else
    {
        count = 9;
        return new Offset2D[]
        {
            new Offset2D(0.5 - r, 0.5 - r), new Offset2D(0.5, 0.5 - r), new Offset2D(0.5 + r, 0.5 - r),
            new Offset2D(0.5 - r, 0.5),     new Offset2D(0.5, 0.5),     new Offset2D(0.5 + r, 0.5),
            new Offset2D(0.5 - r, 0.5 + r), new Offset2D(0.5, 0.5 + r), new Offset2D(0.5 + r, 0.5 + r)
        };
    }
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    byte opacityByte = (byte)(BlendOpacity * 255 / 100);

    // Цвет фона пробоины с учетом слайдера непрозрачности
    ColorBgra effectiveVoidCol = VoidColor;
    effectiveVoidCol.A = (byte)(VoidOpacity * 255 / 100);

    int aaCount;
    Offset2D[] aaOffsets = GetAAOffsets(Smoothness, out aaCount);

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            double accR = 0, accG = 0, accB = 0, accA = 0;

            for (int s = 0; s < aaCount; s++)
            {
                double px = x + aaOffsets[s].X;
                double py = y + aaOffsets[s].Y;

                int primaryIdx, secondaryIdx;
                double d1, d2;
                FindClosestShards(px, py, out primaryIdx, out secondaryIdx, out d1, out d2);

                GlassSite site = shards[primaryIdx];
                double edgeDist = d2 - d1;

                // 1. Обработка Выпавших Осколков и Пробоины (Fallen Shards / Blow Hole -> effectiveVoidCol)
                if (site.IsFallen)
                {
                    ColorBgra baseVoidCol = effectiveVoidCol;

                    // Нарисовать свечение грани соседнего сохранившегося осколка поверх дыры
                    GlassSite secondSite = shards[secondaryIdx];
                    if (!secondSite.IsFallen && edgeDist < 3.5 && GlassEdgeGlow > 0.01)
                    {
                        double t = 1.0 - (edgeDist / 3.5);
                        double glowIntensity = t * t * GlassEdgeGlow;
                        byte glowA = (byte)Math.Min(255, (int)(glowIntensity * (GlassEdgeColor.A / 255.0) * 255));

                        baseVoidCol = ColorBgra.Blend(baseVoidCol, GlassEdgeColor, glowA);
                    }

                    double va = baseVoidCol.A / 255.0;
                    accR += baseVoidCol.R * va;
                    accG += baseVoidCol.G * va;
                    accB += baseVoidCol.B * va;
                    accA += va;
                    continue;
                }

                // 2. Оптическое 3D-Преломление и Сдвиг
                double sampleX = px + site.TiltX * RefractionIndex;
                double sampleY = py + site.TiltY * RefractionIndex;

                // 3. Сэмплирование с Хроматической Дисперсией и Двойным преломлением
                ColorBgra sampleCol;
                if (ChromaticDispersion > 0.05)
                {
                    double dispShift = ChromaticDispersion * (1.0 + Math.Abs(site.TiltX) + Math.Abs(site.TiltY));
                    ColorBgra cR = SamplePixelBilinear(src, sampleX + site.TiltX * dispShift, sampleY + site.TiltY * dispShift, WrapMode);
                    ColorBgra cG = SamplePixelBilinear(src, sampleX, sampleY, WrapMode);
                    ColorBgra cB = SamplePixelBilinear(src, sampleX - site.TiltX * dispShift, sampleY - site.TiltY * dispShift, WrapMode);

                    sampleCol = ColorBgra.FromBgra(cB.B, cG.G, cR.R, cG.A);
                }
                else
                {
                    sampleCol = SamplePixelBilinear(src, sampleX, sampleY, WrapMode);
                }

                if (DoubleRefraction > 0.01)
                {
                    ColorBgra ghostCol = SamplePixelBilinear(src, sampleX + site.TiltX * 8.0 * DoubleRefraction, sampleY + site.TiltY * 8.0 * DoubleRefraction, WrapMode);
                    sampleCol = ColorBgra.Blend(sampleCol, ghostCol, (byte)(35 * DoubleRefraction));
                }

                // 4. Матовость и помутнение (Glass Frosting)
                if (GlassFrosting > 0.01 && sampleCol.A > 0)
                {
                    double noise = FastNoise(px * 0.2, py * 0.2, CustomSeed + 500);
                    double frostAmount = noise * GlassFrosting * 0.6;
                    byte frostByte = (byte)(frostAmount * 255);
                    ColorBgra frostWhite = ColorBgra.FromBgra(240, 245, 255, 255);
                    sampleCol = ColorBgra.Blend(sampleCol, frostWhite, frostByte);
                }

                // 5. Свечение граней (Glass Edge Glow)
                double crackGap = CrackThickness;
                double glowWidth = 3.5;

                if (GlassEdgeGlow > 0.01 && edgeDist >= crackGap && edgeDist < crackGap + glowWidth)
                {
                    double t = 1.0 - ((edgeDist - crackGap) / glowWidth);
                    double glowIntensity = t * t * GlassEdgeGlow;

                    byte glowA = (byte)Math.Min(255, (int)(glowIntensity * (GlassEdgeColor.A / 255.0) * 255));
                    sampleCol = ColorBgra.Blend(sampleCol, GlassEdgeColor, glowA);
                }

                // 6. Темная щель трещины (Crack Gap Void)
                if (crackGap > 0.01 && edgeDist < crackGap)
                {
                    double gapAlpha = (1.0 - (edgeDist / crackGap)) * (CrackColor.A / 255.0);
                    byte cA = (byte)(gapAlpha * 255);
                    sampleCol = ColorBgra.Blend(sampleCol, CrackColor, cA);
                }

                // Накопление субпикселей
                double a = sampleCol.A / 255.0;
                accR += sampleCol.R * a;
                accG += sampleCol.G * a;
                accB += sampleCol.B * a;
                accA += a;
            }

            // Итоговый цвет после Anti-Aliasing
            ColorBgra finalCol;
            if (accA <= 0.0001)
            {
                finalCol = ColorBgra.FromBgra(0, 0, 0, 0);
            }
            else
            {
                double avgA = accA / aaCount;
                byte outA = (byte)Math.Min(255, Math.Max(0, Math.Round(avgA * 255.0)));
                byte outR = (byte)Math.Min(255, Math.Max(0, Math.Round(accR / accA)));
                byte outG = (byte)Math.Min(255, Math.Max(0, Math.Round(accG / accA)));
                byte outB = (byte)Math.Min(255, Math.Max(0, Math.Round(accB / accA)));
                finalCol = ColorBgra.FromBgra(outB, outG, outR, outA);
            }

            // Итоговое смешение с оригиналом
            if (opacityByte < 255)
            {
                ColorBgra origCol = src[x, y];
                dst[x, y] = ColorBgra.Blend(origCol, finalCol, opacityByte);
            }
            else
            {
                dst[x, y] = finalCol;
            }
        }
    }
}