require("./lib/core")

// ----------------------------------------------------------------------

const SiteScale = 1.5;

const IconColor_FolderOpen = { R: 255, G: 0, B: 255 };
const IconColor_Folder     = { R: 255, G: 255, B: 0 };
const IconColor_File       = { R: 255, G: 0, B: 0 };
const IconColor_Loading    = { R: 0, G: 255, B: 255 };

async function Click(X, Y){
    await Agent.Mouse.Move(X, Y, 1);
    await Agent.Mouse.Click();
}

let BrowserRect;

let ReferenceRect = { X: 0, Y: 0, W: 1920, H: 1040 };

async function ReadAddress(){
    let Rect = Agent.Other.Relative(
        BrowserRect,
        ReferenceRect,
        {X: 435, Y: 25, W: 797, H: 28},
        { ScaleH: false }
    );
    return await Agent.Vision.OCR.Read(Rect.X, Rect.Y, Rect.W, Rect.H, {
        Invert: true
    });
}

function CheckPlugin(){
    let Point = Agent.Other.Relative(
        BrowserRect,
        ReferenceRect,
        {X: 1888, Y: 138}
    );
    let Color = Agent.Vision.Pixel(Point.X, Point.Y);
    return Agent.Other.CompareColors(Color, { R: 156, G: 13, B: 5 }) > 0.9;
}

async function CheckCorrectPage(){
    return (await ReadAddress()).includes("github.com") && CheckPlugin();
}

async function WaitColor(X, Y, Color){
    while(!(Agent.Other.CompareColors(Agent.Vision.Pixel(X, Y), Color) > 0.9)){
        await Agent.Other.Wait(0.1);
    }
}

async function ReloadPage(){
    // Обновляем страницу...
    let Point = Agent.Other.Relative(
        BrowserRect,
        ReferenceRect,
        { X: 126, Y: 38 }
    );
    await Click(Point.X, Point.Y);

    await Agent.Other.Wait(0.1);

    await WaitColor(Point.X, Point.Y, { R: 251, G: 251, B: 254 });

    await Agent.Other.Wait(1);

    Point = Agent.Other.Relative(
        BrowserRect,
        ReferenceRect,
        { X: 71, Y: 147 }
    );
    await WaitColor(Point.X, Point.Y, IconColor_FolderOpen);
    console.log("Страница перезагружена!");
}

// ----------------------------------------------------------------------

let CurrentFolder = "";

let ParsedData = [];

async function StartParse(){
    await ReloadPage();

    // Закрываем 1-ю папку
    let Point = Agent.Other.Relative(
        BrowserRect,
        ReferenceRect,
        { X: 36, Y: 147 }
    );
    await Click(Point.X, Point.Y);
}

function WhatType(Color){
    if(Agent.Other.CompareColors(Color, IconColor_File      ) > 0.9){ return 1; }
    if(Agent.Other.CompareColors(Color, IconColor_Folder    ) > 0.9){ return 2; }
    if(Agent.Other.CompareColors(Color, IconColor_FolderOpen) > 0.9){ return 3; }
    if(Agent.Other.CompareColors(Color, IconColor_Loading   ) > 0.9){ return 4; }

    return -1;
}

// Размер иконки
const IS = 16 * SiteScale;
// Половина размера иконки
const HIS = IS/2;

let FindFile = {
    Type: 0,
    X: 0,
    Y: 0
}

let LastY = 0;
async function Parse(){
    const Top = 124;

    let X = 0;
    let Y = Top + HIS + LastY;

    function Align(){
        while(true){
            let Point = Agent.Other.Relative(BrowserRect, ReferenceRect, { X: FindFile.X, Y: FindFile.Y - 1 });
            let Color = Agent.Vision.Pixel(Point.X, Point.Y);
            if(WhatType(Color) === FindFile.Type){
                FindFile.Y--;
            }else{
                break;
            }
        }

        while(true){
            let Point = Agent.Other.Relative(BrowserRect, ReferenceRect, { X: FindFile.X - 1, Y: FindFile.Y });
            let Color = Agent.Vision.Pixel(Point.X, Point.Y);
            if(WhatType(Color) === FindFile.Type){
                FindFile.X--;
            }else{
                break;
            }
        }

        FindFile.X += HIS;
        FindFile.Y += HIS;
    }

    while(true){
        let Point = Agent.Other.Relative(
            BrowserRect,
            ReferenceRect,
            { X: X, Y: Y }
        );

        let Color = Agent.Vision.Pixel(Point.X, Point.Y);
        let Type = WhatType(Color);

        if(Type !== -1){
            if(Type === 4){
                X = 0;
            }else{
                FindFile.Type = Type;
                FindFile.Y = Y;
                FindFile.X = X;

                LastY = Y - (Top + HIS) + IS*2;
                console.log("FIND", FindFile)

                Align();

                break;
            }
        }

        X += 4;

        if(X >= 300){
            X = 0;
            Y += HIS;
        }
    }
}

async function CheckFile(){
    let Type = FindFile.Type;

    async function ReadName(){
        let Point = Agent.Other.Relative(
            BrowserRect,
            ReferenceRect,
            { X: FindFile.X + HIS, Y: FindFile.Y - HIS }
        );

        let Name = await Agent.Vision.OCR.Read(Point.X, Point.Y - 2, 500, IS + 4);
    }

    await ReadName();

    if(Type === 1){

    }else{
        let Opened = Type === 3;

        if(!Opened){
            let Point = Agent.Other.Relative(
                BrowserRect,
                ReferenceRect,
                { X: FindFile.X - HIS - 16 * SiteScale, Y: FindFile.Y }
            );

            await Click(Point.X, Point.Y);
        }
    }
}

// ----------------------------------------------------------------------

function Exit(){
    console.log("Завершение!");
    Agent.Render.Destroy();
}

try{
    console.log("Запуск...")

    // Браузер цель
    let BrowserPID = Agent.Programs.GetBrowser();

    console.log("Инициализация рендера")

    Agent.Vision.ShowDebug = true;

    Agent.Render.Init(() => {
        async function InitBrowser(){
            console.log(`Привязка к браузеру: ${BrowserPID}, "${Agent.Programs.Window.GetTitle(BrowserPID)}"`)

            Agent.Keyboard.Event_Down(KeyCode => {
                if(KeyCode === 27){
                    Exit();
                }
            });

            let CurrentStage = -1;

            const Stage_CheckPage = 0;
            const Stage_StartParse = 1;
            const Stage_Parse = 2;
            const Stage_CheckFile = 3;

            async function InitCycle(){
                try{
                    CurrentStage = Stage_CheckPage;

                    let Counter = 0;

                    let Work = true;
                    while(Work){
                        Counter++;

                        if(!Agent.Programs.Window.Exists(BrowserPID)){
                            console.warn("Браузер был потерян!");
                            break;
                        }

                        BrowserRect = Agent.Programs.Window.GetRect(BrowserPID);

                        Agent.Render.Color = "lime";

                        Agent.Render.Frame(BrowserRect.X, BrowserRect.Y, BrowserRect.W, BrowserRect.H);

                        switch(CurrentStage){
                            case Stage_CheckPage: {
                                if(await CheckCorrectPage()){
                                    console.log("Открыта нужная страница и включен плагин!")
                                    CurrentStage = Stage_StartParse;
                                }else{
                                    console.warn("Откройте страницу с репозиторием! И включите плагин!");
                                }

                                break;
                            }
                            case Stage_StartParse: {
                                await StartParse();
                                CurrentStage = Stage_Parse;
                                break;
                            }
                            case Stage_Parse: {
                                await Parse();
                                CurrentStage = Stage_CheckFile;
                                break;
                            }
                            case Stage_CheckFile: {
                                await CheckFile();
                                CurrentStage = Stage_Parse;
                                break;
                            }
                        }

                        Agent.Render.Update();
                        await Agent.Other.Wait(0.05);
                    }
                }catch(e){
                    throw new Error(`Ошибка в цикле: ${e.message}`)
                }
            }
            await InitCycle();
        }
        InitBrowser().then(function(){
            console.log("Цикл остановлен...");

            Exit();
        });
    });
}catch(e){
    console.error(e)
    throw e;
}