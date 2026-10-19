// Name: Fade
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Fade
// Version: 1.4
// Desc: Генератор затухания и внутреннего градиента для произвольных форм выделения (Лассо, Овал, Маска) для Paint.NET 5.0.13
// Keywords: inner,glow,fade,gradient,lasso,distance,selection,vignette,alpha,градиент,лассо,затухание,свечение
// URL:
// Help:

#region UICode
ColorWheelControl EdgeColor = ColorBgra.FromBgra(0, 0, 0, 255); // [Black?] Цвет края (Outer / Edge)
ColorWheelControl CenterColor = ColorBgra.FromBgra(255, 255, 255, 255); // [White?] Цвет центра (Inner / Center)
ListBoxControl RadiusMode = 0;              // Режим радиуса|Относительно центра (0-100%)|Фиксированный радиус (Пиксели)
DoubleSliderControl RadiusPercent = 100.0;  // [1.0,100.0] Глубина градиента (%)
IntSliderControl CustomRadius = 100;        // [1,1000] Радиус затухания (в пикселях)
ListBoxControl FalloffCurve = 1;            // Форма спада (Curve)|Линейная|Плавная (Smoothstep)|Квадратичная|Экспоненциальная|Затухающая (Inverse)|Волновая (Contour Wave)
DoubleSliderControl PowerExponent = 1.0;    // [0.1,10.0] Сила / Изгиб кривой
ListBoxControl BlendMode = 0;               // Режим наложения|Замена градиентом|Маска прозрачности (Alpha Fade)|Умножение (Multiply)|Экран (Screen)|Добавление (Additive Glow)
CheckboxControl InvertGradient = false;     // Инвертировать направления (Центр <-> Край)
CheckboxControl AddDither = true;           // Сглаживание ступеней (Dithering)
#endregion

private float[] distMap;
private int selX, selY, selW, selH;
private float maxDistFound = 1.0f;

// 1. Построение Exact Euclidean Distance Field (EDT) за O(N) в PreRender
void PreRender(Surface dst, Surface src)
{
    PdnRegion selRegion = EnvironmentParameters.GetSelectionAsPdnRegion();
    Rectangle bounds = EnvironmentParameters.SelectionBounds;

    selX = bounds.X;
    selY = bounds.Y;
    selW = Math.Max(1, bounds.Width);
    selH = Math.Max(1, bounds.Height);

    int total = selW * selH;
    if (distMap == null || distMap.Length < total)
    {
        distMap = new float[total];
    }

    float INF = 1e9f;
    float[] g = new float[total];

    for (int y = 0; y < selH; y++)
    {
        int py = selY + y;
        int rowIdx = y * selW;

        for (int x = 0; x < selW; x++)
        {
            int px = selX + x;
            bool isInside = selRegion.IsVisible(px, py);

            if (!isInside)
            {
                g[rowIdx + x] = 0f;
            }
            else
            {
                bool isBorder = (px == 0 || px == src.Width - 1 || py == 0 || py == src.Height - 1 ||
                                 !selRegion.IsVisible(px - 1, py) || !selRegion.IsVisible(px + 1, py) ||
                                 !selRegion.IsVisible(px, py - 1) || !selRegion.IsVisible(px, py + 1));

                g[rowIdx + x] = isBorder ? 0f : INF;
            }
        }
    }

    float[] fCol = new float[selH];
    float[] dCol = new float[selH];

    for (int x = 0; x < selW; x++)
    {
        for (int y = 0; y < selH; y++) fCol[y] = g[y * selW + x];
        DT1D(fCol, dCol, selH);
        for (int y = 0; y < selH; y++) g[y * selW + x] = dCol[y];
    }

    float[] fRow = new float[selW];
    float[] dRow = new float[selW];

    maxDistFound = 0.001f;

    for (int y = 0; y < selH; y++)
    {
        int rowIdx = y * selW;
        for (int x = 0; x < selW; x++) fRow[x] = g[rowIdx + x];
        DT1D(fRow, dRow, selW);

        for (int x = 0; x < selW; x++)
        {
            float d2 = dRow[x];
            float dist = (float)Math.Sqrt(d2);
            distMap[rowIdx + x] = dist;

            if (dist > maxDistFound)
            {
                maxDistFound = dist;
            }
        }
    }
}

private static void DT1D(float[] f, float[] d, int n)
{
    int[] k = new int[n];
    float[] v = new float[n];
    int kIdx = 0;
    k[0] = 0;
    v[0] = -1e9f;

    for (int q = 1; q < n; q++)
    {
        float s = ((f[q] + q * q) - (f[k[kIdx]] + k[kIdx] * k[kIdx])) / (2.0f * q - 2.0f * k[kIdx]);
        while (s <= v[kIdx])
        {
            kIdx--;
            s = ((f[q] + q * q) - (f[k[kIdx]] + k[kIdx] * k[kIdx])) / (2.0f * q - 2.0f * k[kIdx]);
        }
        kIdx++;
        k[kIdx] = q;
        v[kIdx] = s;
    }

    for (int q = n - 1; q >= 0; q--)
    {
        while (v[kIdx] > q) kIdx--;
        float dq = q - k[kIdx];
        d[q] = dq * dq + f[k[kIdx]];
    }
}

private static double Hash21(int x, int y)
{
    unchecked
    {
        int h = x * 374761393 + y * 668265263;
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7fffffff) / (double)0x7fffffff;
    }
}

private static byte ClampByte(double val)
{
    return (byte)Math.Max(0, Math.Min(255, (int)Math.Round(val)));
}

private static double Lerp(double a, double b, double t)
{
    return a + (b - a) * t;
}

// 2. Отрисовка внутреннего затухания
void Render(Surface dst, Surface src, Rectangle rect)
{
    PdnRegion selRegion = EnvironmentParameters.GetSelectionAsPdnRegion();

    double targetRadius = (RadiusMode == 0) 
        ? maxDistFound * (RadiusPercent / 100.0) 
        : CustomRadius;

    targetRadius = Math.Max(0.001, targetRadius);

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        int localY = y - selY;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            ColorBgra srcPixel = src[x, y];

            if (!selRegion.IsVisible(x, y) || localY < 0 || localY >= selH)
            {
                dst[x, y] = srcPixel;
                continue;
            }

            int localX = x - selX;
            if (localX < 0 || localX >= selW)
            {
                dst[x, y] = srcPixel;
                continue;
            }

            float dist = distMap[localY * selW + localX];
            double t = dist / targetRadius;
            t = Math.Max(0.0, Math.Min(1.0, t));

            if (InvertGradient) t = 1.0 - t;

            switch (FalloffCurve)
            {
                case 0:
                    break;
                case 1:
                    t = t * t * (3.0 - 2.0 * t);
                    break;
                case 2:
                    t = t * t;
                    break;
                case 3:
                    t = (Math.Exp(t * 3.0) - 1.0) / (Math.Exp(3.0) - 1.0);
                    break;
                case 4:
                    t = 1.0 - (1.0 - t) * (1.0 - t);
                    break;
                case 5:
                    t = 0.5 + 0.5 * Math.Cos(t * Math.PI * PowerExponent);
                    break;
            }

            if (FalloffCurve != 5 && PowerExponent != 1.0)
            {
                t = Math.Pow(t, PowerExponent);
            }

            if (AddDither)
            {
                double dither = (Hash21(x, y) - 0.5) * (1.0 / 255.0);
                t = Math.Max(0.0, Math.Min(1.0, t + dither));
            }

            // ColorBgra.Blend корректно интерполирует все 4 канала,
            // включая альфу — поэтому EdgeColor/CenterColor с разной
            // прозрачностью дадут плавный переход и по alpha тоже
            ColorBgra gradColor = ColorBgra.Blend(EdgeColor, CenterColor, (byte)(t * 255.0));
            double gradAlphaWeight = gradColor.A / 255.0;

            ColorBgra finalPixel = srcPixel;

            switch (BlendMode)
            {
                case 0: // Замена градиентом
                    finalPixel = gradColor;
                    break;

                case 1: // Маска прозрачности (Alpha Fade)
                    byte newAlpha = ClampByte(srcPixel.A * t * gradAlphaWeight);
                    finalPixel = ColorBgra.FromBgra(srcPixel.B, srcPixel.G, srcPixel.R, newAlpha);
                    break;

                case 2: // Умножение (Multiply) — теперь учитывает альфуgradColor
                    {
                        double mulB = (srcPixel.B / 255.0) * (gradColor.B / 255.0) * 255.0;
                        double mulG = (srcPixel.G / 255.0) * (gradColor.G / 255.0) * 255.0;
                        double mulR = (srcPixel.R / 255.0) * (gradColor.R / 255.0) * 255.0;

                        finalPixel = ColorBgra.FromBgra(
                            ClampByte(Lerp(srcPixel.B, mulB, gradAlphaWeight)),
                            ClampByte(Lerp(srcPixel.G, mulG, gradAlphaWeight)),
                            ClampByte(Lerp(srcPixel.R, mulR, gradAlphaWeight)),
                            srcPixel.A
                        );
                    }
                    break;

                case 3: // Экран (Screen) — теперь учитывает альфу gradColor
                    {
                        double scrB = (1.0 - (1.0 - srcPixel.B / 255.0) * (1.0 - gradColor.B / 255.0)) * 255.0;
                        double scrG = (1.0 - (1.0 - srcPixel.G / 255.0) * (1.0 - gradColor.G / 255.0)) * 255.0;
                        double scrR = (1.0 - (1.0 - srcPixel.R / 255.0) * (1.0 - gradColor.R / 255.0)) * 255.0;

                        finalPixel = ColorBgra.FromBgra(
                            ClampByte(Lerp(srcPixel.B, scrB, gradAlphaWeight)),
                            ClampByte(Lerp(srcPixel.G, scrG, gradAlphaWeight)),
                            ClampByte(Lerp(srcPixel.R, scrR, gradAlphaWeight)),
                            srcPixel.A
                        );
                    }
                    break;

                case 4: // Добавление (Additive Glow)
                    finalPixel = ColorBgra.FromBgra(
                        ClampByte(srcPixel.B + gradColor.B * gradAlphaWeight),
                        ClampByte(srcPixel.G + gradColor.G * gradAlphaWeight),
                        ClampByte(srcPixel.R + gradColor.R * gradAlphaWeight),
                        srcPixel.A
                    );
                    break;
            }

            dst[x, y] = finalPixel;
        }
    }
}