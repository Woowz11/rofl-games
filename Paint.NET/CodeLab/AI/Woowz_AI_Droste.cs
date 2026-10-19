// Name: Droste
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Droste
// Version: 1.4.1
// Desc: Бесконечные спирали Эшера с туманом вглубь рекурсии, туннели, авто-вложение по альфа-каналу и сглаживание (Anti-Aliasing)
// Keywords: droste,spiral,fractal,escher,tunnel,recursion,conformal,infinity,alpha,repeat,fog,smooth,antialiasing
// URL:
// Help:

#region UICode
ListBoxControl EffectMode = 0;            // Режим работы|Спираль Эшера (Conformal)|Бесконечный Туннель|Рекурсия по Альфа-каналу
DoubleSliderControl CenterX = 0.0;        // [-1.0,1.0] Центр X
DoubleSliderControl CenterY = 0.0;        // [-1.0,1.0] Центр Y
DoubleSliderControl Zoom = 1.0;           // [0.01,1000.0] Масштаб (Zoom)
AngleControl TwistAngle = 0;              // [0,360] Угол скручивания / Поворот
IntSliderControl Strands = 1;             // [1,30] Количество рукавов спирали
DoubleSliderControl InnerRadius = 0.25;    // [0.001,0.99] Толщина витка (Inner Radius)
ListBoxControl WrapMode = 0;             // Заворачивание краев|Повтор (Seamless Repeat)|Зеркало (Reflect)|Зажим (Clamp)|Прозрачность
CheckboxControl AlternateMirror = false;  // Зеркалировать каждый второй виток
IntSliderControl MaxDepth = 15;           // [1,50] Глубина рекурсии (для Альфа-режима)

// --- Сглаживание / Anti-Aliasing ---
DoubleSliderControl Smoothness = 0.0;     // [0.0,5.0] Сглаживание (Anti-Aliasing)

// --- Эффект тумана / затемнения ---
CheckboxControl EnableFog = false;        // Включить туман / затемнение (По дефолту выключен)
ColorWheelControl FogColor = ColorBgra.FromBgra(0, 0, 0, 255); // Цвет тумана / тени
DoubleSliderControl FogDensity = 0.8;     // [0.0,2.0] Плотность тумана (Fog Density)
ListBoxControl FogDirection = 0;          // Направление тумана|К центру (Вглубь)|К краям (Наружу)
CheckboxControl FogPreserveAlpha = true;  // Сохранять прозрачность (Туман не влияет на альфа-канал)

IntSliderControl BlendOpacity = 100;      // [0,100] Непрозрачность (Смешение)
#endregion

private int selX, selY, selWidth, selHeight;
private double cX, cY;

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

    // Центр рекурсии
    cX = selX + (selWidth * 0.5) + (CenterX * selWidth * 0.5);
    cY = selY + (selHeight * 0.5) + (CenterY * selHeight * 0.5);
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
    double rIn = Math.Max(0.01, Math.Min(0.95, InnerRadius));
    double P = -Math.Log(rIn); // Период логарифма ln(r_out / r_in)
    double N = Strands;

    // Базовый угол закрутки спирали Эшера
    double baseDrosteAngle = Math.Atan2(2.0 * Math.PI * N, P);
    double userAngleRad = -TwistAngle * Math.PI / 180.0;
    double alpha = baseDrosteAngle + userAngleRad;

    double cosA = Math.Cos(alpha);
    double sinA = Math.Sin(alpha);

    byte opacityByte = (byte)(BlendOpacity * 255 / 100);
    double halfW = selWidth * 0.5;
    double halfH = selHeight * 0.5;

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

                ColorBgra sampleCol = ColorBgra.FromBgra(0, 0, 0, 0);
                double fogFactor = 0.0;

                // -------------------------------------------------------------
                // РЕЖИМ 1 И 2: Спираль Эшера (Conformal) / Бесконечный Туннель
                // -------------------------------------------------------------
                if (EffectMode == 0 || EffectMode == 1)
                {
                    double dx = (px - cX) / (halfW * Zoom);
                    double dy = (py - cY) / (halfH * Zoom);

                    double r = Math.Sqrt(dx * dx + dy * dy);
                    double theta = Math.Atan2(dy, dx);

                    if (r < 1e-9) r = 1e-9;

                    double logR = Math.Log(r);
                    double uMod = 0, mappedTheta = 0;
                    double loopIdx = 0;

                    if (EffectMode == 0) // Спираль Эшера
                    {
                        double u = (logR * cosA - theta * sinA) * cosA;
                        double v = (logR * sinA + theta * cosA) * cosA;

                        loopIdx = Math.Floor(u / P);
                        uMod = u % P;
                        if (uMod < 0) uMod += P;

                        double vMod = v % (2.0 * Math.PI);
                        if (vMod < 0) vMod += (2.0 * Math.PI);

                        mappedTheta = vMod;
                    }
                    else // Бесконечный Туннель
                    {
                        loopIdx = Math.Floor(logR / P);
                        uMod = logR % P;
                        if (uMod < 0) uMod += P;

                        mappedTheta = theta + userAngleRad;
                    }

                    double mappedR = Math.Exp(uMod - P);

                    if (AlternateMirror && ((int)Math.Abs(loopIdx) % 2 == 1))
                    {
                        mappedR = Math.Exp((P - uMod) - P);
                    }

                    double sampleX = cX + mappedR * Math.Cos(mappedTheta) * halfW;
                    double sampleY = cY + mappedR * Math.Sin(mappedTheta) * halfH;

                    sampleCol = SamplePixelBilinear(src, sampleX, sampleY, WrapMode);

                    if (EnableFog)
                    {
                        if (FogDirection == 0) // К центру
                        {
                            fogFactor = Math.Max(0.0, Math.Min(1.0, (1.0 - Math.Min(1.0, r)) * FogDensity));
                        }
                        else // К краям
                        {
                            fogFactor = Math.Max(0.0, Math.Min(1.0, r * FogDensity));
                        }
                    }
                }
                // -------------------------------------------------------------
                // РЕЖИМ 3: Автоматическая рекурсия по Альфа-каналу (Alpha Hole)
                // -------------------------------------------------------------
                else
                {
                    double currX = px;
                    double currY = py;

                    double scaleFactor = rIn * Zoom;
                    double cosRot = Math.Cos(-userAngleRad);
                    double sinRot = Math.Sin(-userAngleRad);

                    ColorBgra accumulatedCol = SamplePixelBilinear(src, px, py, WrapMode);
                    int reachedDepth = 0;

                    if (accumulatedCol.A < 255)
                    {
                        for (int depth = 0; depth < MaxDepth; depth++)
                        {
                            double localX = (currX - cX) / scaleFactor;
                            double localY = (currY - cY) / scaleFactor;

                            double rotX = localX * cosRot - localY * sinRot;
                            double rotY = localX * sinRot + localY * cosRot;

                            currX = cX + rotX;
                            currY = cY + rotY;

                            ColorBgra sampled = SamplePixelBilinear(src, currX, currY, WrapMode);

                            if (sampled.A > 0)
                            {
                                reachedDepth = depth + 1;
                                accumulatedCol = ColorBgra.Blend(sampled, accumulatedCol, accumulatedCol.A);
                                if (accumulatedCol.A >= 254) break;
                            }
                        }
                    }

                    sampleCol = accumulatedCol;

                    if (EnableFog)
                    {
                        if (FogDirection == 0) // Вглубь
                        {
                            fogFactor = Math.Min(1.0, ((double)reachedDepth / MaxDepth) * FogDensity * 1.5);
                        }
                        else // К наружному слою
                        {
                            fogFactor = Math.Min(1.0, (1.0 - ((double)reachedDepth / MaxDepth)) * FogDensity);
                        }
                    }
                }

                // Наложение тумана на текущий сэмпл
                if (EnableFog && fogFactor > 0.001)
                {
                    if (!FogPreserveAlpha || sampleCol.A > 0)
                    {
                        byte origA = sampleCol.A;
                        byte fogByte = (byte)(Math.Max(0.0, Math.Min(1.0, fogFactor)) * 255);
                        sampleCol = ColorBgra.Blend(sampleCol, FogColor, fogByte);
                        if (FogPreserveAlpha) sampleCol.A = origA;
                    }
                }

                // Накопление субпикселей (Premultiplied Alpha Accumulation)
                double a = sampleCol.A / 255.0;
                accR += sampleCol.R * a;
                accG += sampleCol.G * a;
                accB += sampleCol.B * a;
                accA += a;
            }

            // Итоговый цвет пикселя с учетом субпикселей
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