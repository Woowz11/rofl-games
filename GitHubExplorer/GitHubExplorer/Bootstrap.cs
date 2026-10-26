using System;
using System.Collections.Concurrent;
using System.Diagnostics;
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

public class SearchResultRow{
    public string Path { get; set; } = "";
    public long?  Size { get; set; }
    public byte   Type { get; set; }
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

    private const int SearchResultLimit = 500;

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

                case "OpenExternal":{
                    var Url = Data.GetProperty("Url").GetString() ?? "";
                    __HandleOpenExternal(Url);
                    break;
                }

                case "SearchRepo":{
                    var Term = Data.GetProperty("Term").GetString() ?? "";
                    __HandleSearchRepo(Term);
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

            // Сначала сообщаем UI, что открывается новый репозиторий — клиент сбросит
            // локальное состояние (путь, выделение, очередь, исключения, поиск),
            // чтобы не унаследовать данные от предыдущей базы.
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

    private static void __HandleSearchRepo(string Term){
        if(__DB == null) return;

        Term = Term.Trim();
        if(Term.Length == 0){
            __Send("SearchResults", new{ Term, Results = Array.Empty<object>(), Truncated = false });
            return;
        }

        try{
            var Prefix  = __DB.ParameterPrefix;
            var Escaped = Term.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

            // Простой поиск по вхождению в путь по всей базе (не только текущая папка).
            // Параметры и фильтры появятся позже — сейчас это базовый полнотекстовый поиск по пути.
            var Sql = $"""
                SELECT Path, Size, Type
                FROM FileRow
                WHERE Path LIKE {Prefix}Pattern ESCAPE '\'
                ORDER BY Path COLLATE NOCASE ASC
                LIMIT {Prefix}Limit
                """;

            var Rows = __DB.Query<SearchResultRow>(Sql, new{ Pattern = "%" + Escaped + "%", Limit = SearchResultLimit + 1 }).ToList();

            var Truncated = Rows.Count > SearchResultLimit;
            var Results   = Rows.Take(SearchResultLimit).Select(R => new{ R.Path, R.Size, R.Type });

            __Send("SearchResults", new{ Term, Results, Truncated });
        }catch(Exception E){
            __SendError("Ошибка поиска по репозиторию", E);
        }
    }

    private static void __HandleOpenExternal(string Url){
        if(string.IsNullOrWhiteSpace(Url)) return;

        try{
            if(!Url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
               !Url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)){
                return;
            }

            Process.Start(new ProcessStartInfo(Url){ UseShellExecute = true });
        }catch(Exception E){
            __SendError("Не удалось открыть ссылку", E);
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

        /* Тёмные полосы прокрутки — по умолчанию WebView2 (Chromium) рисует их светлыми */
        *::-webkit-scrollbar{ width: 10px; height: 10px; }
        *::-webkit-scrollbar-track{ background: #1e1e1e; }
        *::-webkit-scrollbar-thumb{ background: #4a4a4a; border-radius: 5px; border: 2px solid #1e1e1e; }
        *::-webkit-scrollbar-thumb:hover{ background: #5a5a5a; }
        *::-webkit-scrollbar-corner{ background: #1e1e1e; }

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

        /* Тёмный ползунок (range-слайдер размера тайлов) */
        input[type="range"]{
            -webkit-appearance: none;
            appearance: none;
            background: transparent;
            padding: 0;
            height: 16px;
        }
        input[type="range"]::-webkit-slider-runnable-track{
            height: 4px;
            background: #3c3c3c;
            border-radius: 2px;
        }
        input[type="range"]::-webkit-slider-thumb{
            -webkit-appearance: none;
            appearance: none;
            width: 14px;
            height: 14px;
            margin-top: -5px;
            border-radius: 50%;
            background: #0e639c;
            border: 2px solid #1e1e1e;
            cursor: pointer;
        }
        input[type="range"]::-webkit-slider-thumb:hover{ background: #1177bb; }

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
            white-space: nowrap;
        }
        button:hover{ background: #1177bb; }
        button:disabled{ background: #444; cursor: not-allowed; opacity: .45; pointer-events: none; }
        button.Secondary{ background: #3a3a3a; }
        button.Secondary:hover{ background: #4a4a4a; }
        button.Danger{ background: #823030; }
        button.Danger:hover{ background: #9a3a3a; }

        #RepoInfo{ font-size: 12px; color: #aaa; line-height: 1.7; flex: 0 0 auto; }
        #RepoInfo b{ color: #ddd; }
        #RepoInfo a{ color: #4fc1ff; text-decoration: none; }
        #RepoInfo a:hover{ text-decoration: underline; }
        #RepoWarning{ color: #e0c33e; margin-top: 4px; }

        #DownloadQueuePanel{ flex: 1; display: flex; flex-direction: column; min-height: 120px; gap: 8px; overflow: hidden; }
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

        /* Список очереди не растягивается на всю высоту сайдбара — иначе
           кнопки управления очередью "утекали" в самый низ. */
        #DownloadQueueList{ overflow-y: auto; display: flex; flex-direction: column; gap: 4px; max-height: 280px; }

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
        .QueueItem.Excluded{ border-color: #823030; background: #2a1616; }
        .QueueItem.Excluded .QueueItemName{ color: #ff9b9b; }
        .QueueSubItem{ margin-left: 16px; }
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
        #GlobalSearchBtn{ background: #3a3a3a; }
        #GlobalSearchBtn:hover{ background: #4a4a4a; }

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
        thead th.SortableHeader{ cursor: pointer; user-select: none; }
        thead th.SortableHeader:hover{ color: #ccc; }
        tbody tr:hover{ background: #2a2d2e; }
        tbody tr.Queued{ background: #173a1a; }
        tbody tr.Queued:hover{ background: #1f4a23; }
        tbody tr.Excluded{ background: #3a1414; color: #ff9b9b; }
        tbody tr.Excluded:hover{ background: #4a1a1a; }
        tbody tr.Selected{ background: #0e639c4d !important; }
        tbody td{ padding: 5px 14px; border-bottom: 1px solid #2a2a2a; white-space: nowrap; }
        tbody td span[title]{ cursor: help; border-bottom: 1px dotted #666; }
        tbody tr{ cursor: pointer; }
        #Placeholder{ margin: auto; color: #666; padding: 40px; text-align: center; }

        /* grid-auto-rows/align-items: без этого тайлы растягивались по высоте
           на всю строку грида (по умолчанию align-items: stretch) */
        #TileGrid{
            display: grid;
            grid-template-columns: repeat(auto-fill, minmax(var(--TileSize), 1fr));
            grid-auto-rows: max-content;
            align-content: start;
            align-items: start;
            gap: 10px;
            padding: 10px;
            min-height: 100%;
        }
        .Tile{
            display: flex;
            flex-direction: column;
            align-items: center;
            gap: 2px;
            padding: 8px;
            border-radius: 6px;
            text-align: center;
        }
        .Tile.IsFolder{ cursor: pointer; }
        .Tile:not(.IsFolder){ cursor: default; }
        .Tile:hover{ background: #2a2d2e; }
        .Tile.Queued{ background: #173a1a; outline: 1px solid #2d8a3e; }
        .Tile.Excluded{ background: #3a1414; outline: 1px solid #a33838; }
        .Tile.Excluded .TileName{ color: #ff9b9b; }
        .Tile.Selected{ background: #0e639c4d !important; outline: 1px solid #0e639c; }
        .Tile .TileIcon{ font-size: calc(var(--TileSize) * 0.32); line-height: 1; }
        .Tile .TileName{ font-size: 11px; word-break: break-word; max-width: 100%; margin-top: 6px; color: #ccc; }
        .Tile.IsFolder .TileName{ font-weight: 700; color: #fff; }
        .Tile .TileMeta{ font-size: 10px; color: #888; margin-top: 2px; white-space: nowrap; }
        .Tile .TileMeta span[title]{ cursor: help; border-bottom: 1px dotted #555; }

        #BottomBar{ display: flex; align-items: center; justify-content: space-between; gap: 14px; }
        #StatusBar{ font-size: 12px; color: #999; }
        #StatusBar span[title]{ cursor: help; border-bottom: 1px dotted #666; }
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
        #SelectionInfo span[title]{ cursor: help; border-bottom: 1px dotted #6ab2e0; }

        /* Панель поиска по всему репозиторию заменяет рабочую область */
        #GlobalSearchView{
            display: none;
            flex: 1;
            flex-direction: column;
            gap: 10px;
            overflow: hidden;
        }
        #Content.GlobalSearchActive #ListArea,
        #Content.GlobalSearchActive #SelectionInfo,
        #Content.GlobalSearchActive #BottomBar,
        #Content.GlobalSearchActive #BackBtn,
        #Content.GlobalSearchActive #Breadcrumb,
        #Content.GlobalSearchActive #SearchInput,
        #Content.GlobalSearchActive #GlobalSearchBtn{ display: none; }
        #Content.GlobalSearchActive #GlobalSearchView{ display: flex; }

        #GlobalSearchHeader{ display: flex; align-items: center; gap: 10px; }
        #GlobalSearchHeader strong{ color: #ddd; font-size: 14px; white-space: nowrap; }
        #GlobalSearchInput{ flex: 1; }
        #GlobalSearchHint{ font-size: 12px; color: #777; }
        #GlobalSearchResultsArea{ flex: 1; overflow-y: auto; border: 1px solid #333; border-radius: 4px; }
        #GlobalSearchPlaceholder{ margin: auto; color: #666; padding: 40px; text-align: center; }
        #GlobalSearchSummary{ font-size: 12px; color: #999; }

        #ContextMenu{
            position: fixed;
            display: none;
            background: #2b2b2b;
            border: 1px solid #444;
            border-radius: 4px;
            padding: 4px;
            min-width: 220px;
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

        #Toast{
            position: fixed;
            bottom: 20px;
            right: 20px;
            background: #2b2b2b;
            color: #fff;
            padding: 10px 16px;
            border-radius: 6px;
            font-size: 13px;
            box-shadow: 0 4px 14px rgba(0,0,0,.45);
            opacity: 0;
            transform: translateY(10px);
            transition: opacity .2s ease, transform .2s ease;
            pointer-events: none;
            z-index: 2000;
            border: 1px solid #444;
            max-width: 360px;
            word-break: break-all;
        }
        #Toast.Show{ opacity: 1; transform: translateY(0); }
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
            <div class="InlineButtons">
                <button id="ClearQueueBtn" class="Danger">🗑 Удалить всё</button>
            </div>
            <div id="DownloadQueueSummary"></div>
        </div>
    </div>

    <div id="Content">
        <div id="Toolbar">
            <button id="BackBtn" disabled>←</button>
            <div id="Breadcrumb"></div>
            <input id="SearchInput" type="text" placeholder="🔍 Поиск в этой папке..." />
            <button id="GlobalSearchBtn">🔎 Поиск по репозиторию</button>
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

        <div id="GlobalSearchView">
            <div id="GlobalSearchHeader">
                <button id="CloseGlobalSearchBtn" class="Secondary">← Назад к папкам</button>
                <strong>Поиск по всему репозиторию</strong>
                <input id="GlobalSearchInput" type="text" placeholder="Имя файла или часть пути..." />
                <button id="RunGlobalSearchBtn">Искать</button>
            </div>
            <div id="GlobalSearchHint">Дополнительные параметры поиска и фильтры появятся позже. Сейчас можно сортировать результаты по пути, типу и размеру — клик по заголовку столбца.</div>
            <div id="GlobalSearchResultsArea">
                <div id="GlobalSearchPlaceholder">Введите запрос и нажмите «Искать»</div>
            </div>
            <div id="GlobalSearchSummary"></div>
        </div>
    </div>

    <div id="ContextMenu"></div>
    <div id="Toast"></div>

    <script>
    /* language=js */
    (function(){
        var CurrentPath      = "";
        var LastChildren     = [];
        var SelectedPaths    = {};
        var LastClickedIndex = null;
        var ViewMode         = "list";
        var DownloadQueue    = {};
        var ExcludedPaths    = {}; // Path -> { IsFolder, Size, FileCount } — вручную исключённые из уже добавленных папок элементы
        var ToastTimeoutId   = null;

        var RepoTotalSize  = 0;
        var RepoTotalFiles = 0;

        var GlobalSearchResults   = [];
        var GlobalSearchTruncated = false;
        var GlobalSearchLastTerm  = "";
        var GlobalSearchSortField = "Path";
        var GlobalSearchSortDir   = 1; // 1 = по возрастанию, -1 = по убыванию

        function FormatSize(Bytes){
            if(Bytes == null) return "—";
            if(Bytes < 1024) return Bytes + " Б";
            var Units = ["КБ","МБ","ГБ","ТБ"];
            var Value = Bytes;
            var UnitIndex = -1;
            do{ Value /= 1024; UnitIndex++; }while(Value >= 1024 && UnitIndex < Units.length - 1);
            return Value.toFixed(2) + " " + Units[UnitIndex];
        }

        function FormatBytesExact(Bytes){
            if(Bytes == null) return "размер неизвестен";
            try{
                return Bytes.toLocaleString("ru-RU") + " байт";
            }catch(E){
                return Bytes + " байт";
            }
        }

        // Человекочитаемый размер + точный размер в байтах во всплывающей подсказке
        function SizeHtml(Bytes){
            return '<span title="' + FormatBytesExact(Bytes) + '">' + FormatSize(Bytes) + '</span>';
        }

        // Проценты от общего веса репозитория — показываются рядом с суммарным размером выделения
        function FormatPercentOfRepo(SelectedSize){
            if(!RepoTotalSize || RepoTotalSize <= 0) return "";
            var Percent = (SelectedSize / RepoTotalSize) * 100;
            if(Percent <= 0) return "";
            var Text = Percent < 0.1 ? "<0.1" : (Percent >= 10 ? Percent.toFixed(0) : Percent.toFixed(1));
            return ' <span title="' + FormatBytesExact(SelectedSize) + ' из ' + FormatBytesExact(RepoTotalSize) + '">(' + Text + '% от репозитория)</span>';
        }

        function ShowToast(Message){
            var El = document.getElementById("Toast");
            El.textContent = Message;
            El.classList.add("Show");
            if(ToastTimeoutId) clearTimeout(ToastTimeoutId);
            ToastTimeoutId = setTimeout(function(){ El.classList.remove("Show"); }, 2500);
        }

        // Отдельная функция копирования. Важно: в кастомном WebView2-окружении
        // navigator.clipboard.writeText() может ни разу не вызвать ни then, ни catch
        // (промис зависает из-за отсутствия обычной модели разрешений браузера).
        // Поэтому ставим таймаут-страховку с переключением на execCommand('copy').
        function CopyTextToClipboard(Text, OnDone){
            var Done = false;

            function Finish(Success){
                if(Done) return;
                Done = true;
                OnDone(Success);
            }

            function FallbackCopy(){
                var Success = false;
                try{
                    var TextArea = document.createElement("textarea");
                    TextArea.value = Text;
                    TextArea.style.position = "fixed";
                    TextArea.style.top = "-9999px";
                    TextArea.style.left = "-9999px";
                    document.body.appendChild(TextArea);
                    TextArea.focus();
                    TextArea.select();
                    Success = document.execCommand("copy");
                    document.body.removeChild(TextArea);
                }catch(E){
                    Success = false;
                }
                Finish(Success);
            }

            try{
                if(navigator.clipboard && navigator.clipboard.writeText){
                    var TimeoutId = setTimeout(FallbackCopy, 400);
                    navigator.clipboard.writeText(Text).then(
                        function(){ clearTimeout(TimeoutId); Finish(true); },
                        function(){ clearTimeout(TimeoutId); FallbackCopy(); }
                    );
                }else{
                    FallbackCopy();
                }
            }catch(E){
                FallbackCopy();
            }
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

        // Покрыт ли путь какой-то добавленной в очередь папкой-предком
        function IsCoveredByQueue(FullPath){
            var Keys = Object.keys(DownloadQueue);
            for(var I = 0; I < Keys.length; I++){
                var Key   = Keys[I];
                var Entry = DownloadQueue[Key];
                if(Entry.IsFolder && Key !== FullPath && FullPath.indexOf(Key) === 0) return true;
            }
            return false;
        }

        // Исключён ли путь напрямую или через исключённую папку-предка
        function IsPathExcluded(FullPath){
            if(ExcludedPaths[FullPath]) return true;
            var Keys = Object.keys(ExcludedPaths);
            for(var I = 0; I < Keys.length; I++){
                var Key   = Keys[I];
                var Entry = ExcludedPaths[Key];
                if(Entry.IsFolder && Key !== FullPath && FullPath.indexOf(Key) === 0) return true;
            }
            return false;
        }

        // Итоговый статус элемента для подсветки в списке/тайлах
        function GetEffectiveStatus(FullPath){
            if(IsPathExcluded(FullPath)) return "excluded";
            if(DownloadQueue[FullPath]) return "direct";
            if(IsCoveredByQueue(FullPath)) return "covered";
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

        // Исключение, перекрытое другим (вложенным выше) исключением — чтобы не считать дважды
        function IsExcludeEntryIgnored(Path){
            var Keys = Object.keys(ExcludedPaths);
            for(var I = 0; I < Keys.length; I++){
                var Key = Keys[I];
                if(Key === Path) continue;
                var Entry = ExcludedPaths[Key];
                if(Entry.IsFolder && Path.indexOf(Key) === 0) return true;
            }
            return false;
        }

        // Суммарные размер/кол-во файлов, исключённых внутри указанной папки очереди
        function GetExcludedTotalsUnder(FolderPath){
            var Size = 0, Files = 0;
            var Keys = Object.keys(ExcludedPaths);
            for(var I = 0; I < Keys.length; I++){
                var Key = Keys[I];
                if(Key === FolderPath || Key.indexOf(FolderPath) !== 0) continue;
                if(IsExcludeEntryIgnored(Key)) continue;
                var Entry = ExcludedPaths[Key];
                Files += (Entry.IsFolder ? (Entry.FileCount || 0) : 1);
                Size  += (Entry.Size || 0);
            }
            return { Size: Size, Files: Files };
        }

        // Удаляет исключения, которые больше не находятся внутри ни одной добавленной папки
        function CleanupOrphanedExclusions(){
            var Keys = Object.keys(ExcludedPaths);
            for(var I = 0; I < Keys.length; I++){
                if(!IsCoveredByQueue(Keys[I])) delete ExcludedPaths[Keys[I]];
            }
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

            // Раньше здесь суммировался FileCount рекурсивно (для папки — все файлы внутри неё),
            // из-за чего выделение одной папки с 500 файлами показывало "файлов: 500", хотя
            // физически был выбран один элемент. Теперь считаем ровно то, что реально выделено.
            var FolderCount = 0, FileCount = 0, TotalSize = 0;
            for(var I = 0; I < Keys.length; I++){
                var Item = SelectedPaths[Keys[I]];
                if(Item.IsFolder) FolderCount++;
                else FileCount++;
                TotalSize += (Item.Size || 0); // вес остаётся рекурсивным — это реальный объём на скачивание
            }

            Box.style.display = "block";
            Box.innerHTML = "Выбрано: файлов " + FileCount + ", папок " + FolderCount + ", суммарный размер " + SizeHtml(TotalSize) + FormatPercentOfRepo(TotalSize);
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

                // Не перерисовываем DOM синхронно внутри dragstart — если удалить
                // перетаскиваемый узел из документа прямо сейчас, браузер оборвёт drag.
                setTimeout(function(){
                    RenderChildren();
                    UpdateSelectionInfo();
                }, 0);
            }

            var Paths   = Object.keys(SelectedPaths);
            var Payload = Paths.map(function(P){ return { Path: P, IsFolder: SelectedPaths[P].IsFolder, Size: SelectedPaths[P].Size, FileCount: SelectedPaths[P].FileCount }; });

            Event.dataTransfer.setData("text/plain", JSON.stringify(Payload));
            Event.dataTransfer.effectAllowed = "copy";
        }

        function AddItemsToQueue(Items){
            var Changed = 0;
            for(var I = 0; I < Items.length; I++){
                var It = Items[I];

                // Перетаскивание ранее исключённого элемента обратно на дроп-зону — снимаем исключение
                if(ExcludedPaths[It.Path]){
                    delete ExcludedPaths[It.Path];
                    Changed++;
                    continue;
                }

                if(GetEffectiveStatus(It.Path) !== "none") continue;
                DownloadQueue[It.Path] = { Path: It.Path, IsFolder: It.IsFolder, Size: It.Size, FileCount: It.FileCount };
                Changed++;
            }
            if(Changed > 0) RenderDownloadQueue();
            RenderChildren();
        }

        function AddSelectionToQueue(){
            var Keys  = Object.keys(SelectedPaths);
            var Items = Keys.map(function(K){ var It = SelectedPaths[K]; return { Path: K, IsFolder: It.IsFolder, Size: It.Size, FileCount: It.FileCount }; });
            AddItemsToQueue(Items);
        }

        function RemoveSelectionFromQueue(){
            var Keys    = Object.keys(SelectedPaths);
            var Removed = 0;
            for(var I = 0; I < Keys.length; I++){
                if(DownloadQueue[Keys[I]]){
                    delete DownloadQueue[Keys[I]];
                    Removed++;
                }
            }
            if(Removed > 0){
                CleanupOrphanedExclusions();
                RenderDownloadQueue();
                RenderChildren();
                ShowToast(Removed === 1 ? "Убрано из очереди" : "Убрано из очереди: " + Removed);
            }
        }

        function ExcludeSelection(){
            var Keys  = Object.keys(SelectedPaths);
            var Added = 0;
            for(var I = 0; I < Keys.length; I++){
                var Path = Keys[I];
                if(ExcludedPaths[Path]) continue;
                if(!IsCoveredByQueue(Path)) continue;
                var It = SelectedPaths[Path];
                ExcludedPaths[Path] = { IsFolder: It.IsFolder, Size: It.Size, FileCount: It.FileCount };
                Added++;
            }
            if(Added > 0){
                RenderDownloadQueue();
                RenderChildren();
                ShowToast(Added === 1 ? "Исключено из скачивания" : "Исключено из скачивания: " + Added);
            }
        }

        function IncludeSelectionBack(){
            var Keys    = Object.keys(SelectedPaths);
            var Removed = 0;
            for(var I = 0; I < Keys.length; I++){
                if(ExcludedPaths[Keys[I]]){
                    delete ExcludedPaths[Keys[I]];
                    Removed++;
                }
            }
            if(Removed > 0){
                RenderDownloadQueue();
                RenderChildren();
                ShowToast(Removed === 1 ? "Исключение снято" : "Снято исключений: " + Removed);
            }
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

            var Keys = Object.keys(SelectedPaths);
            var Item = LastChildren[Index];

            var HasNone = false, HasQueuedDirect = false, HasCovered = false, HasExcludedDirect = false;
            for(var I = 0; I < Keys.length; I++){
                var P = Keys[I];
                if(ExcludedPaths[P])            HasExcludedDirect = true;
                else if(DownloadQueue[P])       HasQueuedDirect   = true;
                else if(IsCoveredByQueue(P))    HasCovered        = true;
                else if(!IsPathExcluded(P))     HasNone           = true;
            }

            var Items = [];
            if(Keys.length === 1 && Item.IsFolder){
                Items.push({ Label: "📂 Открыть", Action: function(){ Navigate(FullPath); } });
                Items.push({ Separator: true });
            }

            if(HasNone)           Items.push({ Label: "⬇ Добавить в очередь на скачивание", Action: AddSelectionToQueue });
            if(HasQueuedDirect)   Items.push({ Label: "✖ Убрать из очереди", Action: RemoveSelectionFromQueue });
            if(HasCovered)        Items.push({ Label: "🚫 Исключить из скачивания", Action: ExcludeSelection });
            if(HasExcludedDirect) Items.push({ Label: "✅ Убрать исключение", Action: IncludeSelectionBack });

            Items.push({ Separator: true });
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

        // Копирование путей с гарантированным уведомлением (что именно скопировано)
        function CopySelectedPaths(){
            var Keys = Object.keys(SelectedPaths);
            if(Keys.length === 0){
                ShowToast("Нечего копировать — ничего не выбрано");
                return;
            }

            var Text = Keys.join("\n");
            CopyTextToClipboard(Text, function(Success){
                if(!Success){
                    ShowToast("Не удалось скопировать путь");
                    return;
                }

                if(Keys.length === 1){
                    var Preview = Keys[0];
                    if(Preview.length > 70) Preview = Preview.slice(0, 67) + "...";
                    ShowToast("Скопировано: " + Preview);
                }else{
                    ShowToast("Скопировано путей: " + Keys.length);
                }
            });
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
            Menu.style.left = Math.min(X, window.innerWidth - 240) + "px";
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
                var Status     = GetEffectiveStatus(FullPath);
                var RowClasses = [];
                if(SelectedPaths[FullPath]) RowClasses.push("Selected");
                if(Status === "excluded") RowClasses.push("Excluded");
                else if(Status !== "none") RowClasses.push("Queued");

                Html += '<tr class="' + RowClasses.join(" ") + '" draggable="true" data-index="' + I + '">'
                     +  '<td>' + Icon + ' ' + Item.Name + '</td>'
                     +  '<td>' + (Item.IsFolder ? "Папка" : "Файл") + '</td>'
                     +  '<td>' + SizeHtml(Item.Size) + '</td>'
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
                var Status      = GetEffectiveStatus(FullPath);
                var TileClasses = ["Tile"];
                if(Item.IsFolder) TileClasses.push("IsFolder");
                if(SelectedPaths[FullPath]) TileClasses.push("Selected");
                if(Status === "excluded") TileClasses.push("Excluded");
                else if(Status !== "none") TileClasses.push("Queued");

                var TypeLabel = Item.IsFolder ? "Папка" : "Файл";

                Html += '<div class="' + TileClasses.join(" ") + '" draggable="true" data-index="' + I + '" title="' + Item.Name + '">'
                     +  '<div class="TileIcon">' + Icon + '</div>'
                     +  '<div class="TileName">' + Item.Name + '</div>'
                     +  '<div class="TileMeta">' + TypeLabel + ' · ' + SizeHtml(Item.Size) + '</div>'
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
            document.getElementById("StatusBar").innerHTML =
                "Файлов: " + Data.FileCount + " · Папок: " + Data.FolderCount + " · Суммарный размер: " + SizeHtml(Data.TotalSize);
        }

        function UpdateRepoInfo(Repo){
            var RepoUrl = "https://github.com/" + encodeURIComponent(Repo.Owner) + "/" + encodeURIComponent(Repo.Name);

            var Html =
                "<b>" + Repo.Owner + "/" + Repo.Name + "</b><br/>" +
                "Ветка: " + Repo.DefaultBranch + "<br/>" +
                "Коммит: " + Repo.CommitSha.substring(0, 10) + "<br/>" +
                "Всего файлов: " + Repo.FilesFound + "<br/>" +
                "Всего папок: " + Repo.FoldersFound + "<br/>" +
                "Общий размер: " + SizeHtml(Repo.TotalSize) + "<br/>" +
                '<a href="' + RepoUrl + '" id="RepoLink">🔗 Открыть на GitHub</a>';

            if(Repo.Status !== 2){
                Html += '<div id="RepoWarning">⚠ Сканирование не завершено' +
                    (Repo.PendingCount > 0 ? " (в очереди " + Repo.PendingCount + " папок)" : "") +
                    '</div>';
            }

            document.getElementById("RepoInfo").innerHTML = Html;

            var LinkEl = document.getElementById("RepoLink");
            if(LinkEl){
                LinkEl.onclick = function(Event){
                    Event.preventDefault();
                    Send("OpenExternal", { Url: RepoUrl });
                };
            }
        }

        function RenderDownloadQueue(){
            var Container = document.getElementById("DownloadQueueList");
            var Summary   = document.getElementById("DownloadQueueSummary");
            var Keys      = Object.keys(DownloadQueue);

            if(Keys.length === 0){
                Container.innerHTML = "";
                Summary.innerHTML = "";
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
                var TitleAttr = Path + " · " + FormatBytesExact(Entry.Size);

                Html += '<div class="' + ItemCls + '"><span class="QueueItemName" title="' + TitleAttr + '">' + Icon + ' ' + Path + Suffix + '</span>'
                     +  '<button data-path="' + Path + '">×</button></div>';

                if(!Ignored){
                    var ExcludedInfo = Entry.IsFolder ? GetExcludedTotalsUnder(Path) : { Size: 0, Files: 0 };
                    TotalFiles += (Entry.IsFolder ? (Entry.FileCount || 0) : 1) - ExcludedInfo.Files;
                    TotalSize  += (Entry.Size || 0) - ExcludedInfo.Size;

                    if(Entry.IsFolder){
                        var ExcludedKeys = Object.keys(ExcludedPaths).filter(function(P){
                            return P !== Path && P.indexOf(Path) === 0 && !IsExcludeEntryIgnored(P);
                        });
                        for(var K = 0; K < ExcludedKeys.length; K++){
                            var EPath  = ExcludedKeys[K];
                            var EEntry = ExcludedPaths[EPath];
                            var EIcon  = EEntry.IsFolder ? "📁" : "📄";
                            Html += '<div class="QueueItem QueueSubItem Excluded"><span class="QueueItemName" title="' + EPath + ' · ' + FormatBytesExact(EEntry.Size) + '">' + EIcon + ' ' + EPath + ' <i>(исключено)</i></span>'
                                 +  '<button data-excluded-path="' + EPath + '">×</button></div>';
                        }
                    }
                }
            }
            Container.innerHTML = Html;
            Summary.innerHTML = "Всего файлов: " + TotalFiles + " · Общий размер: " + SizeHtml(TotalSize);

            var RemoveButtons = Container.querySelectorAll("button[data-path]");
            for(var J = 0; J < RemoveButtons.length; J++){
                (function(Btn){
                    Btn.onclick = function(){
                        delete DownloadQueue[Btn.dataset.path];
                        CleanupOrphanedExclusions();
                        RenderDownloadQueue();
                        RenderChildren();
                    };
                })(RemoveButtons[J]);
            }

            var UnexcludeButtons = Container.querySelectorAll("button[data-excluded-path]");
            for(var M = 0; M < UnexcludeButtons.length; M++){
                (function(Btn){
                    Btn.onclick = function(){
                        delete ExcludedPaths[Btn.dataset.excludedPath];
                        RenderDownloadQueue();
                        RenderChildren();
                    };
                })(UnexcludeButtons[M]);
            }
        }

        // ---------- Поиск по всему репозиторию ----------

        function OpenGlobalSearch(){
            document.getElementById("Content").classList.add("GlobalSearchActive");
            document.getElementById("GlobalSearchInput").focus();
        }

        function CloseGlobalSearch(){
            document.getElementById("Content").classList.remove("GlobalSearchActive");
        }

        function RunGlobalSearch(){
            var Term = document.getElementById("GlobalSearchInput").value.trim();
            if(Term === ""){
                document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Введите запрос и нажмите «Искать»</div>';
                document.getElementById("GlobalSearchSummary").innerHTML = "";
                return;
            }
            document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Поиск...</div>';
            Send("SearchRepo", { Term: Term });
        }

        function SearchTypeLabel(Type){
            if(Type === 1) return "Папка";
            if(Type === 2) return "Подмодуль";
            return "Файл";
        }

        function SearchTypeIcon(Type){
            if(Type === 1) return "📁";
            if(Type === 2) return "🔗";
            return "📄";
        }

        function SortIndicator(Field){
            if(GlobalSearchSortField !== Field) return "";
            return GlobalSearchSortDir === 1 ? " ▲" : " ▼";
        }

        function SetGlobalSearchSort(Field){
            if(GlobalSearchSortField === Field) GlobalSearchSortDir = -GlobalSearchSortDir;
            else{
                GlobalSearchSortField = Field;
                GlobalSearchSortDir   = 1;
            }
            RenderGlobalSearchResults();
        }

        function RenderGlobalSearchResults(){
            var Area = document.getElementById("GlobalSearchResultsArea");

            if(GlobalSearchResults.length === 0){
                Area.innerHTML = '<div id="GlobalSearchPlaceholder">' + (GlobalSearchLastTerm ? "Ничего не найдено" : "Введите запрос и нажмите «Искать»") + '</div>';
                document.getElementById("GlobalSearchSummary").innerHTML = "";
                return;
            }

            var Sorted = GlobalSearchResults.slice();
            Sorted.sort(function(A, B){
                var Result = 0;
                if(GlobalSearchSortField === "Path"){
                    var AL = A.Path.toLowerCase(), BL = B.Path.toLowerCase();
                    Result = AL < BL ? -1 : (AL > BL ? 1 : 0);
                }else if(GlobalSearchSortField === "Type"){
                    Result = A.Type - B.Type;
                }else if(GlobalSearchSortField === "Size"){
                    Result = (A.Size || 0) - (B.Size || 0);
                }
                return Result * GlobalSearchSortDir;
            });

            var Html = '<table><thead><tr>'
                + '<th class="SortableHeader" data-field="Path">Путь' + SortIndicator("Path") + '</th>'
                + '<th class="SortableHeader" data-field="Type">Тип' + SortIndicator("Type") + '</th>'
                + '<th class="SortableHeader" data-field="Size">Размер' + SortIndicator("Size") + '</th>'
                + '</tr></thead><tbody>';

            for(var I = 0; I < Sorted.length; I++){
                var Item = Sorted[I];
                Html += '<tr data-path="' + Item.Path + '">'
                     +  '<td title="' + Item.Path + '">' + SearchTypeIcon(Item.Type) + ' ' + Item.Path + '</td>'
                     +  '<td>' + SearchTypeLabel(Item.Type) + '</td>'
                     +  '<td>' + SizeHtml(Item.Size) + '</td>'
                     +  '</tr>';
            }
            Html += '</tbody></table>';
            Area.innerHTML = Html;

            var Headers = Area.querySelectorAll(".SortableHeader");
            for(var H = 0; H < Headers.length; H++){
                (function(HeaderEl){
                    HeaderEl.onclick = function(){ SetGlobalSearchSort(HeaderEl.dataset.field); };
                })(Headers[H]);
            }

            var Rows = Area.querySelectorAll("tbody tr");
            for(var R = 0; R < Rows.length; R++){
                (function(RowEl){
                    RowEl.ondblclick = function(){
                        var Parent = GetParentPath(RowEl.dataset.path);
                        CloseGlobalSearch();
                        Navigate(Parent);
                    };
                })(Rows[R]);
            }

            document.getElementById("GlobalSearchSummary").textContent =
                "Найдено: " + GlobalSearchResults.length + (GlobalSearchTruncated ? " (показаны первые " + GlobalSearchResults.length + ", уточните запрос)" : "");
        }

        document.getElementById("GlobalSearchBtn").onclick     = OpenGlobalSearch;
        document.getElementById("CloseGlobalSearchBtn").onclick = CloseGlobalSearch;
        document.getElementById("RunGlobalSearchBtn").onclick   = RunGlobalSearch;
        document.getElementById("GlobalSearchInput").onkeydown  = function(Event){
            if(Event.key === "Enter") RunGlobalSearch();
        };

        // ---------- /Поиск по всему репозиторию ----------

        document.getElementById("OpenDbBtn").onclick = function(){ Send("OpenDatabase", {}); };

        document.getElementById("BackBtn").onclick = function(){ Navigate(GetParentPath(CurrentPath)); };

        document.getElementById("ClearQueueBtn").onclick = function(){
            DownloadQueue = {};
            ExcludedPaths = {};
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

                    RepoTotalSize  = Msg.Data.TotalSize  || 0;
                    RepoTotalFiles = Msg.Data.FilesFound || 0;

                    // Сбрасываем всё, что относится к предыдущему репозиторию —
                    // иначе очередь/исключения/выделение/путь/поиск будут указывать
                    // на несуществующие в новой базе объекты.
                    CurrentPath      = "";
                    SelectedPaths    = {};
                    LastClickedIndex = null;
                    DownloadQueue    = {};
                    ExcludedPaths    = {};
                    RenderDownloadQueue();

                    GlobalSearchResults  = [];
                    GlobalSearchLastTerm = "";
                    CloseGlobalSearch();
                    document.getElementById("GlobalSearchInput").value = "";
                    document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Введите запрос и нажмите «Искать»</div>';
                    document.getElementById("GlobalSearchSummary").innerHTML = "";
                    break;

                case "FolderListing":
                    // Путь — источник истины от сервера. Раньше CurrentPath обновлялся
                    // только внутри Navigate(), из-за чего после смены базы данных
                    // оставался путь от старого репозитория.
                    CurrentPath      = Msg.Data.Path;
                    LastChildren     = Msg.Data.Children;
                    SelectedPaths    = {};
                    LastClickedIndex = null;
                    RenderBreadcrumb(Msg.Data.Path);
                    RenderChildren();
                    UpdateSelectionInfo();
                    UpdateStatusBar(Msg.Data);
                    break;

                case "SearchResults":
                    GlobalSearchResults   = Msg.Data.Results || [];
                    GlobalSearchTruncated = Msg.Data.Truncated;
                    GlobalSearchLastTerm  = Msg.Data.Term;
                    RenderGlobalSearchResults();
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