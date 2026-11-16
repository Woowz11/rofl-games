using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using WLO.Math;

namespace LagSwitch;

public static class Bootstrap{
    private static WLO.Window.GLFW  Window = null!;
    private static WLO.Web.WebView2 Web    = null!;

    // Сообщения для UI. Пишутся из любого потока, читаются в UI-цикле.
    private static readonly ConcurrentQueue<string> __PendingMessages = [];

    // Все тяжёлые операции (netsh, переключения) выполняются в одном фоновом потоке.
    private static readonly BlockingCollection<Action> __Commands = new();

    // Защищает переключение состояния и сетевые операции.
    private static readonly object __Sync = new object();

    // Защищает поля бинда. Берётся в хуке клавиатуры, поэтому внутри только присваивания.
    private static readonly object __KeyLock = new object();

    private static volatile bool __Enabled  = false; // пишется под __Sync
    private static volatile bool __Stopping = false; // пишется под __Sync

    // 1, пока переключение выполняется или стоит в очереди. Меняется только через Interlocked.
    private static int __Busy = 0;

    private static bool     __Capturing  = false;                          // __KeyLock
    private static bool     __HotkeyHeld = false;                          // __KeyLock
    private static KeyBind? __BoundKey   = new KeyBind(VK_NUMPAD4, false); // __KeyLock

    private static readonly LowLevelKeyboardProc __HookProc = __OnLowLevelKeyboard;

    private const uint VK_ESCAPE  = 0x1B;
    private const uint VK_NUMPAD4 = 0x64;

    private const int  WH_KEYBOARD_LL = 13;
    private const int  WM_KEYDOWN     = 0x0100;
    private const int  WM_KEYUP       = 0x0101;
    private const int  WM_SYSKEYDOWN  = 0x0104;
    private const int  WM_SYSKEYUP    = 0x0105;
    private const uint LLKHF_EXTENDED = 0x01;

    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            __PrepareNetwork();

            __StartCommandWorker();

            Web = WLO.Web.WebView2.Create();

            Window = new WLO.Window.GLFW(new Vector2I(256, 256), "LagSwitch", Web);
            Window.TODO_UseDarkMode();

            __WindowHwnd = Window.HWND;
            __LoadIcons();
            __ApplyIcon(false);
            
            Web.OnMessage += __OnWebMessage;
            Web.NavigateCustom(Page.HTML);

            __StartKeyboardHook();

            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();

                while(__PendingMessages.TryDequeue(out string? Pending)){
                    Web.PostMessage(Pending);
                }
            }

            __Shutdown();
            Window.Close();
        }catch(Exception e){
            __Shutdown();
            WL.Logger.Fatal("Опять всё взорвалось...", e);
            return WL.Core.Stop(1);
        }

        return WL.Core.Stop(0);
    }

    // ----------------------------------------------------------------------
    // Очередь команд

    private static void __StartCommandWorker(){
        Thread Worker = new Thread(() => {
            foreach(Action Command in __Commands.GetConsumingEnumerable()){
                try{
                    Command();
                }catch(Exception e){
                    WL.Logger.Error("Ошибка в фоновой команде!", e);
                }
            }
        }){
            IsBackground = true,
            Name         = "CommandWorker"
        };

        Worker.Start();
    }

    private static void __Post(Action Command){
        try{
            __Commands.Add(Command);
        }catch(InvalidOperationException){
            // Очередь закрыта, приложение завершается.
        }
    }

    // ----------------------------------------------------------------------
    // Web → C#

    private static void __OnWebMessage(string Message){
        try{
            using JsonDocument Doc = JsonDocument.Parse(Message);
            string Type = Doc.RootElement.GetProperty("Type").GetString() ?? "";

            switch(Type){
                case "Ready":
                    __EnqueueState("Init");
                    break;

                case "Toggle":
                    __TryRequestToggle("Click");
                    break;

                case "StartCapture":
                    __SetCapturing(true);
                    break;

                case "CancelCapture":
                    __SetCapturing(false);
                    break;

                default:
                    WL.Logger.Warn($"Получено неизвестное сообщение от Web, Тип: {Type}");
                    break;
            }
        }catch(Exception e){
            WL.Logger.Error($"Не удалось обработать сообщение от Web!\nСообщение: {Message}", e);
        }
    }

    // ----------------------------------------------------------------------
    // Состояние

    // Единственная точка входа для переключения (клик и хоткей).
    // Если предыдущее переключение ещё не закончилось, новый запрос отбрасывается.
    private static bool __TryRequestToggle(string Source){
        if(__Stopping){ return false; }

        if(Interlocked.CompareExchange(ref __Busy, 1, 0) != 0){
            WL.Logger.Info($"Переключение ({Source}) проигнорировано: предыдущее ещё выполняется.");
            return false;
        }

        // UI сразу становится жёлтым, не дожидаясь netsh.
        __EnqueueState(Source);

        __Post(() => {
            try{
                __Toggle(Source);
            }finally{
                Interlocked.Exchange(ref __Busy, 0);
                __EnqueueState(Source);
            }
        });

        return true;
    }

    private static void __Toggle(string Source){
        lock(__Sync){
            if(__Stopping){ return; }

            __SetEnabled(!__Enabled, Source);
        }
    }

    // Вызывать только под __Sync.
    private static void __SetEnabled(bool Value, string Source){
        if(__Enabled == Value){ return; }

        // Value = true означает "лаг включён", то есть адаптеры нужно отключить.
        if(!__SetInternetEnabled(!Value)){
            WL.Logger.Error(Value
                ? "Лаг не включён: адаптеры остались как были."
                : "Интернет не восстановлен. Запустите RestoreInternet.bat.");

            __EnqueueState(Source); // состояние не меняем
            return;
        }

        __Enabled = Value;
        __OnEnabledChanged(Value, Source);
    }

    private static void __OnEnabledChanged(bool Enabled, string Source){
        WL.Logger.Info(Enabled ? "Интернет отключен!" : "Интернет включен!");
        __ApplyIcon(Enabled);
        __EnqueueState(Source);
    }

    // Вызывается при выходе. Выключает лаг и запрещает дальнейшие переключения.
    private static void __Shutdown(){
        lock(__Sync){
            if(__Stopping){ return; }
            __Stopping = true;

            try{
                __SetEnabled(false, "Exit");
            }catch(Exception e){
                WL.Logger.Error("Не удалось восстановить интернет при выходе!", e);
            }
        }

        __Commands.CompleteAdding();
    }

    private static void __SetCapturing(bool Capturing){
        lock(__KeyLock){
            __Capturing = Capturing;
        }

        __EnqueueState("Capture");
    }

    private static void __EnqueueState(string Source){
        bool     Capturing;
        KeyBind? Key;

        lock(__KeyLock){
            Capturing = __Capturing;
            Key       = __BoundKey;
        }

        bool Enabled = __Enabled;
        bool Busy    = Volatile.Read(ref __Busy) != 0;

        string JSON = JsonSerializer.Serialize(new{
            Type = "State",
            Data = new{
                Enabled,
                Busy,
                Capturing,
                KeyName = __KeyName(Key),
                Source,
                Time = DateTime.Now.ToString("HH:mm:ss")
            }
        });

        __PendingMessages.Enqueue(JSON);
    }

    // ----------------------------------------------------------------------
    // Сетевые адаптеры
    //
    // Трафик блокируется правилами брандмауэра, привязанными к адаптеру.
    // Сам адаптер остаётся включённым, поэтому линк не перезапускается
    // и переключение занимает доли секунды вместо 15-30 секунд.

    // Адаптеры, трафик которых блокируется. Имена точно как в ncpa.cpl.
    private static readonly string[] AdapterNames = ["Ethernet"];

    private const string __RuleNameIn  = "LagSwitch Block In";
    private const string __RuleNameOut = "LagSwitch Block Out";

    // Файл от старой версии (отключение адаптера). Если приложение упало в том режиме, адаптер нужно включить.
    private static readonly string __AdapterFile = Path.Combine(AppContext.BaseDirectory, "disabled_adapters.txt");

    // Вызывать только под __Sync (или при старте до запуска потоков).
    private static bool __SetInternetEnabled(bool Enabled) => Enabled ? __Unblock() : __Block();

    // Вызывается один раз при старте: включает адаптеры от старой версии и заново создаёт правила выключенными.
    private static void __PrepareNetwork(){
        if(File.Exists(__AdapterFile)){
            List<string> Old = File.ReadAllLines(__AdapterFile)
                                   .Select(N => N.Trim())
                                   .Where(N => N.Length > 0)
                                   .ToList();

            WL.Logger.Info($"Найден файл прошлого отключения, включаю адаптеры: {string.Join(", ", Old)}");

            bool Ok = true;
            foreach(string Name in Old){
                Ok &= __EnableAdapter(Name);
            }

            if(Ok){ File.Delete(__AdapterFile); }
        }

        __RecreateRules();
    }

    // ---
    private static bool __EnableAdapter(string Name) =>
        __Run("netsh", $"interface set interface name=\"{Name}\" admin=enabled", $"Адаптер '{Name}'", out _);

    // ---
    // Удаляем старые правила LagSwitch (например, после сбоя) и создаём новые выключенными.
    private static bool __RecreateRules(){
        string Interfaces = string.Join(",", AdapterNames.Select(N => $"'{N}'"));

        string Script =
            "Remove-NetFirewallRule -DisplayName 'LagSwitch Block*' -ErrorAction SilentlyContinue;" +
            $"New-NetFirewallRule -DisplayName '{__RuleNameIn}' -Direction Inbound -Action Block -InterfaceAlias {Interfaces} -Enabled False | Out-Null;" +
            $"New-NetFirewallRule -DisplayName '{__RuleNameOut}' -Direction Outbound -Action Block -InterfaceAlias {Interfaces} -Enabled False | Out-Null;";

        // EncodedCommand избавляет от проблем с кавычками в аргументах.
        string Encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Script));

        bool Ok = __Run("powershell", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {Encoded}", "Правила брандмауэра", out _);
        if(Ok){ WL.Logger.Info("Правила брандмауэра созданы (выключены)."); }

        return Ok;
    }

    // ---
    private static bool __SetRules(bool Enabled){
        string State = Enabled ? "yes" : "no";
        bool   Ok    = true;

        foreach(string Rule in new[]{ __RuleNameIn, __RuleNameOut }){
            Ok &= __Run("netsh", $"advfirewall firewall set rule name=\"{Rule}\" new enable={State}", $"Правило '{Rule}'", out _);
        }

        return Ok;
    }

    // ---
    private static bool __Block(){
        Stopwatch Watch = Stopwatch.StartNew();

        if(!__SetRules(true)){ return false; }

        WL.Logger.Info($"  трафик {string.Join(", ", AdapterNames)} заблокирован за {Watch.ElapsedMilliseconds} мс");
        __LogAdapters("После блокировки");
        return true;
    }

    // ---
    private static bool __Unblock(){
        Stopwatch Watch = Stopwatch.StartNew();

        if(!__SetRules(false)){ return false; }

        WL.Logger.Info($"  трафик {string.Join(", ", AdapterNames)} разблокирован за {Watch.ElapsedMilliseconds} мс");
        __LogAdapters("После разблокировки");
        return true;
    }

    // ---
    private static void __LogAdapters(string Title){
        WL.Logger.Info($"[Адаптеры] {Title}:");

        foreach(NetworkInterface NI in NetworkInterface.GetAllNetworkInterfaces()){
            WL.Logger.Info($"  '{NI.Name}' | {NI.NetworkInterfaceType} | {NI.OperationalStatus} | {NI.Description}");
        }
    }

    // ---
    // Запуск внешней программы. Возвращает true при коде 0. Вывод программы попадает в Output.
    private static bool __Run(string FileName, string Arguments, string Title, out string Output){
        Output = "";

        try{
            ProcessStartInfo PSI = new ProcessStartInfo{
                FileName               = FileName,
                Arguments              = Arguments,
                UseShellExecute        = false,
                CreateNoWindow         = true,
                RedirectStandardOutput = true,
                RedirectStandardError  = true
            };

            using Process? P = Process.Start(PSI);
            if(P == null){
                WL.Logger.Error($"{Title}: не удалось запустить {FileName}!");
                return false;
            }

            Task<string> OutTask = P.StandardOutput.ReadToEndAsync();
            Task<string> ErrTask = P.StandardError.ReadToEndAsync();

            if(!P.WaitForExit(10_000)){
                P.Kill();
                WL.Logger.Error($"{Title}: {FileName} не ответил за 10 секунд.");
                return false;
            }

            Output = $"{OutTask.Result} {ErrTask.Result}".Trim();

            if(P.ExitCode != 0){
                WL.Logger.Error($"{Title}: ОШИБКА, код {P.ExitCode}. {Output}");
                return false;
            }

            return true;
        }catch(Exception e){
            WL.Logger.Error($"{Title}: ошибка запуска {FileName}!", e);
            return false;
        }
    }

    // ----------------------------------------------------------------------
    // Глобальный хук клавиатуры

    private static void __StartKeyboardHook(){
        Thread HookThread = new Thread(__KeyboardHookThread){
            IsBackground = true,
            Name         = "KeyboardHook"
        };

        HookThread.Start();
    }

    private static void __KeyboardHookThread(){
        IntPtr Hook = SetWindowsHookEx(WH_KEYBOARD_LL, __HookProc, GetModuleHandle(null), 0);
        if(Hook == IntPtr.Zero){
            WL.Logger.Error("Не удалось установить глобальный хук клавиатуры!", new Win32Exception(Marshal.GetLastWin32Error()));
            return;
        }

        while(GetMessage(out MSG Msg, IntPtr.Zero, 0, 0) > 0){
            TranslateMessage(ref Msg);
            DispatchMessage(ref Msg);
        }

        UnhookWindowsHookEx(Hook);
    }

    // Callback хука должен быть очень быстрым. Здесь нет netsh и блокировки __Sync.
    private static IntPtr __OnLowLevelKeyboard(int nCode, IntPtr wParam, IntPtr lParam){
        try{
            if(nCode >= 0){
                int  Msg    = wParam.ToInt32();
                bool IsDown = Msg == WM_KEYDOWN || Msg == WM_SYSKEYDOWN;
                bool IsUp   = Msg == WM_KEYUP   || Msg == WM_SYSKEYUP;

                if(IsDown || IsUp){
                    KBDLLHOOKSTRUCT Data     = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                    bool            Extended = (Data.Flags & LLKHF_EXTENDED) != 0;
                    uint            VK       = Data.VkCode;

                    if(!Extended && __NumpadNavToVK.TryGetValue(VK, out uint NumpadVK)){
                        VK = NumpadVK;
                    }

                    __HandleKey(new KeyBind(VK, Extended), IsDown);
                }
            }
        }catch{
            // В хуке нельзя падать: ошибка просто пропускается.
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private static void __HandleKey(KeyBind Key, bool IsDown){
        bool Fire        = false;
        bool BindChanged = false;
        bool Cleared     = false;

        lock(__KeyLock){
            if(__Capturing){
                if(IsDown){
                    __Capturing  = false;
                    Cleared      = Key.VK == VK_ESCAPE;
                    __BoundKey   = Cleared ? null : Key;
                    __HotkeyHeld = !Cleared;
                    BindChanged  = true;
                }
            }else if(__BoundKey == Key){
                if(!IsDown){
                    __HotkeyHeld = false;
                }else if(!__HotkeyHeld){
                    __HotkeyHeld = true;
                    Fire         = true;
                }
            }
        }

        if(BindChanged){
            __Post(() => {
                WL.Logger.Info(Cleared ? "Горячая клавиша сброшена" : $"Горячая клавиша назначена: {__KeyName(Key)}");
                __EnqueueState("Bind");
            });
        }

        if(Fire){
            __TryRequestToggle("Hotkey");
        }
    }

    // ----------------------------------------------------------------------
    // Названия клавиш

    private static readonly Dictionary<uint, string> __KeyNames = new(){
        [0x08] = "Backspace", [0x09] = "Tab",        [0x0D] = "Enter",      [0x13] = "Pause",
        [0x14] = "Caps Lock", [0x1B] = "Esc",        [0x20] = "Space",      [0x21] = "Page Up",
        [0x22] = "Page Down", [0x23] = "End",        [0x24] = "Home",       [0x25] = "←",
        [0x26] = "↑",         [0x27] = "→",          [0x28] = "↓",          [0x2C] = "Print Screen",
        [0x2D] = "Insert",    [0x2E] = "Delete",     [0x5B] = "Win (L)",    [0x5C] = "Win (R)",
        [0x5D] = "Menu",      [0x6A] = "Numpad *",   [0x6B] = "Numpad +",   [0x6C] = "Numpad Sep",
        [0x6D] = "Numpad -",  [0x6E] = "Numpad .",   [0x6F] = "Numpad /",   [0x90] = "Num Lock",
        [0x91] = "Scroll Lock",
        [0xA0] = "Shift (L)", [0xA1] = "Shift (R)",  [0xA2] = "Ctrl (L)",   [0xA3] = "Ctrl (R)",
        [0xA4] = "Alt (L)",   [0xA5] = "Alt (R)",
        [0xBA] = ";",         [0xBB] = "=",          [0xBC] = ",",          [0xBD] = "-",
        [0xBE] = ".",         [0xBF] = "/",          [0xC0] = "`",          [0xDB] = "[",
        [0xDC] = "\\",        [0xDD] = "]",          [0xDE] = "'"
    };

    private static readonly Dictionary<uint, uint> __NumpadNavToVK = new(){
        [0x2D] = 0x60, // Insert  → Numpad 0
        [0x23] = 0x61, // End     → Numpad 1
        [0x28] = 0x62, // Down    → Numpad 2
        [0x22] = 0x63, // PageDn  → Numpad 3
        [0x25] = 0x64, // Left    → Numpad 4
        [0x0C] = 0x65, // Clear   → Numpad 5
        [0x27] = 0x66, // Right   → Numpad 6
        [0x24] = 0x67, // Home    → Numpad 7
        [0x26] = 0x68, // Up      → Numpad 8
        [0x21] = 0x69, // PageUp  → Numpad 9
        [0x2E] = 0x6E  // Delete  → Numpad .
    };

    private static string? __KeyName(KeyBind? Key){
        if(Key is not KeyBind K){ return null; }

        uint VK = K.VK;

        if(VK == 0x0D && K.Extended){ return "Numpad Enter"; }
        if(VK >= 0x60 && VK <= 0x69){ return $"Numpad {VK - 0x60}"; }
        if(__KeyNames.TryGetValue(VK, out string? Name)){ return Name; }
        if((VK >= 0x30 && VK <= 0x39) || (VK >= 0x41 && VK <= 0x5A)){ return ((char)VK).ToString(); }
        if(VK >= 0x70 && VK <= 0x87){ return $"F{VK - 0x6F}"; }

        return $"VK 0x{VK:X2}";
    }
    
    // ----------------------------------------------------------------------
    // Иконка окна

    private const string __IconPathOn   = @"%SystemRoot%\System32\shell32.dll";
    private const int    __IconIndexOn  = 109;
    private const string __IconPathOff  = @"%SystemRoot%\System32\shell32.dll";
    private const int    __IconIndexOff = 13;

    private const uint WM_SETICON = 0x0080;
    private const int  ICON_SMALL = 0;
    private const int  ICON_BIG   = 1;

    private static IntPtr __WindowHwnd;
    private static IntPtr __IconOnBig,  __IconOnSmall;
    private static IntPtr __IconOffBig, __IconOffSmall;

    private static void __LoadIcons(){
        __LoadIcon(__IconPathOn,  __IconIndexOn,  out __IconOnBig,  out __IconOnSmall);
        __LoadIcon(__IconPathOff, __IconIndexOff, out __IconOffBig, out __IconOffSmall);
    }

    private static void __LoadIcon(string FilePath, int Index, out IntPtr Big, out IntPtr Small){
        Big   = IntPtr.Zero;
        Small = IntPtr.Zero;

        string Path = Environment.ExpandEnvironmentVariables(FilePath);
        IntPtr[] Large = new IntPtr[1];
        IntPtr[] Tiny  = new IntPtr[1];

        ExtractIconEx(Path, Index, Large, Tiny, 1);

        if(Large[0] == IntPtr.Zero){
            WL.Logger.Warn($"Не удалось загрузить значок {Index} из '{Path}'. Оставлен стандартный.");
            return;
        }

        Big   = Large[0];
        Small = Tiny[0] != IntPtr.Zero ? Tiny[0] : Large[0];
    }

    private static void __ApplyIcon(bool Enabled){
        if(__WindowHwnd == IntPtr.Zero){ return; }

        IntPtr Big   = Enabled ? __IconOnBig   : __IconOffBig;
        IntPtr Small = Enabled ? __IconOnSmall : __IconOffSmall;

        if(Big == IntPtr.Zero){ return; }

        PostMessage(__WindowHwnd, WM_SETICON, (IntPtr)ICON_BIG,   Big  );
        PostMessage(__WindowHwnd, WM_SETICON, (IntPtr)ICON_SMALL, Small);
    }

    // ----------------------------------------------------------------------
    // Типы и P/Invoke

    private readonly record struct KeyBind(uint VK, bool Extended);

    [StructLayout(LayoutKind.Sequential)]
    private struct KBDLLHOOKSTRUCT{
        public uint   VkCode;
        public uint   ScanCode;
        public uint   Flags;
        public uint   Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT{
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG{
        public IntPtr Hwnd;
        public uint   Message;
        public IntPtr WParam;
        public IntPtr LParam;
        public uint   Time;
        public POINT  Pt;
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
    
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconEx(string lpszFile, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
}