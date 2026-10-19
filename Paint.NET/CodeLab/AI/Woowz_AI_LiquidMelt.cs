// Name: Liquid Melt
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Liquid Melt
// Version: 1.0
// Desc: Эффект жидкого плавления, подтеков и вязких струй с 3D-блеском, селективностью и хроматической аберрацией
// Keywords: melt,liquid,fluid,drip,flow,viscous,gloss,distortion,aberration,smooth
// URL:
// Help:

#region UICode
ListBoxControl MeltTrigger = 0;           // Селективность плавления|Все изображение|Светлые участки (Highlights)|Темные участки (Shadows)|Насыщенные цвета (Saturation)|Границы объектов (Edges)
DoubleSliderControl MeltAmount = 40.0;     // [0.0,200.0] Сила плавления (Melt Distance)
AngleControl MeltAngle = 90;              // [0,360] Направление стекания (Direction)
DoubleSliderControl Stretch = 2.0;        // [0.1,10.0] Растяжение потеков (Drip Stretch)
DoubleSliderControl NoiseScale = 3.0;     // [0.1,20.0] Масштаб турбулентности (Viscosity Scale)
DoubleSliderControl NoiseDetail = 1.0;    // [0.1,5.0] Детализация струй (Turbulence Detail)
DoubleSliderControl MeltThreshold = 0.5;  // [0.0,1.0] Порог селективности (Trigger Threshold)

// --- Оптические эффекты и 3D ---
DoubleSliderControl ChromaticAberration = 1.5; // [0.0,10.0] Хроматическая аберрация (Color Split)
DoubleSliderControl WetGloss = 0.8;       // [0.0,3.0] Мокрый 3D-блик (Wet Specular)
AngleControl LightAngle = 45;             // [0,360] Направление света (Light Source)

// --- Качество и рендеринг ---
ListBoxControl WrapMode = 2;              // Заворачивание краев|Повтор (Seamless Repeat)|Зеркало (Reflect)|Зажим (Clamp)|Прозрачность
DoubleSliderControl Smoothness = 0.0;     // [0.0,5.0] Сглаживание (Anti-Aliasing)
IntSliderControl BlendOpacity = 100;      // [0,100] Непрозрачность (Blend Opacity)
IntSliderControl CustomSeed = 0;          // [0,1000] Сид случайности (Random Seed)
#endregion

private int selX, selY, selWidth, selHeight;
private double dirX, dirY, perpX, perpY;

private struct Offset2D
{
    public double X;
    public double Y;
    public Offset2D(double x, double y) { X = x; Y = y; }
}

private static readonly Offset2D[] SingleOffset = new Offset2D[] { new Offset2D(0.5, 0.5) };

void PreRender(Surface dst, Surface src)
{
    Rectangle selBounds = EnvironmentParameters.SelectionBounds;
    selX = selBounds.X;
    selY = selBounds.Y;
    selWidth = Math.Max(1, selBounds.Width);
    selHeight = Math.Max(1, selBounds.Height);

    // Направление стекания (0 = Вправо, 90 = Вниз)
    double angleRad = MeltAngle * Math.PI / 180.0;
    dirX = Math.Cos(angleRad);
    dirY = Math.Sin(angleRad);

    // Перпендикулярный вектор для поперечных волн
    perpX = -dirY;
    perpY = dirX;
}

// -------------------------------------------------------------------
// Быстрый GC-Free 2D Noise Generator (Value / Simplex Hybrid)
// -------------------------------------------------------------------
private static double Hash2D(int x, int y, int seed)
{
    int n = x * 1619 + y * 31337 + seed * 1013;
    n = (n << 13) ^ n;
    return (1.0 - ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 1073741824.0);
}

private static double SmoothNoise2D(double x, double y, int seed)
{
    int ix = (int)Math.Floor(x);
    int iy = (int)Math.Floor(y);
    double fx = x - ix;
    double fy = y - iy;

    // Quintic smooth curve: 6t^5 - 15t^4 + 10t^3
    double u = fx * fx * fx * (fx * (fx * 6.0 - 15.0) + 10.0);
    double v = fy * fy * fy * (fy * (fy * 6.0 - 15.0) + 10.0);

    double n00 = Hash2D(ix, iy, seed);
    double n10 = Hash2D(ix + 1, iy, seed);
    double n01 = Hash2D(ix, iy + 1, seed);
    double n11 = Hash2D(ix + 1, iy + 1, seed);

    double nx0 = n00 + u * (n10 - n00);
    double nx1 = n01 + u * (n11 - n01);

    return nx0 + v * (nx1 - nx0);
}

private static double FractalNoise2D(double x, double y, int octaves, double persistence, int seed)
{
    double total = 0;
    double frequency = 1.0;
    double amplitude = 1.0;
    double maxValue = 0;

    for (int i = 0; i < octaves; i++)
    {
        total += SmoothNoise2D(x * frequency, y * frequency, seed + i * 17) * amplitude;
        maxValue += amplitude;
        amplitude *= persistence;
        frequency *= 2.0;
    }

    return total / maxValue;
}

private static double SmoothStep(double edge0, double edge1, double x)
{
    double t = Math.Max(0.0, Math.Min(1.0, (x - edge0) / (edge1 - edge0 + 1e-6)));
    return t * t * (3.0 - 2.0 * t);
}

private ColorBgra SamplePixelRaw(Surface src, int ix, int iy, int wrap, int w, int h)
{
    switch (wrap)
    {
        case 0: // Repeat (Seamless Tile)
            ix = ((ix % w) + w) % w;
            iy = ((iy % h) + h) % h;
            break;

        case 1: // Reflect (Mirror)
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

private double GetLuminance(ColorBgra c)
{
    return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
}

private double GetSaturation(ColorBgra c)
{
    double r = c.R / 255.0;
    double g = c.G / 255.0;
    double b = c.B / 255.0;
    double max = Math.Max(r, Math.Max(g, b));
    double min = Math.Min(r, Math.Min(g, b));
    if (max < 1e-5) return 0.0;
    return (max - min) / max;
}

private double ComputeTriggerWeight(Surface src, double x, double y, int triggerMode, double threshold)
{
    if (triggerMode == 0) return 1.0;

    ColorBgra center = SamplePixelBilinear(src, x, y, 2);

    if (triggerMode == 1) // Highlights
    {
        double lum = GetLuminance(center);
        return SmoothStep(threshold - 0.2, threshold + 0.2, lum);
    }
    else if (triggerMode == 2) // Shadows
    {
        double lum = GetLuminance(center);
        return SmoothStep(threshold - 0.2, threshold + 0.2, 1.0 - lum);
    }
    else if (triggerMode == 3) // Saturation
    {
        double sat = GetSaturation(center);
        return SmoothStep(threshold - 0.2, threshold + 0.2, sat);
    }
    else if (triggerMode == 4) // Edges
    {
        ColorBgra cR = SamplePixelRaw(src, (int)x + 2, (int)y, 2, src.Width, src.Height);
        ColorBgra cL = SamplePixelRaw(src, (int)x - 2, (int)y, 2, src.Width, src.Height);
        ColorBgra cD = SamplePixelRaw(src, (int)x, (int)y + 2, 2, src.Width, src.Height);
        ColorBgra cU = SamplePixelRaw(src, (int)x, (int)y - 2, 2, src.Width, src.Height);

        double gx = GetLuminance(cR) - GetLuminance(cL);
        double gy = GetLuminance(cD) - GetLuminance(cU);
        double edgeMag = Math.Sqrt(gx * gx + gy * gy) * 2.0;

        return SmoothStep(threshold - 0.2, threshold + 0.2, edgeMag);
    }

    return 1.0;
}

private double ComputeDisplacement(Surface src, double x, double y, int triggerMode, double threshold,
                                   double noiseScale, int octaves, double stretch, double meltAmount,
                                   int seed, out double outCross)
{
    double weight = ComputeTriggerWeight(src, x, y, triggerMode, threshold);
    if (weight <= 0.0001)
    {
        outCross = 0.0;
        return 0.0;
    }

    double scale = noiseScale * 0.005;

    // Растяжение координат вдоль линии стекания
    double uStretched = (x * perpX + y * perpY) * scale;
    double vStretched = (x * dirX + y * dirY) * (scale / Math.Max(0.1, stretch));

    double nMain = FractalNoise2D(uStretched, vStretched, octaves, 0.5, seed);
    double nDrip = SmoothNoise2D(uStretched * 1.5, vStretched * 0.5, seed + 99);

    double mainDist = meltAmount * weight * (0.5 + 0.5 * nMain) * (1.0 + nDrip * 0.4);

    // Поперечные турбулентные подтеки
    outCross = meltAmount * weight * 0.25 * FractalNoise2D(x * scale + 10.0, y * scale + 10.0, 2, 0.5, seed + 200);

    return mainDist;
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
    int octaves = Math.Max(1, Math.Min(5, (int)Math.Round(NoiseDetail)));
    byte opacityByte = (byte)(BlendOpacity * 255 / 100);

    // Подготовка векторов источника света для 3D-блика
    double lAngleRad = LightAngle * Math.PI / 180.0;
    double lx = Math.Cos(lAngleRad);
    double ly = Math.Sin(lAngleRad);
    double lz = 0.7;
    double lLen = Math.Sqrt(lx * lx + ly * ly + lz * lz);
    lx /= lLen; ly /= lLen; lz /= lLen;

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

                double crossDist;
                double mainDist = ComputeDisplacement(src, px, py, MeltTrigger, MeltThreshold,
                                                     NoiseScale, octaves, Stretch, MeltAmount, CustomSeed, out crossDist);

                double sampleX = px - dirX * mainDist - perpX * crossDist;
                double sampleY = py - dirY * mainDist - perpY * crossDist;

                // -------------------------------------------------------------
                // Сэмплирование цвета с Хроматической Аберрацией (Color Split)
                // -------------------------------------------------------------
                ColorBgra sampleCol;
                if (ChromaticAberration > 0.05 && mainDist > 0.1)
                {
                    double caShift = ChromaticAberration * (mainDist / Math.Max(1.0, MeltAmount));
                    ColorBgra sampleR = SamplePixelBilinear(src, sampleX - dirX * caShift, sampleY - dirY * caShift, WrapMode);
                    ColorBgra sampleG = SamplePixelBilinear(src, sampleX, sampleY, WrapMode);
                    ColorBgra sampleB = SamplePixelBilinear(src, sampleX + dirX * caShift, sampleY + dirY * caShift, WrapMode);

                    sampleCol = ColorBgra.FromBgra(sampleB.B, sampleG.G, sampleR.R, sampleG.A);
                }
                else
                {
                    sampleCol = SamplePixelBilinear(src, sampleX, sampleY, WrapMode);
                }

                // -------------------------------------------------------------
                // Расчет 3D Мокрого Блеска (Wet Specular Lighting)
                // -------------------------------------------------------------
                if (WetGloss > 0.001 && mainDist > 0.1 && sampleCol.A > 0)
                {
                    double dummy;
                    double dR = ComputeDisplacement(src, px + 2.0, py, MeltTrigger, MeltThreshold, NoiseScale, octaves, Stretch, MeltAmount, CustomSeed, out dummy);
                    double dD = ComputeDisplacement(src, px, py + 2.0, MeltTrigger, MeltThreshold, NoiseScale, octaves, Stretch, MeltAmount, CustomSeed, out dummy);

                    double gradX = (dR - mainDist) * 0.5;
                    double gradY = (dD - mainDist) * 0.5;

                    double normX = -gradX * 0.15;
                    double normY = -gradY * 0.15;
                    double normZ = 1.0;
                    double nLen = Math.Sqrt(normX * normX + normY * normY + normZ * normZ);
                    normX /= nLen; normY /= nLen; normZ /= nLen;

                    double dot = normX * lx + normY * ly + normZ * lz;
                    if (dot > 0.0)
                    {
                        double spec = Math.Pow(dot, 16) * WetGloss * 255.0;
                        byte r = (byte)Math.Min(255, sampleCol.R + (int)spec);
                        byte g = (byte)Math.Min(255, sampleCol.G + (int)spec);
                        byte b = (byte)Math.Min(255, sampleCol.B + (int)spec);
                        sampleCol = ColorBgra.FromBgra(b, g, r, sampleCol.A);
                    }
                }

                // Накопление субпикселей
                double a = sampleCol.A / 255.0;
                accR += sampleCol.R * a;
                accG += sampleCol.G * a;
                accB += sampleCol.B * a;
                accA += a;
            }

            // Итоговый цвет после субпиксельного антиалиасинга
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