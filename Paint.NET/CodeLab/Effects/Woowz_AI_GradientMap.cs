// Name: Gradient Map
// Submenu: Woowz-AI
// Author: Woowz11 & AI
// Title: Gradient Map
// Version: 1.5
// Desc: Превращает чёрно-белое (или любое) изображение в градиент по заданным цветовым стопам или готовым пресетам
// Keywords: gradient,map,colorize,duotone,tritone,preset,градиент,карта,дуотон,раскраска,пресет
// URL:
// Help:

#region UICode
ListBoxControl PresetMode = 0; // Пресет градиента|Пользовательский (Custom)|Радуга (Rainbow)|Огонь (Fire)|Океан (Ocean)|Закат (Sunset)|Чёрно-белый (Grayscale)|Дуотон Сине-Жёлтый|Пастель (Pastel)|Неон (Neon)|Земля (Earth)|Ледяной (Ice)

IntSliderControl ColorCount = 10; // [2,10] Количество цветов (только для Пользовательского режима)

ColorWheelControl Color1 = ColorBgra.FromBgra(0, 0, 255, 255); // [Red?] Цвет 1
DoubleSliderControl Position1 = 0.0;   // [0,100] Позиция цвета 1 (%)

ColorWheelControl Color2 = ColorBgra.FromBgra(0, 153, 255, 255); // [Orange?] Цвет 2
DoubleSliderControl Position2 = 11.0;  // [0,100] Позиция цвета 2 (%)

ColorWheelControl Color3 = ColorBgra.FromBgra(0, 255, 204, 255); // [Chartreuse?] Цвет 3
DoubleSliderControl Position3 = 22.0;  // [0,100] Позиция цвета 3 (%)

ColorWheelControl Color4 = ColorBgra.FromBgra(0, 255, 51, 255); // [Green?] Цвет 4
DoubleSliderControl Position4 = 33.0;  // [0,100] Позиция цвета 4 (%)

ColorWheelControl Color5 = ColorBgra.FromBgra(153, 255, 0, 255); // [SpringGreen?] Цвет 5
DoubleSliderControl Position5 = 44.0;  // [0,100] Позиция цвета 5 (%)

ColorWheelControl Color6 = ColorBgra.FromBgra(255, 255, 0, 255); // [Cyan?] Цвет 6
DoubleSliderControl Position6 = 56.0;  // [0,100] Позиция цвета 6 (%)

ColorWheelControl Color7 = ColorBgra.FromBgra(255, 102, 0, 255); // [Azure?] Цвет 7
DoubleSliderControl Position7 = 67.0;  // [0,100] Позиция цвета 7 (%)

ColorWheelControl Color8 = ColorBgra.FromBgra(255, 0, 51, 255); // [Violet?] Цвет 8
DoubleSliderControl Position8 = 78.0;  // [0,100] Позиция цвета 8 (%)

ColorWheelControl Color9 = ColorBgra.FromBgra(255, 0, 204, 255); // [Purple?] Цвет 9
DoubleSliderControl Position9 = 89.0;  // [0,100] Позиция цвета 9 (%)

ColorWheelControl Color10 = ColorBgra.FromBgra(153, 0, 255, 255); // [DeepPink?] Цвет 10
DoubleSliderControl Position10 = 100.0; // [0,100] Позиция цвета 10 (%)

ListBoxControl GrayscaleMethod = 0; // Метод определения яркости|Luminance (Rec.601, стандарт)|Среднее RGB|Luminance (Rec.709, HD)|Только красный канал|Только зелёный канал|Только синий канал
ListBoxControl Interpolation = 1;   // Интерполяция между цветами|Линейная|Плавная (Smoothstep)|Квадратичная|Кубическая|Константа (без смешивания, ступени)
CheckboxControl MultiplyOriginalAlpha = true; // Учитывать прозрачность исходного изображения
CheckboxControl AddDither = true;   // Сглаживание ступеней (Dithering)
CheckboxControl InvertGradient = false; // Инвертировать направление градиента
#endregion

private ColorBgra[] sortedColors;
private double[] sortedPositions;
private int stopCount;

void PreRender(Surface dst, Surface src)
{
    ColorBgra[] colors;
    double[] positions;
    int count;

    if (PresetMode == 0)
    {
        count = Math.Max(2, Math.Min(10, ColorCount));
        colors = new ColorBgra[count];
        positions = new double[count];

        FillStop(0, Color1, Position1, colors, positions);
        FillStop(1, Color2, Position2, colors, positions);
        if (count > 2) FillStop(2, Color3, Position3, colors, positions);
        if (count > 3) FillStop(3, Color4, Position4, colors, positions);
        if (count > 4) FillStop(4, Color5, Position5, colors, positions);
        if (count > 5) FillStop(5, Color6, Position6, colors, positions);
        if (count > 6) FillStop(6, Color7, Position7, colors, positions);
        if (count > 7) FillStop(7, Color8, Position8, colors, positions);
        if (count > 8) FillStop(8, Color9, Position9, colors, positions);
        if (count > 9) FillStop(9, Color10, Position10, colors, positions);
    }
    else
    {
        GetPresetColors(PresetMode, out colors, out positions);
        count = colors.Length;
    }

    for (int i = 0; i < count - 1; i++)
    {
        for (int j = 0; j < count - 1 - i; j++)
        {
            if (positions[j] > positions[j + 1])
            {
                double tp = positions[j];
                positions[j] = positions[j + 1];
                positions[j + 1] = tp;

                ColorBgra tc = colors[j];
                colors[j] = colors[j + 1];
                colors[j + 1] = tc;
            }
        }
    }

    sortedColors = colors;
    sortedPositions = positions;
    stopCount = count;
}

private void FillStop(int idx, ColorBgra baseColor, double positionPercent,
                       ColorBgra[] colors, double[] positions)
{
    colors[idx] = baseColor;
    positions[idx] = Math.Max(0.0, Math.Min(100.0, positionPercent)) / 100.0;
}

private void GetPresetColors(int preset, out ColorBgra[] colors, out double[] positions)
{
    switch (preset)
    {
        case 1:
            colors = new ColorBgra[] {
                Col(255,0,0), Col(255,165,0), Col(255,255,0),
                Col(0,255,0), Col(0,0,255), Col(75,0,130), Col(238,130,238)
            };
            positions = new double[] { 0, 0.16, 0.33, 0.5, 0.66, 0.83, 1.0 };
            break;

        case 2:
            colors = new ColorBgra[] {
                Col(0,0,0), Col(128,0,0), Col(255,69,0), Col(255,165,0), Col(255,255,0), Col(255,255,255)
            };
            positions = new double[] { 0, 0.25, 0.5, 0.7, 0.88, 1.0 };
            break;

        case 3:
            colors = new ColorBgra[] {
                Col(0,0,30), Col(0,30,80), Col(0,105,148), Col(72,180,200), Col(220,245,255)
            };
            positions = new double[] { 0, 0.3, 0.55, 0.8, 1.0 };
            break;

        case 4:
            colors = new ColorBgra[] {
                Col(25,10,65), Col(130,30,90), Col(230,70,70), Col(250,150,60), Col(255,220,130)
            };
            positions = new double[] { 0, 0.3, 0.55, 0.8, 1.0 };
            break;

        case 5:
            colors = new ColorBgra[] { Col(0,0,0), Col(255,255,255) };
            positions = new double[] { 0, 1.0 };
            break;

        case 6:
            colors = new ColorBgra[] { Col(10,20,90), Col(255,225,70) };
            positions = new double[] { 0, 1.0 };
            break;

        case 7:
            colors = new ColorBgra[] {
                Col(255,214,224), Col(255,240,200), Col(205,255,210), Col(200,230,255), Col(225,200,255)
            };
            positions = new double[] { 0, 0.25, 0.5, 0.75, 1.0 };
            break;

        case 8:
            colors = new ColorBgra[] {
                Col(10,0,30), Col(255,0,200), Col(0,255,255), Col(180,255,0)
            };
            positions = new double[] { 0, 0.4, 0.7, 1.0 };
            break;

        case 9:
            colors = new ColorBgra[] {
                Col(40,30,20), Col(110,70,40), Col(150,120,60), Col(190,170,110), Col(230,220,190)
            };
            positions = new double[] { 0, 0.3, 0.55, 0.8, 1.0 };
            break;

        case 10:
            colors = new ColorBgra[] {
                Col(0,10,40), Col(30,80,130), Col(130,200,230), Col(220,245,255)
            };
            positions = new double[] { 0, 0.35, 0.65, 1.0 };
            break;

        default:
            colors = new ColorBgra[] { Col(0,0,0), Col(255,255,255) };
            positions = new double[] { 0, 1.0 };
            break;
    }
}

private static ColorBgra Col(int r, int g, int b)
{
    return ColorBgra.FromBgra((byte)b, (byte)g, (byte)r, 255);
}

private double GetLuminance(ColorBgra p)
{
    double r = p.R / 255.0;
    double g = p.G / 255.0;
    double b = p.B / 255.0;

    switch (GrayscaleMethod)
    {
        case 0: return 0.299 * r + 0.587 * g + 0.114 * b; // Rec.601
        case 1: return (r + g + b) / 3.0;                 // Среднее
        case 2: return 0.2126 * r + 0.7152 * g + 0.0722 * b; // Rec.709
        case 3: return r; // Только красный
        case 4: return g; // Только зелёный
        case 5: return b; // Только синий
        default: return 0.299 * r + 0.587 * g + 0.114 * b;
    }
}

private ColorBgra SampleGradient(double t)
{
    t = Math.Max(0.0, Math.Min(1.0, t));
    if (InvertGradient) t = 1.0 - t;

    if (stopCount == 1) return sortedColors[0];
    if (t <= sortedPositions[0]) return sortedColors[0];
    if (t >= sortedPositions[stopCount - 1]) return sortedColors[stopCount - 1];

    for (int i = 0; i < stopCount - 1; i++)
    {
        double posA = sortedPositions[i];
        double posB = sortedPositions[i + 1];

        if (t >= posA && t <= posB)
        {
            // Режим "Константа" — без смешивания, чёткая ступень
            if (Interpolation == 4)
            {
                // Если ровно посередине между стопами — берём ближайший;
                // иначе цвет левого стопа до самой границы правого
                double mid = (posA + posB) / 2.0;
                return (t < mid) ? sortedColors[i] : sortedColors[i + 1];
            }

            double localT = (posB - posA < 1e-9) ? 0.0 : (t - posA) / (posB - posA);

            switch (Interpolation)
            {
                case 0: break; // Линейная
                case 1: localT = localT * localT * (3.0 - 2.0 * localT); break; // Smoothstep
                case 2: localT = localT * localT; break; // Квадратичная
                case 3: localT = localT * localT * localT; break; // Кубическая
            }

            return ColorBgra.Blend(sortedColors[i], sortedColors[i + 1], (byte)(localT * 255.0));
        }
    }

    return sortedColors[stopCount - 1];
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

void Render(Surface dst, Surface src, Rectangle rect)
{
    for (int y = rect.Top; y < rect.Bottom; y++)
    {
        if (IsCancelRequested) return;

        for (int x = rect.Left; x < rect.Right; x++)
        {
            ColorBgra srcPixel = src[x, y];

            double lum = GetLuminance(srcPixel);

            // Дизеринг отключаем для режима "Константа" — иначе появится шум на границах ступеней
            if (AddDither && Interpolation != 4)
            {
                double dither = (Hash21(x, y) - 0.5) * (1.0 / 255.0);
                lum = Math.Max(0.0, Math.Min(1.0, lum + dither));
            }

            ColorBgra mapped = SampleGradient(lum);

            byte finalAlpha = mapped.A;
            if (MultiplyOriginalAlpha)
            {
                finalAlpha = ClampByte(finalAlpha * (srcPixel.A / 255.0));
            }

            dst[x, y] = ColorBgra.FromBgra(mapped.B, mapped.G, mapped.R, finalAlpha);
        }
    }
}