using System;
using System.Collections.Generic;
using NAudio.Wave;
using NAudio.CoreAudioApi;

class Program{
    static void Main(){
        var enumerator = new MMDeviceEnumerator();

        var cableOutput = GetDevice(enumerator, DataFlow.Capture, "CABLE Output");
        if (cableOutput == null) { Console.WriteLine("CABLE Output не найден!"); return; }
        Console.WriteLine($"Захват с кабеля: {cableOutput.FriendlyName}");

        var speakers = GetDevice(enumerator, DataFlow.Render, "Динамики");
        if (speakers == null) { Console.WriteLine("Колонки не найдены!"); return; }
        Console.WriteLine($"Вывод на: {speakers.FriendlyName}");

        var capture = new WasapiCapture(cableOutput);
        capture.ShareMode = AudioClientShareMode.Shared;

        var buffer = new BufferedWaveProvider(capture.WaveFormat) { DiscardOnBufferOverflow = true };

        capture.DataAvailable += (s, e) =>
        {
            if (capture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat) return;

            float[] floatBuffer = new float[e.BytesRecorded / 4];
            Buffer.BlockCopy(e.Buffer, 0, floatBuffer, 0, e.BytesRecorded);

            ProcessAudio(floatBuffer);

            byte[] outBytes = new byte[e.BytesRecorded];
            Buffer.BlockCopy(floatBuffer, 0, outBytes, 0, e.BytesRecorded);
            buffer.AddSamples(outBytes, 0, outBytes.Length);
        };

        using var waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 100);
        waveOut.Init(buffer);

        capture.StartRecording();
        waveOut.Play();

        Console.WriteLine("Эффект включен! Enter для выхода...");
        Console.ReadLine();

        capture.StopRecording();
        waveOut.Stop();
        capture.Dispose();
        Console.WriteLine("Готово!");
    }
    
    static MMDevice GetDevice(MMDeviceEnumerator enumerator, DataFlow flow, string nameContains){
        foreach (var dev in enumerator.EnumerateAudioEndPoints(flow, DeviceState.Active))
            if (dev.FriendlyName.Contains(nameContains))
                return dev;
        return null;
    }
    
static Random rnd = new Random();

    static int maxSeconds = 30;
    static int sampleRate = 48000;
    static int channels = 2;
    static float[] historyBuffer = new float[sampleRate * maxSeconds * channels];
    static int historyIndex = 0;

    class Repeat
    {
        public int StartIndex;
        public int Remaining;
        public float PlaybackSpeed;
        public float SpeedIndex;
        public int Channels;
    }

    static List<Repeat> activeRepeats = new List<Repeat>();
    static int maxRepeats = 4; // максимум случайных повторов

    // --- Основной поток: небольшая рандомизация ---
    static float mainSpeed = 1f;
    static float mainIndex = 0f;
    static int mainOffset = 0;

    public static void ProcessAudio(float[] buffer, int bufChannels = 2)
    {
        int bufLen = buffer.Length;

        try
        {
            for (int i = 0; i < bufLen; i += bufChannels)
            {
                for (int c = 0; c < bufChannels; c++)
                {
                    int idx = i + c;
                    float sample = buffer[idx];

                    try
                    {
                        if (rnd.NextDouble() < 0.005)
                        {
                            mainOffset = rnd.Next(0, sampleRate * bufChannels / 2); // до 0.5 сек смещения
                            mainSpeed = (float)(0.98 + rnd.NextDouble() * 0.04); // 0.98..1.02×
                        }

                        int histIdx1 = (historyIndex - mainOffset + (int)mainIndex + historyBuffer.Length) % historyBuffer.Length;
                        int histIdx2 = (histIdx1 + 1) % historyBuffer.Length;
                        float t = mainIndex - (int)mainIndex;
                        sample = historyBuffer[histIdx1] * (1 - t) + historyBuffer[histIdx2] * t;
                        mainIndex += mainSpeed;
                        if (mainIndex >= historyBuffer.Length) mainIndex -= historyBuffer.Length;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка основного потока: {ex.Message}");
                    }

                    try
                    {
                        // --- Записываем входной сэмпл в историю ---
                        historyBuffer[historyIndex] = buffer[idx];
                        historyIndex = (historyIndex + 1) % historyBuffer.Length;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка записи в историю: {ex.Message}");
                    }

                    try
                    {
                        if (activeRepeats.Count < maxRepeats && rnd.NextDouble() < 0.05)
                        {
                            int randomOffset = rnd.Next(0, historyBuffer.Length);
                            int start = (historyIndex - randomOffset + historyBuffer.Length) % historyBuffer.Length;

                            int minLength = sampleRate * 1 * bufChannels;
                            int maxLength = sampleRate * 5 * bufChannels;
                            int remaining = (historyBuffer.Length - start + historyBuffer.Length) % historyBuffer.Length;
                            int maxAllowed = Math.Min(maxLength, remaining);

                            // --- Проверка: minLength не может быть больше maxAllowed ---
                            if (minLength <= maxAllowed)
                            {
                                int repeatLength = rnd.Next(minLength, maxAllowed + 1);

                                activeRepeats.Add(new Repeat
                                {
                                    StartIndex = start,
                                    Remaining = repeatLength,
                                    PlaybackSpeed = (float)(0.5 + rnd.NextDouble()), // 0.5..1.5×
                                    SpeedIndex = 0f,
                                    Channels = bufChannels
                                });
                            }
                            // иначе поток не создаём, истории мало
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка создания потоков: {ex.Message}");
                    }

                    try
                    {
                        for (int r = activeRepeats.Count - 1; r >= 0; r--)
                        {
                            var rep = activeRepeats[r];
                            if (rep.Remaining <= 0)
                            {
                                activeRepeats.RemoveAt(r);
                                continue;
                            }

                            int idx1 = (rep.StartIndex + (int)rep.SpeedIndex) % historyBuffer.Length;
                            int idx2 = (idx1 + 1) % historyBuffer.Length;
                            float t = rep.SpeedIndex - (int)rep.SpeedIndex;

                            sample += historyBuffer[idx1] * (1 - t) + historyBuffer[idx2] * t;

                            // только SpeedIndex увеличиваем, StartIndex остаётся фиксированным
                            rep.SpeedIndex += rep.PlaybackSpeed;
                            if (rep.SpeedIndex >= historyBuffer.Length) rep.SpeedIndex -= historyBuffer.Length;

                            rep.Remaining--;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка воспроизведения повторов: {ex.Message}");
                    }

                    // Ограничение амплитуды [-1..1]
                    try
                    {
                        if (sample > 1f) sample = 1f;
                        if (sample < -1f) sample = -1f;
                        buffer[idx] = sample;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Ошибка записи сэмпла в буфер: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Общая ошибка ProcessAudio: {ex.Message}");
        }
    }
}