// Name: Turing Organic Patterns
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Turing Organic Patterns
// Version: 3.0
// Desc: Настоящая реакционно-диффузная симуляция Тьюринга (Gray-Scott): Лабиринты, Зебра, Бактерии, Точки-Тире, Контуры
// Keywords: turing,pattern,reaction,diffusion,gray-scott,organic,zebra,bacteria,coral,labyrinth,seamless
// URL:
// Help:

#region UICode
ListBoxControl PatternMode = 0;           // Режим генерации|Лабиринт / Коралл (Isotropic Labyrinth)|Зебра / Полосы (Zebra Stripes)|Бактерии / Точки (Bacterial Dots)|Точки и Тире (Dots & Dashes)|Тонкие Контуры (Thin Wireframe)
DoubleSliderControl PatternScale = 12.0;   // [2.0,60.0] Масштаб узора (размер ячейки симуляции в пикселях)
AngleControl FlowAngle = 45;              // [0,360] Направление полос (для Зебры)
DoubleSliderControl Anisotropy = 0.75;    // [0.0,1.0] Направленность / Вытягивание полос
DoubleSliderControl Thickness = 0.5;      // [0.05,0.95] Толщина линий / Баланс (Thickness)
DoubleSliderControl Warp = 0.5;           // [0.0,2.0] Извивание / Органический изгиб (Wiggle)
CheckboxControl Seamless = true;          // Бесшовный повтор текстуры (Seamless Tile)

// --- Палитра ---
ColorWheelControl Color1 = ColorBgra.FromBgra(0, 0, 0, 255);       // Цвет линий / Узора
ColorWheelControl Color2 = ColorBgra.FromBgra(255, 255, 255, 255); // Цвет фона
CheckboxControl Invert = false;           // Инвертировать цвета

// --- Качество и рендеринг ---
DoubleSliderControl Smoothness = 1.0;     // [0.0,5.0] Сглаживание (Anti-Aliasing)
IntSliderControl BlendOpacity = 100;      // [0,100] Непрозрачность (Blend Opacity)
IntSliderControl CustomSeed = 0;          // [0,1000] Сид случайности (Random Seed)
#endregion

private struct Offset2D
{
    public double X;
    public double Y;
    public Offset2D(double x, double y) { X = x; Y = y; }
}

private static readonly Offset2D[] SingleOffset = new Offset2D[] { new Offset2D(0.5, 0.5) };

private int selX, selY, selWidth, selHeight;
private int gridW, gridH;
private double[] fieldNormalized;
private bool useWrap;
private string lastCacheKey = null;

private static int Clamp(int v, int lo, int hi) { return Math.Max(lo, Math.Min(hi, v)); }

private static double Hash(int seed, int index, int salt)
{
    int n = seed * 1619 + index * 31337 + salt * 1013;
    n = (n << 13) ^ n;
    return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483648.0;
}

private static double Smoothstep(double edge0, double edge1, double x)
{
    double t = Math.Max(0.0, Math.Min(1.0, (x - edge0) / (edge1 - edge0 + 1e-6)));
    return t * t * (3.0 - 2.0 * t);
}

void PreRender(Surface dst, Surface src)
{
    Rectangle selBounds = EnvironmentParameters.SelectionBounds;
    selX = selBounds.X;
    selY = selBounds.Y;
    selWidth = Math.Max(1, selBounds.Width);
    selHeight = Math.Max(1, selBounds.Height);

    useWrap = Seamless;

    double cellsXf = selWidth / (double)PatternScale;
    double cellsYf = selHeight / (double)PatternScale;
    gridW = Clamp((int)Math.Round(cellsXf), 36, 180);
    gridH = Clamp((int)Math.Round(cellsYf), 36, 180);

    // Кэшируем результат тяжёлой симуляции — пересчитываем её только когда
    // меняются параметры, реально влияющие на топологию узора.
    string cacheKey = PatternMode + "_" + PatternScale.ToString("F3") + "_" + FlowAngle + "_" +
                       Anisotropy.ToString("F3") + "_" + Seamless + "_" + CustomSeed + "_" +
                       selWidth + "_" + selHeight;

    if (cacheKey != lastCacheKey || fieldNormalized == null)
    {
        RunReactionDiffusion();
        lastCacheKey = cacheKey;
    }
}

private int WrapIndex(int v, int n)
{
    if (useWrap)
    {
        v %= n;
        if (v < 0) v += n;
        return v;
    }
    else
    {
        if (v < 0) return 0;
        if (v >= n) return n - 1;
        return v;
    }
}

private double Laplacian9(double[] arr, int w, int h, int x, int y)
{
    int xW = WrapIndex(x - 1, w);
    int xE = WrapIndex(x + 1, w);
    int yN = WrapIndex(y - 1, h);
    int yS = WrapIndex(y + 1, h);

    double c = arr[y * w + x];
    double n = arr[yN * w + x];
    double s = arr[yS * w + x];
    double e = arr[y * w + xE];
    double wv = arr[y * w + xW];
    double nw = arr[yN * w + xW];
    double ne = arr[yN * w + xE];
    double sw = arr[yS * w + xW];
    double se = arr[yS * w + xE];

    // Классическое 9-точечное ядро Лапласиана (K. Sims), сумма весов = 0
    return (n + s + e + wv) * 0.2 + (nw + ne + sw + se) * 0.05 - c;
}

private double SampleBilinear(double[] arr, int w, int h, double x, double y)
{
    int x0 = (int)Math.Floor(x);
    int y0 = (int)Math.Floor(y);
    double fx = x - x0;
    double fy = y - y0;

    int x1 = x0 + 1;
    int y1 = y0 + 1;

    int ix0 = WrapIndex(x0, w);
    int ix1 = WrapIndex(x1, w);
    int iy0 = WrapIndex(y0, h);
    int iy1 = WrapIndex(y1, h);

    double v00 = arr[iy0 * w + ix0];
    double v10 = arr[iy0 * w + ix1];
    double v01 = arr[iy1 * w + ix0];
    double v11 = arr[iy1 * w + ix1];

    double top = v00 + (v10 - v00) * fx;
    double bottom = v01 + (v11 - v01) * fx;
    return top + (bottom - top) * fy;
}

// Анизотропный Лапласиан: сильная диффузия поперёк направления полос
// подавляет ветвление и вытягивает структуру в прямую линию.
private double AnisotropicLaplacian(double[] arr, int w, int h, int x, int y, double cosA, double sinA, double anisoStrength)
{
    double c = arr[y * w + x];

    double pPlus = SampleBilinear(arr, w, h, x + cosA, y + sinA);
    double pMinus = SampleBilinear(arr, w, h, x - cosA, y - sinA);
    double lapPar = pPlus + pMinus - 2.0 * c;

    double qPlus = SampleBilinear(arr, w, h, x - sinA, y + cosA);
    double qMinus = SampleBilinear(arr, w, h, x + sinA, y - cosA);
    double lapPerp = qPlus + qMinus - 2.0 * c;

    return lapPar + lapPerp * anisoStrength;
}

private void GetFeedKill(out double F, out double K)
{
    switch (PatternMode)
    {
        case 1: // Зебра (та же база, что и коралл + анизотропия ниже)
            F = 0.0545; K = 0.0620; break;
        case 2: // Бактерии / Точки — изолированные пятна
            F = 0.0250; K = 0.0600; break;
        case 3: // Точки и Тире — "черви" среднего размера
            F = 0.0300; K = 0.0565; break;
        case 4: // Тонкие контуры — база как у лабиринта
            F = 0.0545; K = 0.0620; break;
        default: // Лабиринт / Коралл
            F = 0.0545; K = 0.0620; break;
    }
}

private void RunReactionDiffusion()
{
    int w = gridW, h = gridH;
    int size = w * h;

    double[] U = new double[size];
    double[] V = new double[size];
    double[] Un = new double[size];
    double[] Vn = new double[size];

    for (int i = 0; i < size; i++) { U[i] = 1.0; V[i] = 0.0; }

    double F, K;
    GetFeedKill(out F, out K);

    bool anisotropic = (PatternMode == 1);
    double angleRad = FlowAngle * Math.PI / 180.0;
    double cosA = Math.Cos(angleRad);
    double sinA = Math.Sin(angleRad);
    double anisoStrength = 1.0 + Anisotropy * 6.0;

    // Засеваем случайные очаги возмущения, чтобы запустить формирование узора
    int numSeeds = Math.Max(6, size / 180);
    for (int s = 0; s < numSeeds; s++)
    {
        int cx = (int)(Hash(CustomSeed, s, 101) * w);
        int cy = (int)(Hash(CustomSeed, s, 202) * h);
        int radius = 2 + (int)(Hash(CustomSeed, s, 303) * 3);

        for (int oy = -radius; oy <= radius; oy++)
        {
            for (int ox = -radius; ox <= radius; ox++)
            {
                int xx = WrapIndex(cx + ox, w);
                int yy = WrapIndex(cy + oy, h);
                int idx = yy * w + xx;
                U[idx] = 0.5;
                V[idx] = 1.0;
            }
        }
    }

    // Небольшой шум, чтобы нарушить симметрию
    for (int i = 0; i < size; i++)
    {
        V[i] += (Hash(CustomSeed, i, 555) - 0.5) * 0.02;
        if (V[i] < 0) V[i] = 0;
    }

    double Du = 1.0;
    double Dv = 0.5;
    double dt = 1.0;

    int iterations = anisotropic ? 2000 : 2600;

    for (int iter = 0; iter < iterations; iter++)
    {
        if (IsCancelRequested) break;

        for (int y = 0; y < h; y++)
        {
            int rowC = y * w;

            for (int x = 0; x < w; x++)
            {
                int idx = rowC + x;
                double u = U[idx];
                double v = V[idx];

                double lapU = Laplacian9(U, w, h, x, y);
                double lapV = anisotropic
                    ? AnisotropicLaplacian(V, w, h, x, y, cosA, sinA, anisoStrength)
                    : Laplacian9(V, w, h, x, y);

                double uvv = u * v * v;
                double newU = u + (Du * lapU - uvv + F * (1.0 - u)) * dt;
                double newV = v + (Dv * lapV + uvv - (F + K) * v) * dt;

                if (newU < 0) newU = 0; else if (newU > 1) newU = 1;
                if (newV < 0) newV = 0; else if (newV > 1) newV = 1;

                Un[idx] = newU;
                Vn[idx] = newV;
            }
        }

        var tmpU = U; U = Un; Un = tmpU;
        var tmpV = V; V = Vn; Vn = tmpV;
    }

    double[] baseField;

    if (PatternMode == 4) // Тонкие Контуры — градиент поля V
    {
        double[] grad = new double[size];
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int xW = WrapIndex(x - 1, w);
                int xE = WrapIndex(x + 1, w);
                int yN = WrapIndex(y - 1, h);
                int yS = WrapIndex(y + 1, h);

                double gx = V[y * w + xE] - V[y * w + xW];
                double gy = V[yS * w + x] - V[yN * w + x];
                grad[y * w + x] = Math.Sqrt(gx * gx + gy * gy);
            }
        }
        baseField = grad;
    }
    else
    {
        baseField = V;
    }

    double minV = double.MaxValue, maxV = double.MinValue;
    for (int i = 0; i < size; i++)
    {
        if (baseField[i] < minV) minV = baseField[i];
        if (baseField[i] > maxV) maxV = baseField[i];
    }

    double range = maxV - minV;
    if (range < 1e-6) range = 1e-6;

    fieldNormalized = new double[size];
    for (int i = 0; i < size; i++)
    {
        fieldNormalized[i] = (baseField[i] - minV) / range;
    }
}

private double EvaluateField(double px, double py)
{
    double u = (px - selX) / (double)selWidth * gridW;
    double v = (py - selY) / (double)selHeight * gridH;

    if (Warp > 0.001)
    {
        double wf = 0.3;
        double wX = Math.Sin(v * wf + 1.2) * Math.Cos(u * wf + 0.4);
        double wY = Math.Cos(v * wf + 0.8) * Math.Sin(u * wf + 1.7);

        u += wX * Warp * 2.5;
        v += wY * Warp * 2.5;
    }

    double raw = SampleBilinear(fieldNormalized, gridW, gridH, u, v);

    double threshVal = Thickness;
    double epsilon = 0.06;

    return Smoothstep(threshVal - epsilon, threshVal + epsilon, raw);
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

    ColorBgra c1 = Invert ? Color2 : Color1;
    ColorBgra c2 = Invert ? Color1 : Color2;

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

                double t = EvaluateField(px, py);

                ColorBgra sampleCol = ColorBgra.Blend(c2, c1, (byte)(t * 255.0));

                double a = sampleCol.A / 255.0;
                accR += sampleCol.R * a;
                accG += sampleCol.G * a;
                accB += sampleCol.B * a;
                accA += a;
            }

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