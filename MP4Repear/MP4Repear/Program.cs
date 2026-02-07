// Program.cs
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using H264Sharp; // nuget: H264Sharp
// using H264SharpBitmapExtentions; // nuget: H264SharpBitmapExtentions (для .ToBitmap() расширения)
using System.Linq;

namespace MP4Repair
{
    class Program
    {
        // Поменяй под свой путь
        private const string VideoPath = @"W:\Woowz11\Videos\Fix\VIDEO.mp4";
        private const string FramesFolder = @"W:\Woowz11\Videos\Fix\frames";
        // Если нужно, укажи точный путь к openh264 dll (64 bit)
        // Defines.CiscoDllName64bit = @"W:\path\to\openh264-2.4.0-win64.dll";

        static void Main()
        {
            Directory.CreateDirectory(FramesFolder);

            Console.WriteLine("Чтение файла...");
            byte[] file = File.ReadAllBytes(VideoPath);
            Console.WriteLine($"Файл загружен: {file.Length:N0} байт");

            // Определяем формат: есть ли start-коды 0x00000001 или 0x000001
            bool hasStartCodes = HasAnnexBStartCodes(file);
            Console.WriteLine("Формат: " + (hasStartCodes ? "Annex-B (start codes)" : "AVCC / length-prefixed (предположительно)"));

            // Нормализуем: получаем список NAL (каждый NAL — без старт-кода, т.е. payload с заголовком NAL)
            var nalList = new List<byte[]>();
            if (hasStartCodes)
                nalList = ExtractNalsFromAnnexB(file);
            else
                nalList = ExtractNalsFromAvcc(file, 4); // 4 — стандартный размер length-field в MP4

            Console.WriteLine($"Найдено NAL'ов: {nalList.Count}");

            // Найдём SPS и распарсим разрешение
            (int width, int height) = FindResolutionFromSps(nalList);
            if (width == 0 || height == 0)
            {
                Console.WriteLine("SPS не найден или не удалось распарсить разрешение. Декодирование может не работать.");
            }
            else
            {
                Console.WriteLine($"Разрешение из SPS: {width}x{height}");
            }

            // Инициализация декодера H264Sharp (обёртка над OpenH264)
            H264Decoder decoder = new H264Decoder();
            decoder.Initialize(); // стандартная инициализация
            Console.WriteLine("Декодер инициализирован.");

            // Подготовим RgbImage заранее если знаем размер, иначе будем использовать YUV-pointer вариант.
            RgbImage? rgbOut = null;
            if (width > 0 && height > 0)
                rgbOut = new RgbImage(H264Sharp.ImageFormat.Rgb, width, height);

            // Накопитель для передачи в декодер (можно передавать по NAL'ам)
            using var temp = new MemoryStream();
            int savedFrames = 0;
            long processedBytes = 0;
            long totalBytes = file.Length;

            for (int i = 0; i < nalList.Count; i++)
            {
                byte[] nalPayload = nalList[i];

                // В H264Sharp декодер лучше подавать полные NAL'ы с старт-кодом 0x00 00 00 01 либо в виде объединённого буфера.
                // Мы добавим start-code перед NAL при передаче.
                temp.Write(StartCode4, 0, StartCode4.Length);
                temp.Write(nalPayload, 0, nalPayload.Length);

                processedBytes += nalPayload.Length;

                // Попробуем декодировать накопленный буфер
                byte[] buffer = temp.ToArray();

                // Сначала пробуем получить RgbImage (если у нас есть заранее выделенный rgbOut)
                if (rgbOut != null)
                {
                    try
                    {
                        if (decoder.Decode(buffer, 0, buffer.Length, noDelay: true, out DecodingState ds, ref rgbOut))
                        {
                            // Успешно декодирован кадр в rgbOut
                            savedFrames++;
                            SaveRgbImageToPng(rgbOut, Path.Combine(FramesFolder, $"frame_{savedFrames:D6}.png"));
                            Console.Write($"\rСохранено кадров: {savedFrames}   ");
                            temp.SetLength(0); // сбрасываем накопитель
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nОшибка декодера (rgb variant): {ex.Message}");
                        temp.SetLength(0);
                    }
                }
                else
                {
                    // rgbOut не выделён — используем вариант с YUV pointer -> сконвертируем в Bitmap вручную
                    try
                    {
                        if (decoder.Decode(buffer, 0, buffer.Length, noDelay: true, out DecodingState ds, out YUVImagePointer yuvPtr))
                        {
                            // Получили указатель на YUV I420 с информацией в DecodingState (ширина/высота)
                            int w = ds.
                            int h = ds.iHeight;
                            using var bmp = ConvertI420ToBitmap(yuvPtr, w, h);
                            savedFrames++;
                            string outPath = Path.Combine(FramesFolder, $"frame_{savedFrames:D6}.png");
                            bmp.Save(outPath, ImageFormat.Png);
                            Console.Write($"\rСохранено кадров: {savedFrames}   ");
                            temp.SetLength(0);
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"\nОшибка декодера (yuv pointer variant): {ex.Message}");
                        temp.SetLength(0);
                    }
                }

                // Прогресс по байтам
                double percent = totalBytes > 0 ? (processedBytes / (double)totalBytes) * 100.0 : -1;
                if (percent >= 0)
                    Console.Write($"\rОбработано: {percent:0.00}% ({processedBytes:N0}/{totalBytes:N0})   ");
            }

            // В конце вызовем декодер с EOS (если нужно) — H264Sharp/OpenH264 обычно не требует специального финального вызова в этом API.
            Console.WriteLine("\nГотово.");
            Console.WriteLine($"Кадров сохранено: {savedFrames}");
        }

        // ---------- Вспомогательные данные и функции ----------

        static readonly byte[] StartCode4 = new byte[] { 0x00, 0x00, 0x00, 0x01 };

        // Проверяем наличие start-code в блоке
        static bool HasAnnexBStartCodes(byte[] data)
        {
            for (int i = 0; i + 3 < data.Length; i++)
            {
                if (data[i] == 0x00 && data[i + 1] == 0x00 && ((data[i + 2] == 0x01) || (data[i + 2] == 0x00 && data[i + 3] == 0x01)))
                    return true;
            }
            return false;
        }

        // Извлекает NAL из Annex-B (возвращает payload без старт-кода)
        static List<byte[]> ExtractNalsFromAnnexB(byte[] data)
        {
            var result = new List<byte[]>();
            int pos = 0;
            int len = data.Length;

            int FindNextStart(int from)
            {
                for (int i = from; i + 3 < len; i++)
                {
                    if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x00 && data[i + 3] == 0x01) return i;
                    if (data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x01) return i;
                }
                return -1;
            }

            while (pos < len)
            {
                int s = FindNextStart(pos);
                if (s == -1) break;
                int scLen = (s + 3 < len && data[s] == 0x00 && data[s + 1] == 0x00 && data[s + 2] == 0x00 && data[s + 3] == 0x01) ? 4 : 3;
                int nalStart = s + scLen;
                int next = FindNextStart(nalStart);
                int nalEnd = (next == -1) ? len : next;
                if (nalEnd > nalStart)
                {
                    int nalLen = nalEnd - nalStart;
                    var nal = new byte[nalLen];
                    Array.Copy(data, nalStart, nal, 0, nalLen);
                    result.Add(nal);
                }
                pos = nalEnd;
            }
            return result;
        }

        // Извлекает NAL из AVCC (length-prefixed), lengthFieldSize в байтах (обычно 4)
        static List<byte[]> ExtractNalsFromAvcc(byte[] data, int lengthFieldSize)
        {
            var outList = new List<byte[]>();
            int pos = 0;
            while (pos + lengthFieldSize <= data.Length)
            {
                // Обычно MP4 имеет контейнеры; если на вход попал весь MP4, возможно мы наткнёмся на заголовки и не найдём корректное поле.
                // Здесь мы делаем эвристику: читаем 4 байта big-endian как длину NAL и проверяем валидность.
                uint nalLen = 0;
                for (int i = 0; i < lengthFieldSize; i++)
                {
                    nalLen = (nalLen << 8) | data[pos + i];
                }
                pos += lengthFieldSize;
                if (nalLen == 0 || pos + nalLen > data.Length)
                {
                    // Если что-то не так — пытаемся найти Annex-B в оставшемся блоке (fallback)
                    // в практике: если файл — целый MP4, правильнее парсить mdat; но для случая "без moov" часто данные хранятся как raw h264
                    break;
                }
                var nal = new byte[nalLen];
                Array.Copy(data, pos, nal, 0, nalLen);
                outList.Add(nal);
                pos += (int)nalLen;
            }

            // Если не обнаружили NAL'ов, попробуем fallback — искать Annex-B
            if (outList.Count == 0)
            {
                return ExtractNalsFromAnnexB(data);
            }

            return outList;
        }

        // Ищем SPS (NAL type 7) и парсим разрешение
        static (int width, int height) FindResolutionFromSps(List<byte[]> nals)
        {
            foreach (var nal in nals)
            {
                if (nal.Length == 0) continue;
                int nalHeader = nal[0];
                int nalType = nalHeader & 0x1F;
                if (nalType == 7) // SPS
                {
                    try
                    {
                        // SPS payload — RBSP: skip nal header (1 byte) и убираем emulation prevention bytes before parsing
                        byte[] rbsp = RemoveEmulationPreventionBytes(nal, 1);
                        var (w, h) = ParseSps(rbsp);
                        if (w > 0 && h > 0) return (w, h);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }
            return (0, 0);
        }

        static byte[] RemoveEmulationPreventionBytes(byte[] data, int offset)
        {
            using var ms = new MemoryStream();
            for (int i = offset; i < data.Length; i++)
            {
                if (i + 2 < data.Length && data[i] == 0x00 && data[i + 1] == 0x00 && data[i + 2] == 0x03)
                {
                    ms.WriteByte(0x00);
                    ms.WriteByte(0x00);
                    i += 2; // skip 0x03
                    continue;
                }
                ms.WriteByte(data[i]);
            }
            return ms.ToArray();
        }

        // Простая реализация BitReader и Exp-Golomb для парсинга SPS (взята как стандартный минимальный парсер)
        class BitReader
        {
            private readonly byte[] data;
            private int bytePos;
            private int bitPos; // 0..7 (msb first)
            public BitReader(byte[] data)
            {
                this.data = data;
                bytePos = 0; bitPos = 0;
            }
            public int ReadBits(int n)
            {
                int v = 0;
                for (int i = 0; i < n; i++)
                {
                    v = (v << 1) | ReadBit();
                }
                return v;
            }
            private int ReadBit()
            {
                if (bytePos >= data.Length) return 0;
                int b = (data[bytePos] >> (7 - bitPos)) & 1;
                bitPos++;
                if (bitPos == 8) { bitPos = 0; bytePos++; }
                return b;
            }
            public int ReadUe()
            {
                int zeros = 0;
                while (ReadBit() == 0 && bytePos < data.Length) zeros++;
                int value = 0;
                if (zeros > 0)
                {
                    value = (1 << zeros) - 1 + ReadBits(zeros);
                }
                return value;
            }
            public int ReadSe()
            {
                int ueVal = ReadUe();
                int sign = ((ueVal & 1) == 1) ? 1 : -1;
                return ((ueVal + 1) / 2) * sign;
            }
        }

        // Парсер SPS: возвращает (width, height) или (0,0) при ошибке
        static (int width, int height) ParseSps(byte[] rbsp)
        {
            var br = new BitReader(rbsp);
            int profile_idc = br.ReadBits(8);
            br.ReadBits(8); // constraint + reserved
            br.ReadBits(8); // level_idc
            int seq_parameter_set_id = br.ReadUe();

            int chroma_format_idc = 1;
            if (profile_idc == 100 || profile_idc == 110 || profile_idc == 122 || profile_idc == 244 ||
                profile_idc == 44 || profile_idc == 83 || profile_idc == 86 || profile_idc == 118 || profile_idc == 128 || profile_idc == 138 || profile_idc == 144)
            {
                chroma_format_idc = br.ReadUe();
                if (chroma_format_idc == 3)
                {
                    br.ReadBits(1); // separate_colour_plane_flag
                }
                br.ReadUe(); // bit_depth_luma_minus8
                br.ReadUe(); // bit_depth_chroma_minus8
                br.ReadBits(1); // qpprime_y_zero_transform_bypass_flag
                int seq_scaling_matrix_present_flag = br.ReadBits(1);
                if (seq_scaling_matrix_present_flag != 0)
                {
                    // skip scaling lists (complex). We will not parse them.
                    // safe skip: this parser is permissive — for most SPS these bits are absent.
                }
            }

            br.ReadUe(); // log2_max_frame_num_minus4
            int pic_order_cnt_type = br.ReadUe();
            if (pic_order_cnt_type == 0)
            {
                br.ReadUe(); // log2_max_pic_order_cnt_lsb_minus4
            }
            else if (pic_order_cnt_type == 1)
            {
                br.ReadBits(1); // delta_pic_order_always_zero_flag
                br.ReadSe(); // offset_for_non_ref_pic
                br.ReadSe(); // offset_for_top_to_bottom_field
                int num_ref_frames_in_pic_order_cnt_cycle = br.ReadUe();
                for (int i = 0; i < num_ref_frames_in_pic_order_cnt_cycle; i++)
                    br.ReadSe();
            }

            br.ReadUe(); // max_num_ref_frames
            br.ReadBits(1); // gaps_in_frame_num_value_allowed_flag
            int pic_width_in_mbs_minus1 = br.ReadUe();
            int pic_height_in_map_units_minus1 = br.ReadUe();
            int frame_mbs_only_flag = br.ReadBits(1);
            if (frame_mbs_only_flag == 0)
                br.ReadBits(1); // mb_adaptive_frame_field_flag
            br.ReadBits(1); // direct_8x8_inference_flag

            int frame_cropping_flag = br.ReadBits(1);
            int crop_left = 0, crop_right = 0, crop_top = 0, crop_bottom = 0;
            if (frame_cropping_flag != 0)
            {
                crop_left = br.ReadUe();
                crop_right = br.ReadUe();
                crop_top = br.ReadUe();
                crop_bottom = br.ReadUe();
            }

            // compute chroma subsampling
            int subWidthC = 1, subHeightC = 1;
            if (chroma_format_idc == 1) { subWidthC = 2; subHeightC = 2; } // 4:2:0
            else if (chroma_format_idc == 2) { subWidthC = 2; subHeightC = 1; } // 4:2:2
            else if (chroma_format_idc == 3) { subWidthC = 1; subHeightC = 1; } // 4:4:4

            int cropUnitX = subWidthC;
            int cropUnitY = (frame_mbs_only_flag == 1) ? subHeightC : (subHeightC * 2);

            int width = (pic_width_in_mbs_minus1 + 1) * 16 - (crop_left + crop_right) * cropUnitX;
            int height = (pic_height_in_map_units_minus1 + 1) * 16 * (frame_mbs_only_flag == 1 ? 1 : 2) - (crop_top + crop_bottom) * cropUnitY;

            if (width <= 0 || height <= 0) return (0, 0);
            return (width, height);
        }

        static void SaveRgbImageToPng(RgbImage rgb, string path)
        {
            // RgbImage имеет метод ToBitmap() в H264SharpBitmapExtentions
            // Если расширение подключено, то можно прямо:
            Bitmap bmp = rgb.ToBitmap(); // Требуется H264SharpBitmapExtentions
            bmp.Save(path, ImageFormat.Png);
            bmp.Dispose();
        }

        // Конвертация I420 (из YUV pointer) -> Bitmap (наивная реализация, использует Marshal копирование)
        // Здесь используем DecodingState и YUVImagePointer поля: предполагается, что YUVImagePointer предоставляет поля DataY, DataU, DataV и StrideY/StrideU/StrideV.
        // В H264Sharp эти поля доступны в YUVImagePointer и DecodingState. Если имена отличаются — придется адаптировать.
        static Bitmap ConvertI420ToBitmap(YUVImagePointer yuvPtr, int width, int height)
        {
            // Создадим RGB bmp 24bpp
            var bmp = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            var bd = bmp.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, bmp.PixelFormat);
            unsafe
            {
                byte* dst = (byte*)bd.Scan0;
                int dstStride = bd.Stride;

                // См. H.264 I420: Y plane size = w*h, U plane = (w/2)*(h/2), V plane same.
                // Предполагаем, что yuvPtr.Y, yuvPtr.U, yuvPtr.V — IntPtr на данные, и yuvPtr.StrideY/StrideU/StrideV доступны.
                byte* pY = (byte*)yuvPtr.Y;
                byte* pU = (byte*)yuvPtr.U;
                byte* pV = (byte*)yuvPtr.V;
                int strideY = yuvPtr.StrideY;
                int strideU = yuvPtr.StrideU;
                int strideV = yuvPtr.StrideV;

                for (int y = 0; y < height; y++)
                {
                    byte* dstRow = dst + y * dstStride;
                    int chromaY = y / 2;
                    for (int x = 0; x < width; x++)
                    {
                        int chromaX = x / 2;
                        byte Y = pY[y * strideY + x];
                        byte U = pU[chromaY * strideU + chromaX];
                        byte V = pV[chromaY * strideV + chromaX];

                        // YUV(I420) -> RGB conversion
                        int c = Y - 16;
                        int d = U - 128;
                        int e = V - 128;
                        int r = (298 * c + 409 * e + 128) >> 8;
                        int g = (298 * c - 100 * d - 208 * e + 128) >> 8;
                        int b = (298 * c + 516 * d + 128) >> 8;
                        if (r < 0) r = 0; if (r > 255) r = 255;
                        if (g < 0) g = 0; if (g > 255) g = 255;
                        if (b < 0) b = 0; if (b > 255) b = 255;

                        int dstIdx = x * 3;
                        dstRow[dstIdx + 2] = (byte)r;
                        dstRow[dstIdx + 1] = (byte)g;
                        dstRow[dstIdx + 0] = (byte)b;
                    }
                }
            }
            bmp.UnlockBits(bd);
            return bmp;
        }
    }
}
