// Name: Fractal
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Fractal
// Version: 4.1
// Desc: Высокопроизводительный генератор фракталов (Идеально сглаженный Ньютон, Нова и Коллатц, точное Дерево Пифагора и Серпинский), поддержка Alpha фона
// Keywords: fractal,mandelbrot,julia,barnsley,sierpinski,pythagoras,render,chaos,фрактал,мандельброт,alpha
// URL:
// Help:

#region UICode
ListBoxControl FractalType = 0;             // Тип фрактала|Множество Мандельброта|Множество Жюлиа|Горящий корабль (Burning Ship)|Треугольник Серпинского|Ковер Серпинского|Дерево Пифагора|Папоротник Барнсли (IFS)|Мультиброт 3|Мультиброт 4|Трикорн (Tricorn)|Кельтский Мандельброт|Буффало (Buffalo)|Феникс (Phoenix)|Лестница дьявола|Фрактал Ньютона|Гибрид Нова (Nova)|Комплексный Коллатц
IntSliderControl MaxIterations = 100;       // [1,500] Макс. итераций / Глубина
DoubleSliderControl Zoom = 200.0;           // [1.0,10000.0] Базовый масштаб (Zoom)
DoubleSliderControl ScaleX = 1.0;           // [0.01,10.0] Масштаб по оси X (Scale X)
DoubleSliderControl ScaleY = 1.0;           // [0.01,10.0] Масштаб по оси Y (Scale Y)
DoubleSliderControl OffsetX = 0.0;          // [-2000.0,2000.0] Смещение X
DoubleSliderControl OffsetY = 0.0;          // [-2000.0,2000.0] Смещение Y
AngleControl Rotation = 0;                  // [-180,180] Угол поворота
DoubleSliderControl JuliaCx = -0.7;         // [-2.0,2.0] Константа Cx (Жюлиа, Феникс, Нова)
DoubleSliderControl JuliaCy = 0.27015;      // [-2.0,2.0] Константа Cy (Жюлиа, Феникс, Нова)
ColorWheelControl BgColor = ColorBgra.FromBgra(0, 0, 0, 255); // [Black?] Цвет заднего фона (Background)
ListBoxControl ColorMode = 4;               // Цветовая палитра|Классический огонь|Электрический синий|Изумрудные грезы|Монохром|Радужный спектр|Психоделический неон|Пастельное свечение|Золотое свечение|Глубокий космос|Фазовый цвет|Монохром (Волна)
DoubleSliderControl ColorCycle = 5.0;       // [0.1,50.0] Частота / Цикличность цвета
DoubleSliderControl ColorOffset = 0.0;      // [0.0,1.0] Сдвиг фазы цвета
CheckboxControl SmoothColors = true;        // Плавный градиент цветов (Smooth Colors)
CheckboxControl InvertColors = false;       // Инвертировать палитру
CheckboxControl OrbitTrap = false;          // Окрашивание по орбитальным ловушкам (Orbit Trap)
IntSliderControl AntiAliasing = 1;          // [1,3] Сглаживание (Anti-Aliasing)
#endregion

private int[] fernGrid = null;
private int maxFernHits = 1;

// Собственный детерминированный генератор случайных чисел (обход бага CodeLab с RandomNumberInstanceSeed)
private struct XorShift32
{
    private uint state;
    public XorShift32(uint seed)
    {
        state = (seed == 0) ? 123456789u : seed;
    }
    public double NextDouble()
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state & 0x7FFFFFFF) / (double)0x7FFFFFFF;
    }
}

void PreRender(Surface dst, Surface src)
{
    if (FractalType == 6) // Папоротник Барнсли (IFS Density Map)
    {
        int w = src.Width;
        int h = src.Height;
        int totalElements = w * h;

        if (fernGrid == null || fernGrid.Length != totalElements)
        {
            fernGrid = new int[totalElements];
        }
        else
        {
            Array.Clear(fernGrid, 0, fernGrid.Length);
        }

        int totalParticles = 2500000;
        double fx = 0.0, fy = 0.0;
        XorShift32 rand = new XorShift32(12345);
        int maxHits = 0;

        double effectiveZoomX = Zoom * ScaleX;
        double effectiveZoomY = Zoom * ScaleY;
        double cx = w * 0.5 + OffsetX;
        double cy = h * 0.5 + OffsetY;

        for (int p = 0; p < totalParticles; p++)
        {
            double r = rand.NextDouble();
            double nfx, nfy;

            if (r < 0.01)
            {
                nfx = 0.0;
                nfy = 0.16 * fy;
            }
            else if (r < 0.86)
            {
                nfx = 0.85 * fx + 0.04 * fy;
                nfy = -0.04 * fx + 0.85 * fy + 1.6;
            }
            else if (r < 0.93)
            {
                nfx = 0.20 * fx - 0.26 * fy;
                nfy = 0.23 * fx + 0.22 * fy + 1.6;
            }
            else
            {
                nfx = -0.15 * fx + 0.28 * fy;
                nfy = 0.26 * fx + 0.24 * fy + 0.44;
            }

            fx = nfx;
            fy = nfy;

            if (p > 20)
            {
                int scX = (int)Math.Round(cx + (fx - 0.2) * effectiveZoomX * 0.15);
                int scY = (int)Math.Round(cy - (fy - 5.0) * effectiveZoomY * 0.15);

                if (scX >= 0 && scX < w && scY >= 0 && scY < h)
                {
                    int idx = scY * w + scX;
                    fernGrid[idx]++;
                    if (fernGrid[idx] > maxHits)
                    {
                        maxHits = fernGrid[idx];
                    }
                }
            }
        }
        maxFernHits = maxHits;
    }
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    int width = src.Width;
    int height = src.Height;

    int maxIter = MaxIterations;
    int aaSamples = AntiAliasing;
    int colorMode = ColorMode;
    double colorCycle = ColorCycle;
    double colorOffset = ColorOffset;
    bool invertColors = InvertColors;
    bool orbitTrap = OrbitTrap;
    bool smoothColors = SmoothColors;
    int fractalType = FractalType;

    double effectiveZoomX = Zoom * ScaleX;
    double effectiveZoomY = Zoom * ScaleY;
    double cx = width * 0.5 + OffsetX;
    double cy = height * 0.5 + OffsetY;

    double rotRad = Rotation * Math.PI / 180.0;
    double cosA = Math.Cos(rotRad);
    double sinA = Math.Sin(rotRad);

    double juliaCx = JuliaCx;
    double juliaCy = JuliaCy;

    ColorBgra effectiveBgColor = BgColor;

    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            double rSum = 0, gSum = 0, bSum = 0, aSum = 0;

            for (int sy = 0; sy < aaSamples; sy++)
            {
                for (int sx = 0; sx < aaSamples; sx++)
                {
                    double subX = (sx + 0.5) / aaSamples;
                    double subY = (sy + 0.5) / aaSamples;

                    double px = (x + subX - cx) / effectiveZoomX;
                    double py = (y + subY - cy) / effectiveZoomY;

                    if (rotRad != 0.0)
                    {
                        double rx = px * cosA - py * sinA;
                        double ry = px * sinA + py * cosA;
                        px = rx;
                        py = ry;
                    }

                    double iter = 0;
                    double trapDist = 1e10;
                    int rootIndex = -1;

                    switch (fractalType)
                    {
                        case 0: // Mandelbrot
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                zy = 2.0 * zx * zy + cy0;
                                zx = zx2 - zy2 + cx0;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 1: // Julia
                        {
                            double zx = px, zy = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                zy = 2.0 * zx * zy + juliaCy;
                                zx = zx2 - zy2 + juliaCx;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 2: // Burning Ship
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                zy = Math.Abs(2.0 * zx * zy) + cy0;
                                zx = zx2 - zy2 + cx0;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 3: // Треугольник Серпинского (Исправлен фон отверстий)
                        {
                            double stX = px + 0.5;
                            double stY = py + 0.5;

                            if (stX < 0.0 || stY < 0.0 || (stX + stY) > 1.0)
                            {
                                iter = maxIter; // Вне треугольника -> Задний фон
                            }
                            else
                            {
                                int i = 0;
                                bool isHole = false;
                                double code = 0.0;
                                double weight = 1.0;

                                for (; i < maxIter; i++)
                                {
                                    stX *= 2.0;
                                    stY *= 2.0;

                                    if (stX >= 1.0 && stY < 1.0)
                                    {
                                        stX -= 1.0;
                                        code += 1.0 * weight;
                                    }
                                    else if (stX < 1.0 && stY >= 1.0)
                                    {
                                        stY -= 1.0;
                                        code += 2.0 * weight;
                                    }
                                    else if (stX < 1.0 && stY < 1.0)
                                    {
                                        if ((stX + stY) > 1.0)
                                        {
                                            isHole = true; // Вырезанное отверстие!
                                            break;
                                        }
                                        code += 0.0 * weight;
                                    }
                                    else
                                    {
                                        isHole = true;
                                        break;
                                    }
                                    weight *= 3.0;
                                }

                                if (isHole)
                                {
                                    iter = maxIter; // Дырка -> Задний фон!
                                }
                                else
                                {
                                    iter = Math.Abs(code) % maxIter;
                                    if (smoothColors)
                                    {
                                        iter += (stX + stY) * 0.5;
                                    }
                                }
                            }
                            break;
                        }

                        case 4: // Ковер Серпинского (Исправлен фон отверстий)
                        {
                            double carX = (px + 1.0) * 0.5;
                            double carY = (py + 1.0) * 0.5;

                            if (carX < 0.0 || carX > 1.0 || carY < 0.0 || carY > 1.0)
                            {
                                iter = maxIter; // За пределами квадрата -> Задний фон
                            }
                            else
                            {
                                int i = 0;
                                bool isHole = false;
                                double code = 0.0;
                                double weight = 1.0;

                                for (; i < maxIter; i++)
                                {
                                    carX *= 3.0;
                                    carY *= 3.0;
                                    int mx = (int)carX;
                                    int my = (int)carY;

                                    if (mx == 1 && my == 1)
                                    {
                                        isHole = true; // Вырезанный центр!
                                        break;
                                    }

                                    int cell = my * 3 + mx;
                                    if (cell > 4) cell--;
                                    code += cell * weight;
                                    weight *= 8.0;

                                    carX -= mx;
                                    carY -= my;
                                }

                                if (isHole)
                                {
                                    iter = maxIter; // Дырка -> Задний фон!
                                }
                                else
                                {
                                    iter = Math.Abs(code) % maxIter;
                                    if (smoothColors)
                                    {
                                        iter += (carX + carY) * 0.5;
                                    }
                                }
                            }
                            break;
                        }

                        case 5: // Дерево Пифагора (Точная геометрия сплошных квадратов)
                        {
                            double ptx = px;
                            double pty = -py; // Рост дерева вверх
                            int i = 0;
                            bool hitSquare = false;

                            for (; i < maxIter; i++)
                            {
                                // Проверка попадания в квадрат уровня i: [-0.5, 0.5] x [0, 1]
                                if (ptx >= -0.5 && ptx <= 0.5 && pty >= 0.0 && pty <= 1.0)
                                {
                                    hitSquare = true;
                                    break;
                                }

                                if (pty < 0.0) break; // Ниже основания ствола -> Задний фон

                                // Обратное аффинное преобразование веток в ствол
                                if (ptx < 0.0)
                                {
                                    // Левая ветка
                                    double nx = ptx + pty - 1.0;
                                    double ny = pty - ptx - 1.5;
                                    ptx = nx;
                                    pty = ny;
                                }
                                else
                                {
                                    // Правая ветка
                                    double nx = ptx - pty + 1.0;
                                    double ny = ptx + pty - 1.5;
                                    ptx = nx;
                                    pty = ny;
                                }
                            }

                            if (hitSquare)
                            {
                                iter = i; // Весь квадрат закрашивается цельным цветом слоя i
                            }
                            else
                            {
                                iter = maxIter; // Задний фон
                            }
                            break;
                        }

                        case 6: // Barnsley Fern (IFS Density Map)
                        {
                            int ix = (int)x;
                            int iy = (int)y;
                            if (ix >= 0 && ix < width && iy >= 0 && iy < height && maxFernHits > 0 && fernGrid != null)
                            {
                                double density = (double)fernGrid[iy * width + ix] / maxFernHits;
                                iter = (density > 0) ? Math.Min(maxIter - 0.001, density * maxIter * 5.0) : maxIter;
                            }
                            else
                            {
                                iter = maxIter;
                            }
                            break;
                        }

                        case 7: // Multibrot 3
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                double nzx = zx * zx2 - 3.0 * zx * zy2 + cx0;
                                double nzy = 3.0 * zx2 * zy - zy * zy2 + cy0;
                                zx = nzx;
                                zy = nzy;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(3.0)) / Math.Log(3.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 8: // Multibrot 4
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                double real2 = zx2 - zy2;
                                double imag2 = 2.0 * zx * zy;
                                zx = real2 * real2 - imag2 * imag2 + cx0;
                                zy = 2.0 * real2 * imag2 + cy0;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(4.0)) / Math.Log(4.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 9: // Tricorn
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                zy = -2.0 * zx * zy + cy0;
                                zx = zx2 - zy2 + cx0;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 10: // Celtic Mandelbrot
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                double nzx = Math.Abs(zx2 - zy2) + cx0;
                                double nzy = 2.0 * zx * zy + cy0;
                                zx = nzx;
                                zy = nzy;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 11: // Buffalo
                        {
                            double zx = 0, zy = 0;
                            double cx0 = px, cy0 = py;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                double nzx = Math.Abs(zx2 - zy2) + cx0;
                                double nzy = -Math.Abs(2.0 * zx * zy) + cy0;
                                zx = nzx;
                                zy = nzy;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 12: // Phoenix
                        {
                            double zx = py, zy = px;
                            double zxOld = 0, zyOld = 0;
                            double cx0 = juliaCx, cy0 = juliaCy;
                            int i = 0;
                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                if (zx2 + zy2 > 256.0) break;
                                double nzx = zx2 - zy2 + cx0 + 0.5 * zxOld;
                                double nzy = 2.0 * zx * zy + cy0 + 0.5 * zyOld;
                                zxOld = zx;
                                zyOld = zy;
                                zx = nzx;
                                zy = nzy;
                                if (orbitTrap)
                                {
                                    double d = Math.Abs(zx) + Math.Abs(zy);
                                    if (d < trapDist) trapDist = d;
                                }
                            }
                            if (i < maxIter)
                            {
                                if (smoothColors)
                                {
                                    double log_zn = Math.Log(zx * zx + zy * zy) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(2.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - nu;
                                }
                                else iter = i;
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 13: // Лестница дьявола
                        {
                            double normX = px - Math.Floor(px);
                            double cantY = 0.0;
                            double p2 = 0.5;
                            double valX = normX;
                            int i = 0;
                            int steps = Math.Min(maxIter, 30);

                            for (; i < steps; i++)
                            {
                                valX *= 3.0;
                                int digit = (int)valX;
                                valX -= digit;

                                if (digit == 1)
                                {
                                    cantY += p2;
                                    break;
                                }
                                else if (digit == 2)
                                {
                                    cantY += p2;
                                }
                                p2 *= 0.5;
                            }

                            double targetY = (cantY - 0.5) * 2.0;
                            double dist = Math.Abs(py - targetY);

                            if (dist < 0.04)
                            {
                                iter = Math.Min(maxIter - 0.001, smoothColors ? i + (1.0 - dist / 0.04) : i);
                            }
                            else
                            {
                                iter = maxIter;
                            }
                            break;
                        }

                        case 14: // Newton (z^3 - 1 = 0) (Математически точное сглаживание)
                        {
                            double zx = px, zy = py;
                            int i = 0;
                            double r1x = 1.0, r1y = 0.0;
                            double r2x = -0.5, r2y = 0.8660254037844386;
                            double r3x = -0.5, r3y = -0.8660254037844386;

                            double rootDist = 1.0;

                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                double d2 = zx2 + zy2;
                                if (d2 < 1e-12) break;

                                double denom = 3.0 * d2 * d2;
                                if (denom < 1e-12) break;
                                double invZ2x = (zx2 - zy2) / denom;
                                double invZ2y = (-2.0 * zx * zy) / denom;

                                double nzx = (2.0 / 3.0) * zx + invZ2x;
                                double nzy = (2.0 / 3.0) * zy + invZ2y;
                                zx = nzx;
                                zy = nzy;

                                double d1 = (zx - r1x) * (zx - r1x) + (zy - r1y) * (zy - r1y);
                                double d2r = (zx - r2x) * (zx - r2x) + (zy - r2y) * (zy - r2y);
                                double d3 = (zx - r3x) * (zx - r3x) + (zy - r3y) * (zy - r3y);

                                if (d1 < 1e-6) { rootIndex = 0; rootDist = Math.Sqrt(d1); break; }
                                if (d2r < 1e-6) { rootIndex = 1; rootDist = Math.Sqrt(d2r); break; }
                                if (d3 < 1e-6) { rootIndex = 2; rootDist = Math.Sqrt(d3); break; }
                            }

                            if (i < maxIter)
                            {
                                if (smoothColors && rootDist > 0.0 && rootDist < 1e-3)
                                {
                                    // Квадратично непрерывный сглаживающий потенциал корня
                                    double logRatio = Math.Log(rootDist) / Math.Log(1e-3);
                                    if (logRatio >= 1.0)
                                    {
                                        double nu = Math.Log(logRatio) / Math.Log(2.0);
                                        iter = i - nu;
                                    }
                                    else
                                    {
                                        iter = i;
                                    }
                                }
                                else
                                {
                                    iter = i;
                                }
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 15: // Nova Hybrid (Бесшовный сглаженный градиент)
                        {
                            double zx = px, zy = py;
                            int i = 0;
                            double diffDist = 1.0;
                            double prevDiff = 1.0;

                            for (; i < maxIter; i++)
                            {
                                double zx2 = zx * zx, zy2 = zy * zy;
                                double d2 = zx2 + zy2;
                                if (d2 < 1e-12) break;

                                double denom = 3.0 * d2 * d2;
                                if (denom < 1e-12) break;
                                double invZ2x = (zx2 - zy2) / denom;
                                double invZ2y = (-2.0 * zx * zy) / denom;

                                double nzx = (2.0 / 3.0) * zx + invZ2x + juliaCx;
                                double nzy = (2.0 / 3.0) * zy + invZ2y + juliaCy;

                                double diffX = nzx - zx;
                                double diffY = nzy - zy;
                                zx = nzx;
                                zy = nzy;

                                prevDiff = diffDist;
                                diffDist = Math.Sqrt(diffX * diffX + diffY * diffY);

                                if (diffDist < 1e-4)
                                {
                                    break;
                                }
                            }

                            if (i < maxIter)
                            {
                                if (smoothColors && diffDist < 1e-4 && prevDiff > 1e-4)
                                {
                                    // Непрерывный сдвиг перехода порога epsilon
                                    double logThreshold = Math.Log(1e-4);
                                    double logPrev = Math.Log(prevDiff);
                                    double logCur = Math.Log(diffDist);

                                    double frac = (logPrev - logThreshold) / (logPrev - logCur);
                                    frac = Math.Max(0.0, Math.Min(1.0, frac));

                                    iter = (i - 1) + frac;
                                }
                                else
                                {
                                    iter = i;
                                }
                            }
                            else iter = maxIter;
                            break;
                        }

                        case 16: // Collatz Complex (Непрерывное сглаживание)
                        {
                            double zx = px, zy = py;
                            int i = 0;
                            double mag2 = 0.0;

                            for (; i < maxIter; i++)
                            {
                                mag2 = zx * zx + zy * zy;
                                if (mag2 > 10000.0) break;

                                double piX = Math.PI * zx;
                                double piY = Math.PI * zy;
                                double cosX = Math.Cos(piX) * Math.Cosh(piY);
                                double cosY = -Math.Sin(piX) * Math.Sinh(piY);

                                double uR = 1.0 + 2.0 * zx;
                                double uI = 2.0 * zy;
                                double prodR = uR * cosX - uI * cosY;
                                double prodI = uR * cosY + uI * cosX;

                                double nzx = 0.25 * (1.0 + 4.0 * zx - prodR);
                                double nzy = 0.25 * (4.0 * zy - prodI);
                                zx = nzx;
                                zy = nzy;
                            }

                            if (i < maxIter)
                            {
                                if (smoothColors && mag2 > 1.0)
                                {
                                    double log_zn = Math.Log(mag2) / 2.0;
                                    double nu = Math.Log(log_zn / Math.Log(100.0)) / Math.Log(2.0);
                                    iter = i + 1.0 - Math.Max(0.0, Math.Min(1.0, nu));
                                }
                                else
                                {
                                    iter = i;
                                }
                            }
                            else iter = maxIter;
                            break;
                        }
                    }

                    ColorBgra sampleColor = MapColor(iter, maxIter, trapDist, rootIndex, orbitTrap, colorMode, colorCycle, colorOffset, invertColors, effectiveBgColor);
                    rSum += sampleColor.R;
                    gSum += sampleColor.G;
                    bSum += sampleColor.B;
                    aSum += sampleColor.A;
                }
            }

            int totalSamples = aaSamples * aaSamples;
            byte finalR = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(rSum / totalSamples)));
            byte finalG = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(gSum / totalSamples)));
            byte finalB = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(bSum / totalSamples)));
            byte finalA = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(aSum / totalSamples)));

            dst[x, y] = ColorBgra.FromBgra(finalB, finalG, finalR, finalA);
        }
    }
}

private ColorBgra MapColor(double iter, int maxIter, double trapDist, int rootIndex, bool orbitTrap, int mode, double cycle, double offset, bool invert, ColorBgra bgColor)
{
    // Заливка фона для пикселей вне фрактала и внутри дыр
    if (!orbitTrap && iter >= maxIter)
    {
        return bgColor;
    }

    double t;
    if (orbitTrap)
    {
        t = trapDist * cycle + offset;
    }
    else if (rootIndex >= 0)
    {
        double rootHue = rootIndex / 3.0;
        t = (rootHue + (iter / maxIter) * 0.3 * cycle + offset);
    }
    else
    {
        t = (iter / maxIter) * cycle + offset;
    }

    t = t - Math.Floor(t);
    if (invert) t = 1.0 - t;

    byte r = 0, g = 0, b = 0;

    switch (mode)
    {
        case 0: // Классический огонь
            r = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sin(t * Math.PI))));
            g = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Pow(t, 2.0))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Pow(t, 4.0))));
            break;
        case 1: // Электрический синий
            r = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Pow(t, 3.0))));
            g = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sin(t * Math.PI))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sqrt(t))));
            break;
        case 2: // Изумрудные грезы
            r = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Pow(t, 3.0))));
            g = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sqrt(t))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sin(t * Math.PI * 0.5))));
            break;
        case 3: // Монохром
            byte val = (byte)Math.Max(0, Math.Min(255, (int)(255 * t)));
            r = val; g = val; b = val;
            break;
        case 4: // Радужный спектр
            GetHsvColor(t * 360.0, 1.0, 1.0, out r, out g, out b);
            break;
        case 5: // Психоделический неон
            GetHsvColor((t * 720.0) % 360.0, 1.0, Math.Sin(t * Math.PI * 2.0) * 0.5 + 0.5, out r, out g, out b);
            break;
        case 6: // Пастельное свечение
            GetHsvColor(t * 360.0, 0.4, 0.95, out r, out g, out b);
            break;
        case 7: // Золотое свечение
            r = (byte)Math.Max(0, Math.Min(255, (int)(255 * t)));
            g = (byte)Math.Max(0, Math.Min(255, (int)(215 * Math.Pow(t, 1.2))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(100 * Math.Pow(t, 2.5))));
            break;
        case 8: // Глубокий космос
            r = (byte)Math.Max(0, Math.Min(255, (int)(180 * Math.Sin(t * Math.PI))));
            g = (byte)Math.Max(0, Math.Min(255, (int)(50 * Math.Pow(t, 2.0))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Sqrt(t))));
            break;
        case 9: // Фазовый цвет
            r = (byte)Math.Max(0, Math.Min(255, (int)(127.5 * (1.0 + Math.Sin(t * Math.PI * 2.0)))));
            g = (byte)Math.Max(0, Math.Min(255, (int)(127.5 * (1.0 + Math.Sin(t * Math.PI * 2.0 + 2.0 * Math.PI / 3.0)))));
            b = (byte)Math.Max(0, Math.Min(255, (int)(127.5 * (1.0 + Math.Sin(t * Math.PI * 2.0 + 4.0 * Math.PI / 3.0)))));
            break;
        case 10: // Монохром (Волна): 0 = белый, 0.5 = черный, 1 = белый
            byte valWave = (byte)Math.Max(0, Math.Min(255, (int)(255 * Math.Abs(2.0 * t - 1.0))));
            r = valWave; g = valWave; b = valWave;
            break;
    }

    return ColorBgra.FromBgra(b, g, r, 255);
}

private void GetHsvColor(double h, double s, double v, out byte r, out byte g, out byte b)
{
    h = h % 360.0;
    if (h < 0) h += 360.0;
    double c = v * s;
    double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
    double m = v - c;
    double r1 = 0, g1 = 0, b1 = 0;

    if (h < 60) { r1 = c; g1 = x; b1 = 0; }
    else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
    else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
    else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
    else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
    else { r1 = c; g1 = 0; b1 = x; }

    r = (byte)Math.Max(0, Math.Min(255, (int)((r1 + m) * 255.0)));
    g = (byte)Math.Max(0, Math.Min(255, (int)((g1 + m) * 255.0)));
    b = (byte)Math.Max(0, Math.Min(255, (int)((b1 + m) * 255.0)));
}