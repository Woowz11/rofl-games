// Name: Seamless
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Seamless
// Version: 4.6
// Desc: Делает текстуру бесшовной: зеркало, растягивание линии или кроссфейд 50/50
// Keywords: seamless,tile,texture,wrap,mirror,blend,edge,stretch
// URL:
// Help:

#region UICode
ListBoxControl Method = 0;               // Метод|Зеркало|Растягивание|Кроссфейд
CheckboxControl FixHorizontal = true;    // Чинить шов по горизонтали
CheckboxControl FixVertical = true;      // Чинить шов по вертикали
CheckboxControl FlipDirectionX = true;  // Обрабатывать противоположную сторону от шва по X
CheckboxControl FlipDirectionY = true;  // Обрабатывать противоположную сторону от шва по Y
IntSliderControl SeamWidth = 128;         // [1,5000] Ширина шва
DoubleSliderControl SeamBlur = 1.0;      // [0.1,10] Размытость шва (меньше = чётче)
DoubleSliderControl SeamPositionX = 0;   // [0,1] Позиция шва по X
DoubleSliderControl SeamPositionY = 0;   // [0,1] Позиция шва по Y
DoubleSliderControl NoiseAmount = 0;     // [0,100] Сила шума на шве
IntSliderControl NoiseScale = 32;        // [2,500] Масштаб шума
IntSliderControl NoiseSeed = 12345;      // [0,999999] Сид шума
ReseedButtonControl Reseed = 0;          // Перегенерировать шум
CheckboxControl ShowSeamOnly = false;    // Показать только шов
#endregion

private int width, height;
private int finalNoiseSeed;

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

void PreRender(Surface dst, Surface src)
{
    width = src.Width;
    height = src.Height;
    finalNoiseSeed = NoiseSeed + Reseed * 7919;
}

int Clamp(int v, int max) => Math.Max(0, Math.Min(max - 1, v));
int WrapHard(int v, int max) => ((v % max) + max) % max;

double EdgeWeight(double dist, int seamWidth, double blur)
{
    double t = 1.0 - Math.Min(1.0, dist / seamWidth);
    t = Math.Max(0.0, t);
    double s = t * t * (3 - 2 * t);
    double gamma = 1.0 / Math.Max(0.1, blur);
    return Math.Pow(s, gamma);
}

// Добавляет шум к расстоянию до шва, но не даёт ему вылезти за пределы зоны (чтобы шум не включал эффект вдали от шва)
double ApplyNoiseToDistance(double dist, int seamWidth, double noiseVal)
{
    if (NoiseAmount <= 0 || dist > seamWidth) return dist;

    double prox = dist / seamWidth;
    double amplitude = noiseVal * seamWidth * (NoiseAmount / 100.0) * prox;
    return Math.Max(0, Math.Min(seamWidth, dist + amplitude));
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    int seamX = width - 1 - (int)Math.Round(SeamPositionX * (width - 1));
    int seamY = height - 1 - (int)Math.Round(SeamPositionY * (height - 1));

    // Пиксель сразу за границей шва (физически противоположная сторона)
    int acrossX = WrapHard(seamX + 1, width);
    int acrossY = WrapHard(seamY + 1, height);

    bool oneSided = (Method == 0 || Method == 1);

    // Для зеркала/растягивания источник зависит от направления, для кроссфейда всегда через acrossX/Y
    int anchorX = FlipDirectionX ? seamX : acrossX;
    int anchorY = FlipDirectionY ? seamY : acrossY;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            ColorBgra orig = src[x, y];

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
            else // Кроссфейд — всегда симметрично с обеих сторон шва
            {
                distH = Math.Min(nearH, farH);
                distV = Math.Min(nearV, farV);
            }

            if (NoiseAmount > 0)
            {
                double nh = (SmoothNoise(x, y, NoiseScale, finalNoiseSeed) - 0.5) * 2.0;
                double nv = (SmoothNoise(x + 1000, y + 1000, NoiseScale, finalNoiseSeed) - 0.5) * 2.0;
                distH = ApplyNoiseToDistance(distH, SeamWidth, nh);
                distV = ApplyNoiseToDistance(distV, SeamWidth, nv);
            }

            double wh = FixHorizontal ? EdgeWeight(distH, SeamWidth, SeamBlur) : 0;
            double wv = FixVertical ? EdgeWeight(distV, SeamWidth, SeamBlur) : 0;

            ColorBgra hSample, vSample, dSample;

            if (Method == 1) // Растягивание — одна линия пикселей с противоположной стороны шва
            {
                hSample = src[anchorX, y];
                vSample = src[x, anchorY];
                dSample = src[anchorX, anchorY];
            }
            else // Зеркало и Кроссфейд — реальное отражение относительно границы шва
            {
                int mirrorX = WrapHard(2 * seamX + 1 - x, width);
                int mirrorY = WrapHard(2 * seamY + 1 - y, height);

                hSample = src[Clamp(mirrorX, width), y];
                vSample = src[x, Clamp(mirrorY, height)];
                dSample = src[Clamp(mirrorX, width), Clamp(mirrorY, height)];

                if (Method == 2) // Кроссфейд 50/50 — всегда ровно половина
                {
                    wh *= 0.5;
                    wv *= 0.5;
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