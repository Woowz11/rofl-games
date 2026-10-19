// Name: Voronoi
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Voronoi
// Version: 1.6
// Desc: Генератор шума Вороного с 12 стилями отрисовки, поддержкой замкнутой рамки (Framed), выбором типа границ и метрик
// Keywords: voronoi,noise,cellular,texture,seamless,pattern,worley,glitch,selection,lasso,bevel,ripples,stainedglass
// URL:
// Help:

#region UICode
ListBoxControl BoundaryMode = 0;          // Тип границ|Бесшовный (Seamless)|Стандартный (Infinite Grid)|Ограниченный рамкой (Bounded)|Замкнутая рамка (Framed)
ListBoxControl RenderMode = 0;            // Стиль отрисовки|Границы ячеек (F2 - F1)|Ячейки + Границы|Сплошные ячейки (Solid)|Расстояние F1 (Центры)|Расстояние F2|Кристаллы (F1 * F2)|Отношение (F1 / F2)|Объемные 3D-ячейки (Bevel)|Волновые кольца (Ripples)|Изолинии (Contour Lines)|Витраж (Stained Glass)|Органические сферы (Bubbles)
ListBoxControl DistanceMetric = 0;        // Метрика расстояния|Евклидова (Euclidean)|Манхэттен (Manhattan)|Чебышёва (Chebyshev)|Минковского (Minkowski)
IntSliderControl CellSize = 60;           // [1,500] Размер ячеек
IntSliderControl Jitter = 100;            // [0,100] Случайность ячеек (%)
DoubleSliderControl MinkowskiP = 3.0;     // [1.0,10.0] Степень Минковского (для метрики Минковского)
DoubleSliderControl EdgeContrast = 1.0;   // [0.1,20.0] Контраст границ / Размера
CheckboxControl Invert = false;            // Инвертировать цвета
ColorWheelControl PrimaryColor = ColorBgra.FromBgra(255, 255, 255, 255); // Цвет переднего плана (Границы / Линии)
ColorWheelControl SecondaryColor = ColorBgra.FromBgra(0, 0, 0, 255);    // Цвет фона
IntSliderControl CustomSeed = 12345;      // [0,999999] Сид (Seed)
ReseedButtonControl Reseed = 0;           // Случайный сид
#endregion

private int numCellsX;
private int numCellsY;
private double cellWidth;
private double cellHeight;
private int finalSeed;

// Координаты и размеры выделенной области (Selection)
private int selX;
private int selY;
private int selWidth;
private int selHeight;

void PreRender(Surface dst, Surface src)
{
    Rectangle selBounds = EnvironmentParameters.SelectionBounds;
    
    selX = selBounds.X;
    selY = selBounds.Y;
    selWidth = Math.Max(1, selBounds.Width);
    selHeight = Math.Max(1, selBounds.Height);

    numCellsX = Math.Max(1, (int)Math.Round((double)selWidth / CellSize));
    numCellsY = Math.Max(1, (int)Math.Round((double)selHeight / CellSize));

    cellWidth = (double)selWidth / numCellsX;
    cellHeight = (double)selHeight / numCellsY;

    finalSeed = CustomSeed + Reseed * 7919;
}

private double Hash(int x, int y, int seed)
{
    unchecked
    {
        int h = x * 374761393 + y * 668265263 + seed * 2147483647;
        h = (h ^ (h >> 13)) * 1274126177;
        h = h ^ (h >> 16);
        return (h & 0x7fffffff) * (1.0 / 0x7fffffff);
    }
}

private void GetVoronoi(double px, double py, out double f1, out double f2, out int bestCellId)
{
    double localX = px - selX;
    double localY = py - selY;

    int cx = (int)Math.Floor(localX / cellWidth);
    int cy = (int)Math.Floor(localY / cellHeight);

    f1 = double.MaxValue;
    f2 = double.MaxValue;
    bestCellId = 0;

    double jitterAmount = Jitter / 100.0;
    double pExp = MinkowskiP;

    for (int oy = -2; oy <= 2; oy++)
    {
        for (int ox = -2; ox <= 2; ox++)
        {
            int nx = cx + ox;
            int ny = cy + oy;

            double ptX = 0, ptY = 0;
            int cellId = 0;

            if (BoundaryMode == 0) // 1. Бесшовный (Seamless Tiling по рамке выделения)
            {
                int gx = ((nx % numCellsX) + numCellsX) % numCellsX;
                int gy = ((ny % numCellsY) + numCellsY) % numCellsY;

                double jx = Hash(gx, gy, finalSeed);
                double jy = Hash(gx, gy, finalSeed + 1);

                double offsetX = (nx - gx) * cellWidth;
                double offsetY = (ny - gy) * cellHeight;

                ptX = selX + (gx + 0.5 + (jx - 0.5) * jitterAmount) * cellWidth + offsetX;
                ptY = selY + (gy + 0.5 + (jy - 0.5) * jitterAmount) * cellHeight + offsetY;
                cellId = gx + gy * numCellsX;
            }
            else if (BoundaryMode == 1) // 2. Стандартный (Infinite Grid)
            {
                int gx = nx;
                int gy = ny;

                double jx = Hash(gx, gy, finalSeed);
                double jy = Hash(gx, gy, finalSeed + 1);

                ptX = selX + (gx + 0.5 + (jx - 0.5) * jitterAmount) * cellWidth;
                ptY = selY + (gy + 0.5 + (jy - 0.5) * jitterAmount) * cellHeight;
                cellId = gx + gy * 10007;
            }
            else if (BoundaryMode == 2) // 3. Ограниченный рамкой выделения (Bounded)
            {
                if (nx < 0 || nx >= numCellsX || ny < 0 || ny >= numCellsY) continue;

                int gx = nx;
                int gy = ny;

                double jx = Hash(gx, gy, finalSeed);
                double jy = Hash(gx, gy, finalSeed + 1);

                double rawPtX = (gx + 0.5 + (jx - 0.5) * jitterAmount) * cellWidth;
                double rawPtY = (gy + 0.5 + (jy - 0.5) * jitterAmount) * cellHeight;

                ptX = selX + Math.Max(0, Math.Min(selWidth, rawPtX));
                ptY = selY + Math.Max(0, Math.Min(selHeight, rawPtY));
                cellId = gx + gy * numCellsX;
            }
            else // 4. Замкнутая рамка (Framed - математическое отражение точек за границы)
            {
                int gx = Math.Max(0, Math.Min(numCellsX - 1, nx));
                int gy = Math.Max(0, Math.Min(numCellsY - 1, ny));

                double jx = Hash(gx, gy, finalSeed);
                double jy = Hash(gx, gy, finalSeed + 1);

                double basePtX = selX + (gx + 0.5 + (jx - 0.5) * jitterAmount) * cellWidth;
                double basePtY = selY + (gy + 0.5 + (jy - 0.5) * jitterAmount) * cellHeight;

                ptX = basePtX;
                ptY = basePtY;

                if (nx < 0)
                {
                    ptX = 2.0 * selX - basePtX;
                }
                else if (nx >= numCellsX)
                {
                    ptX = 2.0 * (selX + selWidth) - basePtX;
                }

                if (ny < 0)
                {
                    ptY = 2.0 * selY - basePtY;
                }
                else if (ny >= numCellsY)
                {
                    ptY = 2.0 * (selY + selHeight) - basePtY;
                }

                cellId = gx + gy * numCellsX;
                if (nx < 0 || nx >= numCellsX || ny < 0 || ny >= numCellsY)
                {
                    cellId = (gx + 1) * 10007 + (gy + 1) * 31337 + (nx + 50) * 101 + (ny + 50);
                }
            }

            double dx = Math.Abs(ptX - px);
            double dy = Math.Abs(ptY - py);
            double dist = 0;

            switch (DistanceMetric)
            {
                case 0: // Евклидова
                    dist = Math.Sqrt(dx * dx + dy * dy);
                    break;
                case 1: // Манхэттен
                    dist = dx + dy;
                    break;
                case 2: // Чебышёва
                    dist = Math.Max(dx, dy);
                    break;
                case 3: // Минковского
                    dist = Math.Pow(Math.Pow(dx, pExp) + Math.Pow(dy, pExp), 1.0 / pExp);
                    break;
            }

            // Нормализация расстояния
            dist /= (cellWidth + cellHeight) * 0.5;

            if (dist < f1)
            {
                f2 = f1;
                f1 = dist;
                bestCellId = cellId;
            }
            else if (dist < f2)
            {
                f2 = dist;
            }
        }
    }
}

void Render(Surface dst, Surface src, Rectangle rect)
{
    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            double f1, f2;
            int cellId;
            GetVoronoi(x, y, out f1, out f2, out cellId);

            double val = 0;

            switch (RenderMode)
            {
                case 0: // Границы ячеек (F2 - F1)
                    val = Math.Max(0.0, 1.0 - (f2 - f1) * EdgeContrast);
                    break;

                case 1: // Ячейки + Границы
                    double edge = Math.Max(0.0, 1.0 - (f2 - f1) * EdgeContrast);
                    double cellHash = Hash(cellId, 7, finalSeed);
                    val = cellHash * (1.0 - edge);
                    break;

                case 2: // Сплошные ячейки (Solid)
                    val = Hash(cellId, 7, finalSeed);
                    break;

                case 3: // Расстояние F1
                    val = Math.Max(0.0, 1.0 - f1 * (EdgeContrast * 0.5));
                    break;

                case 4: // Расстояние F2
                    val = Math.Max(0.0, 1.0 - f2 * (EdgeContrast * 0.5));
                    break;

                case 5: // Кристаллы (F1 * F2)
                    val = Math.Min(1.0, (f1 * f2) * EdgeContrast);
                    break;

                case 6: // Отношение (F1 / F2) - Мягкие Вороной / Метаболлы
                    double ratio = f1 / Math.Max(1e-5, f2);
                    val = Math.Pow(Math.Max(0.0, Math.Min(1.0, ratio)), EdgeContrast);
                    break;

                case 7: // Объемные 3D-ячейки (Bevel)
                    double vF = Math.Max(0.0, 1.0 - f1 * (EdgeContrast * 0.5));
                    val = Math.Sqrt(vF);
                    break;

                case 8: // Волновые кольца (Ripples)
                    val = 0.5 + 0.5 * Math.Sin(f1 * Math.PI * 4.0 * EdgeContrast);
                    break;

                case 9: // Изолинии (Contour Lines)
                    double wave = (f1 * 5.0 * EdgeContrast) % 1.0;
                    val = Math.Pow(Math.Sin(wave * Math.PI), 2.0);
                    break;

                case 10: // Витраж (Stained Glass)
                    double sgEdge = Math.Max(0.0, 1.0 - (f2 - f1) * EdgeContrast * 2.0);
                    double sgHash = Hash(cellId, 7, finalSeed);
                    double sgShade = Math.Max(0.0, 1.0 - f1 * 0.4);
                    val = sgEdge + (1.0 - sgEdge) * (sgHash * sgShade);
                    break;

                case 11: // Органические сферы (Bubbles)
                    double radius = 0.5 / Math.Max(0.1, EdgeContrast);
                    double sphereD = f1 / radius;
                    val = (sphereD < 1.0) ? Math.Sqrt(1.0 - sphereD * sphereD) : 0.0;
                    break;
            }

            if (Invert) val = 1.0 - val;
            val = Math.Max(0.0, Math.Min(1.0, val));

            // Смешивание цветов
            dst[x, y] = ColorBgra.Blend(SecondaryColor, PrimaryColor, (byte)(val * 255));
        }
    }
}