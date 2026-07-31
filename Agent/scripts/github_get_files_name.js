const { app, BrowserWindow } = require('electron/main')
const path = require('node:path')

function createWindow () {
    const win = new BrowserWindow({
        width: 800,
        height: 600,
        transparent: true,
        frame: false,
        backgroundColor: '#00000000',
        alwaysOnTop: true,
        skipTaskbar: true,
        webPreferences: {

        }
    })

    win.setIgnoreMouseEvents(true);

    const htmlString = `
    <!DOCTYPE html>
    <html>
      <head>
        <meta charset="UTF-8">
        <title>Динамическая страница</title>
      </head>
      <body>
        <h1>Привет, мир!</h1>
        <p>Эта страница была создана из строки.</p>
        <button onclick="alert('Привет!')">Нажми меня</button>
      </body>
    </html>
  `;

    // Загружаем строку через Data URL
    win.loadURL(`data:text/html;charset=utf-8,${encodeURIComponent(htmlString)}`);
}

app.whenReady().then(() => {
    createWindow()

    app.on('activate', () => {
        if (BrowserWindow.getAllWindows().length === 0) {
            createWindow()
        }
    })
})

app.on('window-all-closed', () => {
    if (process.platform !== 'darwin') {
        app.quit()
    }
})

/*
return

require("./lib/core")

try{
    console.log("Запуск...")

    let AllProcesses = Agent.Programs.GetAllCSV();

    // Браузер цель
    let BrowserPID = Agent.Programs.GetBrowser();

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
 */