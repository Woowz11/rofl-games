using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using WLO;
using WLO.Math;

namespace YouTubePublish;

public static class Bootstrap{
    private static WLO.Web.WebView2 Web     = null!;
    private static YouTube          YouTube = null!;
    private static WLO.Window.GLFW  Window  = null!;

    private static readonly ConcurrentQueue<string> __PendingMessages = [];

    private static int __TotalVideos     = 0;
    private static int __PublishedVideos = 0;

    private static volatile string? __PendingTitle = null;

    private static volatile int __CurrentAutoPublishRunNumber = 0;

    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            Web = WLO.Web.WebView2.Create();

            Window = new WLO.Window.GLFW(new Vector2I(1280, 720), "YouTube Publisher", Web);
            Window.TODO_UseDarkMode();

            Web.OnMessage += __OnWebMessage;

            /* language=js */
            const string JS = """
const ListElement  = document.getElementById("VideoList");
const EmptyElement = document.getElementById("Empty");
const CountElement = document.getElementById("VideoCount");
const ErrorOverlay = document.getElementById("ErrorOverlay");
const ErrorTitle   = document.getElementById("ErrorTitle");
const ErrorDetails = document.getElementById("ErrorDetails");

const AutoPublishButton = document.getElementById("Btn-AutoPublish");
const ConcurrencyInput  = document.getElementById("Input-Concurrency");
const ChunkSizeInput    = document.getElementById("Input-ChunkSize");

const CardsByName  = new Map();
const FailedByName = new Map();

const PublishingNames = new Set();
const ProgressByName  = new Map();

let LastVideos    = [];
let SortKey       = "Name";
let SortAscending = true;

// ----------------------------------------------------------------------

function ShowError(Title, Details){
    ErrorTitle.textContent = Title;
    ErrorDetails.textContent = Details ?? "";
    ErrorOverlay.style.display = "flex";
}

document.getElementById("ErrorClose").onclick = () => {
    ErrorOverlay.style.display = "none";
};

window.onerror = (Message, Source, Line, Column, Error) => {
    ShowError("Необработанная ошибка JavaScript", `${Message}\n${Source}:${Line}:${Column}\n${Error?.stack ?? ""}`);
};

window.addEventListener("unhandledrejection", Event => {
    ShowError("Необработанная ошибка Promise", String(Event.reason?.stack ?? Event.reason));
});

function EscapeHtml(Text){
    const Div = document.createElement("div");
    Div.textContent = Text ?? "";
    return Div.innerHTML;
}

// ----------------------------------------------------------------------

function FormatSize(Bytes){
    if(Bytes < 1024){ return Bytes + " B"; }
    if(Bytes < 1024 * 1024){ return (Bytes / 1024).toFixed(1) + " KB"; }
    if(Bytes < 1024 * 1024 * 1024){ return (Bytes / (1024 * 1024)).toFixed(1) + " MB"; }
    return (Bytes / (1024 * 1024 * 1024)).toFixed(2) + " GB";
}

// ----------------------------------------------------------------------

function SortVideos(Videos){
    const Sorted = [...Videos].sort((A, B) => {
        let Result = 0;

        if(SortKey === "Name"){
            Result = A.Name.localeCompare(B.Name, undefined, { numeric: true, sensitivity: "base" });
        }else if(SortKey === "SizeBytes"){
            Result = A.SizeBytes - B.SizeBytes;
        }else if(SortKey === "SortDate"){
            Result = A.SortDate - B.SortDate;
        }else if(SortKey === "IsPublished"){
            Result = (A.IsPublished === B.IsPublished) ? 0 : (A.IsPublished ? 1 : -1);
        }

        return SortAscending ? Result : -Result;
    });

    return Sorted;
}

function UpdateSortButtonsUI(){
    const Buttons = {
        "Name":        document.getElementById("Btn-SortName"),
        "SizeBytes":   document.getElementById("Btn-SortSize"),
        "SortDate":    document.getElementById("Btn-SortDate"),
        "IsPublished": document.getElementById("Btn-SortStatus")
    };

    for(const Key in Buttons){
        const Btn = Buttons[Key];
        if(!Btn){ continue; }

        Btn.classList.remove("Active");
        Btn.textContent = Btn.dataset.label;
    }

    const ActiveBtn = Buttons[SortKey];
    if(ActiveBtn){
        ActiveBtn.classList.add("Active");
        ActiveBtn.textContent = `${ActiveBtn.dataset.label} ${SortAscending ? "↑" : "↓"}`;
    }
}

function SetSort(Key){
    if(SortKey === Key){
        SortAscending = !SortAscending;
    }else{
        SortKey = Key;
        SortAscending = true;
    }

    UpdateSortButtonsUI();
    RenderVideos(SortVideos(LastVideos));
}

document.getElementById("Btn-SortName").onclick   = () => SetSort("Name");
document.getElementById("Btn-SortSize").onclick   = () => SetSort("SizeBytes");
document.getElementById("Btn-SortDate").onclick   = () => SetSort("SortDate");
document.getElementById("Btn-SortStatus").onclick = () => SetSort("IsPublished");

UpdateSortButtonsUI();

// ----------------------------------------------------------------------

function GetChunkSizeMB(){
    let Value = parseInt(ChunkSizeInput.value, 10);
    if(!Number.isFinite(Value) || Value < 1){ Value = 1; }
    if(Value > 512){ Value = 512; }
    ChunkSizeInput.value = Value;
    return Value;
}

// ----------------------------------------------------------------------

function StartPublishUI(Video){
    if(PublishingNames.has(Video.Name)){ return; }

    const IsAutoPublish = AutoPublishInFlight.has(Video.Name);

    FailedByName.delete(Video.Name);
    PublishingNames.add(Video.Name);
    ProgressByName.set(Video.Name, { BytesSent: 0, TotalBytes: Video.SizeBytes, Percent: 0 });

    RenderVideos(SortVideos(LastVideos));

    SendToHost("PublishRequest", { Name: Video.Name, IsAutoPublish, ChunkSizeMB: GetChunkSizeMB() });
}

// ----------------------------------------------------------------------

function RenderVideos(Videos){
    ListElement.innerHTML = "";
    CardsByName.clear();
    CountElement.textContent = Videos ? Videos.length : 0;

    if(!Videos || Videos.length === 0){
        EmptyElement.style.display = "flex";
        return;
    }

    EmptyElement.style.display = "none";

    const MaxWeightBytes = 10 * 1024 * 1024 * 1024;

    function GetWeightColor(Bytes){
        const Ratio = Math.min(Bytes / MaxWeightBytes, 1);
        const Hue = 120 - (120 * Ratio);
        return `hsla(${Hue}, 70%, 45%, 0.35)`;
    }

    for(const Video of Videos){
        const IsFailed     = FailedByName.has(Video.Name);
        const IsPublishing = PublishingNames.has(Video.Name);

        const Card = document.createElement("div");
        Card.className = "VideoCard"
            + (Video.IsPublished ? " Published" : "")
            + (IsFailed ? " Failed" : "")
            + (IsPublishing ? " Publishing" : "");
        Card.dataset.name = Video.Name;
        Card.style.background = `linear-gradient(90deg, ${GetWeightColor(Video.SizeBytes)}, #1e1e1e 70%)`;

        const Top = document.createElement("div");
        Top.className = "VideoCardTop";

        const Info = document.createElement("div");
        Info.className = "VideoInfo";

        const Name = document.createElement("div");
        Name.className = "VideoName";
        Name.textContent = Video.Name;
        Name.title = Video.Name;

        const Meta = document.createElement("div");
        Meta.className = "VideoMeta";
        Meta.innerHTML = `
            <span>📦 ${FormatSize(Video.SizeBytes)}</span>
            <span>${Video.HasDate ? "📅" : "❓"} ${Video.Date ?? "Дата неизвестна"}</span>
            ${Video.IsPublished ? `<span>✅ Опубликовано${Video.MyID ? ` (ID: ${Video.MyID})` : ""}</span>` : ""}
            ${IsFailed ? "<span class=\"FailedChip\">❌ Ошибка публикации</span>" : ""}
        `;

        Info.appendChild(Name);
        Info.appendChild(Meta);

        const Button = document.createElement("button");
        Button.className = "PublishButton";

        if(Video.IsPublished){
            Button.textContent = "Опубликовано";
            Button.disabled = true;
        }else if(IsPublishing){
            Button.textContent = "Публикация...";
            Button.disabled = true;
        }else{
            Button.textContent = IsFailed ? "⚠ Повторить попытку" : "Опубликовать";
            Button.onclick = () => StartPublishUI(Video);
        }

        Top.appendChild(Info);
        Top.appendChild(Button);
        Card.appendChild(Top);

        if(IsPublishing){
            const LastProgress = ProgressByName.get(Video.Name) ?? { BytesSent: 0, TotalBytes: Video.SizeBytes, Percent: 0 };
            const Remaining = Math.max(LastProgress.TotalBytes - LastProgress.BytesSent, 0);
            const Percent   = Math.round(LastProgress.Percent);

            const Progress = document.createElement("div");
            Progress.className = "ProgressWrapper";
            Progress.innerHTML = `
                <div class="ProgressBar"><div class="ProgressBarFill" style="width:${Percent}%"></div></div>
                <div class="ProgressText">${Percent}% — ${FormatSize(Remaining)} осталось</div>
            `;
            Card.appendChild(Progress);
        }

        if(IsFailed){
            const Banner = document.createElement("div");
            Banner.className = "ErrorBanner";
            Banner.innerHTML = `<span class="ErrorBannerIcon">⚠️</span><span>${EscapeHtml(FailedByName.get(Video.Name))}</span>`;
            Card.appendChild(Banner);
        }

        ListElement.appendChild(Card);
        CardsByName.set(Video.Name, Card);
    }
}

function UpdateProgress(Data){
    ProgressByName.set(Data.Name, Data);

    const Card = CardsByName.get(Data.Name);
    if(!Card){ return; }

    const Fill = Card.querySelector(".ProgressBarFill");
    const Text = Card.querySelector(".ProgressText");
    if(!Fill || !Text){ return; }

    const Remaining = Math.max(Data.TotalBytes - Data.BytesSent, 0);
    const Percent   = Math.round(Data.Percent);

    Fill.style.width = `${Percent}%`;
    Text.textContent = `${Percent}% — ${FormatSize(Remaining)} осталось`;
}

// ----------------------------------------------------------------------

const QuotaFailReasons = new Set(["QuotaExceeded", "UploadLimitExceeded"]);

let AutoPublishActive   = false;
let AutoPublishQueue    = [];
let AutoPublishInFlight = new Set();

function GetConcurrency(){
    let Value = parseInt(ConcurrencyInput.value, 10);
    if(!Number.isFinite(Value) || Value < 1){ Value = 1; }
    if(Value > 20){ Value = 20; }
    ConcurrencyInput.value = Value;
    return Value;
}

function UpdateAutoPublishButtonUI(){
    if(AutoPublishActive){
        AutoPublishButton.textContent = "⏹ Остановить автопубликацию";
        AutoPublishButton.classList.add("Active");
    }else{
        AutoPublishButton.textContent = "🚀 Автопубликация";
        AutoPublishButton.classList.remove("Active");
    }

    ConcurrencyInput.disabled = AutoPublishActive;
}

function StartAutoPublish(){
    const Candidates = SortVideos(LastVideos).filter(V => !V.IsPublished && !PublishingNames.has(V.Name));

    if(Candidates.length === 0){
        ShowError("Автопубликация", "Нет видео для публикации.");
        return;
    }

    SendToHost("AutoPublishStarted");

    AutoPublishQueue = Candidates.map(V => V.Name);
    AutoPublishInFlight.clear();
    AutoPublishActive = true;

    UpdateAutoPublishButtonUI();

    const Concurrency = GetConcurrency();
    for(let i = 0; i < Concurrency; i++){
        AutoPublishPumpNext();
    }
}

function StopAutoPublish(Reason){
    if(!AutoPublishActive){ return; }

    AutoPublishActive = false;
    AutoPublishQueue  = [];

    UpdateAutoPublishButtonUI();

    if(Reason){
        ShowError("Автопубликация остановлена", Reason);
    }
}

function AutoPublishPumpNext(){
    if(!AutoPublishActive){ return; }

    if(AutoPublishQueue.length === 0){
        if(AutoPublishInFlight.size === 0){
            StopAutoPublish(null);
        }
        return;
    }

    const Name = AutoPublishQueue.shift();
    const Video = LastVideos.find(V => V.Name === Name);

    if(!Video || Video.IsPublished || PublishingNames.has(Name)){
        AutoPublishPumpNext();
        return;
    }

    AutoPublishInFlight.add(Name);
    StartPublishUI(Video);
}

AutoPublishButton.onclick = () => {
    if(AutoPublishActive){
        StopAutoPublish("Остановлено пользователем.");
    }else{
        StartAutoPublish();
    }
};

// ----------------------------------------------------------------------

function MarkPublished(Name, MyID){
    PublishingNames.delete(Name);
    ProgressByName.delete(Name);

    const Video = LastVideos.find(V => V.Name === Name);
    if(Video){
        Video.IsPublished = true;
        if(MyID !== undefined && MyID !== null){
            Video.MyID = MyID;
        }
    }

    RenderVideos(SortVideos(LastVideos));

    if(AutoPublishInFlight.has(Name)){
        AutoPublishInFlight.delete(Name);
        AutoPublishPumpNext();
    }
}

function ResetCard(Name, Reason, FailReasonCode){
    PublishingNames.delete(Name);
    ProgressByName.delete(Name);
    FailedByName.set(Name, Reason ?? "Неизвестная ошибка");

    RenderVideos(SortVideos(LastVideos));

    const WasAutoPublishing = AutoPublishInFlight.has(Name);
    if(WasAutoPublishing){
        AutoPublishInFlight.delete(Name);
    }

    const IsQuotaError = QuotaFailReasons.has(FailReasonCode);

    if(WasAutoPublishing && IsQuotaError){
        ShowError("Автопубликация остановлена: превышена квота YouTube API", `Видео: ${Name}\n${Reason ?? ""}`);
        StopAutoPublish(null);
    }else{
        ShowError("Не удалось опубликовать видео", Reason ?? "");

        if(WasAutoPublishing && AutoPublishActive){
            AutoPublishPumpNext();
        }
    }
}

document.getElementById("Btn-Refresh").onclick = () => {
    SendToHost("RefreshRequest");
};

document.getElementById("Btn-OpenQuota").onclick = () => {
    SendToHost("OpenQuotaPage");
};

function SendToHost(Type, Data){
    if(!window.chrome?.webview){
        console.warn(`Нет хоста WebView2, сообщение [${Type}] не отправлено.`);
        ShowError("Нет связи с приложением", `window.chrome.webview недоступен, сообщение [${Type}] не отправлено.`);
        return;
    }
    window.chrome.webview.postMessage(JSON.stringify({ Type, Data }));
}

if(window.chrome?.webview){
    window.chrome.webview.addEventListener("message", Event => {
        let Message = Event.data;

        if(typeof Message === "string"){
            try{
                Message = JSON.parse(Message);
            }catch(e){
                console.error("Не удалось распарсить сообщение от C#:", Event.data, e);
                ShowError("Не удалось распарсить сообщение от C#", e.message + "\n" + Event.data);
                return;
            }
        }

        if(!Message || typeof Message.Type !== "string"){
            console.error("Сообщение от C# не соответствует протоколу { Type, Data }:", Message);
            ShowError("Некорректный формат сообщения от C#", JSON.stringify(Message));
            return;
        }

        if(Message.Type === "VideoList"){
            LastVideos = Message.Data || [];
            RenderVideos(SortVideos(LastVideos));
        }else if(Message.Type === "PublishProgress"){
            UpdateProgress(Message.Data);
        }else if(Message.Type === "PublishComplete"){
            MarkPublished(Message.Data.Name, Message.Data.MyID);
        }else if(Message.Type === "PublishFailed"){
            ResetCard(Message.Data.Name, Message.Data.Reason, Message.Data.FailReasonCode);
        }else if(Message.Type === "Error"){
            ShowError(Message.Data?.Title ?? "Ошибка", Message.Data?.Details ?? "");
        }
    });

    SendToHost("Ready");
}else{
    console.warn("window.chrome.webview недоступен — страница запущена не внутри WebView2.");
    ShowError("Нет связи с приложением", "window.chrome.webview недоступен. Страница запущена не внутри WebView2.");
}
""";

            /* language=html */
            Web.NavigateCustom($$"""
<!DOCTYPE html>
<html lang="ru">
<head>
    <meta charset="UTF-8">
    <title>YouTube Publisher</title>

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
}

#App{
    display: flex;
    height: 100vh;
}

#Sidebar{
    width: 220px;
    flex-shrink: 0;
    background: #1a1a1a;
    border-right: 1px solid #2a2a2a;
    display: flex;
    flex-direction: column;
    padding: 16px 12px;
    gap: 8px;
}

.SidebarButton{
    display: flex;
    align-items: center;
    gap: 10px;
    background: transparent;
    border: none;
    color: #e0e0e0;
    font-size: 14px;
    padding: 10px 12px;
    border-radius: 6px;
    cursor: pointer;
    text-align: left;
}

.SidebarButton:hover{
    background: #262626;
}

.SidebarButton.Primary{
    background: #cc0000;
    color: #ffffff;
    font-weight: 600;
    margin-bottom: 4px;
}

.SidebarButton.Primary:hover{
    background: #e60000;
}

.SidebarButton.Primary.Active{
    background: #ff8800;
}

.SidebarButton.Primary.Active:hover{
    background: #ffa233;
}

.SidebarLabel{
    font-size: 11px;
    color: #6a6a6a;
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin: 8px 0 2px 4px;
}

.SortButton.Active{
    background: #262626;
    color: #4fc3f7;
    font-weight: 600;
}

.SidebarSeparator{
    height: 1px;
    background: #2a2a2a;
    margin: 6px 4px;
}

.SidebarBottom{
    margin-top: auto;
    display: flex;
    flex-direction: column;
    gap: 6px;
    padding-top: 10px;
    border-top: 1px solid #2a2a2a;
}

.SidebarInputLabel{
    font-size: 11px;
    color: #9a9a9a;
    margin: 2px 4px 0 4px;
}

.SidebarInput{
    background: #0f0f0f;
    border: 1px solid #333333;
    color: #e0e0e0;
    border-radius: 6px;
    padding: 8px 10px;
    font-size: 13px;
    width: 100%;
}

.SidebarInput:focus{
    outline: none;
    border-color: #4fc3f7;
}

.SidebarInput:disabled{
    opacity: 0.5;
}

.SidebarHint{
    font-size: 10px;
    color: #6a6a6a;
    margin: 0 4px 4px 4px;
    line-height: 1.4;
}

#Content{
    flex-grow: 1;
    display: flex;
    flex-direction: column;
    padding: 24px 28px;
    overflow: hidden;
}

#ContentHeader{
    display: flex;
    align-items: baseline;
    gap: 10px;
    margin-bottom: 16px;
    flex-shrink: 0;
}

#ContentHeader h1{
    font-size: 20px;
    color: #ffffff;
}

#VideoCount{
    font-size: 13px;
    color: #8a8a8a;
}

#VideoListWrapper{
    flex-grow: 1;
    overflow-y: auto;
    padding-right: 4px;
}

#VideoList{
    display: flex;
    flex-direction: column;
    gap: 10px;
}

.VideoCard{
    display: flex;
    flex-direction: column;
    gap: 8px;
    border: 1px solid #2a2a2a;
    border-radius: 8px;
    padding: 12px 16px;
    transition: border-color 0.2s ease, opacity 0.2s ease;
}

.VideoCard.Publishing{
    border-color: #2196f3;
}

.VideoCard.Published{
    opacity: 0.45;
}

.VideoCard.Failed{
    border-color: #cc0000;
    border-width: 2px;
    box-shadow: 0 0 0 1px rgba(204, 0, 0, 0.35);
    opacity: 1;
}

.VideoCardTop{
    display: flex;
    align-items: center;
    justify-content: space-between;
}

.VideoInfo{
    display: flex;
    flex-direction: column;
    gap: 4px;
    overflow: hidden;
}

.VideoName{
    font-size: 14px;
    font-weight: 600;
    color: #ffffff;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
    max-width: 600px;
}

.VideoMeta{
    font-size: 12px;
    color: #9a9a9a;
    display: flex;
    gap: 16px;
}

.FailedChip{
    color: #ff6666;
    font-weight: 600;
}

.ErrorBanner{
    display: flex;
    align-items: flex-start;
    gap: 8px;
    background: rgba(204, 0, 0, 0.12);
    border: 1px solid rgba(204, 0, 0, 0.45);
    border-radius: 6px;
    padding: 8px 10px;
    font-size: 12px;
    line-height: 1.4;
    color: #ffb3b3;
    white-space: pre-wrap;
    word-break: break-word;
}

.ErrorBannerIcon{
    flex-shrink: 0;
}

.PublishButton{
    background: #cc0000;
    color: #ffffff;
    border: none;
    border-radius: 6px;
    padding: 8px 16px;
    font-size: 13px;
    font-weight: 600;
    cursor: pointer;
    flex-shrink: 0;
}

.PublishButton:hover{
    background: #e60000;
}

.PublishButton:disabled{
    background: #5a5a5a;
    cursor: default;
}

.ProgressWrapper{
    display: flex;
    flex-direction: column;
    gap: 4px;
}

.ProgressBar{
    width: 100%;
    height: 6px;
    background: #2a2a2a;
    border-radius: 3px;
    overflow: hidden;
}

.ProgressBarFill{
    height: 100%;
    width: 0%;
    background: #2196f3;
    transition: width 0.2s ease;
}

.ProgressText{
    font-size: 11px;
    color: #9a9a9a;
}

#Empty{
    display: none;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    color: #7a7a7a;
    font-size: 13px;
    padding: 60px 20px;
    text-align: center;
    gap: 8px;
}

#VideoListWrapper::-webkit-scrollbar{
    width: 8px;
}

#VideoListWrapper::-webkit-scrollbar-thumb{
    background: #333333;
    border-radius: 4px;
}

#ErrorOverlay{
    display: none;
    position: fixed;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    background: rgba(0, 0, 0, 0.75);
    align-items: center;
    justify-content: center;
    z-index: 1000;
}

#ErrorBox{
    background: #2a1111;
    border: 1px solid #cc0000;
    border-radius: 8px;
    padding: 20px 24px;
    max-width: 600px;
    max-height: 70vh;
    display: flex;
    flex-direction: column;
    gap: 12px;
}

#ErrorTitle{
    color: #ff6666;
    font-size: 16px;
}

#ErrorDetails{
    color: #e0b0b0;
    font-size: 12px;
    white-space: pre-wrap;
    word-break: break-word;
    overflow-y: auto;
    max-height: 300px;
}

#ErrorBox button{
    align-self: flex-end;
    background: #cc0000;
    color: #fff;
    border: none;
    border-radius: 6px;
    padding: 6px 14px;
    cursor: pointer;
}

#ErrorBox button:hover{
    background: #e60000;
}
</style>

</head>

<body>

<div id="App">

    <div id="Sidebar">
        <button class="SidebarButton Primary" id="Btn-Refresh">🔄 Обновить список</button>
        <button class="SidebarButton" id="Btn-OpenQuota">📊 Открыть квоту API</button>

        <div class="SidebarSeparator"></div>

        <div class="SidebarLabel">Сортировка</div>
        <button class="SidebarButton SortButton" id="Btn-SortName" data-label="Имя">Имя</button>
        <button class="SidebarButton SortButton" id="Btn-SortSize" data-label="Размер">Размер</button>
        <button class="SidebarButton SortButton" id="Btn-SortDate" data-label="Дата">Дата</button>
        <button class="SidebarButton SortButton" id="Btn-SortStatus" data-label="Статус">Статус</button>

        <div class="SidebarBottom">
            <div class="SidebarLabel">Загрузка</div>

            <label class="SidebarInputLabel" for="Input-ChunkSize">Размер блока (МБ)</label>
            <input type="number" id="Input-ChunkSize" class="SidebarInput" min="1" max="512" value="16">
            <div class="SidebarHint">
                Чем больше блок — тем меньше запросов к YouTube и обычно выше скорость на быстром интернете.
                Но при обрыве соединения заново отправляется весь текущий блок. Для видео ~1 ГБ хорошо подходит
                8–32 МБ. Слишком маленький блок (1–2 МБ) может искусственно тормозить загрузку на быстром канале.
            </div>

            <div class="SidebarLabel">Автопубликация</div>
            <label class="SidebarInputLabel" for="Input-Concurrency">Одновременно видео</label>
            <input type="number" id="Input-Concurrency" class="SidebarInput" min="1" max="20" value="3">
            <button class="SidebarButton Primary" id="Btn-AutoPublish">🚀 Автопубликация</button>
        </div>
    </div>

    <div id="Content">
        <div id="ContentHeader">
            <h1>Найденные видео</h1>
            <span id="VideoCount">0</span>
        </div>

        <div id="VideoListWrapper">
            <div id="VideoList"></div>
            <div id="Empty">
                <span>📭</span>
                <span>Видео не найдены</span>
            </div>
        </div>
    </div>

</div>

<div id="ErrorOverlay">
    <div id="ErrorBox">
        <h2 id="ErrorTitle">Ошибка</h2>
        <pre id="ErrorDetails"></pre>
        <button id="ErrorClose">Закрыть</button>
    </div>
</div>

<script>
{{JS}}
</script>

</body>
</html>
""");

            // ----------------------------------------------------------------------

            __LoadConfig();

            YouTube = new YouTube(File.ReadAllText(@"W:\Other\rofl-games\YouTubePublish\client_secret.json"), @"W:\Other\rofl-games\YouTubePublish\UserLogin", "YoutubePublisher");

            // ----------------------------------------------------------------------

            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();

                while(__PendingMessages.TryDequeue(out string? Pending)){
                    Web.PostMessage(Pending);
                }

                string? TitleToApply = Interlocked.Exchange(ref __PendingTitle, null);
                if(TitleToApply != null){
                    Window.Title = TitleToApply;
                }
            }

            Window.Close();
        }catch(Exception e){
            WL.Logger.Fatal("Всё взорвалось...", e);
            return WL.Core.Stop(1);
        }

        return WL.Core.Stop(0);
    }

    // ----------------------------------------------------------------------

    private static void __UpdateWindowTitle(){
        int Remaining = Math.Max(__TotalVideos - __PublishedVideos, 0);
        __PendingTitle = $"YouTube Publisher [{__PublishedVideos}/{Remaining}]";
    }

    // ----------------------------------------------------------------------

    private static void __OnWebMessage(string Message){
        try{
            using JsonDocument Doc = JsonDocument.Parse(Message);
            string Type = Doc.RootElement.GetProperty("Type").GetString() ?? "";

            switch(Type){
                case "Ready":
                case "RefreshRequest":
                    __SendVideoList();
                    break;

                case "PublishRequest":{
                    JsonElement DataEl = Doc.RootElement.GetProperty("Data");
                    string Name = DataEl.GetProperty("Name").GetString() ?? "";
                    bool IsAutoPublish = DataEl.TryGetProperty("IsAutoPublish", out JsonElement IsAutoEl) && IsAutoEl.GetBoolean();
                    int ChunkSizeMB = DataEl.TryGetProperty("ChunkSizeMB", out JsonElement ChunkEl) ? ChunkEl.GetInt32() : 16;
                    __StartPublish(Name, IsAutoPublish, ChunkSizeMB);
                    break;
                }

                case "AutoPublishStarted":
                    __CurrentAutoPublishRunNumber = __IncrementAutoPublishCount();
                    break;

                case "OpenQuotaPage":
                    __OpenQuotaPage();
                    break;

                default:
                    WL.Logger.Warn($"Получено неизвестное сообщение от Web, Тип: {Type}");
                    break;
            }
        }catch(Exception e){
            __SendError($"Не удалось обработать сообщение от Web!\nСообщение: {Message}", e);
        }
    }

    private const string QuotaPageURL = "https://console.cloud.google.com/apis/api/youtube.googleapis.com/quotas?project=youtubepublish-512210";

    private static void __OpenQuotaPage(){
        try{
            Process.Start(new ProcessStartInfo{
                FileName = QuotaPageURL,
                UseShellExecute = true
            });
        }catch(Exception e){
            __SendError("Не удалось открыть страницу квоты в браузере!", e);
        }
    }

    private const string ConfigFilePath = @"W:\Woowz11\Videos\Записи\YT\!Config.json";

    private sealed class __AppConfig{
        public int LastID{ get; set; }
        public int AutoPublishCount{ get; set; }
    }

    private static          __AppConfig __Config     = null!;
    private static readonly object      __ConfigLock = new object();

    private static void __LoadConfig(){
        if(!File.Exists(ConfigFilePath)){
            throw new ExceptionWL($"Файл конфигурации не найден!\nПуть: {ConfigFilePath}");
        }

        string JSON = File.ReadAllText(ConfigFilePath);
        __Config = JsonSerializer.Deserialize<__AppConfig>(JSON)
                   ?? throw new ExceptionWL($"Не удалось распарсить файл конфигурации!\nПуть: {ConfigFilePath}");
    }

    private static void __SaveConfig(){
        string JSON = JsonSerializer.Serialize(__Config, new JsonSerializerOptions{ WriteIndented = true });
        File.WriteAllText(ConfigFilePath, JSON);
    }

    private static int __GetNextID(){
        lock(__ConfigLock){
            __Config.LastID++;
            __SaveConfig();
            return __Config.LastID;
        }
    }

    private static int __IncrementAutoPublishCount(){
        lock(__ConfigLock){
            __Config.AutoPublishCount++;
            __SaveConfig();
            return __Config.AutoPublishCount;
        }
    }

    private const string ResultFilePath = @"W:\Woowz11\Videos\Записи\YT\!Result.js";
    private static readonly object __ResultFileLock = new object();

    private static string __FormatDuration(TimeSpan Duration){
        return Duration.Hours > 0
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");
    }

    // ----------------------------------------------------------------------

    private static readonly Regex __ResultEntryIDPattern = new Regex(@"^\s*\[(?<id>\d+)\s*,", RegexOptions.Multiline);

    private static void __AppendResultEntry(int ID, string CreateDate, string VideoID, TimeSpan Duration, bool IsShorts){
        string PublishDate    = DateTime.Now.ToString("yyyy.MM.dd");
        string DurationString = __FormatDuration(Duration);
        string Tags           = IsShorts ? "T_Shorts, T_WIP" : "T_WIP";

        string Entry =
            $"\t[{ID}, \"~\", \"{CreateDate}\", \"{VideoID}\", \"{PublishDate}\", \"{DurationString}\", P_Hide,\n" +
            $"\t[{Tags}],\n" +
            "\t\"Опубликовано YouTubePublisher\"],";

        lock(__ResultFileLock){
            string Existing = File.Exists(ResultFilePath) ? File.ReadAllText(ResultFilePath) : "";

            List<(int ID, string Text)> Entries = __ParseResultEntries(Existing);
            Entries.Add((ID, Entry));
            Entries.Sort((A, B) => B.ID.CompareTo(A.ID));

            string NewContent = string.Join("\n\n", Entries.Select(E => E.Text)) + "\n\n";
            File.WriteAllText(ResultFilePath, NewContent);
        }
    }

    // ----------------------------------------------------------------------

    private static List<(int ID, string Text)> __ParseResultEntries(string Content){
        List<(int ID, string Text)> Result = [];

        if(string.IsNullOrWhiteSpace(Content)){ return Result; }

        string[] Blocks = Regex.Split(Content, @"\n\s*\n");

        foreach(string Block in Blocks){
            string Trimmed = Block.Trim('\n', '\r', ' ');
            if(string.IsNullOrWhiteSpace(Trimmed)){ continue; }

            Match M = __ResultEntryIDPattern.Match(Trimmed);
            if(!M.Success){ continue; }

            int ID = int.Parse(M.Groups["id"].Value);
            Result.Add((ID, Trimmed));
        }

        return Result;
    }

    // ----------------------------------------------------------------------

    private const           string   VideosFolder    = @"W:\Woowz11\Videos\Записи\YT";
    private const           string   PublishedFolder = @"W:\Woowz11\Videos\Записи\YT\Published";
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".webm"];

    private static void __SendVideoList(){
        List<VideoFileInfo> Videos = __ScanVideos();

        __TotalVideos     = Videos.Count;
        __PublishedVideos = Videos.Count(V => V.IsPublished);
        __UpdateWindowTitle();

        string JSON = JsonSerializer.Serialize(new{
            Type = "VideoList",
            Data = Videos
        });

        Web.PostMessage(JSON);
    }

    private static List<VideoFileInfo> __ScanVideos(){
        List<VideoFileInfo> Result = [];

        Result.AddRange(__ScanFolder(VideosFolder, IsPublished: false));
        Result.AddRange(__ScanFolder(PublishedFolder, IsPublished: true));

        return Result;
    }

    private static List<VideoFileInfo> __ScanFolder(string Folder, bool IsPublished){
        List<VideoFileInfo> Result = [];

        if(!Directory.Exists(Folder)){
            WL.Logger.Info($"Папка с видео не найдена!\nПуть: {Folder}");
            return Result;
        }

        foreach(string Path in Directory.GetFiles(Folder)){
            string Extension = System.IO.Path.GetExtension(Path).ToLowerInvariant();
            if(!VideoExtensions.Contains(Extension)){ continue; }

            FileInfo FI = new FileInfo(Path);
            (string DateString, bool HasDate, DateTime SortDateTime) = __GetDateInfo(FI);

            Result.Add(new VideoFileInfo(
                Name: FI.Name,
                SizeBytes: FI.Length,
                Date: DateString,
                HasDate: HasDate,
                SortDate: SortDateTime.Ticks,
                IsPublished: IsPublished
            ));
        }

        return Result;
    }

    private static (string DateString, bool HasDate, DateTime SortDateTime) __GetDateInfo(FileInfo FI){
        ParsedDate? Parsed = __TryParseDateFromName(FI.Name);
        bool HasDate = Parsed != null;
        DateTime SortDateTime = Parsed?.Date ?? FI.CreationTime;

        string DateString = Parsed switch{
            { HasSeconds: true } P => P.Date.ToString("yyyy.MM.dd HH:mm:ss"),
            { HasTime: true }    P => P.Date.ToString("yyyy.MM.dd HH:mm"),
            { } P                  => P.Date.ToString("yyyy.MM.dd"),
            null                   => FI.CreationTime.ToString("yyyy.MM.dd HH:mm:ss")
        };

        return (DateString, HasDate, SortDateTime);
    }

    // ----------------------------------------------------------------------

    private static void __StartPublish(string Name, bool IsAutoPublish, int ChunkSizeMB){
        Task.Run(() => {
            string Path = System.IO.Path.Combine(VideosFolder, Name);

            YouTubeOperationResult<YouTubeVideo>? UploadResult;
            string   CreateDate;
            TimeSpan Duration;
            bool     IsShorts;
            int      NewID;

            try{
                if(!File.Exists(Path)){
                    __EnqueuePublishFailed(Name, "Файл не найден (возможно, уже опубликован, перемещён или удалён).", "NotFound");
                    return;
                }

                FileInfo FI = new FileInfo(Path);
                (CreateDate, bool _, DateTime _) = __GetDateInfo(FI);

                VideoInfo Info = __TryGetVideoInfo(Path);
                IsShorts = __IsShorts(Info);
                Duration = Info.Duration ?? TimeSpan.Zero;

                NewID = __GetNextID();
                string Title = $"[{NewID}]";

                int AutoPublishRunNumber = __CurrentAutoPublishRunNumber;

                string Description = IsAutoPublish
                    ? $"Опубликовано YouTubePublisher ({AutoPublishRunNumber})\n\nДата: {DateTime.Now:yyyy.MM.dd HH:mm:ss}\nФайл: {FI.Name}\nРазмер: {FI.Length} байт"
                    : $"Опубликовано YouTubePublisher\n\nДата: {DateTime.Now:yyyy.MM.dd HH:mm:ss}\nФайл: {FI.Name}\nРазмер: {FI.Length} байт";

                int ChunkSizeBytes = Math.Max(1, ChunkSizeMB) * 1024 * 1024;

                UploadResult = YouTube.UploadVideo(
                    VideoPath: Path,
                    Title: Title,
                    Description: Description,
                    Privacy: YouTubePrivacy.Unpublic,
                    OnProgress: (Sent, Total) => __EnqueueProgress(Name, Sent, Total),
                    ChunkSizeBytes: ChunkSizeBytes
                );

                if(!UploadResult.Success){
                    WL.Logger.Error($"Не удалось опубликовать видео [{Name}]: {UploadResult.ErrorMessage}", UploadResult.Error);
                    __EnqueuePublishFailed(Name, UploadResult.ErrorMessage, UploadResult.FailReason.ToString());
                    return;
                }
            }catch(Exception e){
                WL.Logger.Error($"Исключение при загрузке видео [{Name}] на YouTube!", e);
                __EnqueuePublishFailed(Name, __FormatExceptionForUser(e), "Other");
                return;
            }

            string VideoID         = UploadResult.Value!.ID;
            string RenamedFileName = $"[{NewID}] {Name}";
            string DestinationPath = System.IO.Path.Combine(PublishedFolder, RenamedFileName);

            try{
                Directory.CreateDirectory(PublishedFolder);
                File.Move(Path, DestinationPath, overwrite: true);
            }catch(Exception e){
                WL.Logger.Fatal($"ВИДЕО [{Name}] УЖЕ ЗАГРУЖЕНО НА YOUTUBE (ID: {VideoID}), но файл не удалось переместить!", e);
                __EnqueuePublishFailed(Name,
                    $"⚠ ВИДЕО УЖЕ ЗАГРУЖЕНО НА YOUTUBE (ID: {VideoID})!\n" +
                    $"Не удалось переместить локальный файл в Published: {__FormatExceptionForUser(e)}\n\n" +
                    "НЕ НАЖИМАЙТЕ «Повторить» — переместите файл в Published вручную и допишите запись в Result.js!",
                    "LocalError");
                return;
            }

            try{
                __AppendResultEntry(NewID, CreateDate, VideoID, Duration, IsShorts);
            }catch(Exception e){
                WL.Logger.Error($"Видео [{Name}] опубликовано (ID: {VideoID}), файл перемещён, но не удалось дописать запись в Result.js!", e);
                __EnqueuePublishFailed(Name,
                    $"Видео успешно опубликовано (ID: {VideoID}), файл перемещён в Published, " +
                    $"но запись в Result.js дописать не удалось: {__FormatExceptionForUser(e)}\n\nДопишите запись вручную!",
                    "LocalError");
                __EnqueuePublishComplete(Name, NewID);
                return;
            }

            __EnqueuePublishComplete(Name, NewID);
        });
    }

    private static string __FormatExceptionForUser(Exception e){
        string Result = e.Message;
        Exception? Inner = e.InnerException;
        while(Inner != null){
            Result += $"\n→ {Inner.Message}";
            Inner = Inner.InnerException;
        }
        return Result;
    }

    private readonly record struct VideoInfo(TimeSpan? Duration, int? Width, int? Height);

    private static VideoInfo __TryGetVideoInfo(string Path){
        try{
            ProcessStartInfo PSI = new ProcessStartInfo{
                FileName               = "ffprobe",
                Arguments              = $"-v quiet -print_format json -show_format -show_streams \"{Path}\"",
                RedirectStandardOutput = true,
                UseShellExecute        = false,
                CreateNoWindow         = true
            };

            using Process? Proc = Process.Start(PSI);
            if(Proc == null){ throw new ExceptionWL("Не удалось запустить ffprobe!"); }

            string Output = Proc.StandardOutput.ReadToEnd();
            Proc.WaitForExit();

            using JsonDocument Doc = JsonDocument.Parse(Output);

            TimeSpan? Duration = null;
            if(Doc.RootElement.TryGetProperty("format", out JsonElement Format) &&
               Format.TryGetProperty("duration", out JsonElement DurationProp) &&
               double.TryParse(DurationProp.GetString(), out double Seconds))
            {
                Duration = TimeSpan.FromSeconds(Seconds);
            }

            int? Width  = null;
            int? Height = null;

            if(Doc.RootElement.TryGetProperty("streams", out JsonElement Streams)){
                foreach(JsonElement Stream in Streams.EnumerateArray()){
                    if(!Stream.TryGetProperty("codec_type", out JsonElement CodecType) || CodecType.GetString() != "video"){
                        continue;
                    }

                    if(Stream.TryGetProperty("width", out JsonElement W)){ Width = W.GetInt32(); }
                    if(Stream.TryGetProperty("height", out JsonElement H)){ Height = H.GetInt32(); }
                    break;
                }
            }

            return new VideoInfo(Duration, Width, Height);
        }catch(Exception e){
            WL.Logger.Warn($"Не удалось определить параметры видео через ffprobe!\nФайл: {Path}", e);
            return new VideoInfo(null, null, null);
        }
    }

    private static bool __IsShorts(VideoInfo Info){
        if(Info.Duration == null || Info.Width == null || Info.Height == null){ return false; }
        return Info.Height >= Info.Width && Info.Duration <= TimeSpan.FromMinutes(3);
    }

    private static void __EnqueueProgress(string Name, long Sent, long Total){
        double Percent = Total > 0 ? (double)Sent / Total * 100.0 : 0;

        string JSON = JsonSerializer.Serialize(new{
            Type = "PublishProgress",
            Data = new{ Name, BytesSent = Sent, TotalBytes = Total, Percent }
        });

        __PendingMessages.Enqueue(JSON);
    }

    private static void __EnqueuePublishComplete(string Name, int MyID){
        Interlocked.Increment(ref __PublishedVideos);
        __UpdateWindowTitle();

        string JSON = JsonSerializer.Serialize(new{ Type = "PublishComplete", Data = new{ Name, MyID } });
        __PendingMessages.Enqueue(JSON);
    }

    private static void __EnqueuePublishFailed(string Name, string Reason, string ReasonCode = "Other"){
        string JSON = JsonSerializer.Serialize(new{ Type = "PublishFailed", Data = new{ Name, Reason, FailReasonCode = ReasonCode } });
        __PendingMessages.Enqueue(JSON);
    }

    private static void __SendError(string Message, Exception e){
        WL.Logger.Error(Message, e);
        try{
            string Details = e.ToString();
            string JSON = JsonSerializer.Serialize(new{ Type = "Error", Data = new{ Title = Message, Details } });
            Web.PostMessage(JSON);
        }catch{
        }
    }

    private static readonly Regex[] __DatePatterns = [
        new Regex(@"\[(?<y>\d{4})\.(?<m>\d{2})\.(?<d>\d{2})\]\s*(?<h>\d{2});(?<mi>\d{2});(?<s>\d{2})"),
        new Regex(@"(?<y>\d{4})-(?<m>\d{2})-(?<d>\d{2})"),
        new Regex(@"(?<y>\d{4})(?<m>\d{2})(?<d>\d{2})"),
        new Regex(@"(?<d>\d{2})-(?<m>\d{2})-(?<y>\d{4})")
    ];

    private readonly record struct ParsedDate(DateTime Date, bool HasTime, bool HasSeconds);

    private static ParsedDate? __TryParseDateFromName(string FileName){
        foreach(Regex Pattern in __DatePatterns){
            Match M = Pattern.Match(FileName);
            if(!M.Success){ continue; }

            if(!int.TryParse(M.Groups["y"].Value, out int Year) ||
               !int.TryParse(M.Groups["m"].Value, out int Month) ||
               !int.TryParse(M.Groups["d"].Value, out int Day))
            {
                continue;
            }

            bool HasTime    = M.Groups["h"].Success && M.Groups["mi"].Success;
            bool HasSeconds = HasTime && M.Groups["s"].Success;

            int Hour   = 0;
            int Minute = 0;
            int Second = 0;

            if(HasTime){
                int.TryParse(M.Groups["h"].Value,  out Hour);
                int.TryParse(M.Groups["mi"].Value, out Minute);
                if(HasSeconds){ int.TryParse(M.Groups["s"].Value, out Second); }
            }

            try{
                DateTime Date = new DateTime(Year, Month, Day, Hour, Minute, Second);
                return new ParsedDate(Date, HasTime, HasSeconds);
            }catch{
                continue;
            }
        }

        return null;
    }
}

public record VideoFileInfo(string Name, long SizeBytes, string? Date, bool HasDate, long SortDate, bool IsPublished);