require("./lib/core")

// ----------------------------------------------------------------------

let BrowserRect;

async function ReadAddress(){
    let Rect = Agent.Other.Relative(
        BrowserRect,
        {X: 0, Y: 0, W: 1920, H: 1040},
        {X: 338, Y: 25, W: 938, H: 26},
        { ScaleH: false }
    );
    return await Agent.Vision.OCR.Read(Rect.X, Rect.Y, Rect.W, Rect.H);
}

function CheckPlugin(){
    let Point = Agent.Other.Relative(
        BrowserRect,
        {X: 0, Y: 0, W: 1920, H: 1040},
        {X: 1888, Y: 138}
    );
    let Color = Agent.Vision.Pixel(Point.X, Point.Y);
    return Agent.Other.CompareColors(Color, { R: 156, G: 13, B: 5 }) > 0.9;
}

async function CheckCorrectPage(){
    return (await ReadAddress()).includes("github.com") && CheckPlugin();
}

// ----------------------------------------------------------------------

try{
    console.log("Запуск...")

    let AllProcesses = Agent.Programs.GetAllCSV();

    // Браузер цель
    let BrowserPID = Agent.Programs.GetBrowser();

    console.log("Инициализация рендера")

    Agent.Vision.ShowDebug = true;

    Agent.Render.Init(() => {
        async function InitBrowser(){
            console.log(`Привязка к браузеру: ${BrowserPID}, "${Agent.Programs.Window.GetTitle(BrowserPID)}"`)

            let CurrentStage = -1;

            const Stage_CheckPage = 0;

            async function InitCycle(){
                try{
                    CurrentStage = Stage_CheckPage;

                    let Counter = 0;

                    let Work = true;
                    while(Work){
                        Counter++;

                        if(Agent.Keyboard.IsDown(0x1B /* ESC */)){
                            Work = false;
                        }

                        if(!Agent.Programs.Window.Exists(BrowserPID)){
                            console.warn("Браузер был потерян!");
                            break;
                        }

                        BrowserRect = Agent.Programs.Window.GetRect(BrowserPID);

                        Agent.Render.Color = "red";

                        Agent.Render.Frame(BrowserRect.X, BrowserRect.Y, BrowserRect.W, BrowserRect.H);

                        switch(CurrentStage){
                            case Stage_CheckPage: {
                                if(await CheckCorrectPage()){
                                    console.log("😋")
                                }else{
                                    console.warn("Откройте страницу с репозиторием! И включите плагин!");
                                }

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
            console.log("Завершение...");

            Agent.Render.Destroy();
        });
    });
}catch(e){
    console.error(e)
    throw e;
}