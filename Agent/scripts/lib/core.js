const __Agent = {}

let { Mouse } = require("mechatron");
__Agent.Mouse = Mouse;
Mouse = undefined;

let { app, BrowserWindow, screen } = require("electron/main");
__Agent.EWindow = BrowserWindow;
BrowserWindow = undefined;
__Agent.Electron = app;
app = undefined;
/**
 * @type {import("electron/main").BrowserWindow}
 */
__Agent.HUD = undefined;
__Agent.Screen = screen
screen = undefined;

// ----------------------------------------------------------------------

const __FFI = require('ffi-napi');
const __REF = require('ref-napi');

const RECT = __REF.types.struct({
    
})

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
    __Agent: __Agent,

    Mouse: {
        SetPosition: function(X, Y){
            __Agent.Mouse.setPos(X, Y);
        },
        GetPosition: function(){
            let Point = __Agent.Mouse.getPos();
            return [Point.x, Point.y];
        }
    },

    Programs: {
        /**
         * @returns {[{Node: string, CommandLine: string, ExecutablePath: string, Name: string, ProcessId: string}]}
         */
        GetAllCSV: function(){
            try{
                const Result = __Agent.Exec(`wmic process get ProcessId,Name,CommandLine,ExecutablePath /format:csv`, __ExecParam);
                return ParseCSV(Result);
            }catch(e){
                console.warn(e);
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
                console.warn(e);
                return null;
            }
        },

        Window: {
            Exists: function(PID){
                try{
                    const Result = __Agent.Powershell(`(Get-Process -Id ${PID} -ErrorAction SilentlyContinue).MainWindowHandle -ne 0`);
                    return Result && Result.trim() === "True";
                }catch(e){
                    console.warn(e);
                    return false;
                }
            },

            GetTitle: function(PID){
                try{
                    const Result = __Agent.Powershell(`(Get-Process -Id ${PID} -ErrorAction SilentlyContinue).MainWindowTitle`);
                    return Result ? Result.trim() : "";
                }catch(e){
                    console.warn(e);
                    return "";
                }
            },

            GetRect: function(PID){
                try{
                    const Result = __Agent.Powershell(`
                Add-Type -TypeDefinition @"
                using System;
                using System.Runtime.InteropServices;
                public class User32 {
                    [DllImport("user32.dll")]
                    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
                    [DllImport("user32.dll")]
                    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
                    
                    public struct RECT {
                        public int Left;
                        public int Top;
                        public int Right;
                        public int Bottom;
                    }
                }
"@;
                $hwnd = (Get-Process -Id ${PID}).MainWindowHandle;
                $rect = New-Object User32+RECT;
                [User32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null;
                
                $Width = $rect.Right - $rect.Left;
                $Height = $rect.Bottom - $rect.Top;
                
                Write-Output "$($rect.Left),$($rect.Top),$($Width),$($Height)"
            `);
                    if(!Result){ return null; }

                    const Parts = Result.trim().split(',');
                    if(Parts.length !== 4){ return null; }

                    return {
                        X: parseInt(Parts[0]),
                        Y: parseInt(Parts[1]),
                        W: parseInt(Parts[2]),
                        H: parseInt(Parts[3])
                    };
                }catch(e){
                    console.warn(e);
                    return null;
                }
            }
        }
    },

    Render: {
        Destroy: function(){
            try{
                if(__Agent.Electron !== undefined){ __Agent.Electron.quit(); }
                __Agent.Electron = undefined;
            }catch(e){
                console.error(e);
            }
        },

        Init: function(WhenReady){
            try{
                if(__Agent.HUD !== undefined){ throw new Error("Рендер уже инициализирован!"); }

                __Agent.Electron.whenReady().then(() => {
                    let TX =  Infinity;
                    let TY =  Infinity;
                    let TR = -Infinity;
                    let TB = -Infinity;

                    // 🙄 хуйня не хочет делаться на несколько мониторов
                    __Agent.Screen.getAllDisplays().forEach(Display => {
                       const Bounds = Display.bounds;
                        if(Bounds.x < TX){ TX = Bounds.x; }
                        if(Bounds.y < TY){ TY = Bounds.y; }
                        if(Bounds.x + Bounds.width  > TR){ TR = Bounds.x + Bounds.width ; }
                        if(Bounds.y + Bounds.height > TB){ TB = Bounds.y + Bounds.height; }
                    });

                    const TW = TR - TX;
                    const TH = TB - TY;

                    __Agent.HUD = new __Agent.EWindow({
                        x     : TX,
                        y     : TY,
                        width : TW,
                        height: TH,

                        transparent    : true,
                        backgroundColor: "#00000000",

                        frame      : false,
                        alwaysOnTop: true,
                        skipTaskbar: true,
                        focusable  : false,

                        webPreferences: {
                            nodeIntegration: true,
                            contextIsolation: false,
                            enableRemoteModule: false
                        }
                    });

                    __Agent.HUD.setIgnoreMouseEvents(true);

                    const HTML =
`<!DOCTYPE html>
<html lang='ru'>
    <head>
        <meta charset="UTF-8">
        <style>
        * { margin: 0; padding: 0; box-sizing: border-box; }
        body { 
            background: rgba(0, 255, 0, 0.1) !important;
            overflow: hidden;
            width: 100vw;
            height: 100vh;
            position: relative;
        }
        .overlay {
            position: absolute;
            top: 0;
            left: 0;
            width: 100%;
            height: 100%;
            pointer-events: none;
        }
        .rect {
            position: absolute;
            border: 2px solid rgba(255,255,255,0.8);
            border-radius: 4px;
            pointer-events: none;
        }
        </style>
    </head>
    <body>
        <div class="overlay" id="overlay"></div>
        <script>

const Overlay = document.getElementById("overlay");

window.Render = function(Components){
    let HTML = "";
    
    for(let i = 0; i < Components.length; i++){
        const Component = Components[i];
        const Type = Component[0];
        
        switch(Type){
            case 0: {
                let X = Component[1];
                let Y = Component[2];
                let W = Component[3];
                let H = Component[4];
                
                let Color = Component[5];
                
                HTML += '<div class="rect" style="' +
                            'left: ' + X + 'px; ' +
                            'top: ' + Y + 'px; ' +
                            'width: ' + W + 'px; ' +
                            'height: ' + H + 'px; ' +
                            'background: ' + Color + ';' +
                            '"></div>';
                break;
            }
        }
    }
    
    Overlay.innerHTML = HTML;
}

const { ipcRenderer } = require("electron");
ipcRenderer.on("render", (e, Components) => {
    window.Render(Components);
})

        </script>
    </body>
</html>`;

                    __Agent.HUD.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(HTML)}`);

                    if(WhenReady !== undefined){
                        WhenReady();
                    }
                });

                __Agent.Electron.on("window-all-closed", () => {
                   if(process.platform !== "darwin"){
                       __Agent.Electron.quit();
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

        Rect: function(X, Y, W, H){
            this.Components.push([0, X, Y, W, H, this.Color])
        },

        Update: function(){
            try{
                __Agent.HUD.webContents.send("render", this.Components);

                this.Components = [];

                return true;
            }catch(e){
                console.warn(e);
                return false;
            }
        }
    }
}