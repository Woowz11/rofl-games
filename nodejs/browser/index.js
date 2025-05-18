const { app, BrowserWindow, Menu, ipcMain } = require("electron");
const path = require("path");

let W;

app.on("ready", () => {
	W = new BrowserWindow({
		width: 1600,
		height: 900,
		frame: false,
		title: "Browser - Loading...",
		icon : path.join(__dirname, "icon.ico"),
		resizable: true,
		webPreferences: {
			nodeIntegration: true,
			contextIsolation: false,
			webSecurity: false,
			allowRunningInsecureContent: true,
			webviewTag: true
		}
	});

	Menu.setApplicationMenu(null);

	W.loadFile("main.html");
});

//==========================

ipcMain.on("B_Minimize", () => {
	W.minimize();
});

ipcMain.on("B_Maximize", () => {
	if(W.isMaximized()){
		W.unmaximize();
	}else{
		W.maximize();
	}
});

ipcMain.on("B_Quit", () => {
	W.close();
});

//==========================

app.on("window-all-closed", () => {
	if (process.platform !== "darwin"){
		app.quit();
	}
});

app.on("activate", () => {
	if (W === null){
		createWindow();
	}
});