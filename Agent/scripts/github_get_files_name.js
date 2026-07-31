require("./lib/core")

try{
    console.log("Запуск...")

    let AllProcesses = Agent.Programs.GetAllCSV();

    // Браузер цель
    let BrowserPID = Agent.Programs.GetBrowser();

    console.log("Инициализация рендера")

    Agent.Render.Init(() => {
        async function InitBrowser(){
            console.log(`Привязка к браузеру: ${BrowserPID}, "${Agent.Programs.Window.GetTitle(BrowserPID)}"`)

            async function InitCycle(){
                try{
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

                        let BrowserRect = Agent.Programs.Window.GetRect(BrowserPID);

                        Agent.Render.Color = "red";

                        Agent.Render.Frame(BrowserRect.X, BrowserRect.Y, BrowserRect.W, BrowserRect.H);

                        let MousePosition = Agent.Mouse.GetPosition();
                        let PIXEL = Agent.Vision.Pixel(MousePosition.X, MousePosition.Y);

                        Agent.Render.Color = PIXEL.HEX;
                        Agent.Render.Rect(0, 0, 200, 200);

                        Agent.Render.Update();

                        await new Promise(R => setTimeout(R, 50));
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