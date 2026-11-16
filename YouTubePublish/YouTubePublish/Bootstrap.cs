using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Encodings.Web;
using IOPath = System.IO.Path;
using System.Text.Json;
using System.Text.RegularExpressions;
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
    
    private static readonly ConcurrentDictionary<string, byte>   __InProgress     = new();
    private static readonly ConcurrentDictionary<string, string> __Locked         = new(); // Name -> причина
    private static readonly ConcurrentDictionary<string, long>   __LastProgressAt = new();
    private static readonly ConcurrentQueue<string>              __StartupNotices = new();

    private static int                     __CriticalSections = 0;
    private static System.Threading.Timer? __StallTimer;

    private static readonly JsonSerializerOptions __JournalJson = new(){
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private const string JournalFilePath = @"W:\Woowz11\Videos\Записи\YT\!Journal.jsonl";
    private const string BackupFolder    = @"W:\Woowz11\Videos\Записи\YT\!Backups";

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

const ListWrapper      = document.getElementById("VideoListWrapper");
const FollowCheckbox   = document.getElementById("Input-Follow");
const StageByName      = new Map();
const WarningByName    = new Map();
const LockedByName     = new Map();
const RetryCountByName = new Map();
let FollowPausedUntil  = 0;

// Если ты сам крутишь список, автопрокрутка молчит 15 секунд
for(const Ev of ["wheel", "touchstart", "mousedown", "keydown"]){
    ListWrapper.addEventListener(Ev, () => { FollowPausedUntil = Date.now() + 15000; }, { passive: true });
}

function FollowVideo(Name){
    if(!FollowCheckbox.checked || Date.now() < FollowPausedUntil){ return; }
    CardsByName.get(Name)?.scrollIntoView({ block: "center", behavior: "auto" });
}

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
    StageByName.set(Video.Name, "Запуск…");

    RenderVideos(SortVideos(LastVideos));
    if(IsAutoPublish){ FollowVideo(Video.Name); }

    SendToHost("PublishRequest", { Name: Video.Name, IsAutoPublish, ChunkSizeMB: GetChunkSizeMB() });
}

// ----------------------------------------------------------------------

function AddBanner(Card, Icon, Text){
    const Banner = document.createElement("div");
    Banner.className = "ErrorBanner";
    Banner.innerHTML = `<span class="ErrorBannerIcon">${Icon}</span><span>${EscapeHtml(Text)}</span>`;
    Card.appendChild(Banner);
}

function RenderVideos(Videos){
    const SavedScroll = ListWrapper.scrollTop;

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
        const LockedReason = Video.LockedReason ?? LockedByName.get(Video.Name);

        const Card = document.createElement("div");
        Card.className = "VideoCard"
            + (Video.IsPublished ? " Published" : "")
            + ((IsFailed || LockedReason) ? " Failed" : "")
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
            ${LockedReason ? "<span class=\"FailedChip\">🔒 Заблокировано</span>" : ""}
        `;

        Info.appendChild(Name);
        Info.appendChild(Meta);

        const Button = document.createElement("button");
        Button.className = "PublishButton";

        if(Video.IsPublished){
            Button.textContent = "Опубликовано";
            Button.disabled = true;
        }else if(LockedReason){
            Button.textContent = "Нужны ручные действия";
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
                <div class="StageText">${EscapeHtml(StageByName.get(Video.Name) ?? "")}</div>
                <div class="ProgressBar"><div class="ProgressBarFill" style="width:${Percent}%"></div></div>
                <div class="ProgressText">${Percent}% — ${FormatSize(Remaining)} осталось</div>
            `;
            Card.appendChild(Progress);
        }

        if(LockedReason){ AddBanner(Card, "🔒", LockedReason); }
        if(IsFailed){ AddBanner(Card, "⚠️", FailedByName.get(Video.Name)); }
        if(WarningByName.has(Video.Name)){ AddBanner(Card, "⚠️", WarningByName.get(Video.Name)); }

        ListElement.appendChild(Card);
        CardsByName.set(Video.Name, Card);
    }

    ListWrapper.scrollTop = SavedScroll;
}

function UpdateProgress(Data){
    ProgressByName.set(Data.Name, Data);
    UpdateStage(Data.Name, "Загрузка на YouTube");

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

function UpdateStage(Name, Stage){
    StageByName.set(Name, Stage);
    const El = CardsByName.get(Name)?.querySelector(".StageText");
    if(El){ El.textContent = Stage; }
}

// ----------------------------------------------------------------------

const QuotaFailReasons = new Set(["QuotaExceeded", "UploadLimitExceeded"]);

let AutoPublishActive   = false;
let AutoPublishQueue    = [];
let AutoPublishInFlight = new Set();

const NoRetryCodes   = new Set(["NotFound", "LocalError", "FileBusy", "QuotaExceeded", "UploadLimitExceeded"]);
const MaxAutoRetries = 2;
let   AutoStats      = { Done: 0, Problems: [] };

function ShowAutoSummary(Title){
    const Lines = [`Опубликовано: ${AutoStats.Done}`, `Проблем: ${AutoStats.Problems.length}`];
    if(AutoStats.Problems.length > 0){ Lines.push("", ...AutoStats.Problems); }
    ShowError(Title, Lines.join("\n"));
}

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

function StopAutoPublish(Reason){
    if(!AutoPublishActive){ return; }

    AutoPublishActive = false;
    AutoPublishQueue  = [];

    UpdateAutoPublishButtonUI();

    if(Reason){
        ShowError("Автопубликация остановлена", Reason);
    }
}

AutoPublishButton.onclick = () => {
    if(AutoPublishActive){
        StopAutoPublish("Остановлено пользователем.");
    }else{
        StartAutoPublish();
    }
};

function StartAutoPublish(){
    const Candidates = SortVideos(LastVideos).filter(V =>
        !V.IsPublished && !PublishingNames.has(V.Name) && !V.LockedReason && !LockedByName.has(V.Name));

    if(Candidates.length === 0){
        ShowError("Автопубликация", "Нет видео для публикации.");
        return;
    }

    SendToHost("AutoPublishStarted");

    AutoStats = { Done: 0, Problems: [] };
    RetryCountByName.clear();
    AutoPublishQueue  = Candidates.map(V => V.Name);
    AutoPublishActive = true;

    UpdateAutoPublishButtonUI();

    const Concurrency = GetConcurrency();
    for(let i = AutoPublishInFlight.size; i < Concurrency; i++){
        AutoPublishPumpNext();
    }
}

function AutoPublishPumpNext(){
    if(!AutoPublishActive){ return; }

    if(AutoPublishQueue.length === 0){
        if(AutoPublishInFlight.size === 0){
            StopAutoPublish(null);
            ShowAutoSummary("Автопубликация завершена");
        }
        return;
    }

    const Name  = AutoPublishQueue.shift();
    const Video = LastVideos.find(V => V.Name === Name);

    if(!Video || Video.IsPublished || PublishingNames.has(Name)){
        AutoPublishPumpNext();
        return;
    }

    AutoPublishInFlight.add(Name);
    StartPublishUI(Video);
}

function MarkPublished(Name, MyID, Warning){
    PublishingNames.delete(Name);
    ProgressByName.delete(Name);
    StageByName.delete(Name);
    FailedByName.delete(Name);

    const Video = LastVideos.find(V => V.Name === Name);
    if(Video){
        Video.IsPublished = true;
        if(MyID !== undefined && MyID !== null){ Video.MyID = MyID; }
    }

    const WasAuto = AutoPublishInFlight.delete(Name);

    if(Warning){
        WarningByName.set(Name, Warning);
        if(WasAuto){ AutoStats.Problems.push(`${Name}: ${Warning}`); }
        else{ ShowError("Видео опубликовано, но есть предупреждение", Warning); }
    }
    if(WasAuto){ AutoStats.Done++; }

    RenderVideos(SortVideos(LastVideos));

    if(WasAuto){ AutoPublishPumpNext(); }
}

function ResetCard(Name, Reason, FailReasonCode){
    PublishingNames.delete(Name);
    ProgressByName.delete(Name);
    StageByName.delete(Name);

    if(FailReasonCode === "LocalError"){
        LockedByName.set(Name, Reason ?? "Нужны ручные действия");
    }

    const WasAuto      = AutoPublishInFlight.delete(Name);
    const IsQuotaError = QuotaFailReasons.has(FailReasonCode);

    // Автоповтор временных ошибок (в конец очереди)
    if(WasAuto && AutoPublishActive && !IsQuotaError && !NoRetryCodes.has(FailReasonCode)){
        const Retries = RetryCountByName.get(Name) ?? 0;
        if(Retries < MaxAutoRetries){
            RetryCountByName.set(Name, Retries + 1);
            AutoPublishQueue.push(Name);
            FailedByName.set(Name, `Попытка ${Retries + 1} не удалась, будет автоповтор.\n${Reason ?? ""}`);
            RenderVideos(SortVideos(LastVideos));
            AutoPublishPumpNext();
            return;
        }
    }

    FailedByName.set(Name, Reason ?? "Неизвестная ошибка");
    RenderVideos(SortVideos(LastVideos));

    if(WasAuto){
        AutoStats.Problems.push(`${Name}: ${Reason ?? "Неизвестная ошибка"}`);

        if(IsQuotaError){
            StopAutoPublish(null);
            ShowAutoSummary("Автопубликация остановлена: превышена квота YouTube API");
        }else if(AutoPublishActive){
            AutoPublishPumpNext();   // без модалки: ошибка видна на карточке и войдёт в сводку
        }
    }else{
        ShowError("Не удалось опубликовать видео", Reason ?? "");
    }
}

// ----------------------------------------------------------------------

document.getElementById("Btn-Refresh").onclick = () => {
    SendToHost("RefreshRequest");
};

document.getElementById("Btn-OpenQuota").onclick = () => {
    SendToHost("OpenQuotaPage");
};

document.getElementById("Btn-OpenVideos").onclick = () => {
    SendToHost("OpenFolder", "Videos");
};

document.getElementById("Btn-OpenPublished").onclick = () => {
    SendToHost("OpenFolder", "Published");
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
        }else if(Message.Type === "PublishStage"){
            UpdateStage(Message.Data.Name, Message.Data.Stage);
        }else if(Message.Type === "PublishComplete"){
            MarkPublished(Message.Data.Name, Message.Data.MyID, Message.Data.Warning);
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
    overflow-y: auto;
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

.StageText{
    font-size: 11px;
    color: #4fc3f7;
}

.SidebarCheck{
    display: flex;
    align-items: center;
    gap: 6px;
    font-size: 12px;
    color: #c0c0c0;
    margin: 4px 4px 0 4px;
    cursor: pointer;
}
</style>

</head>

<body>

<div id="App">

    <div id="Sidebar">
        <button class="SidebarButton Primary" id="Btn-Refresh">🔄 Обновить список</button>
        <button class="SidebarButton" id="Btn-OpenQuota">📊 Открыть квоту API</button>
        <button class="SidebarButton" id="Btn-OpenVideos">📁 Папка с видео</button>
        <button class="SidebarButton" id="Btn-OpenPublished">📂 Папка Published</button>

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
            <label class="SidebarCheck">
                <input type="checkbox" id="Input-Follow" checked> Следить за загрузкой
            </label>
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
            __RecoverFromJournal();

            YouTube = new YouTube(File.ReadAllText(@"W:\Other\rofl-games\YouTubePublish\client_secret.json"), @"W:\Other\rofl-games\YouTubePublish\UserLogin", "YoutubePublisher");

            __StallTimer = new System.Threading.Timer(_ => __CheckStalls(), null, 10_000, 10_000);

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

                Thread.Sleep(1);
            }

            Window.Close();

            // дождаться коротких критичных шагов (перемещение файла + запись результата)
            Stopwatch Wait = Stopwatch.StartNew();
            while(Volatile.Read(ref __CriticalSections) > 0 && Wait.Elapsed < TimeSpan.FromSeconds(60)){
                Thread.Sleep(50);
            }
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
                    __SendVideoList();
                    __SendStartupNotices();
                    break;

                case "RefreshRequest":
                    __SendVideoList();
                    break;

                case "OpenFolder":{
                    string Kind = Doc.RootElement.GetProperty("Data").GetString() ?? "";
                    __OpenFolder(Kind == "Published" ? PublishedFolder : VideosFolder);
                    break;
                }

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
    
    private static void __OpenFolder(string Folder){
        try{
            Directory.CreateDirectory(Folder);
            Process.Start(new ProcessStartInfo{
                FileName = Folder,
                UseShellExecute = true
            });
        }catch(Exception e){
            __SendError($"Не удалось открыть папку!\nПуть: {Folder}", e);
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
        __WriteAllTextAtomic(ConfigFilePath, JSON);
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
    
    // ======================================================================
    //  НАДЁЖНОСТЬ: атомарная запись, бэкапы, журнал, восстановление
    // ======================================================================

    private static void __WriteAllTextAtomic(string FilePath, string Content){
        string Temp = FilePath + ".tmp";
        File.WriteAllText(Temp, Content);
        if(File.Exists(FilePath)){ File.Replace(Temp, FilePath, null); }
        else{ File.Move(Temp, FilePath); }
    }

    private static void __BackupFile(string FilePath){
        try{
            if(!File.Exists(FilePath)){ return; }
            Directory.CreateDirectory(BackupFolder);
            
            string BaseName = IOPath.GetFileName(FilePath);
            File.Copy(FilePath, IOPath.Combine(BackupFolder, $"{BaseName}.{DateTime.Now:yyyyMMdd_HHmmss_fff}.bak"), overwrite: false);

            foreach(FileInfo Old in new DirectoryInfo(BackupFolder).GetFiles($"{BaseName}.*.bak").OrderByDescending(F => F.Name).Skip(100)){
                Old.Delete();
            }
        }catch(Exception e){
            WL.Logger.Warn($"Не удалось сделать резервную копию {FilePath}", e);
        }
    }

    private static string __MakeUniquePath(string FilePath){
        string Dir  = IOPath.GetDirectoryName(FilePath)!;
        string Name = IOPath.GetFileNameWithoutExtension(FilePath);
        string Ext  = IOPath.GetExtension(FilePath);
        for(int i = 2; ; i++){
            string Candidate = IOPath.Combine(Dir, $"{Name} ({i}){Ext}");
            if(!File.Exists(Candidate)){ return Candidate; }
        }
    }

    // ----------------------------------------------------------------------

    private sealed class JournalEntry{
        public string  Time            { get; set; } = "";
        public int     ID              { get; set; }
        public string  Event           { get; set; } = "";   // Started, Uploaded, Moved, Recorded, Failed
        public string  Name            { get; set; } = "";
        public string? RenamedFileName { get; set; }
        public string? VideoID         { get; set; }
        public string? CreateDate      { get; set; }
        public double  DurationSeconds { get; set; }
        public bool    IsShorts        { get; set; }
        public string? Note            { get; set; }
    }

    private static readonly object __JournalLock = new();

    private static void __Journal(JournalEntry E){
        E.Time = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string Line = JsonSerializer.Serialize(E, __JournalJson);

        lock(__JournalLock){
            using FileStream FS = new(JournalFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using StreamWriter SW = new(FS);
            SW.WriteLine(Line);
            SW.Flush();
            FS.Flush(true); // реально на диск
        }
    }

    private static void __TryJournal(JournalEntry E){
        try{ __Journal(E); }
        catch(Exception e){ WL.Logger.Error($"Не удалось записать в журнал: {E.Event} [{E.ID}]", e); }
    }

    private static List<JournalEntry> __ReadJournal(){
        List<JournalEntry> Result = [];
        if(!File.Exists(JournalFilePath)){ return Result; }

        foreach(string Line in File.ReadLines(JournalFilePath)){
            if(string.IsNullOrWhiteSpace(Line)){ continue; }
            try{
                JournalEntry? E = JsonSerializer.Deserialize<JournalEntry>(Line, __JournalJson);
                if(E != null){ Result.Add(E); }
            }catch(Exception e){
                WL.Logger.Warn($"Битая строка в журнале (пропущена): {Line}", e);
            }
        }
        return Result;
    }

    // ----------------------------------------------------------------------

    // Идемпотентно: можно вызывать сколько угодно раз для одной записи.
    private static (bool MoveFailed, string? Problems) __FinishPublish(JournalEntry Up){
        List<string> Problems = [];
        bool MoveFailed = false;

        string Source = IOPath.Combine(VideosFolder, Up.Name);
        string Dest   = IOPath.Combine(PublishedFolder, Up.RenamedFileName ?? Up.Name);

        try{
            if(File.Exists(Source)){
                Directory.CreateDirectory(PublishedFolder);

                if(File.Exists(Dest)){
                    Dest = __MakeUniquePath(Dest);
                    Problems.Add($"В Published уже был файл с таким именем, сохранён как «{IOPath.GetFileName(Dest)}».");
                }

                File.Move(Source, Dest); // без overwrite
                __TryJournal(new JournalEntry{ ID = Up.ID, Event = "Moved", Name = Up.Name, RenamedFileName = IOPath.GetFileName(Dest) });
            }
        }catch(Exception e){
            MoveFailed = true;
            WL.Logger.Fatal($"ВИДЕО [{Up.Name}] ЗАГРУЖЕНО (ID: {Up.VideoID}), но файл не перемещён!", e);
            Problems.Add($"Не удалось переместить файл в Published: {__FormatExceptionForUser(e)}");
        }

        try{
            __AppendResultEntry(Up.ID, Up.CreateDate ?? "", Up.VideoID ?? "", TimeSpan.FromSeconds(Up.DurationSeconds), Up.IsShorts);
            __TryJournal(new JournalEntry{ ID = Up.ID, Event = "Recorded", Name = Up.Name });
        }catch(Exception e){
            WL.Logger.Error($"Не удалось дописать запись [{Up.ID}] в Result.js", e);
            Problems.Add($"Запись в Result.js не добавлена (при следующем запуске будет повтор): {__FormatExceptionForUser(e)}");
        }

        return (MoveFailed, Problems.Count == 0 ? null : string.Join("\n", Problems));
    }

    private static string __LockMessage(JournalEntry Up) =>
        $"Видео УЖЕ ЗАГРУЖЕНО на YouTube (ID: {Up.VideoID}), но файл не удалось переместить в Published. " +
        "Не публикуй его повторно: переложи файл вручную или перезапусти программу.";

    // ----------------------------------------------------------------------

    private static void __RecoverFromJournal(){
        List<JournalEntry> All = __ReadJournal();
        if(All.Count == 0){ return; }

        // ID никогда не должны повторяться, даже если Config откатился
        lock(__ConfigLock){
            int MaxID = All.Max(E => E.ID);
            if(MaxID > __Config.LastID){
                WL.Logger.Warn($"LastID в конфиге ({__Config.LastID}) меньше, чем в журнале ({MaxID}). Исправляю.");
                __Config.LastID = MaxID;
                __SaveConfig();
            }
        }

        foreach(IGrouping<int, JournalEntry> G in All.GroupBy(E => E.ID)){
            List<JournalEntry> Events = G.ToList();
            if(Events.Any(E => E.Event is "Recorded" or "Failed")){ continue; }

            JournalEntry? Up = Events.FirstOrDefault(E => E.Event == "Uploaded");

            if(Up != null){
                (bool MoveFailed, string? Problems) = __FinishPublish(Up);
                if(MoveFailed){ __Locked[Up.Name] = __LockMessage(Up); }

                __StartupNotices.Enqueue(Problems == null
                    ? $"✅ [{Up.ID}] «{Up.Name}» (YouTube ID: {Up.VideoID}): после сбоя публикация доведена до конца автоматически."
                    : $"⚠ [{Up.ID}] «{Up.Name}» (YouTube ID: {Up.VideoID}):\n{Problems}");
            }else{
                JournalEntry S = Events[0];
                __StartupNotices.Enqueue(
                    $"ℹ Загрузка [{S.ID}] «{S.Name}» была прервана (закрытие окна или сбой). " +
                    $"Файл остался на месте и будет загружен заново. Если на YouTube есть недогруженное видео «[{S.ID}]», его можно удалить.");
                __TryJournal(new JournalEntry{ ID = S.ID, Event = "Failed", Name = S.Name, Note = "Прервано, обнаружено при запуске" });
            }
        }
    }

    private static void __SendStartupNotices(){
        if(__StartupNotices.IsEmpty){ return; }

        List<string> Lines = [];
        while(__StartupNotices.TryDequeue(out string? N)){ Lines.Add(N); }

        Web.PostMessage(JsonSerializer.Serialize(new{
            Type = "Error",
            Data = new{ Title = "Восстановление после прошлого запуска", Details = string.Join("\n\n", Lines) }
        }));
    }

    private const string ResultFilePath = @"W:\Woowz11\Videos\Записи\YT\!Result.js";
    private static readonly object __ResultFileLock = new object();

    private static string __FormatDuration(TimeSpan Duration){
        return Duration.Hours > 0
            ? Duration.ToString(@"hh\:mm\:ss")
            : Duration.ToString(@"mm\:ss");
    }

    // ----------------------------------------------------------------------
    
    private static readonly Regex __ResultEntryStart = new(@"^\s*\[(?<id>\d+)\s*,");

    private sealed class ResultFileModel{
        public List<string>                Head    = [];  // всё до первой записи
        public List<(int ID, string Text)> Entries = [];
        public List<string>                Tail    = [];  // всё после последней записи
    }

    private static ResultFileModel __ParseResultFile(string Content){
        ResultFileModel M = new();
        if(string.IsNullOrWhiteSpace(Content)){ return M; }

        foreach(string Raw in Regex.Split(Content.Replace("\r\n", "\n"), @"\n\s*\n")){
            string Block = Raw.Trim('\n', '\r', ' ');
            if(string.IsNullOrWhiteSpace(Block)){ continue; }

            Match Start = __ResultEntryStart.Match(Block);

            if(Start.Success){
                // чужие блоки между записями приклеиваем к предыдущей записи
                if(M.Tail.Count > 0){
                    (int PrevID, string PrevText) = M.Entries[^1];
                    M.Entries[^1] = (PrevID, PrevText + "\n\n" + string.Join("\n\n", M.Tail));
                    M.Tail.Clear();
                }
                M.Entries.Add((int.Parse(Start.Groups["id"].Value), Block));
            }else if(M.Entries.Count == 0){
                M.Head.Add(Block);
            }else{
                M.Tail.Add(Block);
            }
        }
        return M;
    }

    private static string __RenderResultFile(ResultFileModel M){
        List<string> Parts = [..M.Head, ..M.Entries.Select(E => E.Text), ..M.Tail];
        return string.Join("\n\n", Parts) + "\n\n";
    }

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
            ResultFileModel Model = __ParseResultFile(Existing);

            if(Model.Entries.Any(E => E.ID == ID)){ return; } // уже записано

            List<string> OldTexts = [..Model.Head, ..Model.Entries.Select(E => E.Text), ..Model.Tail];
            int OldCount = Model.Entries.Count;

            Model.Entries.Add((ID, Entry));
            Model.Entries.Sort((A, B) => B.ID.CompareTo(A.ID));
            string NewContent = __RenderResultFile(Model);

            // Проверка: ничего не потеряно
            bool Ok = __ParseResultFile(NewContent).Entries.Count == OldCount + 1
                   && OldTexts.All(T => NewContent.Contains(T));
            if(!Ok){
                throw new ExceptionWL("Проверка нового Result.js не пройдена, файл НЕ перезаписан (ничего не потеряно).");
            }

            __BackupFile(ResultFilePath);
            __WriteAllTextAtomic(ResultFilePath, NewContent);
        }
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
                IsPublished: IsPublished,
                LockedReason: !IsPublished && __Locked.TryGetValue(FI.Name, out string? LR) ? LR : null
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
        if(__Locked.TryGetValue(Name, out string? LockReason)){
            __EnqueuePublishFailed(Name, LockReason, "LocalError");
            return;
        }

        if(!__InProgress.TryAdd(Name, 0)){
            WL.Logger.Warn($"Повторный запрос публикации [{Name}] проигнорирован: уже идёт.");
            return;
        }

        Task.Run(() => {
            try{
                __PublishCore(Name, IsAutoPublish, ChunkSizeMB);
            }catch(Exception e){
                WL.Logger.Error($"Исключение при публикации [{Name}]!", e);
                __EnqueuePublishFailed(Name, __FormatExceptionForUser(e), "Other");
            }finally{
                __LastProgressAt.TryRemove(Name, out _);
                __InProgress.TryRemove(Name, out _);
            }
        });
    }

    private static string? __CheckFileReady(string FilePath){
        if(DateTime.Now - new FileInfo(FilePath).LastWriteTime < TimeSpan.FromSeconds(10)){
            return "Файл изменялся меньше 10 секунд назад, возможно, запись ещё идёт. Повтори позже.";
        }
        try{
            using FileStream _ = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
        }catch(IOException e){
            return "Файл занят другой программой: " + e.Message;
        }
        return null;
    }

    private static void __PublishCore(string Name, bool IsAutoPublish, int ChunkSizeMB){
        string SourcePath = IOPath.Combine(VideosFolder, Name);

        if(!File.Exists(SourcePath)){
            __EnqueuePublishFailed(Name, "Файл не найден (возможно, уже опубликован, перемещён или удалён).", "NotFound");
            return;
        }

        string? NotReady = __CheckFileReady(SourcePath);
        if(NotReady != null){
            __EnqueuePublishFailed(Name, NotReady, "FileBusy");
            return;
        }

        FileInfo FI = new(SourcePath);
        (string CreateDate, _, _) = __GetDateInfo(FI);

        __EnqueueStage(Name, "Анализ видео (ffprobe)…");
        Stopwatch SW = Stopwatch.StartNew();
        VideoInfo Info = __TryGetVideoInfo(SourcePath);
        WL.Logger.Info($"[{Name}] ffprobe занял {SW.ElapsedMilliseconds} мс");

        bool     IsShorts = __IsShorts(Info);
        TimeSpan Duration = Info.Duration ?? TimeSpan.Zero;

        int    NewID           = __GetNextID();
        string Title           = $"[{NewID}]";
        string RenamedFileName = $"[{NewID}] {Name}";

        // Без записи в журнал не загружаем (если упадёт, то до загрузки)
        __Journal(new JournalEntry{ ID = NewID, Event = "Started", Name = Name, RenamedFileName = RenamedFileName });

        string Description = IsAutoPublish
            ? $"Опубликовано YouTubePublisher ({__CurrentAutoPublishRunNumber})\n\nДата: {DateTime.Now:yyyy.MM.dd HH:mm:ss}\nФайл: {FI.Name}\nРазмер: {FI.Length} байт"
            : $"Опубликовано YouTubePublisher\n\nДата: {DateTime.Now:yyyy.MM.dd HH:mm:ss}\nФайл: {FI.Name}\nРазмер: {FI.Length} байт";

        int ChunkSizeBytes = Math.Max(1, ChunkSizeMB) * 1024 * 1024;

        __EnqueueStage(Name, "Подключение к YouTube…");
        __LastProgressAt[Name] = Environment.TickCount64;
        SW.Restart();
        bool FirstProgress = true;

        YouTubeOperationResult<YouTubeVideo> UploadResult;
        try{
            UploadResult = YouTube.UploadVideo(
                VideoPath: SourcePath,
                Title: Title,
                Description: Description,
                Privacy: YouTubePrivacy.Unpublic,
                OnProgress: (Sent, Total) => {
                    __LastProgressAt[Name] = Environment.TickCount64;
                    if(FirstProgress){
                        FirstProgress = false;
                        WL.Logger.Info($"[{Name}] первый прогресс через {SW.Elapsed.TotalSeconds:F1} с ({Sent}/{Total})");
                    }
                    __EnqueueProgress(Name, Sent, Total);
                },
                ChunkSizeBytes: ChunkSizeBytes
            );
        }catch(Exception e){
            __TryJournal(new JournalEntry{ ID = NewID, Event = "Failed", Name = Name, Note = e.Message });
            WL.Logger.Error($"Исключение при загрузке [{Name}]!", e);
            __EnqueuePublishFailed(Name, __FormatExceptionForUser(e), "Other");
            return;
        }finally{
            __LastProgressAt.TryRemove(Name, out _);
        }

        if(!UploadResult.Success){
            WL.Logger.Error($"Не удалось опубликовать [{Name}]: {UploadResult.ErrorMessage}", UploadResult.Error);
            __TryJournal(new JournalEntry{ ID = NewID, Event = "Failed", Name = Name, Note = UploadResult.ErrorMessage });
            __EnqueuePublishFailed(Name, UploadResult.ErrorMessage, UploadResult.FailReason.ToString());
            return;
        }

        // ───── КРИТИЧЕСКАЯ СЕКЦИЯ: видео уже на YouTube ─────
        Interlocked.Increment(ref __CriticalSections);
        try{
            string VideoID = UploadResult.Value!.ID;

            JournalEntry Uploaded = new(){
                ID = NewID, Event = "Uploaded", Name = Name, VideoID = VideoID,
                RenamedFileName = RenamedFileName, CreateDate = CreateDate,
                DurationSeconds = Duration.TotalSeconds, IsShorts = IsShorts
            };

            try{ __Journal(Uploaded); }
            catch(Exception e){ WL.Logger.Fatal($"ВИДЕО ЗАГРУЖЕНО (YouTube ID: {VideoID}, файл: {Name}), но запись в журнал не удалась!", e); }

            __EnqueueStage(Name, "Перемещение файла и запись результата…");
            (bool MoveFailed, string? Problems) = __FinishPublish(Uploaded);

            if(MoveFailed){
                string Msg = __LockMessage(Uploaded);
                __Locked[Name] = Msg;
                __EnqueuePublishFailed(Name, Msg + "\n\n" + Problems, "LocalError");
            }else{
                __EnqueuePublishComplete(Name, NewID, Problems);
            }
        }finally{
            Interlocked.Decrement(ref __CriticalSections);
        }
    }

    // ----------------------------------------------------------------------

    private static void __EnqueueStage(string Name, string Stage){
        __PendingMessages.Enqueue(JsonSerializer.Serialize(new{ Type = "PublishStage", Data = new{ Name, Stage } }));
    }

    private static void __CheckStalls(){
        long Now = Environment.TickCount64;
        foreach(KeyValuePair<string, long> KV in __LastProgressAt){
            long Seconds = (Now - KV.Value) / 1000;
            if(Seconds >= 120){
                WL.Logger.Warn($"[{KV.Key}] нет прогресса {Seconds} с");
                __EnqueueStage(KV.Key, $"⚠ Нет прогресса уже {Seconds / 60} мин {Seconds % 60} с");
            }
        }
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

    private static VideoInfo __TryGetVideoInfo(string FilePath){
        try{
            ProcessStartInfo PSI = new(){
                FileName = "ffprobe",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute = false,
                CreateNoWindow  = true
            };
            foreach(string A in new[]{ "-v", "quiet", "-print_format", "json", "-show_format", "-show_streams", FilePath }){
                PSI.ArgumentList.Add(A);
            }

            using Process Proc = Process.Start(PSI) ?? throw new ExceptionWL("Не удалось запустить ffprobe!");
            Task<string> OutTask = Proc.StandardOutput.ReadToEndAsync();
            _ = Proc.StandardError.ReadToEndAsync();

            if(!Proc.WaitForExit(30_000)){
                try{ Proc.Kill(true); }catch{}
                throw new ExceptionWL("ffprobe не ответил за 30 секунд!");
            }
            Proc.WaitForExit();

            using JsonDocument Doc = JsonDocument.Parse(OutTask.GetAwaiter().GetResult());

            TimeSpan? Duration = null;
            if(Doc.RootElement.TryGetProperty("format", out JsonElement Format) &&
               Format.TryGetProperty("duration", out JsonElement DurationProp) &&
               double.TryParse(DurationProp.GetString(), System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out double Seconds)){
                Duration = TimeSpan.FromSeconds(Seconds);
            }

            int? Width = null, Height = null;

            if(Doc.RootElement.TryGetProperty("streams", out JsonElement Streams)){
                foreach(JsonElement Stream in Streams.EnumerateArray()){
                    if(!Stream.TryGetProperty("codec_type", out JsonElement CodecType) || CodecType.GetString() != "video"){ continue; }

                    if(Stream.TryGetProperty("width",  out JsonElement W)){ Width  = W.GetInt32(); }
                    if(Stream.TryGetProperty("height", out JsonElement H)){ Height = H.GetInt32(); }

                    int Rotation = 0;
                    if(Stream.TryGetProperty("side_data_list", out JsonElement SideData)){
                        foreach(JsonElement D in SideData.EnumerateArray()){
                            if(D.TryGetProperty("rotation", out JsonElement R) && R.TryGetInt32(out int RV)){ Rotation = RV; }
                        }
                    }
                    if(Math.Abs(Rotation) % 180 == 90){ (Width, Height) = (Height, Width); }
                    break;
                }
            }

            return new VideoInfo(Duration, Width, Height);
        }catch(Exception e){
            WL.Logger.Warn($"Не удалось определить параметры видео через ffprobe!\nФайл: {FilePath}", e);
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

    private static void __EnqueuePublishComplete(string Name, int MyID, string? Warning = null){
        Interlocked.Increment(ref __PublishedVideos);
        __UpdateWindowTitle();
        __PendingMessages.Enqueue(JsonSerializer.Serialize(new{ Type = "PublishComplete", Data = new{ Name, MyID, Warning } }));
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

public record VideoFileInfo(string Name, long SizeBytes, string? Date, bool HasDate, long SortDate, bool IsPublished, string? LockedReason);