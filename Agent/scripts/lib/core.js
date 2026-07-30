const __Agent = {}

let { Mouse } = require("mechatron");
__Agent.M_Mouse = Mouse;
Mouse = undefined;

let { execSync } = require("child_process");
__Agent.Exec = execSync;
execSync = undefined;

const __ExecParam = {
    encoding: "utf8",
    stdio: "pipe"
};

__Agent.Powershell = function(Command){
    try{
        return __Agent.Exec(`powershell -EncodedCommand "${Buffer.from(Command, "utf16le").toString("base64")}"`, __ExecParam);
    }catch(e){
        console.warn("Powershell ошибка:", e.message);
        return "";
    }
}

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
            __Agent.M_Mouse.setPos(X, Y);
        },
        GetPosition: function(){
            let Point = __Agent.M_Mouse.getPos();
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
                    console.warn('GetRect error:', e.message);
                    return null;
                }
            }
        }
    }
}