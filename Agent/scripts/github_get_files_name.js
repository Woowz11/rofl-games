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
                    while(true){
                        Counter++;

                        if(!Agent.Programs.Window.Exists(BrowserPID)){
                            console.warn("Браузер был потерян!");
                            break;
                        }

                        let BrowserRect = Agent.Programs.Window.GetRect(BrowserPID);
                        console.log(BrowserRect);

                        Agent.Render.Rect(BrowserRect.X, BrowserRect.Y, BrowserRect.W, BrowserRect.H);

                        console.log(Agent.Render.Components);
                        Agent.Render.Update();

                        await new Promise(R => setTimeout(R, 10));
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