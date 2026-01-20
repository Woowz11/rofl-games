using System;
using NAudio.Wave;
using NAudio.CoreAudioApi;

class Program
{
    static void Main()
    {
        var enumerator = new MMDeviceEnumerator();

        // 1️⃣ Захват с CABLE Output (запись)
        MMDevice cableOutput = null;
        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
        {
            if (dev.FriendlyName.Contains("CABLE Output"))
            {
                cableOutput = dev;
                break;
            }
        }

        if (cableOutput == null)
        {
            Console.WriteLine("CABLE Output не найден!");
            return;
        }

        Console.WriteLine($"Захват с кабеля: {cableOutput.FriendlyName}");

        // 2️⃣ Физические колонки
        MMDevice speakers = null;
        foreach (var dev in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            if (dev.FriendlyName.Contains("Динамики"))
            {
                speakers = dev;
                break;
            }
        }

        if (speakers == null)
        {
            Console.WriteLine("Колонки не найдены!");
            return;
        }

        Console.WriteLine($"Вывод на: {speakers.FriendlyName}");

        // 3️⃣ Захват
        var capture = new WasapiCapture(cableOutput);
        var buffer = new BufferedWaveProvider(capture.WaveFormat)
        {
            DiscardOnBufferOverflow = true
        };

        capture.DataAvailable += (s, e) =>
        {
            buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
        };

        // 4️⃣ Вывод на колонки
        using var waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 100);
        waveOut.Init(buffer);

        capture.StartRecording();
        waveOut.Play();

        Console.WriteLine("Идёт звук с CABLE Input через CABLE Output → колонки. Enter для выхода...");
        Console.ReadLine();

        capture.StopRecording();
        waveOut.Stop();
        capture.Dispose();
        Console.WriteLine("Готово!");
    }
}