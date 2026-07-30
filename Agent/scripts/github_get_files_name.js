require("./lib/core")

try{
    console.log("Запуск...")

    let AllProcesses = Agent.Programs.GetAllCSV();

    // Браузер цель
    let BrowserPID = -1;

    for(let i = 0; i < AllProcesses.length; i++){
        const Process = AllProcesses[i];

        if(Process.Name.toLowerCase() === "firefox.exe"){
            if(!Process.CommandLine.includes("-contentproc") && !Process.CommandLine.includes("crashhelper")){
                BrowserPID = parseInt(Process.ProcessId);
            }
        }
    }

    if(BrowserPID === -1){
        throw new Error("Браузер не найден!");
    }

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

                    console.log(Agent.Programs.Window.GetRect(BrowserPID));

                    await new Promise(R => setTimeout(R, 100));
                }
            }catch(e){
                throw new Error(`Ошибка в цикле: ${e.message}`)
            }
        }
        await InitCycle();
    }
    InitBrowser().then(function(){
        console.log("Завершение...");
    });
}catch(e){
    console.error(e)
    throw e;
}