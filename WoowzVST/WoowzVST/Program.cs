using System;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using NAudio.CoreAudioApi;

class Program
{
    static void Main()
    {
        // Находим физические колонки
        var enumerator = new MMDeviceEnumerator();
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
            Console.WriteLine("Физические колонки не найдены!");
            return;
        }

        Console.WriteLine($"Используем устройство: {speakers.FriendlyName}");

        // Генератор синусоиды
        var sineWave = new SignalGenerator()
        {
            Gain = 0.5,
            Frequency = 440,
            Type = SignalGeneratorType.Sin
        };

        // Используем WasapiOut напрямую, чтобы выбрать устройство
        using var waveOut = new WasapiOut(speakers, AudioClientShareMode.Shared, false, 100);
        waveOut.Init(sineWave);
        waveOut.Play();

        Console.WriteLine("Играет 5 секунд...");
        System.Threading.Thread.Sleep(5000);

        waveOut.Stop();
        Console.WriteLine("Готово!");
    }
}