const { app, BrowserWindow } = require("electron");

let W;

app.on("ready", () => {
	W = new BrowserWindow({
		width: 800,
		height: 600,
		webPreferences: {
			nodeIntegration: true,
			contextIsolation: false,
			webSecurity: false,
			allowRunningInsecureContent: true,
			webviewTag: true
		}
	});

	W.loadFile("main.html");
});
