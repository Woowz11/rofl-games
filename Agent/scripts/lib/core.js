const { app, BrowserWindow, screen, desktopCapturer } = require("electron/main");

const fs = require("fs");
const { EventEmitter } = require("events");
const { Worker, parentPort} = require("worker_threads");
const path = require("path");

const __EEmitter = new EventEmitter();

/**
 * @type {import("electron/main").BrowserWindow[]}
 */
let HUD = [];

const Tesseract = require("tesseract.js");
const { Jimp } = require("jimp");

// ----------------------------------------------------------------------

let { execSync } = require("child_process");

const __ExecParam = {
    encoding: "utf8",
    stdio: "pipe"
};

const Koffi = require("koffi");

const User32 = Koffi.load("user32.dll");
const Gdi32  = Koffi.load("gdi32.dll" );
const DwmApi = Koffi.load("dwmapi.dll");

const DWMWA_EXTENDED_FRAME_BOUNDS = 9;



const HDC = Koffi.pointer("void");
const HWND    = Koffi.pointer("void");
const LPDWORD = Koffi.pointer("uint32");
const LPWSTR  = Koffi.pointer("char16");

const RECT = Koffi.struct('RECT', {
    left  : "int32",
    top   : "int32",
    right : "int32",
    bottom: "int32"
});
const PRECT = Koffi.pointer(RECT);

const POINT = Koffi.struct('POINT', {
    x: "int32",
    y: "int32"
});
const PPOINT = Koffi.pointer(POINT);

const __N_MouseEvent = User32.func("__stdcall", "mouse_event", "void", ["uint32", "uint32", "uint32", "uint32", "uintptr"]);
const __N_SetCursorPos = User32.func("__stdcall", "SetCursorPos", "bool", ["int", "int"]);
const __N_GetCursorPos = User32.func("__stdcall", "GetCursorPos", "bool", [PPOINT]);
const __N_GetAsyncKeyState = User32.func("__stdcall", "GetAsyncKeyState", "int16", ["int"]);
const __N_EnumWindows = User32.func('__stdcall', 'EnumWindows', 'bool', [Koffi.pointer(Koffi.proto('bool', [HWND, 'intptr'])), 'intptr']);
const __N_GetWindowThreadProcessId = User32.func('__stdcall', 'GetWindowThreadProcessId', 'uint32', [HWND, LPDWORD]);
const __N_GetWindowRect = User32.func('__stdcall', 'GetWindowRect', 'bool', [HWND, PRECT]);
const __N_GetWindowTextW = User32.func('__stdcall', 'GetWindowTextW', 'int', [HWND, LPWSTR, 'int']);
const __N_IsWindow = User32.func('__stdcall', 'IsWindow', 'bool', [HWND]);
const __N_IsWindowVisible = User32.func('__stdcall', 'IsWindowVisible', 'bool', [HWND]);
const __N_GetDC = User32.func("__stdcall", "GetDC", HDC, [HWND]);
const __N_GetPixel = Gdi32.func("__stdcall", "GetPixel", "uint32", [HDC, "int", "int"]);
const __N_ReleaseDC = User32.func("__stdcall", "ReleaseDC", "int", [HWND, HDC]);
const __N_DwmGetWindowAttribute = DwmApi.func("__stdcall", "DwmGetWindowAttribute", "int", [
    HWND,
    "uint32",
    Koffi.pointer("void"),
    "uint32"
]);

function __GetHWNDByPID(PID) {
    let FoundHWND = null;
    PID = parseInt(PID);

    const CallbackType = Koffi.pointer(Koffi.proto("bool", [HWND, "intptr"]));

    const EnumProc = Koffi.register((HWNDArgument, LP) => {
        let PIDBuffer = Buffer.alloc(4);
        __N_GetWindowThreadProcessId(HWNDArgument, PIDBuffer);
        let CurrentPID = PIDBuffer.readUInt32LE(0);

        if(CurrentPID === PID){
            if(__N_IsWindowVisible(HWNDArgument)){
                let TitleBuffer = Buffer.alloc(512);
                let L = __N_GetWindowTextW(HWNDArgument, TitleBuffer, 256);
                if(L > 0){
                    FoundHWND = HWNDArgument;
                    return false;
                }
            }
        }
        return true;
    }, CallbackType);

    __N_EnumWindows(EnumProc, 0);
    Koffi.unregister(EnumProc);

    return FoundHWND;
}

// ----------------------------------------------------------------------

function ParseCSV(CSV) {
    const Lines = CSV.split('\n').filter(Line => Line.trim());
    if(Lines.length < 2){ return []; }

    function ParseLine(Line){
        const Result = [];
        let Current = "";
        let InQuotes = false;

        for(let i = 0; i < Line.length; i++){
            const C = Line[i];

            if(C === '"'){
                if(InQuotes && Line[i + 1] === '"'){
                    Current += '"';
                    i++;
                }else{
                    InQuotes = !InQuotes;
                }
            }else if(C === ',' && !InQuotes){
                Result.push(Current.trim());
                Current = '';
            }else{
                Current += C;
            }
        }
        Result.push(Current.trim());
        return Result;
    }

    const Headers = ParseLine(Lines[0]);

    const FinalResult = [];
    for(let i = 1; i < Lines.length; i++){
        const Values = ParseLine(Lines[i]);
        const Data = {};
        for(let j = 0; j < Headers.length; j++){
            Data[Headers[j]] = Values[j] || '';
        }
        FinalResult.push(Data);
    }
    return FinalResult;
}

// ----------------------------------------------------------------------

global.Agent = {
    Other: {
        Wait: async function(Seconds){
            await new Promise(R => setTimeout(R, Seconds * 1000));
        },

        Relative: function(ParentRect, ReferenceRect, Target, Options = {}){
            const Settings = {
                ScaleX: Options.ScaleX !== false,
                ScaleY: Options.ScaleY !== false,
                ScaleW: Options.ScaleW !== false,
                ScaleH: Options.ScaleH !== false
            };

            const SX = Settings.ScaleX ? (ParentRect.W / ReferenceRect.W) : 1;
            const SY = Settings.ScaleY ? (ParentRect.H / ReferenceRect.H) : 1;

            const SW = Settings.ScaleW  ? (ParentRect.W / ReferenceRect.W) : 1;
            const SH = Settings.ScaleH  ? (ParentRect.H / ReferenceRect.H) : 1;

            const OX = Target.X - ReferenceRect.X;
            const OY = Target.Y - ReferenceRect.Y;

            const Result = {
                X: Math.floor(ParentRect.X + (OX * SX)),
                Y: Math.floor(ParentRect.Y + (OY * SY))
            };

            if(Target.W !== undefined && Target.H !== undefined){
                Result.W = Math.floor(Target.W * SW);
                Result.H = Math.floor(Target.H * SH);
            }

            return Result;
        },

        CompareColors: function(ColorA, ColorB){
            const ToRGB = (V) => {
                if(typeof V === "string"){
                    const HEX = V.replace("#", "");
                    return {
                        R: parseInt(HEX.substring(0, 2), 16),
                        G: parseInt(HEX.substring(2, 4), 16),
                        B: parseInt(HEX.substring(4, 6), 16)
                    }
                }
                return { R: V.R, G: V.G, B: V.B }
            };

            const A = ToRGB(ColorA);
            const B = ToRGB(ColorB);

            const DR = A.R - B.R;
            const DG = A.G - B.G;
            const DB = A.B - B.B;

            const Distance = Math.sqrt(DR*DR + DG*DG + DB*DB);

            return Math.max(0, Math.min(1, 1 - (Distance / 441.6729559300637)));
        }
    },

    Mouse: {
        SetPosition: function(X, Y){
            try {
                __N_SetCursorPos(Math.floor(X), Math.floor(Y));
            } catch(e) {
                console.warn(`Ошибка Agent.Mouse.SetPosition(${X}, ${Y}):`, e);
            }
        },

        GetPosition: function(){
            try {
                let PT = Buffer.alloc(8);
                if (__N_GetCursorPos(PT)) {
                    return {
                        X: PT.readInt32LE(0),
                        Y: PT.readInt32LE(4)
                    };
                }
            } catch(e) {
                console.warn("Ошибка Agent.Mouse.GetPosition:", e);
            }
            return { X: 0, Y: 0 };
        },

        Move: async function(X, Y, Duration = 0.8) {
            const Start = this.GetPosition();
            const DX = X - Start.X;
            const DY = Y - Start.Y;
            const Distance = Math.sqrt(DX * DX + DY * DY);

            if(Distance < 2){ return; }

            const Steps = Math.max(20, Math.floor(Duration * 120));
            const TimePerStep = (Duration * 1000) / Steps;

            const UX = DX / Distance;
            const UY = DY / Distance;

            const PX = -UY;
            const PY =  UX;

            const Frequency = 1.0;
            const Amplitude = (Math.random() < 0.5 ? 1 : -1) * (Distance * 0.03 + 2);

            const EaseInOut = (T) => T < 0.5 ? 2 * T * T : -1 + (4 - 2 * T) * T;

            for(let i = 0; i <= Steps; i++){
                let T = i / Steps;
                let ET = EaseInOut(T);

                let LX = Start.X + DX * ET;
                let LY = Start.Y + DY * ET;

                let Offset = Math.sin(T * Math.PI * Frequency) * Amplitude * (ET * (1 - ET) * 4);

                let CX = LX + PX * Offset;
                let CY = LY + PY * Offset;

                CX += (Math.random() - 0.5) * 0.5;
                CY += (Math.random() - 0.5) * 0.5;

                this.SetPosition(CX, CY);

                await Agent.Other.Wait((TimePerStep / 1000) * (0.95 + Math.random() * 0.1));
            }

            this.SetPosition(X, Y);
        },

        Down: function(Button = "Left"){
            const Keys = { "Left": 0x0002, "Right": 0x0008, "Middle": 0x0020 };
            try {
                __N_MouseEvent(Keys[Button] || 0x0002, 0, 0, 0, 0);
            } catch(e) {
                console.warn(`Ошибка Agent.Mouse.Down(${Button}):`, e);
            }
        },

        Up: function(Button = "Left") {
            const Keys = { "Left": 0x0004, "Right": 0x0010, "Middle": 0x0040 };
            try {
                __N_MouseEvent(Keys[Button] || 0x0004, 0, 0, 0, 0);
            } catch(e) {
                console.warn(`Ошибка Agent.Mouse.Up(${Button}):`, e);
            }
        },

        Click: async function(Button = "Left") {
            this.Down(Button);
            await Agent.Other.Wait(0.06 + Math.random() * 0.09);
            this.Up(Button);
        }
    },

    Keyboard: {
        IsDown: function(KeyCode){
            try{
                return (__N_GetAsyncKeyState(KeyCode) & 0x8000) !== 0;
            }catch(e){
                console.warn(`Ошибка Agent.Keyboard.IsDown(${KeyCode}):`, e);
                return false;
            }
        },

        Event_Down: function(Callback){
            __EEmitter.on("keyboard_down", Callback);
        },

        Event_Up: function(Callback){
            __EEmitter.on("keyboard_up", Callback);
        }
    },

    Vision: {
        ShowDebug: false,

        Pixel: function(X, Y){
            try{
                const HDC__ = __N_GetDC(null);
                const Color = __N_GetPixel(HDC__, X, Y);
                __N_ReleaseDC(null, HDC__);

                const R =  Color        & 0xFF;
                const G = (Color >> 8 ) & 0xFF;
                const B = (Color >> 16) & 0xFF;

                const ToHEX = (C) => C.toString(16).padStart(2, '0').toUpperCase();

                if(this.ShowDebug){
                    Agent.Render.Color = "blue";
                    Agent.Render.Pixel(X + 1, Y + 1);
                    Agent.Render.Pixel(X - 1, Y - 1);
                    Agent.Render.Pixel(X + 1, Y - 1);
                    Agent.Render.Pixel(X - 1, Y + 1);

                    Agent.Render.Update(false);
                }

                return { R: R, G: G, B: B, HEX: `#${ToHEX(R)}${ToHEX(G)}${ToHEX(B)}` }
            }catch(e){
                console.warn(`Ошибка Agent.Vision.Pixel(${X}, ${Y}):`, e);
                return { R: 0, G: 0, B: 0, HEX: "#000000" }
            }
        },

        OCR: {
            /**
             * @type {import("Tesseract.js").Worker}
             */
            __OCR: null,
            /**
             * @type {import("Tesseract.js").Worker}
             */
            __OCR2: null,
            /**
             * @type {import("Tesseract.js").Worker}
             */
            __OCR3: null,

            Init: async function(Lang = "rus+eng", Whitelist = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ.-/:"){
                if(this.__OCR !== null){ throw new Error("OCR уже инициализирован!"); }

                const TessedataPath = path.resolve(__dirname, "../../other/tessedata");
                console.log(TessedataPath)

                this.__OCR = await Tesseract.createWorker(Lang, Tesseract.OEM.DEFAULT, {
                    logger: M => { if(this.ShowDebug){ console.log(M); } },
                    errorHandler: e => console.error("Tesseract Worker Error:", e),
                    cacheMethod: "none",
                    gzip: false,
                    langPath: TessedataPath
                }, {
                    load_system_dawg : "0",
                    load_freq_dawg   : "0",
                    load_unambig_dawg: "0",
                    load_punc_dawg   : "0",
                    load_number_dawg : "0",
                    load_bigram_dawg : "0"
                });
                this.__OCR.setParameters({
                    tessedit_pageseg_mode: Tesseract.PSM.SINGLE_BLOCK,
                    tessedit_char_whitelist: Whitelist,
                    user_defined_dpi: "300"
                });

                this.__OCR2 = await Tesseract.createWorker(Lang, Tesseract.OEM.DEFAULT, {
                    logger: M => { if(this.ShowDebug){ console.log(M); } },
                    errorHandler: e => console.error("Tesseract Worker Error:", e),
                    cacheMethod: "none",
                    gzip: false,
                    langPath: TessedataPath
                }, {
                    load_system_dawg : "0",
                    load_freq_dawg   : "0",
                    load_unambig_dawg: "0",
                    load_punc_dawg   : "0",
                    load_number_dawg : "0",
                    load_bigram_dawg : "0"
                });
                this.__OCR2.setParameters({
                    tessedit_pageseg_mode: Tesseract.PSM.SPARSE_TEXT,
                    tessedit_char_whitelist: Whitelist,
                    user_defined_dpi: "300"
                });

                this.__OCR3 = await Tesseract.createWorker(Lang, Tesseract.OEM.DEFAULT, {
                    logger: M => { if(this.ShowDebug){ console.log(M); } },
                    errorHandler: e => console.error("Tesseract Worker Error:", e),
                    cacheMethod: "none",
                    gzip: false,
                    langPath: TessedataPath
                }, {
                    load_system_dawg : "0",
                    load_freq_dawg   : "0",
                    load_unambig_dawg: "0",
                    load_punc_dawg   : "0",
                    load_number_dawg : "0",
                    load_bigram_dawg : "0"
                });
                this.__OCR3.setParameters({
                    tessedit_pageseg_mode: Tesseract.PSM.SINGLE_WORD,
                    tessedit_char_whitelist: Whitelist,
                    user_defined_dpi: "300"
                });
            },

            /**
             * @returns {Promise<string>}
             */
            Read: async function(X, Y, W, H, Options = {}){
                try{
                    const Settings = {
                        Invert: Options.Invert === true
                    }

                    if(!this.__OCR){ await this.Init(); }

                    const Displays     = screen.getAllDisplays();
                    const TargetDisplay = screen.getDisplayMatching({ x: X, y: Y, width: W, height: H });

                    const DisplayIndex = Displays.findIndex(d => d.id === TargetDisplay.id);

                    const Sources = await desktopCapturer.getSources({
                        types: ["screen"],
                        thumbnailSize: {
                            width : TargetDisplay.bounds.width,
                            height: TargetDisplay.bounds.height
                        }
                    });

                    let Source = Sources.find(s => s.display_id === String(TargetDisplay.id)) || Sources[DisplayIndex] || Sources[0];

                    const ScreenPNG = Source.thumbnail.toPNG();

                    const OriginalImage = await Jimp.read(ScreenPNG);

                    const LX = X - TargetDisplay.bounds.x;
                    const LY = Y - TargetDisplay.bounds.y;

                    OriginalImage.crop({ x: LX, y: LY, w: W, h: H });

                    OriginalImage.greyscale();

                    if(Settings.Invert){
                        OriginalImage.invert();
                    }

                    const BGColor = OriginalImage.getPixelColor(0, 0);

                    OriginalImage.resize({ h: 300 });

                    const PaddedImage = new Jimp({
                        width : OriginalImage.bitmap.width  + 100,
                        height: OriginalImage.bitmap.height + 150,
                        color : BGColor
                    });

                    PaddedImage.composite(OriginalImage, 100, 75);
                    PaddedImage.threshold({ max: 155 });

                    const Buffer = await PaddedImage.getBuffer("image/png");

                    if(Agent.Vision.ShowDebug){
                        Agent.Render.Color = "yellow";
                        Agent.Render.Frame(X - 5, Y - 5, W + 5 * 2, H + 5 * 2);

                        fs.writeFileSync("other/DEBUG_OCR.png", Buffer);

                        Agent.Render.Update(false);
                    }

                    let Result = "";

                    let Attempts = 0;
                    while(Attempts < 3){
                        let OCR = (Attempts === 0 ? this.__OCR : (Attempts === 1 ? this.__OCR2 : this.__OCR3));

                        const { data: { text } } = await OCR.recognize(Buffer, {
                            rotateAuto: true,
                        });

                        Result = text.trim();

                        if(Result !== ""){
                            break;
                        }

                        Attempts++;
                    }

                    if(Agent.Vision.ShowDebug){ console.log(`Результат OCR (Попыток:${Attempts}): ${Result}`); }
                    return Result;
                }catch(e){
                    console.warn(`Ошибка Agent.OCR.Read(${X}, ${Y}, ${W}, ${H}):`, e);
                    return "";
                }
            }
        }
    },

    Programs: {
        /**
         * @returns {[{Node: string, CommandLine: string, ExecutablePath: string, Name: string, ProcessId: string}]}
         */
        GetAllCSV: function(){
            try{
                return ParseCSV(execSync(`wmic process get ProcessId,Name,CommandLine,ExecutablePath /format:csv`, __ExecParam));
            }catch(e){
                console.warn("Ошибка Agent.Programs.GetAllCSV:", e);
                return null;
            }
        },

        GetBrowser: function(CSV = undefined){
            try{
                if(CSV === undefined){ CSV = Agent.Programs.GetAllCSV(); }
                let Result = -1;

                for(let i = 0; i < CSV.length; i++){
                    const Process = CSV[i];

                    if(Process.Name.toLowerCase() === "firefox.exe"){
                        if(!Process.CommandLine.includes("-contentproc") && !Process.CommandLine.includes("crashhelper")){
                            Result = parseInt(Process.ProcessId);
                        }
                    }
                }

                if(Result === -1){
                    throw new Error("Браузер не найден!");
                }

                return Result;
            }catch(e){
                console.warn("Ошибка Agent.Programs.GetBrowser:", e);
                return null;
            }
        },

        Window: {
            Exists: function(PID){
                try{
                    const HWND = __GetHWNDByPID(PID);
                    return HWND !== null && __N_IsWindow(HWND);
                }catch(e){
                    console.warn(`Ошибка Agent.Window.Exists(${PID}):`, e);
                    return false;
                }
            },

            GetTitle: function(PID){
                try{
                    const HWND = __GetHWNDByPID(PID);
                    if(!HWND){ return ""; }

                    let TitleBuffer = Buffer.alloc(512);
                    let L = __N_GetWindowTextW(HWND, TitleBuffer, 256);
                    return TitleBuffer.toString("utf16le", 0, L * 2);
                }catch(e){
                    console.warn(`Ошибка Agent.Window.GetTitle(${PID}):`, e);
                    return "";
                }
            },

            GetRect: function(PID) {
                try{
                    const HWND = __GetHWNDByPID(PID);
                    if(!HWND){ return { X: 0, Y: 0, W: 0, H: 0 }; }

                    let RectBuffer = Buffer.alloc(16);

                    let HR = __N_DwmGetWindowAttribute(HWND, DWMWA_EXTENDED_FRAME_BOUNDS, RectBuffer, 16);

                    if(HR < 0){
                        if(!__N_GetWindowRect(HWND, RectBuffer)){
                            return { X: 0, Y: 0, W: 0, H: 0 };
                        }
                    }

                    const L = RectBuffer.readInt32LE(0 );
                    const T = RectBuffer.readInt32LE(4 );
                    const R = RectBuffer.readInt32LE(8 );
                    const B = RectBuffer.readInt32LE(12);

                    let Result = {
                        X: L,
                        Y: T,
                        W: R - L,
                        H: B - T
                    };

                    if(Result.W < 0 || Result.H < 0){ return { X: 0, Y: 0, W: 0, H: 0 }; }

                    return Result;
                }catch(e){
                    console.warn(`Ошибка Agent.Window.GetRect(${PID}):`, e);
                    return { X: 0, Y: 0, W: 0, H: 0 };
                }
            }
        }
    },

    Render: {
        Destroy: function(){
            try{
                const HasRender = HUD.length > 0;

                HUD.forEach(Window => {
                   if(!Window.isDestroyed()){ Window.close(); }
                });
                HUD = [];

                if(HasRender){ app.quit(); }
            }catch(e){
                console.error(e);
            }
        },

        Init: function(WhenReady){
            try{
                if(HUD.length > 0){ throw new Error("Рендер уже инициализирован!"); }

                app.whenReady().then(() => {
                    const Displays = screen.getAllDisplays();

                    Displays.forEach(Display => {
                       const { x, y, width, height } = Display.bounds;

                       const Window = new BrowserWindow({
                           x: x,
                           y: y,
                           width : width,
                           height: height,

                           transparent    : true,
                           backgroundColor: "#00000000",

                           frame      : false,
                           alwaysOnTop: true,
                           skipTaskbar: true,
                           focusable  : false,

                           webPreferences: {
                               nodeIntegration: true,
                               contextIsolation: false
                           }
                       })

                        Window.setIgnoreMouseEvents(true);

                        const HTML =
`<!DOCTYPE html>
<html lang='ru'>
    <head>
        <meta charset="UTF-8">
        <style>
        * { margin: 0; padding: 0; overflow: hidden; box-sizing: border-box; }
        body { width: 100vw; height: 100vh; position: relative; background: transparent; }
        .frame { position: absolute; border: 2px solid; border-radius: 4px; pointer-events: none; }
        .rect { position: absolute; border-radius: 4px; pointer-events: none; }
        </style>
    </head>
    <body>
        <div id="overlay"></div>
        <script>
const { ipcRenderer } = require("electron");
const Overlay = document.getElementById("overlay");
const OffsetX = ${x};
const OffsetY = ${y};

ipcRenderer.on("render", (e, Components) => {
    let HTML = "";
    
    for(let i = 0; i < Components.length; i++){
        const Component = Components[i];
        const Type = Component[0];
        
        switch(Type){
            case 0: {
                let X = Component[1] - OffsetX;
                let Y = Component[2] - OffsetY;
                let W = Component[3];
                let H = Component[4];
                
                let Color = Component[5];
                
                HTML += '<div class="frame" style="left:' + X + 'px; top:' + Y + 'px; width:' + W + 'px; height:' + H + 'px; border-color:' + Color + ';"></div>';
                break;
            }
            case 1: {
                let X = Component[1] - OffsetX;
                let Y = Component[2] - OffsetY;
                let W = Component[3];
                let H = Component[4];
                
                let Color = Component[5];
                
                HTML += '<div class="rect" style="left:' + X + 'px; top:' + Y + 'px; width:' + W + 'px; height:' + H + 'px; background:' + Color + ';"></div>';
                break;
            }
        }
    }
    
    Overlay.innerHTML = HTML;
})

        </script>
    </body>
</html>`;
                        Window.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(HTML)}`);
                        HUD.push(Window);
                    });

                    if(WhenReady){
                        WhenReady();
                    }
                });

                app.on("window-all-closed", () => {
                   if(process.platform !== "darwin"){
                       app.quit();
                   }
                });

                return true;
            }catch(e){
                console.error(e);
                return false;
            }
        },

        Color: "#FF0000",

        Components: [],

        Frame: function(X, Y, W, H){
            this.Components.push([0, X, Y, W, H, this.Color])
        },

        Rect: function(X, Y, W, H){
            this.Components.push([1, X, Y, W, H, this.Color])
        },

        Pixel: function(X, Y){
            this.Rect(X, Y, 1, 1);
        },

        Update: function(Clear = true){
            try{
                HUD.forEach(Window => {
                    if(!Window.isDestroyed()){
                        Window.webContents.send("render", this.Components, Clear);
                    }
                })

                if(Clear){ this.Components = []; }

                return true;
            }catch(e){
                console.warn(e);
                return false;
            }
        }
    }
}

const __Worker = new Worker(
    // language=JS
`const { parentPort } = require("worker_threads");
const Koffi = require("koffi");
const User32 = Koffi.load("user32.dll");
const GetAsyncKeyState = User32.func("__stdcall", "GetAsyncKeyState", "int16", ["int"])

let States = new Uint8Array(256);
setInterval(() => {
    for(let i = 1; i < 256; i++){
        let IsDown = (GetAsyncKeyState(i) & 0x8000) !== 0;
        
        if(IsDown && States[i] === 0){
            States[i] = 1;
            parentPort.postMessage({ type: "keyboard_down", code: i })
        }else if(!IsDown && States[i] === 1){
            States[i] = 0;
            parentPort.postMessage({ type: "keyboard_up", code: i })
        }
    }
}, 10);
`
    , { eval: true });

__Worker.on("message", Message => {
    __EEmitter.emit(Message.type, Message.code);
})