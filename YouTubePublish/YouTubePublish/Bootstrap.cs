using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using WLO;
using WLO.Math;

namespace YouTubePublish;

public static class Bootstrap{
    private static WLO.Web.WebView2 Web = null!;
    private static YouTube          YouTube = null!;
    
    private static readonly ConcurrentQueue<string> __PendingMessages = [];
    
    [STAThread]
    public static int Main(string[] Args){
        try{
            Web = WLO.Web.WebView2.Create();
            
            WLO.Window.GLFW Window = new WLO.Window.GLFW(new Vector2I(1280, 720), "Uploader", Web);
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

const CardsByName = new Map();

let LastVideos    = [];
let SortKey       = "Name";
let SortAscending = true;

// ----------------------------------------------------------------------
// Отображение ошибок прямо на странице
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

// ----------------------------------------------------------------------

function FormatSize(Bytes){
    if(Bytes < 1024){ return Bytes + " B"; }
    if(Bytes < 1024 * 1024){ return (Bytes / 1024).toFixed(1) + " KB"; }
    if(Bytes < 1024 * 1024 * 1024){ return (Bytes / (1024 * 1024)).toFixed(1) + " MB"; }
    return (Bytes / (1024 * 1024 * 1024)).toFixed(2) + " GB";
}

// ----------------------------------------------------------------------
// Сортировка
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
        }

        return SortAscending ? Result : -Result;
    });

    return Sorted;
}

function UpdateSortButtonsUI(){
    const Buttons = {
        "Name":      document.getElementById("Btn-SortName"),
        "SizeBytes": document.getElementById("Btn-SortSize"),
        "SortDate":  document.getElementById("Btn-SortDate")
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

document.getElementById("Btn-SortName").onclick = () => SetSort("Name");
document.getElementById("Btn-SortSize").onclick = () => SetSort("SizeBytes");
document.getElementById("Btn-SortDate").onclick = () => SetSort("SortDate");

UpdateSortButtonsUI();

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
        const Card = document.createElement("div");
        Card.className = "VideoCard" + (Video.IsPublished ? " Published" : "");
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
            ${Video.IsPublished ? "<span>✅ Опубликовано</span>" : ""}
        `;

        Info.appendChild(Name);
        Info.appendChild(Meta);

        const Button = document.createElement("button");
        Button.className = "PublishButton";

        if(Video.IsPublished){
            Button.textContent = "Опубликовано";
            Button.disabled = true;
        }else{
            Button.textContent = "Опубликовать";
            Button.onclick = () => {
                if(Card.classList.contains("Publishing")){ return; }

                Card.classList.add("Publishing");
                Button.disabled = true;
                Button.textContent = "Публикация...";

                const Progress = document.createElement("div");
                Progress.className = "ProgressWrapper";
                Progress.innerHTML = `
                    <div class="ProgressBar"><div class="ProgressBarFill"></div></div>
                    <div class="ProgressText">0% — ${FormatSize(Video.SizeBytes)} осталось</div>
                `;
                Card.appendChild(Progress);

                SendToHost("PublishRequest", Video.Name);
            };
        }

        Top.appendChild(Info);
        Top.appendChild(Button);
        Card.appendChild(Top);

        ListElement.appendChild(Card);
        CardsByName.set(Video.Name, Card);
    }
}

function UpdateProgress(Data){
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

function MarkPublished(Name){
    const Video = LastVideos.find(V => V.Name === Name);
    if(Video){
        Video.IsPublished = true;
    }

    RenderVideos(SortVideos(LastVideos));
}

function ResetCard(Name, Reason){
    const Card = CardsByName.get(Name);
    if(!Card){ return; }

    Card.classList.remove("Publishing");

    const Progress = Card.querySelector(".ProgressWrapper");
    if(Progress){ Progress.remove(); }

    const Button = Card.querySelector(".PublishButton");
    if(Button){
        Button.disabled = false;
        Button.textContent = "Опубликовать";
    }

    ShowError("Не удалось опубликовать видео", Reason ?? "");
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
            MarkPublished(Message.Data.Name);
        }else if(Message.Type === "PublishFailed"){
            ResetCard(Message.Data.Name, Message.Data.Reason);
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
            
            YouTube = new YouTube(File.ReadAllText(@"W:\Other\rofl-games\YouTubePublish\client_secret.json"), @"W:\Other\rofl-games\YouTubePublish\UserLogin", "YoutubePublisher");
            
            // ----------------------------------------------------------------------
            
            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();
                
                while(__PendingMessages.TryDequeue(out string? Pending)){
                    Web.PostMessage(Pending);
                }
            }

            Window.Close();
        }catch(Exception e){
            WL.Logger.Fatal("Всё взорвалось...", e);
            return 1;
        }
        
        return 0;
    }

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
                    string Name = Doc.RootElement.GetProperty("Data").GetString() ?? "";
                    __StartPublish(Name);
                    break;
                }

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

    private const           string   VideosFolder    = @"W:\Woowz11\Videos\Записи\YT";
    private const           string   PublishedFolder = @"W:\Woowz11\Videos\Записи\YT\Published";
    private static readonly string[] VideoExtensions = [".mp4", ".mov", ".mkv", ".avi", ".wmv", ".flv", ".webm"];
    
    private static void __SendVideoList(){
        List<VideoFileInfo> Videos = __ScanVideos();

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

            ParsedDate? Parsed = __TryParseDateFromName(FI.Name);
            bool HasDate = Parsed != null;

            DateTime SortDateTime = Parsed?.Date ?? FI.CreationTime;

            string DateString = Parsed switch{
                { HasSeconds: true } P => P.Date.ToString("yyyy.MM.dd HH:mm:ss"),
                { HasTime: true }    P => P.Date.ToString("yyyy.MM.dd HH:mm"),
                { } P                  => P.Date.ToString("yyyy.MM.dd"),
                null                   => FI.CreationTime.ToString("yyyy.MM.dd HH:mm:ss")
            };
            
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
    
    private static void __StartPublish(string Name){
        Task.Run(() => {
            try{
                string Path = System.IO.Path.Combine(VideosFolder, Name);

                if(!File.Exists(Path)){
                    __EnqueuePublishFailed(Name, "Файл не найден (возможно, уже опубликован, перемещён или удалён).");
                    return;
                }

                YouTubeOperationResult<YouTubeVideo> Result = YouTube.UploadVideo(
                    VideoPath: Path,
                    Title: System.IO.Path.GetFileNameWithoutExtension(Name),
                    Description: "",
                    Privacy: YouTubePrivacy.Unpublic,
                    OnProgress: (Sent, Total) => __EnqueueProgress(Name, Sent, Total)
                );

                if(!Result.Success){
                    WL.Logger.Error($"Не удалось опубликовать видео [{Name}]: {Result.FailReason}", Result.Error);
                    __EnqueuePublishFailed(Name, Result.FailReason.ToString());
                    return;
                }

                Directory.CreateDirectory(PublishedFolder);
                string DestinationPath = System.IO.Path.Combine(PublishedFolder, Name);
                File.Move(Path, DestinationPath, overwrite: true);

                __EnqueuePublishComplete(Name);
            }catch(Exception e){
                WL.Logger.Error($"Исключение при публикации видео [{Name}]!", e);
                __EnqueuePublishFailed(Name, e.Message);
            }
        });
    }

    private static void __EnqueueProgress(string Name, long Sent, long Total){
        double Percent = Total > 0 ? (double)Sent / Total * 100.0 : 0;

        string JSON = JsonSerializer.Serialize(new{
            Type = "PublishProgress",
            Data = new{ Name, BytesSent = Sent, TotalBytes = Total, Percent }
        });

        __PendingMessages.Enqueue(JSON);
    }

    private static void __EnqueuePublishComplete(string Name){
        string JSON = JsonSerializer.Serialize(new{ Type = "PublishComplete", Data = new{ Name } });
        __PendingMessages.Enqueue(JSON);
    }

    private static void __EnqueuePublishFailed(string Name, string Reason){
        string JSON = JsonSerializer.Serialize(new{ Type = "PublishFailed", Data = new{ Name, Reason } });
        __PendingMessages.Enqueue(JSON);
    }

    private static void __SendError(string Message, Exception e){
        WL.Logger.Error(Message, e);
        try{
            string Details = e.ToString();
            string JSON = JsonSerializer.Serialize(new{ Type = "Error", Data = new{ Message, Details } });
            Web.PostMessage(JSON);
        }catch{
            // ignored
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