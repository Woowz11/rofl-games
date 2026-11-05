namespace LagSwitch;

public static class Page{
    // language=HTML
    public const string HTML = """
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title>LagSwitch</title>

<style>
*{
    box-sizing: border-box;
    margin: 0;
    padding: 0;
}

html, body{
    height: 100%;
    background: #121212;
    color: #e0e0e0;
    font-family: "Segoe UI", sans-serif;
    overflow: hidden;
    user-select: none;
}

body{
    display: flex;
    align-items: center;
    justify-content: center;
}

.Container{
    display: flex;
    flex-direction: column;
    align-items: center;
    gap: 14px;
}

.Title{
    font-size: 18px;
    color: #ffffff;
}

.Toggle{
    position: relative;
    width: 160px;
    height: 80px;
    padding: 0;
    border: none;
    border-radius: 40px;
    background: #2a2a2a;
    cursor: pointer;
    transition: background 0.25s ease, box-shadow 0.25s ease;
}

.Toggle::after{
    content: "";
    position: absolute;
    top: 8px;
    left: 8px;
    width: 64px;
    height: 64px;
    border-radius: 50%;
    background: #6a6a6a;
    transition: transform 0.25s ease, background 0.25s ease;
}

.Toggle.On{
    background: #1b5e20;
    box-shadow: 0 0 24px rgba(76, 175, 80, 0.35);
}

.Toggle.On::after{
    transform: translateX(80px);
    background: #4caf50;
}

@keyframes HitPulse{
    0%   { box-shadow: 0 0 0 0 rgba(79, 195, 247, 0.9); }
    100% { box-shadow: 0 0 0 18px rgba(79, 195, 247, 0); }
}

.Toggle.Flash{
    animation: HitPulse 0.6s ease-out;
}

.State{
    font-size: 18px;
    font-weight: 600;
    letter-spacing: 1px;
    color: #7a7a7a;
    transition: color 0.25s ease;
}

.State.On{
    color: #4caf50;
}

.BindButton{
    background: #262626;
    color: #e0e0e0;
    border: 1px solid #333333;
    border-radius: 6px;
    padding: 6px 14px;
    font-size: 12px;
    min-width: 190px;
    cursor: pointer;
    transition: border-color 0.2s ease, color 0.2s ease;
}

.BindButton:hover{
    background: #303030;
}

.BindButton.Capturing{
    border-color: #ff8800;
    color: #ffb74d;
}

.HotkeyInfo{
    font-size: 11px;
    color: #7a7a7a;
    text-align: center;
    min-height: 14px;
}

.HotkeyInfo.Hit{
    color: #4fc3f7;
}

@keyframes BusyPulse{
    from{ opacity: 0.35; }
    to  { opacity: 1; }
}

.Toggle.Busy{
    background: #6b5200;
    box-shadow: 0 0 24px rgba(255, 193, 7, 0.45);
    cursor: wait;
}

.Toggle.Busy::after{
    transform: translateX(40px);
    background: #ffc107;
    animation: BusyPulse 0.6s ease-in-out infinite alternate;
}

.State.Busy{
    color: #ffc107;
}
</style>

</head>

<body>

<div class="Container">
    <div class="Title">LagSwitch</div>
    <button id="Toggle" class="Toggle" role="switch" aria-checked="false"></button>
    <div id="State" class="State">ВЫКЛ</div>
    <button id="Bind" class="BindButton">Клавиша: …</button>
    <div id="HotkeyInfo" class="HotkeyInfo"></div>
</div>

<script>
const ToggleButton = document.getElementById("Toggle");
const StateElement = document.getElementById("State");
const BindButton   = document.getElementById("Bind");
const InfoElement  = document.getElementById("HotkeyInfo");

let Enabled     = false;
let Busy        = false;
let Capturing   = false;
let KeyName     = null;
let LastHitTime = null;

function Render(){
    ToggleButton.classList.toggle("On", Enabled);
    ToggleButton.classList.toggle("Busy", Busy);
    ToggleButton.disabled = Busy;
    ToggleButton.setAttribute("aria-checked", String(Enabled));
    ToggleButton.setAttribute("aria-busy", String(Busy));

    StateElement.classList.toggle("On", Enabled && !Busy);
    StateElement.classList.toggle("Busy", Busy);

    if(Busy){
        StateElement.textContent = Enabled ? "ВКЛЮЧАЮ ИНТЕРНЕТ…" : "ОТКЛЮЧАЮ ИНТЕРНЕТ…";
    }else{
        StateElement.textContent = Enabled ? "ВКЛ" : "ВЫКЛ";
    }

    BindButton.classList.toggle("Capturing", Capturing);
    BindButton.textContent = Capturing
        ? "Нажмите клавишу…"
        : `Клавиша: ${KeyName ?? "не назначена"}`;

    if(Busy){
        InfoElement.textContent = "Подождите, идёт переключение…";
        InfoElement.classList.remove("Hit");
    }else if(Capturing){
        InfoElement.textContent = "Esc — убрать клавишу, клик — отмена";
        InfoElement.classList.remove("Hit");
    }else if(LastHitTime){
        InfoElement.textContent = `⚡ Горячая клавиша сработала в ${LastHitTime}`;
        InfoElement.classList.add("Hit");
    }else if(KeyName){
        InfoElement.textContent = "Работает из любого окна";
        InfoElement.classList.remove("Hit");
    }else{
        InfoElement.textContent = "Назначьте клавишу для быстрого переключения";
        InfoElement.classList.remove("Hit");
    }
}

function FlashToggle(){
    ToggleButton.classList.remove("Flash");
    void ToggleButton.offsetWidth; // перезапуск анимации
    ToggleButton.classList.add("Flash");
}

function ApplyState(Data){
    Enabled   = Data.Enabled;
    Busy      = Data.Busy;
    Capturing = Data.Capturing;
    KeyName   = Data.KeyName;

    // Вспышку показываем только когда переключение завершено.
    if(Data.Source === "Hotkey" && !Busy){
        LastHitTime = Data.Time;
        FlashToggle();
    }

    Render();
}

function SendToHost(Type, Data){
    if(!window.chrome?.webview){
        console.warn(`Нет хоста WebView2, сообщение [${Type}] не отправлено.`);
        return;
    }
    window.chrome.webview.postMessage(JSON.stringify({ Type, Data }));
}

ToggleButton.onclick = () => {
    ToggleButton.blur(); // иначе Space/Enter после бинда дадут второй клик
    if(Busy){ return; }
    SendToHost("Toggle");
};

BindButton.onclick = () => {
    BindButton.blur();
    SendToHost(Capturing ? "CancelCapture" : "StartCapture");
};

if(window.chrome?.webview){
    window.chrome.webview.addEventListener("message", Event => {
        let Message = Event.data;

        if(typeof Message === "string"){
            try{
                Message = JSON.parse(Message);
            }catch(e){
                console.error("Не удалось распарсить сообщение от C#:", Event.data, e);
                return;
            }
        }

        if(Message?.Type === "State"){
            ApplyState(Message.Data);
        }
    });

    SendToHost("Ready");
}

Render();
</script>

</body>
</html>
""";
}