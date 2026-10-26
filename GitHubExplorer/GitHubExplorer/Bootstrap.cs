using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
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

public class SearchRequestDto{
    public string Pattern   { get; set; } = "";
    public long   RequestId { get; set; }
}

public class ClassifyInfoRequest{
    public string Path     { get; set; } = "";
    public bool   IsFolder { get; set; }
}

public class ClassifyRuleDto{
    public string  Description { get; set; } = "";
    public string? Note        { get; set; }
    public double  Weight      { get; set; }
    public bool    Matched     { get; set; }
}

public class ClassifyBreakdownDto{
    public string ProfileKey  { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string IconUrl     { get; set; } = "";
    public double Score       { get; set; }
    public double Threshold   { get; set; }
    public double Percentage  { get; set; }
    public bool   IsMatch     { get; set; }
    public List<ClassifyRuleDto> Rules { get; set; } = new();
}

public class ProfileInfoDto{
    public string Key         { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string IconUrl     { get; set; } = "";
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
    private static readonly object __DbLock = new();

    private const int SearchResultLimit = 500;

    private static readonly CachedEntryClassifier __Classifier = new(KnownClassificationProfiles.BuildDefault());

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
                    var Path   = Data.GetProperty("Path").GetString() ?? "";
                    var ReqId  = Data.TryGetProperty("RequestId", out var RIdEl) ? RIdEl.GetInt64() : 0;
                    __NavigateAsync(Path, ReqId);
                    break;
                }

                case "OpenExternal":{
                    var Url = Data.GetProperty("Url").GetString() ?? "";
                    __HandleOpenExternal(Url);
                    break;
                }

                case "SearchRepo":{
                    SearchRequestDto Req;
                    try{
                        Req = Data.ValueKind == JsonValueKind.Object
                            ? (JsonSerializer.Deserialize<SearchRequestDto>(Data.GetRawText()) ?? new SearchRequestDto())
                            : new SearchRequestDto();
                    }catch{
                        Req = new SearchRequestDto();
                    }

                    var CapturedReq = Req;
                    Task.Run(() => __HandleSearchRepo(CapturedReq));
                    break;
                }

                case "ClassifyInfo":{
                    ClassifyInfoRequest Req;
                    try{
                        Req = Data.ValueKind == JsonValueKind.Object
                            ? (JsonSerializer.Deserialize<ClassifyInfoRequest>(Data.GetRawText()) ?? new ClassifyInfoRequest())
                            : new ClassifyInfoRequest();
                    }catch{
                        Req = new ClassifyInfoRequest();
                    }
                    __HandleClassifyInfo(Req);
                    break;
                }

                case "GetProfiles":
                    __HandleGetProfiles();
                    break;
            }
        }catch(Exception E){
            __SendError("Ошибка обработки сообщения от интерфейса", E);
        }
    }

    private static void __NavigateAsync(string Path, long RequestId){
        Task.Run(() => __HandleNavigate(Path, RequestId));
    }

    private static void __HandleOpenDatabase(){
        var Path = NativeFileDialog.OpenFile("Файлы базы данных (*.db)\0*.db\0Все файлы (*.*)\0*.*\0\0", "db");
        if(Path == null) return;

        try{
            RepoRow? Repo;
            int      PendingCount;

            lock(__DbLock){
                __DB?.Dispose();
                __DB            = SQLite.Open(Path);
                __CurrentDbPath = Path;

                Repo         = __DB.Table<RepoRow>().All().Take(1).ToList().FirstOrDefault();
                PendingCount = __DB.Table<PendingFolderRow>().All().Count();
            }

            __Classifier.InvalidateAll();

            if(Repo == null) throw new Exception("В базе не найдена запись репозитория — похоже, это не файл результата GitHub Parser.");

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

            __NavigateAsync("", 0);
        }catch(Exception E){
            __SendError("Не удалось открыть базу данных", E);
        }
    }

    private static void __HandleNavigate(string Path, long RequestId = 0){
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

            List<ExplorerChildRow> Children;
            lock(__DbLock){
                Children = __DB.Query<ExplorerChildRow>(Sql, new{ Offset, LikePattern = Escaped + "%", ExactPath = Path }).ToList();
            }

            var FolderCount = Children.Count(C => C.IsFolder == 1);
            var FileCount   = Children.Where(C => C.IsFolder == 0).Sum(C => C.FileCount);
            var TotalSize   = Children.Sum(C => C.TotalSize ?? 0);

            var FileClassification = __ClassifyFilesIn(Path);

            var ProjectedChildren = Children.Select(C => {
                var IsFolder = C.IsFolder == 1;
                string? ClassIconUrl = null;
                string? ClassLabel   = null;

                if(IsFolder){
                    var ChildPath      = Path + C.ChildName + "/";
                    var Classification = __ClassifyFolder(ChildPath);
                    if(Classification.Best != null){
                        ClassIconUrl = Classification.Best.Profile.IconUrl;
                        ClassLabel   = Classification.Best.Profile.DisplayName;
                    }
                }else if(FileClassification.InheritedBest != null){
                    ClassIconUrl = FileClassification.InheritedBest.Profile.IconUrl;
                    ClassLabel   = FileClassification.InheritedBest.Profile.DisplayName;
                }

                return new{
                    Name                  = C.ChildName,
                    IsFolder,
                    Size                  = C.TotalSize,
                    FileCount             = C.FileCount,
                    ClassificationIconUrl = ClassIconUrl,
                    ClassificationLabel   = ClassLabel
                };
            }).ToList();

            __Send("FolderListing", new{
                Path,
                Children = ProjectedChildren,
                FileCount,
                FolderCount,
                TotalSize,
                RequestId
            });
        }catch(Exception E){
            __SendError("Не удалось получить список файлов", E);
        }
    }

    private static ClassificationContext __BuildClassificationContext(string FolderPath){
        const int MaxEntries = 1500;

        var Ctx = new ClassificationContext{ FolderPath = FolderPath };
        if(__DB == null) return Ctx;

        var Prefix  = __DB.ParameterPrefix;
        var Escaped = FolderPath.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

        var Sql = $"""
            SELECT Path, Size, Type
            FROM FileRow
            WHERE Path LIKE {Prefix}LikePattern ESCAPE '\'
            LIMIT {Prefix}MaxEntries
            """;

        List<FileRow> Rows;
        lock(__DbLock){
            Rows = __DB.Query<FileRow>(Sql, new{ LikePattern = Escaped + "%", MaxEntries }).ToList();
        }

        var SeenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach(var Row in Rows){
            if(Row.Path.Length <= FolderPath.Length) continue;
            var RelPath = Row.Path.Substring(FolderPath.Length);

            var FileName  = RelPath;
            var LastSlash = FileName.LastIndexOf('/');
            if(LastSlash >= 0) FileName = FileName.Substring(LastSlash + 1);

            Ctx.Entries.Add(new ClassificationEntry{
                RelativePath = RelPath,
                Name         = FileName,
                IsFolder     = false,
                Size         = Row.Size ?? 0
            });

            var Segments = RelPath.Split('/');
            var Acc = "";
            for(var I = 0; I < Segments.Length - 1; I++){
                Acc = I == 0 ? Segments[I] : Acc + "/" + Segments[I];
                var FolderRel = Acc + "/";
                if(!SeenFolders.Add(FolderRel)) continue;

                Ctx.Entries.Add(new ClassificationEntry{
                    RelativePath = FolderRel,
                    Name         = Segments[I],
                    IsFolder     = true,
                    Size         = 0
                });
            }
        }

        return Ctx;
    }

    private static FolderClassification __ClassifyFolder(string FolderPath) =>
        __Classifier.ClassifyFolder(FolderPath, __BuildClassificationContext);

    private static FileClassification __ClassifyFilesIn(string FolderPath) =>
        __Classifier.ClassifyFile(FolderPath, __BuildClassificationContext);

    private static void __HandleClassifyInfo(ClassifyInfoRequest Req)
    {
        List<ClassificationBreakdown> Breakdown;
        string? SourceFolderPath;

        if (Req.IsFolder)
        {
            var FolderPath = Req.Path.EndsWith("/") ? Req.Path : Req.Path + "/";
            var Classification = __ClassifyFolder(FolderPath);
            Breakdown = Classification.Breakdown;
            SourceFolderPath = FolderPath;
        }
        else
        {
            var Idx = Req.Path.LastIndexOf('/');
            var FolderPath = Idx >= 0 ? Req.Path.Substring(0, Idx + 1) : "";
            var FileClassification = __ClassifyFilesIn(FolderPath);
            Breakdown = FileClassification.SourceBreakdown ?? new List<ClassificationBreakdown>();
            SourceFolderPath = FileClassification.SourceFolderPath;
        }

        var Profiles = Breakdown
                       .Where(B => B.Score > 0)
                       .OrderByDescending(B => B.Score)
                       .Select(B => new ClassifyBreakdownDto
                       {
                           ProfileKey = B.Profile.Key,
                           DisplayName = B.Profile.DisplayName,
                           IconUrl = B.Profile.IconUrl,
                           Score = B.Score,
                           Threshold = B.Profile.Threshold,
                           Percentage = B.Percentage,
                           IsMatch = B.IsMatch,
                           Rules = B.RuleMatches.Select(M => new ClassifyRuleDto
                           {
                               Description = M.Rule.Condition.Description,
                               Note = M.Rule.Note,
                               Weight = M.Rule.Weight,
                               Matched = M.Matched
                           }).ToList()
                       })
                       .ToList();

        __Send("ClassifyInfo", new { Path = Req.Path, SourceFolderPath, Profiles });
    }

    private static void __HandleGetProfiles(){
        var Profiles = __Classifier.Profiles.Select(P => new ProfileInfoDto{
            Key         = P.Key,
            DisplayName = P.DisplayName,
            IconUrl     = P.IconUrl
        }).ToList();

        __Send("ProfilesList", new{ Profiles });
    }

    private static List<List<(bool IsType, string Value)>> __ParseSearchGroups(string Pattern){
        var Groups = new List<List<(bool, string)>>();
        var OrParts = Pattern.Split(new[] { "||" }, StringSplitOptions.None);

        foreach(var OrPart in OrParts){
            var AndParts = OrPart.Split(new[] { "&&" }, StringSplitOptions.None);
            var Terms = new List<(bool, string)>();

            foreach(var Part in AndParts){
                var Trimmed = Part.Trim();
                if(Trimmed.Length == 0) continue;

                if(Trimmed.StartsWith("type:", StringComparison.OrdinalIgnoreCase))
                    Terms.Add((true, Trimmed.Substring(5).Trim()));
                else
                    Terms.Add((false, Trimmed));
            }

            Groups.Add(Terms);
        }

        return Groups;
    }

    private static bool __RowMatchesTypeTerm(string Value, bool RowIsFolder, string? ProfileKey, string? ProfileName){
        if(string.Equals(Value, "folder", StringComparison.OrdinalIgnoreCase)) return RowIsFolder;
        if(string.Equals(Value, "file", StringComparison.OrdinalIgnoreCase)) return !RowIsFolder;

        return (ProfileKey != null && ProfileKey.Contains(Value, StringComparison.OrdinalIgnoreCase))
            || (ProfileName != null && ProfileName.Contains(Value, StringComparison.OrdinalIgnoreCase));
    }

    private static void __HandleSearchRepo(SearchRequestDto Req){
        if(__DB == null) return;

        var Pattern = (Req.Pattern ?? "").Trim();

        if(Pattern.Length == 0){
            __Send("SearchResults", new{ Req.RequestId, Term = Pattern, Results = Array.Empty<object>(), Truncated = false });
            return;
        }

        try{
            var Groups        = __ParseSearchGroups(Pattern);
            var HasTypeFilter = Groups.Any(G => G.Any(T => T.IsType));

            List<FileRow> AllRows;
            lock(__DbLock){
                AllRows = __DB.Table<FileRow>().All().ToList();
            }

            var Total     = AllRows.Count;
            var Processed = 0;
            var ProgressSw = Stopwatch.StartNew();

            __Send("SearchProgress", new{ Req.RequestId, Processed = 0, Total });

            var Matched = new List<FileRow>();

            foreach(var Row in AllRows){
                Processed++;

                var RowIsFolder   = Row.Type == EntryType.Folder;
                string? ProfileKey  = null;
                string? ProfileName = null;

                if(HasTypeFilter){
                    if(RowIsFolder){
                        var FolderPath = Row.Path.EndsWith("/") ? Row.Path : Row.Path + "/";
                        var Classification = __ClassifyFolder(FolderPath);
                        ProfileKey  = Classification.Best?.Profile.Key;
                        ProfileName = Classification.Best?.Profile.DisplayName;
                    }else{
                        var Idx        = Row.Path.LastIndexOf('/');
                        var FolderPath = Idx >= 0 ? Row.Path.Substring(0, Idx + 1) : "";
                        var FileClassification = __ClassifyFilesIn(FolderPath);
                        ProfileKey  = FileClassification.InheritedBest?.Profile.Key;
                        ProfileName = FileClassification.InheritedBest?.Profile.DisplayName;
                    }
                }

                var AnyGroupMatched = false;
                foreach(var Group in Groups){
                    var AllTermsMatch = true;

                    foreach(var Term in Group){
                        bool TermOk;
                        if(Term.IsType)
                            TermOk = __RowMatchesTypeTerm(Term.Value, RowIsFolder, ProfileKey, ProfileName);
                        else
                            TermOk = GlobMatcher.MatchesSingle(Term.Value, Row.Path);

                        if(!TermOk){ AllTermsMatch = false; break; }
                    }

                    if(AllTermsMatch){ AnyGroupMatched = true; break; }
                }

                if(AnyGroupMatched){
                    Matched.Add(Row);
                    if(Matched.Count > SearchResultLimit) break;
                }

                if(ProgressSw.ElapsedMilliseconds >= 120){
                    __Send("SearchProgress", new{ Req.RequestId, Processed, Total });
                    ProgressSw.Restart();
                }
            }

            var Truncated = Matched.Count > SearchResultLimit;
            var Results   = (Truncated ? Matched.Take(SearchResultLimit) : Matched).Select(R => new{ R.Path, R.Size, R.Type });

            __Send("SearchResults", new{ Req.RequestId, Term = Pattern, Results, Truncated });
        }catch(Exception E){
            __Send("SearchResults", new{ Req.RequestId, Term = Pattern, Results = Array.Empty<object>(), Truncated = false });
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
        body.NoDatabase #Content{ display: none; }

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
        .QueueItem .EntryIcon{ width: 12px; height: 12px; }
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
        #SearchInput{ width: 220px; }
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
        .EntryIcon{ width: 14px; height: 14px; vertical-align: middle; object-fit: contain; }
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
        .Tile .TileIcon{ line-height: 1; display: flex; align-items: center; justify-content: center; height: calc(var(--TileSize) * 0.32); }
        .Tile .TileIcon .EntryIcon{ width: calc(var(--TileSize) * 0.32); height: calc(var(--TileSize) * 0.32); }
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
            align-items: center;
            justify-content: space-between;
            gap: 10px;
            font-size: 12px;
            color: #cfe8ff;
            border: 1px solid #4fc1ff;
            border-radius: 4px;
            padding: 7px 12px;
            background: #103049;
        }
        #SelectionInfo.Visible{ display: flex; }
        #SelectionInfo span[title]{ cursor: help; border-bottom: 1px dotted #6ab2e0; }
        #ClearSelectionBtn{
            background: transparent;
            border: none;
            color: #9fd1ff;
            cursor: pointer;
            font-size: 14px;
            padding: 2px 8px;
            flex-shrink: 0;
        }
        #ClearSelectionBtn:hover{ color: #fff; background: transparent; }

        #GlobalSearchView{
            display: none;
            flex: 1;
            flex-direction: column;
            gap: 10px;
            overflow: hidden;
        }
        #Content.GlobalSearchActive #ListArea,
        #Content.GlobalSearchActive #BottomBar,
        #Content.GlobalSearchActive #BackBtn,
        #Content.GlobalSearchActive #Breadcrumb,
        #Content.GlobalSearchActive #SearchInput,
        #Content.GlobalSearchActive #GlobalSearchBtn{ display: none; }
        #Content.GlobalSearchActive #GlobalSearchView{ display: flex; }

        #GlobalSearchHeader{ display: flex; align-items: center; gap: 10px; }
        #GlobalSearchHeader strong{ color: #ddd; font-size: 14px; white-space: nowrap; }
        #GlobalSearchInput{ flex: 1; }
        #GlobalSearchResultsArea{ flex: 1; overflow-y: auto; border: 1px solid #333; border-radius: 4px; }
        #GlobalSearchPlaceholder{ margin: auto; color: #666; padding: 40px; text-align: center; }
        #GlobalSearchSummary{ font-size: 12px; color: #999; }

        #GlobalSearchTypeMenuWrap{ position: relative; }
        #GlobalSearchTypeBtn svg{ display: inline-block; }
        .TypeMenu{
            display: none;
            position: absolute;
            top: 110%;
            right: 0;
            background: #2b2b2b;
            border: 1px solid #444;
            border-radius: 4px;
            padding: 4px;
            min-width: 190px;
            z-index: 50;
            box-shadow: 0 4px 14px rgba(0,0,0,.4);
        }
        .TypeMenu.Show{ display: block; }
        .TypeMenu .MenuItem{ padding: 7px 10px; font-size: 12px; border-radius: 3px; cursor: pointer; color: #ddd; display: flex; align-items: center; gap: 6px; }
        .TypeMenu .MenuItem:hover{ background: #0e639c; }
        .TypeMenu .MenuItem img{ width: 14px; height: 14px; }

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

        #ClassifyModal{
            position: fixed;
            inset: 0;
            display: none;
            align-items: center;
            justify-content: center;
            background: rgba(0,0,0,.55);
            z-index: 3000;
        }
        #ClassifyModal.Show{ display: flex; }
        #ClassifyModalBox{
            background: #252526;
            border: 1px solid #444;
            border-radius: 8px;
            width: 520px;
            max-height: 80vh;
            display: flex;
            flex-direction: column;
            overflow: hidden;
        }
        #ClassifyModalHeader{
            padding: 14px 16px;
            border-bottom: 1px solid #333;
            font-size: 13px;
            color: #ccc;
            display: flex;
            justify-content: space-between;
            align-items: center;
            gap: 10px;
        }
        #ClassifyModalHeader strong{ color: #fff; word-break: break-all; }
        #ClassifyModalBody{ padding: 12px 16px; display: flex; flex-direction: column; gap: 10px; overflow-y: auto; max-height: 60vh; }
        #ClassifyModalFooter{ padding: 10px 16px; border-top: 1px solid #333; text-align: right; }
        .ClassifyRow{ border: 1px solid #3a3a3a; border-radius: 6px; padding: 10px; }
        .ClassifyRow.ClassifyMatch{ border-color: #2d8a3e; background: #15291a; }
        .ClassifyHead{ display: flex; align-items: center; gap: 8px; font-size: 13px; margin-bottom: 6px; }
        .ClassifyHead img{ width: 16px; height: 16px; }
        .ClassifyHead .ClassifyPercent{ margin-left: auto; color: #999; font-size: 12px; }
        .ClassifyHint{ font-size: 11px; color: #777; margin: -4px 0 6px; }
        .ClassifyRules{ list-style: none; margin: 0; padding: 0; font-size: 12px; display: flex; flex-direction: column; gap: 3px; }
        .ClassifyRules li{ cursor: help; border-bottom: 1px dotted transparent; width: fit-content; }
        .ClassifyRules li:hover{ border-bottom-color: #555; }
        .ClassifyRules li.RuleHit{ color: #8fd99b; }
        .ClassifyRules li.RuleMiss{ color: #777; }
        .ClassifyEmpty{ color: #888; font-size: 12px; text-align: center; padding: 10px; }

        #LoadingOverlay{
            position: fixed;
            inset: 0;
            display: none;
            align-items: center;
            justify-content: center;
            flex-direction: column;
            gap: 12px;
            background: rgba(0,0,0,.65);
            color: #fff;
            font-size: 15px;
            letter-spacing: .3px;
            z-index: 5000;
            cursor: wait;
        }
        #LoadingOverlay .LoadingSpinner{
            width: 34px;
            height: 34px;
            border: 3px solid rgba(255,255,255,.25);
            border-top-color: #fff;
            border-radius: 50%;
            animation: LoadingSpin .8s linear infinite;
        }
        @keyframes LoadingSpin{ to{ transform: rotate(360deg); } }
    </style>
    </head>
    <body class="NoDatabase">

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
            <input id="SearchInput" type="text" placeholder="🔍 Поиск" title="* — любые символы, ? — один символ, && — И, || — ИЛИ. Также: type:folder, type:file, type:unity и т.д." />
            <button id="GlobalSearchBtn">🔎 Глобальный поиск</button>
        </div>
        <div id="ListArea">
            <div id="Placeholder">Откройте файл базы данных, чтобы увидеть содержимое репозитория</div>
        </div>
        <div id="SelectionInfo">
            <span id="SelectionInfoText"></span>
            <button id="ClearSelectionBtn" title="Снять выделение">✕</button>
        </div>
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
                <strong>Глобальный поиск</strong>
                <input id="GlobalSearchInput" type="text" placeholder="Поиск" title="* — любые символы, ? — один символ, && — И, || — ИЛИ. Также: type:folder, type:file, type:unity и т.д." />
                <div id="GlobalSearchTypeMenuWrap">
                    <button id="GlobalSearchTypeBtn" class="Secondary" title="Фильтр по типу"><svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" style="vertical-align:-2px;margin-right:5px;"><path d="M20.59 13.41 13.42 20.58a2 2 0 0 1-2.83 0L2 12V2h10l8.59 8.59a2 2 0 0 1 0 2.82Z"/><circle cx="7" cy="7" r="1.3" fill="currentColor"/></svg>Тип ▾</button>
                    <div id="GlobalSearchTypeMenu" class="TypeMenu"></div>
                </div>
            </div>
            <div id="GlobalSearchResultsArea">
                <div id="GlobalSearchPlaceholder">Введите запрос, чтобы начать поиск</div>
            </div>
            <div id="GlobalSearchSummary"></div>
        </div>
    </div>

    <div id="ContextMenu"></div>
    <div id="Toast"></div>

    <div id="ClassifyModal">
        <div id="ClassifyModalBox">
            <div id="ClassifyModalHeader">
                <span>Анализ: <strong id="ClassifyModalPath"></strong></span>
            </div>
            <div id="ClassifyModalBody"></div>
            <div id="ClassifyModalFooter">
                <button id="ClassifyModalCloseBtn" class="Secondary">Закрыть</button>
            </div>
        </div>
    </div>

    <div id="LoadingOverlay"><div class="LoadingSpinner"></div><div>Загрузка...</div></div>

    <script>
    /* language=js */
    (function(){
        var CurrentPath            = "";
        var LastChildren           = [];
        var SelectedPaths          = {};
        var LastClickedIndex       = null;
        var SearchLastClickedIndex = null;
        var ViewMode               = "list";
        var DownloadQueue          = {};
        var ExcludedPaths          = {};
        var ToastTimeoutId         = null;

        var RepoTotalSize  = 0;
        var RepoTotalFiles = 0;

        var GlobalSearchResults            = [];
        var GlobalSearchTruncated          = false;
        var GlobalSearchLastTerm           = "";
        var GlobalSearchSortField          = "Path";
        var GlobalSearchSortDir            = 1;
        var GlobalSearchDebounceId         = null;
        var GlobalSearchLoading            = false;
        var GlobalSearchRequestSeq         = 0;
        var GlobalSearchPendingId          = -1;
        var GlobalSearchProgressProcessed  = 0;
        var GlobalSearchProgressTotal      = 0;

        var NavigateRequestSeq = 0;
        var NavigatePendingId  = 0;

        var FolderSortField = "Name";
        var FolderSortDir   = 1;

        var KnownProfiles = [];

        var EntryTypeFile   = 0;
        var EntryTypeFolder = 1;

        var FileIconUrl    = "https://github.com/Woowz11/woowzsite/raw/refs/heads/main/source/GitHubExplorer/file.svg";
        var FolderIconUrl  = "https://github.com/Woowz11/woowzsite/raw/refs/heads/main/source/GitHubExplorer/folder.svg";
        var UnknownIconUrl = "https://github.com/Woowz11/woowzsite/raw/refs/heads/main/source/GitHubExplorer/file-unknown.svg";
        var ErrorIconUrl   = "https://github.com/Woowz11/woowzsite/raw/refs/heads/main/source/GitHubExplorer/error.svg";
        var IconErrorAttr  = ' onerror="this.onerror=null;this.src=\'' + ErrorIconUrl + '\';"';

        function ResolveEntryIconUrl(Info){
            try{
                if(!Info) return UnknownIconUrl;

                if(Info.ClassificationIconUrl) return Info.ClassificationIconUrl;

                if(typeof Info.Type === "number"){
                    if(Info.Type === EntryTypeFile)   return FileIconUrl;
                    if(Info.Type === EntryTypeFolder) return FolderIconUrl;
                    return UnknownIconUrl;
                }

                if(typeof Info.IsFolder === "boolean"){
                    return Info.IsFolder ? FolderIconUrl : FileIconUrl;
                }

                return UnknownIconUrl;
            }catch(E){
                return UnknownIconUrl;
            }
        }

        function DisplayName(Info){
            var Name = Info.Name || Info.ChildName || "";
            return Info.IsFolder ? (Name + "/") : Name;
        }

        function FormatTypeCell(Info){
            var Base = Info.IsFolder ? "Папка" : "Файл";
            if(Info.ClassificationLabel) Base += " (" + Info.ClassificationLabel + ")";
            return Base;
        }

        function EntryIconHtml(Info, ExtraClass){
            var Url = ResolveEntryIconUrl(Info);
            var TitleAttr = (Info && Info.ClassificationLabel) ? ' title="' + Info.ClassificationLabel + '"' : "";
            return '<img src="' + Url + '" class="EntryIcon' + (ExtraClass ? " " + ExtraClass : "") + '"' + TitleAttr + IconErrorAttr + ' alt="" />';
        }

        function EscapeAttr(Text){
            return String(Text == null ? "" : Text)
                .replace(/&/g, "&amp;")
                .replace(/"/g, "&quot;")
                .replace(/</g, "&lt;")
                .replace(/>/g, "&gt;");
        }

        function MatchesGlobExpression(Pattern, Target){
            if(!Pattern) return true;
            var OrGroups = Pattern.split("||");
            for(var I = 0; I < OrGroups.length; I++){
                var AndTerms = OrGroups[I].split("&&");
                var AllMatch = true;
                for(var J = 0; J < AndTerms.length; J++){
                    var Term = AndTerms[J].trim();
                    if(Term === "") continue;
                    if(!MatchesGlobSingle(Term, Target)){ AllMatch = false; break; }
                }
                if(AllMatch) return true;
            }
            return false;
        }

        function MatchesGlobSingle(Term, Target){
            if(Term.indexOf("*") === -1 && Term.indexOf("?") === -1){
                return Target.toLowerCase().indexOf(Term.toLowerCase()) !== -1;
            }
            var Escaped = Term.replace(/[.+^${}()|[\]\\]/g, "\\$&").replace(/\*/g, ".*").replace(/\?/g, ".");
            try{
                return new RegExp("^" + Escaped + "$", "i").test(Target);
            }catch(E){
                return false;
            }
        }

        function ItemMatchesSingleTerm(Term, Item){
            if(Term.toLowerCase().indexOf("type:") === 0){
                var Value = Term.substring(5).trim().toLowerCase();
                if(Value === "folder") return !!Item.IsFolder;
                if(Value === "file") return !Item.IsFolder;
                var Label = (Item.ClassificationLabel || "").toLowerCase();
                return Label.indexOf(Value) !== -1;
            }
            return MatchesGlobSingle(Term, Item.Name);
        }

        function MatchesSearchExpressionForItem(Pattern, Item){
            if(!Pattern) return true;
            var OrGroups = Pattern.split("||");
            for(var I = 0; I < OrGroups.length; I++){
                var AndTerms = OrGroups[I].split("&&");
                var AllMatch = true;
                for(var J = 0; J < AndTerms.length; J++){
                    var Term = AndTerms[J].trim();
                    if(Term === "") continue;
                    if(!ItemMatchesSingleTerm(Term, Item)){ AllMatch = false; break; }
                }
                if(AllMatch) return true;
            }
            return false;
        }

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

        function SizeHtml(Bytes){
            return '<span title="' + FormatBytesExact(Bytes) + '">' + FormatSize(Bytes) + '</span>';
        }

        function FormatPercentOfRepo(SelectedSize){
            if(!RepoTotalSize || RepoTotalSize <= 0) return "";
            var Percent = (SelectedSize / RepoTotalSize) * 100;
            if(Percent <= 0) return "";
            var Text = Percent.toFixed(3);
            if(parseFloat(Text) === 0) Text = "<0.001";
            return ' <span title="' + FormatBytesExact(SelectedSize) + ' из ' + FormatBytesExact(RepoTotalSize) + '">(' + Text + '% от репозитория)</span>';
        }

        function ShowToast(Message){
            var El = document.getElementById("Toast");
            El.textContent = Message;
            El.classList.add("Show");
            if(ToastTimeoutId) clearTimeout(ToastTimeoutId);
            ToastTimeoutId = setTimeout(function(){ El.classList.remove("Show"); }, 2500);
        }

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

        function ShowLoadingOverlay(){
            document.getElementById("LoadingOverlay").style.display = "flex";
        }

        function HideLoadingOverlay(){
            document.getElementById("LoadingOverlay").style.display = "none";
        }

        function ChildFullPath(Item){
            return CurrentPath + Item.Name + (Item.IsFolder ? "/" : "");
        }

        function SearchItemIsFolder(Item){
            return Item.Type === 1;
        }

        function SearchFullPath(Item){
            var P = Item.Path;
            if(SearchItemIsFolder(Item) && P.charAt(P.length - 1) !== "/") P += "/";
            return P;
        }

        function GetParentPath(Path){
            if(Path === "") return "";
            var Trimmed = Path.replace(/\/$/, "");
            var Idx = Trimmed.lastIndexOf("/");
            return Idx === -1 ? "" : Trimmed.substring(0, Idx + 1);
        }

        function IsCoveredByQueue(FullPath){
            var Keys = Object.keys(DownloadQueue);
            for(var I = 0; I < Keys.length; I++){
                var Key   = Keys[I];
                var Entry = DownloadQueue[Key];
                if(Entry.IsFolder && Key !== FullPath && FullPath.indexOf(Key) === 0) return true;
            }
            return false;
        }

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

        function CleanupOrphanedExclusions(){
            var Keys = Object.keys(ExcludedPaths);
            for(var I = 0; I < Keys.length; I++){
                if(!IsCoveredByQueue(Keys[I])) delete ExcludedPaths[Keys[I]];
            }
        }

        function GetVisibleIndices(){
            var Term = (document.getElementById("SearchInput").value || "").trim();
            var Indices = [];

            if(Term === ""){
                for(var I = 0; I < LastChildren.length; I++) Indices.push(I);
                return Indices;
            }

            for(var J = 0; J < LastChildren.length; J++){
                if(MatchesSearchExpressionForItem(Term, LastChildren[J])) Indices.push(J);
            }
            return Indices;
        }

        function CompareFolderItems(A, B){
            if(FolderSortField === "Type"){
                if(A.IsFolder !== B.IsFolder){
                    return (A.IsFolder ? -1 : 1) * FolderSortDir;
                }
                var TAN = A.Name.toLowerCase(), TBN = B.Name.toLowerCase();
                return TAN < TBN ? -1 : (TAN > TBN ? 1 : 0);
            }

            if(A.IsFolder !== B.IsFolder) return A.IsFolder ? -1 : 1;

            var Result = 0;
            if(FolderSortField === "Name"){
                var AL = A.Name.toLowerCase(), BL = B.Name.toLowerCase();
                Result = AL < BL ? -1 : (AL > BL ? 1 : 0);
            }else if(FolderSortField === "Size"){
                Result = (A.Size || 0) - (B.Size || 0);
            }

            return Result * FolderSortDir;
        }

        function GetSortedVisibleChildren(){
            var Indices = GetVisibleIndices();
            var Items = Indices.map(function(I){ return { Index: I, Item: LastChildren[I] }; });
            Items.sort(function(A, B){ return CompareFolderItems(A.Item, B.Item); });
            return Items;
        }

        function FolderSortIndicator(Field){
            if(FolderSortField !== Field) return "";
            return FolderSortDir === 1 ? " ▲" : " ▼";
        }

        function SetFolderSort(Field){
            if(FolderSortField === Field) FolderSortDir = -FolderSortDir;
            else{
                FolderSortField = Field;
                FolderSortDir   = 1;
            }
            RenderChildren();
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
            var Keys   = Object.keys(SelectedPaths);
            var Box    = document.getElementById("SelectionInfo");
            var TextEl = document.getElementById("SelectionInfoText");

            if(Keys.length === 0){
                Box.classList.remove("Visible");
                return;
            }

            var FolderCount = 0, FileCount = 0, TotalSize = 0;
            for(var I = 0; I < Keys.length; I++){
                var Item = SelectedPaths[Keys[I]];
                if(Item.IsFolder) FolderCount++;
                else FileCount++;
                TotalSize += (Item.Size || 0);
            }

            Box.classList.add("Visible");
            TextEl.innerHTML = "Выбрано: файлов " + FileCount + ", папок " + FolderCount + ", суммарный размер " + SizeHtml(TotalSize) + FormatPercentOfRepo(TotalSize);
        }

        function RefreshAllViews(){
            RenderChildren();
            if(document.getElementById("Content").classList.contains("GlobalSearchActive")) RenderGlobalSearchResults();
        }

        function ClearSelection(){
            SelectedPaths          = {};
            LastClickedIndex       = null;
            SearchLastClickedIndex = null;
            RefreshAllViews();
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

                setTimeout(function(){
                    RenderChildren();
                    UpdateSelectionInfo();
                }, 0);
            }

            var Paths   = Object.keys(SelectedPaths);
            var Payload = Paths.map(function(P){
                var It = SelectedPaths[P];
                return {
                    Path: P,
                    IsFolder: It.IsFolder,
                    Size: It.Size,
                    FileCount: It.FileCount,
                    ClassificationIconUrl: It.ClassificationIconUrl,
                    ClassificationLabel: It.ClassificationLabel
                };
            });

            Event.dataTransfer.setData("text/plain", JSON.stringify(Payload));
            Event.dataTransfer.effectAllowed = "copy";
        }

        function HandleSearchItemClick(Event, SortedArr, Index){
            Event.stopPropagation();
            var Item     = SortedArr[Index];
            var FullPath = SearchFullPath(Item);
            var Spec     = { IsFolder: SearchItemIsFolder(Item), Size: Item.Size, FileCount: 0 };

            if(Event.shiftKey && SearchLastClickedIndex !== null){
                if(!Event.ctrlKey && !Event.metaKey) SelectedPaths = {};
                var From = Math.min(SearchLastClickedIndex, Index);
                var To   = Math.max(SearchLastClickedIndex, Index);
                for(var I = From; I <= To; I++){
                    var It = SortedArr[I];
                    SelectedPaths[SearchFullPath(It)] = { IsFolder: SearchItemIsFolder(It), Size: It.Size, FileCount: 0 };
                }
            }else if(Event.ctrlKey || Event.metaKey){
                if(SelectedPaths[FullPath]) delete SelectedPaths[FullPath];
                else SelectedPaths[FullPath] = Spec;
                SearchLastClickedIndex = Index;
            }else{
                SelectedPaths = {};
                SelectedPaths[FullPath] = Spec;
                SearchLastClickedIndex = Index;
            }

            RenderGlobalSearchResults();
            UpdateSelectionInfo();
        }

        function HandleSearchDragStart(Event, SortedArr, Index){
            var Item     = SortedArr[Index];
            var FullPath = SearchFullPath(Item);

            if(!SelectedPaths[FullPath]){
                SelectedPaths = {};
                SelectedPaths[FullPath] = { IsFolder: SearchItemIsFolder(Item), Size: Item.Size, FileCount: 0 };
                SearchLastClickedIndex = Index;

                setTimeout(function(){
                    RenderGlobalSearchResults();
                    UpdateSelectionInfo();
                }, 0);
            }

            var Paths   = Object.keys(SelectedPaths);
            var Payload = Paths.map(function(P){
                var It = SelectedPaths[P];
                return {
                    Path: P,
                    IsFolder: It.IsFolder,
                    Size: It.Size,
                    FileCount: It.FileCount,
                    ClassificationIconUrl: It.ClassificationIconUrl,
                    ClassificationLabel: It.ClassificationLabel
                };
            });

            Event.dataTransfer.setData("text/plain", JSON.stringify(Payload));
            Event.dataTransfer.effectAllowed = "copy";
        }

        function AddItemsToQueue(Items){
            var Changed = 0;
            for(var I = 0; I < Items.length; I++){
                var It = Items[I];

                if(ExcludedPaths[It.Path]){
                    delete ExcludedPaths[It.Path];
                    Changed++;
                    continue;
                }

                if(GetEffectiveStatus(It.Path) !== "none") continue;
                DownloadQueue[It.Path] = {
                    Path: It.Path,
                    IsFolder: It.IsFolder,
                    Size: It.Size,
                    FileCount: It.FileCount,
                    ClassificationIconUrl: It.ClassificationIconUrl,
                    ClassificationLabel: It.ClassificationLabel
                };
                Changed++;
            }
            if(Changed > 0) RenderDownloadQueue();
            RefreshAllViews();
        }

        function AddSelectionToQueue(){
            var Keys  = Object.keys(SelectedPaths);
            var Items = Keys.map(function(K){
                var It = SelectedPaths[K];
                return {
                    Path: K,
                    IsFolder: It.IsFolder,
                    Size: It.Size,
                    FileCount: It.FileCount,
                    ClassificationIconUrl: It.ClassificationIconUrl,
                    ClassificationLabel: It.ClassificationLabel
                };
            });
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
                RefreshAllViews();
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
                ExcludedPaths[Path] = {
                    IsFolder: It.IsFolder,
                    Size: It.Size,
                    FileCount: It.FileCount,
                    ClassificationIconUrl: It.ClassificationIconUrl,
                    ClassificationLabel: It.ClassificationLabel
                };
                Added++;
            }
            if(Added > 0){
                RenderDownloadQueue();
                RefreshAllViews();
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
                RefreshAllViews();
                ShowToast(Removed === 1 ? "Исключение снято" : "Снято исключений: " + Removed);
            }
        }

        function BuildStandardContextItems(Keys, OpenAction, NavLabel, NavAction){
            var HasNone = false, HasQueuedDirect = false, HasCovered = false, HasExcludedDirect = false;
            for(var I = 0; I < Keys.length; I++){
                var P = Keys[I];
                if(ExcludedPaths[P])            HasExcludedDirect = true;
                else if(DownloadQueue[P])       HasQueuedDirect   = true;
                else if(IsCoveredByQueue(P))    HasCovered        = true;
                else if(!IsPathExcluded(P))     HasNone           = true;
            }

            var Items = [];
            if(OpenAction){
                Items.push({ Label: "📂 Открыть", Action: OpenAction });
                Items.push({ Separator: true });
            }
            if(NavAction){
                Items.push({ Label: NavLabel, Action: NavAction });
                Items.push({ Separator: true });
            }

            if(HasNone)           Items.push({ Label: "⬇ Добавить в очередь на скачивание", Action: AddSelectionToQueue });
            if(HasQueuedDirect)   Items.push({ Label: "✖ Убрать из очереди", Action: RemoveSelectionFromQueue });
            if(HasCovered)        Items.push({ Label: "🚫 Исключить из скачивания", Action: ExcludeSelection });
            if(HasExcludedDirect) Items.push({ Label: "✅ Убрать исключение", Action: IncludeSelectionBack });

            Items.push({ Separator: true });
            Items.push({ Label: "📋 Копировать путь", Action: CopySelectedPaths });

            return Items;
        }

        function RequestClassifyInfo(Path, IsFolder){
            Send("ClassifyInfo", { Path: Path, IsFolder: IsFolder });
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

            var OpenAction = (Keys.length === 1 && Item.IsFolder) ? function(){ Navigate(FullPath); } : null;
            var Items = BuildStandardContextItems(Keys, OpenAction, null, null);

            if(Keys.length === 1){
                Items.push({ Separator: true });
                Items.push({ Label: "🔍 Почему так определено?", Action: function(){ RequestClassifyInfo(FullPath, Item.IsFolder); } });
            }

            ShowContextMenu(Event.clientX, Event.clientY, Items);
        }

        function HandleSearchContextMenu(Event, Item, Index){
            Event.preventDefault();
            Event.stopPropagation();

            var FullPath = SearchFullPath(Item);
            if(!SelectedPaths[FullPath]){
                SelectedPaths = {};
                SelectedPaths[FullPath] = { IsFolder: SearchItemIsFolder(Item), Size: Item.Size, FileCount: 0 };
                SearchLastClickedIndex = Index;
                RenderGlobalSearchResults();
                UpdateSelectionInfo();
            }

            var Keys    = Object.keys(SelectedPaths);
            var IsMulti = Keys.length > 1;
            var NavLabel = SearchItemIsFolder(Item) ? "📂 Перейти в папку" : "📂 Показать в папке";
            if(IsMulti) NavLabel += " (выбранного)";

            var NavAction = function(){
                var TargetPath = SearchItemIsFolder(Item) ? SearchFullPath(Item) : GetParentPath(Item.Path);
                CloseGlobalSearch();
                Navigate(TargetPath);
            };

            var Items = BuildStandardContextItems(Keys, null, NavLabel, NavAction);

            if(Keys.length === 1){
                Items.push({ Separator: true });
                Items.push({ Label: "🔍 Почему так определено?", Action: function(){ RequestClassifyInfo(FullPath, SearchItemIsFolder(Item)); } });
            }

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

            var TypeMenu = document.getElementById("GlobalSearchTypeMenu");
            if(TypeMenu && TypeMenu.classList.contains("Show")){
                if(!Event.target.closest || (!Event.target.closest("#GlobalSearchTypeMenu") && Event.target.id !== "GlobalSearchTypeBtn")){
                    TypeMenu.classList.remove("Show");
                }
            }
        });
        document.addEventListener("scroll", HideContextMenu, true);
        window.addEventListener("blur", HideContextMenu);

        document.getElementById("ClearSelectionBtn").onclick = function(Event){
            Event.stopPropagation();
            ClearSelection();
        };

        function ShowClassifyModal(Data){
            document.getElementById("ClassifyModalPath").textContent = Data.SourceFolderPath || Data.Path;

            var Profiles = Data.Profiles || [];
            var Body = document.getElementById("ClassifyModalBody");

            if(Profiles.length === 0){
                Body.innerHTML = '<div class="ClassifyEmpty">Нет совпадений ни с одним известным профилем</div>';
            }else{
                Body.innerHTML = Profiles.map(function(P){
                    var RowClass = "ClassifyRow" + (P.IsMatch ? " ClassifyMatch" : "");
                    var Rules = (P.Rules || []).map(function(R){
                        var Cls   = R.Matched ? "RuleHit" : "RuleMiss";
                        var Mark  = R.Matched ? "✔" : "✘";
                        var Text  = R.Note || R.Description;
                        var Title = EscapeAttr(R.Description);
                        return '<li class="' + Cls + '" title="' + Title + '">' + Mark + ' ' + Text + ' (вес ' + R.Weight + ')</li>';
                    }).join("");

                    return '<div class="' + RowClass + '">'
                        + '<div class="ClassifyHead">'
                        +   '<img src="' + P.IconUrl + '" alt=""' + IconErrorAttr + ' />'
                        +   '<strong>' + P.DisplayName + '</strong>'
                        +   '<span class="ClassifyPercent">' + P.Percentage.toFixed(0) + '% · ' + P.Score + '/' + P.Threshold + '</span>'
                        + '</div>'
                        + '<div class="ClassifyHint">Процент — относительная доля этого профиля среди всех сработавших весов, не абсолютная вероятность</div>'
                        + '<ul class="ClassifyRules">' + Rules + '</ul>'
                        + '</div>';
                }).join("");
            }

            document.getElementById("ClassifyModal").classList.add("Show");
        }

        function HideClassifyModal(){
            document.getElementById("ClassifyModal").classList.remove("Show");
        }

        document.getElementById("ClassifyModalCloseBtn").onclick = HideClassifyModal;
        document.getElementById("ClassifyModal").onclick = function(Event){
            if(Event.target.id === "ClassifyModal") HideClassifyModal();
        };

        function BuildTypeMenuItems(){
            var Items = [
                { Value: "folder", Label: "📁 Папки", Icon: FolderIconUrl },
                { Value: "file",   Label: "📄 Файлы", Icon: FileIconUrl }
            ];
            for(var I = 0; I < KnownProfiles.length; I++){
                Items.push({ Value: KnownProfiles[I].Key, Label: KnownProfiles[I].DisplayName, Icon: KnownProfiles[I].IconUrl });
            }
            return Items;
        }

        function RenderTypeMenu(){
            var Menu = document.getElementById("GlobalSearchTypeMenu");
            var Items = BuildTypeMenuItems();

            Menu.innerHTML = Items.map(function(It){
                return '<div class="MenuItem" data-value="' + It.Value + '"><img src="' + It.Icon + '" alt=""' + IconErrorAttr + ' />' + It.Label + '</div>';
            }).join("");

            var Rows = Menu.querySelectorAll(".MenuItem");
            for(var I = 0; I < Rows.length; I++){
                (function(Row){
                    Row.onclick = function(){
                        InsertTypeTerm(Row.dataset.value);
                        Menu.classList.remove("Show");
                    };
                })(Rows[I]);
            }
        }

        function InsertTypeTerm(Value){
            var Input = document.getElementById("GlobalSearchInput");
            var Term  = "type:" + Value;
            var Current = Input.value.trim();
            Input.value = Current.length > 0 ? (Current + " && " + Term) : Term;
            Input.focus();
            ScheduleGlobalSearch();
        }

        document.getElementById("GlobalSearchTypeBtn").onclick = function(Event){
            Event.stopPropagation();
            var Menu = document.getElementById("GlobalSearchTypeMenu");
            if(Menu.innerHTML === "") RenderTypeMenu();
            Menu.classList.toggle("Show");
        };

        function RenderListView(){
            var Area  = document.getElementById("ListArea");
            var Items = GetSortedVisibleChildren();

            if(LastChildren.length === 0){
                Area.innerHTML = '<div id="Placeholder">Папка пуста</div>';
                return;
            }
            if(Items.length === 0){
                Area.innerHTML = '<div id="Placeholder">Совпадений не найдено</div>';
                return;
            }

            var Html = '<table><thead><tr>'
                + '<th class="SortableHeader" data-field="Name">Имя' + FolderSortIndicator("Name") + '</th>'
                + '<th class="SortableHeader" data-field="Type">Тип' + FolderSortIndicator("Type") + '</th>'
                + '<th class="SortableHeader" data-field="Size">Размер' + FolderSortIndicator("Size") + '</th>'
                + '</tr></thead><tbody>';

            for(var K = 0; K < Items.length; K++){
                var Entry      = Items[K];
                var Index      = Entry.Index;
                var Item       = Entry.Item;
                var FullPath   = ChildFullPath(Item);
                var Icon       = EntryIconHtml(Item);
                var Status     = GetEffectiveStatus(FullPath);
                var RowClasses = [];
                if(SelectedPaths[FullPath]) RowClasses.push("Selected");
                if(Status === "excluded") RowClasses.push("Excluded");
                else if(Status !== "none") RowClasses.push("Queued");

                Html += '<tr class="' + RowClasses.join(" ") + '" draggable="true" data-index="' + Index + '">'
                     +  '<td>' + Icon + ' ' + DisplayName(Item) + '</td>'
                     +  '<td>' + FormatTypeCell(Item) + '</td>'
                     +  '<td>' + SizeHtml(Item.Size) + '</td>'
                     +  '</tr>';
            }
            Html += "</tbody></table>";
            Area.innerHTML = Html;

            var Headers = Area.querySelectorAll(".SortableHeader");
            for(var H = 0; H < Headers.length; H++){
                (function(HeaderEl){
                    HeaderEl.onclick = function(){ SetFolderSort(HeaderEl.dataset.field); };
                })(Headers[H]);
            }

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
            var Area  = document.getElementById("ListArea");
            var Items = GetSortedVisibleChildren();

            if(LastChildren.length === 0){
                Area.innerHTML = '<div id="Placeholder">Папка пуста</div>';
                return;
            }
            if(Items.length === 0){
                Area.innerHTML = '<div id="Placeholder">Совпадений не найдено</div>';
                return;
            }

            var Html = '<div id="TileGrid">';
            for(var K = 0; K < Items.length; K++){
                var Entry       = Items[K];
                var Index       = Entry.Index;
                var Item        = Entry.Item;
                var FullPath    = ChildFullPath(Item);
                var Icon        = EntryIconHtml(Item);
                var Status      = GetEffectiveStatus(FullPath);
                var TileClasses = ["Tile"];
                if(Item.IsFolder) TileClasses.push("IsFolder");
                if(SelectedPaths[FullPath]) TileClasses.push("Selected");
                if(Status === "excluded") TileClasses.push("Excluded");
                else if(Status !== "none") TileClasses.push("Queued");

                Html += '<div class="' + TileClasses.join(" ") + '" draggable="true" data-index="' + Index + '" title="' + DisplayName(Item) + '">'
                     +  '<div class="TileIcon">' + Icon + '</div>'
                     +  '<div class="TileName">' + DisplayName(Item) + '</div>'
                     +  '<div class="TileMeta">' + FormatTypeCell(Item) + ' · ' + SizeHtml(Item.Size) + '</div>'
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
            ClearSelection();

            NavigateRequestSeq += 1;
            NavigatePendingId   = NavigateRequestSeq;
            ShowLoadingOverlay();

            Send("Navigate", { Path: Path, RequestId: NavigatePendingId });
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
                var Icon     = EntryIconHtml(Entry);
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
                            var EIcon  = EntryIconHtml(EEntry);
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
                        RefreshAllViews();
                    };
                })(RemoveButtons[J]);
            }

            var UnexcludeButtons = Container.querySelectorAll("button[data-excluded-path]");
            for(var M = 0; M < UnexcludeButtons.length; M++){
                (function(Btn){
                    Btn.onclick = function(){
                        delete ExcludedPaths[Btn.dataset.excludedPath];
                        RenderDownloadQueue();
                        RefreshAllViews();
                    };
                })(UnexcludeButtons[M]);
            }
        }

        function OpenGlobalSearch(){
            document.getElementById("Content").classList.add("GlobalSearchActive");
            document.getElementById("GlobalSearchInput").focus();
            if(KnownProfiles.length === 0) Send("GetProfiles", {});
        }

        function CloseGlobalSearch(){
            document.getElementById("Content").classList.remove("GlobalSearchActive");
        }

        function RunGlobalSearch(){
            var Term = document.getElementById("GlobalSearchInput").value.trim();
            if(Term === ""){
                GlobalSearchResults           = [];
                GlobalSearchLastTerm          = "";
                GlobalSearchLoading           = false;
                GlobalSearchPendingId         = -1;
                GlobalSearchProgressProcessed = 0;
                GlobalSearchProgressTotal     = 0;
                document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Введите запрос, чтобы начать поиск</div>';
                document.getElementById("GlobalSearchSummary").innerHTML = "";
                return;
            }

            GlobalSearchRequestSeq += 1;
            GlobalSearchPendingId   = GlobalSearchRequestSeq;
            GlobalSearchLoading     = true;
            GlobalSearchLastTerm    = Term;
            GlobalSearchProgressProcessed = 0;
            GlobalSearchProgressTotal     = 0;
            RenderGlobalSearchResults();

            Send("SearchRepo", { Pattern: Term, RequestId: GlobalSearchPendingId });
        }

        function ScheduleGlobalSearch(){
            if(GlobalSearchDebounceId) clearTimeout(GlobalSearchDebounceId);
            GlobalSearchDebounceId = setTimeout(RunGlobalSearch, 200);
        }

        function SearchTypeLabel(Type){
            if(Type === 1) return "Папка";
            if(Type === 2) return "Подмодуль";
            return "Файл";
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

            if(GlobalSearchLoading){
                var ProgressHtml = "";
                if(GlobalSearchProgressTotal > 0){
                    var Percent = Math.min(100, Math.round((GlobalSearchProgressProcessed / GlobalSearchProgressTotal) * 100));
                    ProgressHtml = '<div style="margin-top:10px;font-size:12px;color:#999;">'
                        + GlobalSearchProgressProcessed + ' / ' + GlobalSearchProgressTotal + ' (' + Percent + '%)'
                        + '</div>';
                }
                Area.innerHTML = '<div id="GlobalSearchPlaceholder">⏳ Загрузка...' + ProgressHtml + '</div>';
                document.getElementById("GlobalSearchSummary").innerHTML = "";
                return;
            }

            if(GlobalSearchResults.length === 0){
                Area.innerHTML = '<div id="GlobalSearchPlaceholder">' + (GlobalSearchLastTerm ? "Ничего не найдено" : "Введите запрос, чтобы начать поиск") + '</div>';
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
                var Item       = Sorted[I];
                var FullPath   = SearchFullPath(Item);
                var Status     = GetEffectiveStatus(FullPath);
                var RowClasses = [];
                if(SelectedPaths[FullPath]) RowClasses.push("Selected");
                if(Status === "excluded") RowClasses.push("Excluded");
                else if(Status !== "none") RowClasses.push("Queued");

                Html += '<tr class="' + RowClasses.join(" ") + '" draggable="true" data-index="' + I + '">'
                     +  '<td title="' + FullPath + '">' + EntryIconHtml(Item) + ' ' + Item.Path + '</td>'
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
                    var Index = parseInt(RowEl.dataset.index, 10);
                    RowEl.onclick       = function(Event){ HandleSearchItemClick(Event, Sorted, Index); };
                    RowEl.ondblclick    = function(){
                        var It = Sorted[Index];
                        var TargetPath = SearchItemIsFolder(It) ? SearchFullPath(It) : GetParentPath(It.Path);
                        CloseGlobalSearch();
                        Navigate(TargetPath);
                    };
                    RowEl.ondragstart   = function(Event){ HandleSearchDragStart(Event, Sorted, Index); };
                    RowEl.oncontextmenu = function(Event){ HandleSearchContextMenu(Event, Sorted[Index], Index); };
                })(Rows[R]);
            }

            document.getElementById("GlobalSearchSummary").textContent =
                "Найдено: " + GlobalSearchResults.length + (GlobalSearchTruncated ? " (показаны первые " + GlobalSearchResults.length + ", уточните запрос)" : "");
        }

        document.getElementById("GlobalSearchBtn").onclick      = OpenGlobalSearch;
        document.getElementById("CloseGlobalSearchBtn").onclick = CloseGlobalSearch;
        document.getElementById("GlobalSearchInput").oninput    = ScheduleGlobalSearch;
        document.getElementById("GlobalSearchInput").onkeydown  = function(Event){
            if(Event.key === "Enter"){
                if(GlobalSearchDebounceId) clearTimeout(GlobalSearchDebounceId);
                RunGlobalSearch();
            }
        };

        document.getElementById("GlobalSearchResultsArea").onclick = function(Event){
            if(Event.target === Event.currentTarget || Event.target.tagName === "TABLE" || Event.target.tagName === "TBODY" || Event.target.id === "GlobalSearchPlaceholder"){
                ClearSelection();
            }
        };

        document.getElementById("OpenDbBtn").onclick = function(){ Send("OpenDatabase", {}); };

        document.getElementById("BackBtn").onclick = function(){ Navigate(GetParentPath(CurrentPath)); };

        document.getElementById("ClearQueueBtn").onclick = function(){
            DownloadQueue = {};
            ExcludedPaths = {};
            RenderDownloadQueue();
            RefreshAllViews();
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
                    document.body.classList.remove("NoDatabase");
                    document.getElementById("DbPathInput").value = Msg.Data.Path;
                    UpdateRepoInfo(Msg.Data);

                    RepoTotalSize  = Msg.Data.TotalSize  || 0;
                    RepoTotalFiles = Msg.Data.FilesFound || 0;

                    CurrentPath            = "";
                    SelectedPaths          = {};
                    LastClickedIndex       = null;
                    SearchLastClickedIndex = null;
                    DownloadQueue          = {};
                    ExcludedPaths          = {};
                    FolderSortField        = "Name";
                    FolderSortDir          = 1;
                    RenderDownloadQueue();

                    GlobalSearchResults           = [];
                    GlobalSearchLastTerm          = "";
                    GlobalSearchLoading           = false;
                    GlobalSearchPendingId         = -1;
                    GlobalSearchProgressProcessed = 0;
                    GlobalSearchProgressTotal     = 0;
                    CloseGlobalSearch();
                    document.getElementById("GlobalSearchInput").value = "";
                    document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Введите запрос, чтобы начать поиск</div>';
                    document.getElementById("GlobalSearchSummary").innerHTML = "";

                    NavigateRequestSeq = 0;
                    NavigatePendingId  = 0;
                    ShowLoadingOverlay();
                    break;

                case "FolderListing":
                    if(Msg.Data.RequestId !== NavigatePendingId) break;
                    HideLoadingOverlay();
                    CurrentPath      = Msg.Data.Path;
                    LastChildren     = Msg.Data.Children;
                    LastClickedIndex = null;
                    RenderBreadcrumb(Msg.Data.Path);
                    RenderChildren();
                    UpdateSelectionInfo();
                    UpdateStatusBar(Msg.Data);
                    break;

                case "SearchProgress":
                    if(Msg.Data.RequestId !== GlobalSearchPendingId) break;
                    GlobalSearchProgressProcessed = Msg.Data.Processed;
                    GlobalSearchProgressTotal     = Msg.Data.Total;
                    RenderGlobalSearchResults();
                    break;

                case "SearchResults":
                    if(Msg.Data.RequestId !== GlobalSearchPendingId) break;
                    GlobalSearchLoading   = false;
                    GlobalSearchResults   = Msg.Data.Results || [];
                    GlobalSearchTruncated = Msg.Data.Truncated;
                    GlobalSearchLastTerm  = Msg.Data.Term;
                    RenderGlobalSearchResults();
                    break;

                case "SearchError":
                    GlobalSearchLoading = false;
                    ShowToast(Msg.Data.Message || "Ошибка в запросе поиска");
                    document.getElementById("GlobalSearchResultsArea").innerHTML = '<div id="GlobalSearchPlaceholder">Ошибка в запросе — проверьте шаблон</div>';
                    document.getElementById("GlobalSearchSummary").innerHTML = "";
                    break;

                case "ClassifyInfo":
                    ShowClassifyModal(Msg.Data);
                    break;

                case "ProfilesList":
                    KnownProfiles = Msg.Data.Profiles || [];
                    RenderTypeMenu();
                    break;

                case "Error":
                    HideLoadingOverlay();
                    alert((Msg.Data.Title || "Ошибка") + "\n\n" + (Msg.Data.Message || ""));
                    break;
            }
        }

        Send("Ready");
        Send("GetProfiles", {});
    })();
    </script>
    </body>
    </html>
    """;
}