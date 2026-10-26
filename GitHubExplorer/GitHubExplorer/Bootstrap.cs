using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using WLI;
using WLO;
using WLO.Database;
using WLO.Math;

namespace GitHubExplorer;

public static class ScanStatus{
    public const byte NotStarted = 0;
    public const byte InProgress = 1;
    public const byte Done       = 2;
}

public static class EntryType{
    public const byte File      = 0;
    public const byte Folder    = 1;
    public const byte Submodule = 2;
}

public class RepoRow{
    public int    Id            { get; set; }
    public string Owner         { get; set; } = "";
    public string Name          { get; set; } = "";
    public string DefaultBranch { get; set; } = "";
    public string CommitSha     { get; set; } = "";
    public byte   Status        { get; set; } = ScanStatus.NotStarted;
    public int    FilesFound    { get; set; }
    public int    FoldersFound  { get; set; }
    public long   TotalSize     { get; set; }
}

public class FileRow{
    public int    Id   { get; set; }
    public string Path { get; set; } = "";
    public long?  Size { get; set; }
    public byte   Type { get; set; }
}

public class PendingFolderRow{
    public int    Id   { get; set; }
    public string Path { get; set; } = "";
    public string Sha  { get; set; } = "";
}

public class ExplorerChildRow{
    public string ChildName { get; set; } = "";
    public int    IsFolder  { get; set; }
    public long?  TotalSize { get; set; }
    public int    FileCount { get; set; }
}

internal static class NativeFileDialog{
    private const int OFN_PATHMUSTEXIST = 0x00000800;
    private const int OFN_FILEMUSTEXIST = 0x00001000;
    private const int OFN_NOCHANGEDIR   = 0x00000008;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct OpenFileName{
        public int     StructSize;
        public IntPtr  Owner;
        public IntPtr  Instance;
        public string? Filter;
        public string? CustomFilter;
        public int     MaxCustFilter;
        public int     FilterIndex;
        public IntPtr  File;
        public int     MaxFile;
        public string? FileTitle;
        public int     MaxFileTitle;
        public string? InitialDir;
        public string? Title;
        public int     Flags;
        public short   FileOffset;
        public short   FileExtension;
        public string? DefExt;
        public IntPtr  CustData;
        public IntPtr  HookProc;
        public string? TemplateName;
        public IntPtr  ReservedPtr;
        public int     ReservedInt;
        public int     FlagsEx;
    }

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetOpenFileNameW(ref OpenFileName Ofn);

    public static string? OpenFile(string FilterText, string DefExt, string? InitialDir = null){
        const int BufferChars = 1024;
        var Buffer = Marshal.AllocHGlobal(BufferChars * 2);
        Marshal.WriteInt16(Buffer, 0, 0);

        try{
            var Ofn = new OpenFileName{
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter     = FilterText,
                File       = Buffer,
                MaxFile    = BufferChars,
                DefExt     = DefExt,
                InitialDir = InitialDir,
                Flags      = OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR
            };

            return GetOpenFileNameW(ref Ofn) ? Marshal.PtrToStringUni(Buffer) : null;
        }finally{
            Marshal.FreeHGlobal(Buffer);
        }
    }
}

public static class Bootstrap{
    private static WLO.Web.WebView2 Web    = null!;
    private static WLO.Window.GLFW  Window = null!;

    private static readonly ConcurrentQueue<string> __IncomingMessages = new();
    private static readonly ConcurrentQueue<string> __OutgoingMessages = new();

    private static Database? __DB;
    private static string?   __CurrentDbPath;

    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            Web = WLO.Web.WebView2.Create();
            Web.OnMessage += Msg => __IncomingMessages.Enqueue(Msg);

            Window = new WLO.Window.GLFW(new Vector2I(1100, 700), "GitHub Explorer", Web);
            Window.TODO_UseDarkMode();

            Web.NavigateCustom(__BuildHtml());

            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();

                while(__IncomingMessages.TryDequeue(out var Msg)){
                    __HandleMessage(Msg);
                }

                while(__OutgoingMessages.TryDequeue(out var OutJson)){
                    try{
                        Web.PostMessage(OutJson);
                    }catch(Exception E){
                        WL.Logger.Error("Не удалось отправить сообщение в UI", E);
                    }
                }
            }

            __DB?.Dispose();
            Window.Close();
        }catch(Exception E){
            WL.Logger.Fatal("Всё взорвалось?...", E);
            return WL.Core.Stop(1);
        }

        return WL.Core.Stop(0);
    }

    private static void __Send(string Type, object? Data = null){
        var Json = JsonSerializer.Serialize(new{ Type, Data });
        __OutgoingMessages.Enqueue(Json);
    }

    private static void __SendError(string Title, Exception E){
        WL.Logger.Error(Title, E);
        __Send("Error", new{ Title, Message = E.Message, Details = E.ToString() });
    }

    private static void __HandleMessage(string Json){
        try{
            using var Doc  = JsonDocument.Parse(Json);
            var       Root = Doc.RootElement;
            var       Type = Root.GetProperty("Type").GetString() ?? "";
            var       Data = Root.TryGetProperty("Data", out var D) ? D : default;

            switch(Type){
                case "Ready":
                    break;

                case "OpenDatabase":
                    __HandleOpenDatabase();
                    break;

                case "Navigate":{
                    var Path = Data.GetProperty("Path").GetString() ?? "";
                    __HandleNavigate(Path);
                    break;
                }
            }
        }catch(Exception E){
            __SendError("Ошибка обработки сообщения от интерфейса", E);
        }
    }

    private static void __HandleOpenDatabase(){
        var Path = NativeFileDialog.OpenFile("Файлы базы данных (*.db)\0*.db\0Все файлы (*.*)\0*.*\0\0", "db");
        if(Path == null) return;

        try{
            __DB?.Dispose();
            __DB            = SQLite.Open(Path);
            __CurrentDbPath = Path;

            var Repo = __DB.Table<RepoRow>().All().Take(1).ToList().FirstOrDefault();
            if(Repo == null) throw new Exception("В базе не найдена запись репозитория — похоже, это не файл результата GitHub Parser.");

            var PendingCount = __DB.Table<PendingFolderRow>().All().Count();

            __Send("DatabaseOpened", new{
                Path,
                Repo.Owner,
                Repo.Name,
                Repo.DefaultBranch,
                Repo.CommitSha,
                Repo.Status,
                Repo.FilesFound,
                Repo.FoldersFound,
                Repo.TotalSize,
                PendingCount
            });

            __HandleNavigate("");
        }catch(Exception E){
            __SendError("Не удалось открыть базу данных", E);
        }
    }

    private static void __HandleNavigate(string Path){
        if(__DB == null) return;

        try{
            var Prefix  = __DB.ParameterPrefix;
            var Escaped = Path.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            var Offset  = Path.Length + 1;

            var Sql = $"""
                SELECT
                    CASE WHEN instr(substr(Path, {Prefix}Offset), '/') > 0
                         THEN substr(substr(Path, {Prefix}Offset), 1, instr(substr(Path, {Prefix}Offset), '/') - 1)
                         ELSE substr(Path, {Prefix}Offset)
                    END AS ChildName,
                    MAX(CASE WHEN instr(substr(Path, {Prefix}Offset), '/') > 0 THEN 1 ELSE 0 END) AS IsFolder,
                    SUM(Size) AS TotalSize,
                    SUM(CASE WHEN Type = 0 THEN 1 ELSE 0 END) AS FileCount
                FROM FileRow
                WHERE Path LIKE {Prefix}LikePattern ESCAPE '\' AND Path <> {Prefix}ExactPath
                GROUP BY ChildName
                ORDER BY IsFolder DESC, ChildName COLLATE NOCASE ASC
                """;

            var Children = __DB.Query<ExplorerChildRow>(Sql, new{ Offset, LikePattern = Escaped + "%", ExactPath = Path });

            var FolderCount = Children.Count(C => C.IsFolder == 1);
            var FileCount   = Children.Where(C => C.IsFolder == 0).Sum(C => C.FileCount);
            var TotalSize   = Children.Sum(C => C.TotalSize ?? 0);

            __Send("FolderListing", new{
                Path,
                Children = Children.Select(C => new{ Name = C.ChildName, IsFolder = C.IsFolder == 1, Size = C.TotalSize, FileCount = C.FileCount }),
                FileCount,
                FolderCount,
                TotalSize
            });
        }catch(Exception E){
            __SendError("Не удалось получить список файлов", E);
        }
    }

    /* language=html */
    private static string __BuildHtml() => """
    <!DOCTYPE html>
    <html lang="ru">
    <head>
    <meta charset="utf-8" />
    <style>
        *{ box-sizing: border-box; }
        body{
            margin: 0;
            font-family: Segoe UI, sans-serif;
            background: #1e1e1e;
            color: #ddd;
            display: flex;
            height: 100vh;
            overflow: hidden;
            user-select: none;
        }
        #Sidebar{
            width: 340px;
            min-width: 340px;
            background: #252526;
            padding: 16px;
            display: flex;
            flex-direction: column;
            gap: 12px;
            border-right: 1px solid #333;
            overflow: hidden;
        }
        #Content{
            flex: 1;
            padding: 16px 20px;
            display: flex;
            flex-direction: column;
            gap: 10px;
            overflow: hidden;
        }
        label{ font-size: 12px; color: #999; }
        input{
            background: #1b1b1b;
            border: 1px solid #3c3c3c;
            color: #eee;
            padding: 8px;
            border-radius: 4px;
            font-size: 13px;
            width: 100%;
        }
        .FieldRow{ display: flex; flex-direction: column; gap: 6px; }
        .InlineButtons{ display: flex; gap: 6px; }
        button{
            background: #0e639c;
            border: none;
            color: #fff;
            padding: 9px;
            border-radius: 4px;
            cursor: pointer;
            font-size: 13px;
        }
        button:hover{ background: #1177bb; }
        button:disabled{ background: #444; cursor: not-allowed; opacity: .45; pointer-events: none; }
        button.Secondary{ background: #3a3a3a; }
        button.Secondary:hover{ background: #4a4a4a; }
        button.Danger{ background: #823030; }
        button.Danger:hover{ background: #9a3a3a; }

        #RepoInfo{ font-size: 12px; color: #aaa; line-height: 1.7; flex: 0 0 auto; }
        #RepoInfo b{ color: #ddd; }
        #RepoWarning{ color: #e0c33e; margin-top: 4px; }

        #DownloadQueuePanel{ flex: 1; display: flex; flex-direction: column; min-height: 120px; gap: 6px; }
        #DownloadDropZone{
            border: 2px dashed #444;
            border-radius: 4px;
            padding: 14px;
            text-align: center;
            color: #777;
            font-size: 12px;
            transition: background .15s, border-color .15s;
        }
        #DownloadDropZone.DragOver{ background: #1b3a52; border-color: #0e639c; color: #ccc; }
        #DownloadQueueList{ flex: 1; overflow-y: auto; display: flex; flex-direction: column; gap: 4px; }
        .QueueItem{
            display: flex;
            align-items: center;
            justify-content: space-between;
            background: #1b1b1b;
            border: 1px solid #333;
            border-radius: 4px;
            padding: 5px 8px;
            font-size: 12px;
            gap: 6px;
        }
        .QueueItem.Ignored{ border-color: #6b5a1f; background: #2a2416; }
        .QueueItem.Ignored .QueueItemName{ color: #d8c37a; }
        .QueueItem .QueueItemName{ overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
        .QueueItem button{ background: transparent; color: #999; padding: 0 4px; font-size: 14px; line-height: 1; }
        .QueueItem button:hover{ color: #e04f3e; background: transparent; }
        #DownloadQueueSummary{ font-size: 12px; color: #999; border-top: 1px solid #333; padding-top: 6px; }

        #Toolbar{ display: flex; align-items: center; gap: 10px; }
        #BackBtn{ padding: 6px 10px; font-size: 14px; background: #3a3a3a; }
        #BackBtn:hover{ background: #4a4a4a; }
        #Breadcrumb{ flex: 1; display: flex; flex-wrap: wrap; gap: 4px; font-size: 13px; }
        #Breadcrumb span{ cursor: pointer; color: #4fc1ff; }
        #Breadcrumb span:hover{ text-decoration: underline; }
        #Breadcrumb .Sep{ color: #666; cursor: default; }
        #SearchInput{ width: 240px; }

        #ListArea{
            flex: 1;
            overflow-y: auto;
            border: 1px solid #333;
            border-radius: 4px;
            --TileSize: 100px;
        }
        table{ width: 100%; border-collapse: collapse; }
        thead th{
            text-align: left;
            padding: 6px 14px;
            color: #999;
            font-weight: normal;
            font-size: 11px;
            border-bottom: 1px solid #333;
            position: sticky;
            top: 0;
            background: #1e1e1e;
        }
        tbody tr:hover{ background: #2a2d2e; }
        tbody tr.Queued{ background: #173a1a; }
        tbody tr.Queued:hover{ background: #1f4a23; }
        tbody tr.Selected{ background: #0e639c4d !important; }
        tbody td{ padding: 5px 14px; border-bottom: 1px solid #2a2a2a; white-space: nowrap; }
        tbody tr{ cursor: pointer; }
        #Placeholder{ margin: auto; color: #666; padding: 40px; text-align: center; }

        #TileGrid{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(var(--TileSize), 1fr));
            gap: 10px;
            padding: 10px;
            min-height: 100%;
        }
        .Tile{
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 4px;
            padding: 8px;
            border-radius: 6px;
            cursor: pointer;
            text-align: center;
        }
        .Tile:hover{ background: #2a2d2e; }
        .Tile.Queued{ background: #173a1a; outline: 1px solid #2d8a3e; }
        .Tile.Selected{ background: #0e639c4d !important; outline: 1px solid #0e639c; }
        .Tile .TileIcon{ font-size: calc(var(--TileSize) * 0.35); line-height: 1; }
        .Tile .TileName{ font-size: 11px; word-break: break-word; max-width: 100%; margin-top: 6px; }

        #BottomBar{ display: flex; align-items: center; justify-content: space-between; gap: 14px; }
        #StatusBar{ font-size: 12px; color: #999; }
        #ViewControls{ display: flex; align-items: center; gap: 14px; }
        .ViewToggleBtn{ background: #3a3a3a; padding: 6px 10px; font-size: 12px; }
        .ViewToggleBtn.Active{ background: #0e639c; }
        .ViewToggleBtn:hover{ background: #1177bb; }
        #TileSizeLabel{ font-size: 12px; color: #999; display: none; align-items: center; gap: 6px; }

        #SelectionInfo{
            display: none;
            font-size: 12px;
            color: #cfe8ff;
            border: 1px solid #4fc1ff;
            border-radius: 4px;
            padding: 7px 12px;
            background: #103049;
        }

        #ContextMenu{
            position: fixed;
            display: none;
            background: #2b2b2b;
            border: 1px solid #444;
            border-radius: 4px;
            padding: 4px;
            min-width: 200px;
            box-shadow: 0 4px 14px rgba(0,0,0,.4);
            z-index: 1000;
        }
        #ContextMenu .MenuItem{
            padding: 7px 12px;
            font-size: 13px;
            border-radius: 3px;
            cursor: pointer;
            color: #ddd;
        }
        #ContextMenu .MenuItem:hover{ background: #0e639c; }
        #ContextMenu .MenuSep{ height: 1px; background: #444; margin: 4px 0; }
    </style>
    </head>
    <body>

    <div id="Sidebar">
        <div class="FieldRow">
            <label>Файл базы данных (.db)</label>
            <input id="DbPathInput" readonly placeholder="База не выбрана" />
            <div class="InlineButtons">
                <button id="OpenDbBtn">📂 Открыть базу</button>
            </div>
        </div>
        <div id="RepoInfo"></div>

        <div id="DownloadQueuePanel">
            <label>Очередь на скачивание</label>
            <div id="DownloadDropZone">Перетащите сюда выделенные файлы или папки</div>
            <div id="DownloadQueueList"></div>
            <div id="DownloadQueueSummary"></div>
            <div class="InlineButtons">
                <button id="ClearQueueBtn" class="Danger">🗑 Удалить всё</button>
            </div>
        </div>
    </div>

    <div id="Content">
        <div id="Toolbar">
            <button id="BackBtn" disabled>←</button>
            <div id="Breadcrumb"></div>
            <input id="SearchInput" type="text" placeholder="🔍 Поиск в этой папке..." />
        </div>
        <div id="ListArea">
            <div id="Placeholder">Откройте файл базы данных, чтобы увидеть содержимое репозитория</div>
        </div>
        <div id="SelectionInfo"></div>
        <div id="BottomBar">
            <div id="StatusBar"></div>
            <div id="ViewControls">
                <button id="ViewListBtn" class="ViewToggleBtn Active">☰ Список</button>
                <button id="ViewTilesBtn" class="ViewToggleBtn">▦ Тайлы</button>
                <label id="TileSizeLabel">Размер тайлов <input type="range" id="TileSizeInput" min="60" max="220" value="100" /></label>
            </div>
        </div>
    </div>

    <div id="ContextMenu"></div>

    <script>
    /* language=js */
    (function(){
        var CurrentPath      = "";
        var LastChildren     = [];
        var SelectedPaths    = {};
        var LastClickedIndex = null;
        var ViewMode         = "list";
        var DownloadQueue    = {};

        function FormatSize(Bytes){
            if(Bytes == null) return "—";
            if(Bytes < 1024) return Bytes + " Б";
            var Units = ["КБ","МБ","ГБ","ТБ"];
            var Value = Bytes;
            var UnitIndex = -1;
            do{ Value /= 1024; UnitIndex++; }while(Value >= 1024 && UnitIndex < Units.length - 1);
            return Value.toFixed(2) + " " + Units[UnitIndex];
        }

        function Send(Type, Data){
            window.chrome.webview.postMessage(JSON.stringify({ Type: Type, Data: Data }));
        }

        function ChildFullPath(Item){
            return CurrentPath + Item.Name + (Item.IsFolder ? "/" : "");
        }

        function GetParentPath(Path){
            if(Path === "") return "";
            var Trimmed = Path.replace(/\/$/, "");
            var Idx = Trimmed.lastIndexOf("/");
            return Idx === -1 ? "" : Trimmed.substring(0, Idx + 1);
        }

        function GetQueueStatus(FullPath){
            if(DownloadQueue[FullPath]) return "direct";
            var Keys = Object.keys(DownloadQueue);
            for(var I = 0; I < Keys.length; I++){
                var Key   = Keys[I];
                var Entry = DownloadQueue[Key];
                if(Entry.IsFolder && Key !== FullPath && FullPath.indexOf(Key) === 0) return "covered";
            }
            return "none";
        }

        function IsQueueEntryIgnored(Path){
            var Keys = Object.keys(DownloadQueue);
            for(var I = 0; I < Keys.length; I++){
                var Key = Keys[I];
                if(Key === Path) continue;
                var Entry = DownloadQueue[Key];
                if(Entry.IsFolder && Path.indexOf(Key) === 0) return true;
            }
            return false;
        }

        function GetVisibleIndices(){
            var Term = (document.getElementById("SearchInput").value || "").trim().toLowerCase();
            var Indices = [];
            for(var I = 0; I < LastChildren.length; I++){
                if(Term === "" || LastChildren[I].Name.toLowerCase().indexOf(Term) !== -1) Indices.push(I);
            }
            return Indices;
        }

        function RenderBreadcrumb(Path){
            var Container = document.getElementById("Breadcrumb");
            Container.innerHTML = "";

            var Root = document.createElement("span");
            Root.textContent = "корень";
            Root.onclick = function(){ Navigate(""); };
            Container.appendChild(Root);

            document.getElementById("BackBtn").disabled = (Path === "");

            if(Path === "") return;

            var Parts = Path.replace(/\/$/, "").split("/");
            var Acc = "";
            for(var I = 0; I < Parts.length; I++){
                Acc += Parts[I] + "/";

                var Sep = document.createElement("span");
                Sep.textContent = " / ";
                Sep.className = "Sep";
                Container.appendChild(Sep);

                (function(Captured){
                    var Seg = document.createElement("span");
                    Seg.textContent = Parts[I];
                    Seg.onclick = function(){ Navigate(Captured); };
                    Container.appendChild(Seg);
                })(Acc);
            }
        }

        function UpdateSelectionInfo(){
            var Keys = Object.keys(SelectedPaths);
            var Box = document.getElementById("SelectionInfo");

            if(Keys.length === 0){
                Box.style.display = "none";
                return;
            }

            var FolderCount = 0, FileCount = 0, TotalSize = 0;
            for(var I = 0; I < Keys.length; I++){
                var Item = SelectedPaths[Keys[I]];
                if(Item.IsFolder) FolderCount++;
                FileCount += (Item.IsFolder ? (Item.FileCount || 0) : 1);
                TotalSize += (Item.Size || 0);
            }

            Box.style.display = "block";
            Box.textContent = "Выбрано: файлов " + FileCount + ", папок " + FolderCount + ", суммарный размер " + FormatSize(TotalSize);
        }

        function ClearSelection(){
            SelectedPaths    = {};
            LastClickedIndex = null;
            RenderChildren();
            UpdateSelectionInfo();
        }

        function HandleItemClick(Event, Index){
            Event.stopPropagation();
            var FullPath = ChildFullPath(LastChildren[Index]);

            if(Event.shiftKey && LastClickedIndex !== null){
                if(!Event.ctrlKey && !Event.metaKey) SelectedPaths = {};
                var From = Math.min(LastClickedIndex, Index);
                var To   = Math.max(LastClickedIndex, Index);
                for(var I = From; I <= To; I++){
                    SelectedPaths[ChildFullPath(LastChildren[I])] = LastChildren[I];
                }
            }else if(Event.ctrlKey || Event.metaKey){
                if(SelectedPaths[FullPath]) delete SelectedPaths[FullPath];
                else SelectedPaths[FullPath] = LastChildren[Index];
                LastClickedIndex = Index;
            }else{
                SelectedPaths = {};
                SelectedPaths[FullPath] = LastChildren[Index];
                LastClickedIndex = Index;
            }

            RenderChildren();
            UpdateSelectionInfo();
        }

        function HandleAreaBackgroundClick(Event){
            if(Event.target === Event.currentTarget
                || Event.target.id === "TileGrid"
                || Event.target.tagName === "TABLE"
                || Event.target.tagName === "TBODY"
                || Event.target.id === "Placeholder"){
                ClearSelection();
            }
        }

        function HandleDragStart(Event, Index){
            var FullPath = ChildFullPath(LastChildren[Index]);

            if(!SelectedPaths[FullPath]){
                SelectedPaths = {};
                SelectedPaths[FullPath] = LastChildren[Index];
                RenderChildren();
                UpdateSelectionInfo();
            }

            var Paths   = Object.keys(SelectedPaths);
            var Payload = Paths.map(function(P){ return { Path: P, IsFolder: SelectedPaths[P].IsFolder, Size: SelectedPaths[P].Size, FileCount: SelectedPaths[P].FileCount }; });

            Event.dataTransfer.setData("text/plain", JSON.stringify(Payload));
            Event.dataTransfer.effectAllowed = "copy";
        }

        function AddItemsToQueue(Items){
            var Added = 0;
            for(var I = 0; I < Items.length; I++){
                var It = Items[I];
                if(GetQueueStatus(It.Path) !== "none") continue;
                DownloadQueue[It.Path] = { Path: It.Path, IsFolder: It.IsFolder, Size: It.Size, FileCount: It.FileCount };
                Added++;
            }
            if(Added > 0) RenderDownloadQueue();
            RenderChildren();
        }

        function AddSelectionToQueue(){
            var Keys  = Object.keys(SelectedPaths);
            var Items = Keys.map(function(K){ var It = SelectedPaths[K]; return { Path: K, IsFolder: It.IsFolder, Size: It.Size, FileCount: It.FileCount }; });
            AddItemsToQueue(Items);
        }

        function HandleContextMenu(Event, Index){
            Event.preventDefault();
            Event.stopPropagation();

            var FullPath = ChildFullPath(LastChildren[Index]);
            if(!SelectedPaths[FullPath]){
                SelectedPaths = {};
                SelectedPaths[FullPath] = LastChildren[Index];
                RenderChildren();
                UpdateSelectionInfo();
            }

            var SelectedCount = Object.keys(SelectedPaths).length;
            var Item          = LastChildren[Index];

            var Items = [];
            if(SelectedCount === 1 && Item.IsFolder){
                Items.push({ Label: "📂 Открыть", Action: function(){ Navigate(FullPath); } });
                Items.push({ Separator: true });
            }

            Items.push({ Label: "⬇ Добавить в очередь на скачивание", Action: AddSelectionToQueue });
            Items.push({ Label: "📋 Копировать путь", Action: CopySelectedPaths });

            ShowContextMenu(Event.clientX, Event.clientY, Items);
        }

        function HandleAreaContextMenu(Event){
            if(Event.target.id !== "ListArea" && Event.target.id !== "TileGrid" && Event.target.tagName !== "TABLE" && Event.target.tagName !== "TBODY") return;
            Event.preventDefault();

            ShowContextMenu(Event.clientX, Event.clientY, [
                { Label: "🔄 Обновить", Action: function(){ Navigate(CurrentPath); } }
            ]);
        }

        function CopySelectedPaths(){
            var Keys = Object.keys(SelectedPaths);
            if(navigator.clipboard && navigator.clipboard.writeText){
                navigator.clipboard.writeText(Keys.join("\n"));
            }
        }

        function ShowContextMenu(X, Y, Items){
            var Menu = document.getElementById("ContextMenu");
            Menu.innerHTML = "";

            for(var I = 0; I < Items.length; I++){
                if(Items[I].Separator){
                    var Sep = document.createElement("div");
                    Sep.className = "MenuSep";
                    Menu.appendChild(Sep);
                    continue;
                }

                (function(Entry){
                    var El = document.createElement("div");
                    El.className = "MenuItem";
                    El.textContent = Entry.Label;
                    El.onclick = function(){
                        HideContextMenu();
                        Entry.Action();
                    };
                    Menu.appendChild(El);
                })(Items[I]);
            }

            Menu.style.display = "block";
            Menu.style.left = Math.min(X, window.innerWidth - 220) + "px";
            Menu.style.top  = Math.min(Y, window.innerHeight - (Items.length * 34 + 20)) + "px";
        }

        function HideContextMenu(){
            document.getElementById("ContextMenu").style.display = "none";
        }

        document.addEventListener("click", function(Event){
            if(Event.target.closest && !Event.target.closest("#ContextMenu")) HideContextMenu();
        });
        document.addEventListener("scroll", HideContextMenu, true);
        window.addEventListener("blur", HideContextMenu);

        function RenderListView(){
            var Area    = document.getElementById("ListArea");
            var Indices = GetVisibleIndices();

            if(LastChildren.length === 0){
                Area.innerHTML = '<div id="Placeholder">Папка пуста</div>';
                return;
            }
            if(Indices.length === 0){
                Area.innerHTML = '<div id="Placeholder">Совпадений не найдено</div>';
                return;
            }

            var Html = "<table><thead><tr><th>Имя</th><th>Тип</th><th>Размер</th></tr></thead><tbody>";
            for(var K = 0; K < Indices.length; K++){
                var I          = Indices[K];
                var Item       = LastChildren[I];
                var FullPath   = ChildFullPath(Item);
                var Icon       = Item.IsFolder ? "📁" : "📄";
                var Status     = GetQueueStatus(FullPath);
                var RowClasses = [];
                if(SelectedPaths[FullPath]) RowClasses.push("Selected");
                if(Status !== "none") RowClasses.push("Queued");

                Html += '<tr class="' + RowClasses.join(" ") + '" draggable="true" data-index="' + I + '">'
                     +  '<td>' + Icon + ' ' + Item.Name + '</td>'
                     +  '<td>' + (Item.IsFolder ? "Папка" : "Файл") + '</td>'
                     +  '<td>' + FormatSize(Item.Size) + '</td>'
                     +  '</tr>';
            }
            Html += "</tbody></table>";
            Area.innerHTML = Html;

            var Rows = Area.querySelectorAll("tbody tr");
            for(var J = 0; J < Rows.length; J++){
                (function(Row){
                    var Index = parseInt(Row.dataset.index, 10);
                    Row.onclick       = function(Event){ HandleItemClick(Event, Index); };
                    Row.ondblclick    = function(){
                        var Item = LastChildren[Index];
                        if(Item.IsFolder) Navigate(CurrentPath + Item.Name + "/");
                    };
                    Row.ondragstart   = function(Event){ HandleDragStart(Event, Index); };
                    Row.oncontextmenu = function(Event){ HandleContextMenu(Event, Index); };
                })(Rows[J]);
            }
        }

        function RenderTileView(){
            var Area    = document.getElementById("ListArea");
            var Indices = GetVisibleIndices();

            if(LastChildren.length === 0){
                Area.innerHTML = '<div id="Placeholder">Папка пуста</div>';
                return;
            }
            if(Indices.length === 0){
                Area.innerHTML = '<div id="Placeholder">Совпадений не найдено</div>';
                return;
            }

            var Html = '<div id="TileGrid">';
            for(var K = 0; K < Indices.length; K++){
                var I           = Indices[K];
                var Item        = LastChildren[I];
                var FullPath    = ChildFullPath(Item);
                var Icon        = Item.IsFolder ? "📁" : "📄";
                var Status      = GetQueueStatus(FullPath);
                var TileClasses = ["Tile"];
                if(SelectedPaths[FullPath]) TileClasses.push("Selected");
                if(Status !== "none") TileClasses.push("Queued");

                Html += '<div class="' + TileClasses.join(" ") + '" draggable="true" data-index="' + I + '">'
                     +  '<div class="TileIcon">' + Icon + '</div>'
                     +  '<div class="TileName">' + Item.Name + '</div>'
                     +  '</div>';
            }
            Html += '</div>';
            Area.innerHTML = Html;

            var Tiles = Area.querySelectorAll(".Tile");
            for(var J = 0; J < Tiles.length; J++){
                (function(Tile){
                    var Index = parseInt(Tile.dataset.index, 10);
                    Tile.onclick       = function(Event){ HandleItemClick(Event, Index); };
                    Tile.ondblclick    = function(){
                        var Item = LastChildren[Index];
                        if(Item.IsFolder) Navigate(CurrentPath + Item.Name + "/");
                    };
                    Tile.ondragstart   = function(Event){ HandleDragStart(Event, Index); };
                    Tile.oncontextmenu = function(Event){ HandleContextMenu(Event, Index); };
                })(Tiles[J]);
            }
        }

        function RenderChildren(){
            if(ViewMode === "list") RenderListView();
            else RenderTileView();
        }

        function Navigate(Path){
            CurrentPath = Path;
            document.getElementById("SearchInput").value = "";
            Send("Navigate", { Path: Path });
        }

        function UpdateStatusBar(Data){
            document.getElementById("StatusBar").textContent =
                "Файлов: " + Data.FileCount + " · Папок: " + Data.FolderCount + " · Суммарный размер: " + FormatSize(Data.TotalSize);
        }

        function UpdateRepoInfo(Repo){
            var Html =
                "<b>" + Repo.Owner + "/" + Repo.Name + "</b><br/>" +
                "Ветка: " + Repo.DefaultBranch + "<br/>" +
                "Коммит: " + Repo.CommitSha.substring(0, 10) + "<br/>" +
                "Всего файлов: " + Repo.FilesFound + "<br/>" +
                "Всего папок: " + Repo.FoldersFound + "<br/>" +
                "Общий размер: " + FormatSize(Repo.TotalSize);

            if(Repo.Status !== 2){
                Html += '<div id="RepoWarning">⚠ Сканирование не завершено' +
                    (Repo.PendingCount > 0 ? " (в очереди " + Repo.PendingCount + " папок)" : "") +
                    '</div>';
            }

            document.getElementById("RepoInfo").innerHTML = Html;
        }

        function RenderDownloadQueue(){
            var Container = document.getElementById("DownloadQueueList");
            var Summary   = document.getElementById("DownloadQueueSummary");
            var Keys      = Object.keys(DownloadQueue);

            if(Keys.length === 0){
                Container.innerHTML = "";
                Summary.textContent = "";
                return;
            }

            var Html = "";
            var TotalFiles = 0, TotalSize = 0;
            for(var I = 0; I < Keys.length; I++){
                var Path     = Keys[I];
                var Entry    = DownloadQueue[Path];
                var Icon     = Entry.IsFolder ? "📁" : "📄";
                var Ignored  = IsQueueEntryIgnored(Path);
                var ItemCls  = Ignored ? "QueueItem Ignored" : "QueueItem";
                var Suffix   = Ignored ? ' <span title="Уже входит в другую добавленную папку">(перекрыто)</span>' : "";

                Html += '<div class="' + ItemCls + '"><span class="QueueItemName" title="' + Path + '">' + Icon + ' ' + Path + Suffix + '</span>'
                     +  '<button data-path="' + Path + '">×</button></div>';

                if(!Ignored){
                    TotalFiles += (Entry.IsFolder ? (Entry.FileCount || 0) : 1);
                    TotalSize  += (Entry.Size || 0);
                }
            }
            Container.innerHTML = Html;
            Summary.textContent = "Всего файлов: " + TotalFiles + " · Общий размер: " + FormatSize(TotalSize);

            var RemoveButtons = Container.querySelectorAll("button[data-path]");
            for(var J = 0; J < RemoveButtons.length; J++){
                (function(Btn){
                    Btn.onclick = function(){
                        delete DownloadQueue[Btn.dataset.path];
                        RenderDownloadQueue();
                        RenderChildren();
                    };
                })(RemoveButtons[J]);
            }
        }

        document.getElementById("OpenDbBtn").onclick = function(){ Send("OpenDatabase", {}); };

        document.getElementById("BackBtn").onclick = function(){ Navigate(GetParentPath(CurrentPath)); };

        document.getElementById("ClearQueueBtn").onclick = function(){
            DownloadQueue = {};
            RenderDownloadQueue();
            RenderChildren();
        };

        document.getElementById("SearchInput").oninput = function(){ RenderChildren(); };

        var DropZone = document.getElementById("DownloadDropZone");
        DropZone.ondragover = function(Event){
            Event.preventDefault();
            DropZone.classList.add("DragOver");
        };
        DropZone.ondragleave = function(){
            DropZone.classList.remove("DragOver");
        };
        DropZone.ondrop = function(Event){
            Event.preventDefault();
            DropZone.classList.remove("DragOver");

            var Raw = Event.dataTransfer.getData("text/plain");
            if(!Raw) return;

            try{
                var Items = JSON.parse(Raw);
                AddItemsToQueue(Items);
            }catch(E){}
        };

        var ListAreaEl = document.getElementById("ListArea");
        ListAreaEl.onclick = HandleAreaBackgroundClick;
        ListAreaEl.oncontextmenu = HandleAreaContextMenu;

        document.getElementById("ViewListBtn").onclick = function(){
            ViewMode = "list";
            document.getElementById("ViewListBtn").classList.add("Active");
            document.getElementById("ViewTilesBtn").classList.remove("Active");
            document.getElementById("TileSizeLabel").style.display = "none";
            RenderChildren();
        };

        document.getElementById("ViewTilesBtn").onclick = function(){
            ViewMode = "tiles";
            document.getElementById("ViewTilesBtn").classList.add("Active");
            document.getElementById("ViewListBtn").classList.remove("Active");
            document.getElementById("TileSizeLabel").style.display = "flex";
            RenderChildren();
        };

        document.getElementById("TileSizeInput").oninput = function(){
            document.getElementById("ListArea").style.setProperty("--TileSize", this.value + "px");
        };

        if(window.chrome && window.chrome.webview){
            window.chrome.webview.addEventListener("message", function(Event){
                OnHostMessage({ data: Event.data });
            });
        }

        function OnHostMessage(Event){
            var Msg;
            try{ Msg = typeof Event.data === "string" ? JSON.parse(Event.data) : Event.data; }
            catch(E){ return; }

            switch(Msg.Type){
                case "DatabaseOpened":
                    document.getElementById("DbPathInput").value = Msg.Data.Path;
                    UpdateRepoInfo(Msg.Data);
                    break;

                case "FolderListing":
                    RenderBreadcrumb(Msg.Data.Path);
                    LastChildren     = Msg.Data.Children;
                    SelectedPaths    = {};
                    LastClickedIndex = null;
                    RenderChildren();
                    UpdateSelectionInfo();
                    UpdateStatusBar(Msg.Data);
                    break;

                case "Error":
                    alert((Msg.Data.Title || "Ошибка") + "\n\n" + (Msg.Data.Message || ""));
                    break;
            }
        }

        Send("Ready");
    })();
    </script>
    </body>
    </html>
    """;
}