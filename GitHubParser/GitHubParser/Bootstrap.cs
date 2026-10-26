using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using WLI;
using WLO;
using WLO.Database;
using WLO.Math;

namespace GitHubParser;

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

public static class ShaCodec{
    public static string ToCompact(string Hex){
        if(string.IsNullOrEmpty(Hex)) return "";
        try{
            var Bytes = Convert.FromHexString(Hex);
            return Convert.ToBase64String(Bytes);
        }catch{
            return Hex;
        }
    }

    public static string ToHex(string Compact){
        if(string.IsNullOrEmpty(Compact)) return "";
        try{
            var Bytes = Convert.FromBase64String(Compact);
            return Convert.ToHexString(Bytes).ToLowerInvariant();
        }catch{
            return Compact;
        }
    }
}

public record GitHubTreeEntry(string Path, string Type, string Sha, long? Size);
public record GitHubTreeResult(List<GitHubTreeEntry> Entries, bool Truncated);
public record CompareResult(string Status, int AheadBy, int BehindBy, int TotalCommits);
public record CommitDiffInfo(string OldSha, string NewSha, int AheadBy, int BehindBy, string Status, int TotalCommits);

public record ScanProgress(
    string Phase,
    int    FilesFound,
    int    FoldersFound,
    string CurrentFolder,
    string CurrentFile,
    int?   TotalKnown,
    int    WaitSeconds,
    int    FoldersRemaining,
    int    RateLimitRemaining,
    int    RateLimitLimit
);

public class AppConfig{
    public string Repo       { get; set; } = "";
    public string Token      { get; set; } = "";
    public string OutputPath { get; set; } = "";

    public static AppConfig LoadFrom(string Path){
        var Json = System.IO.File.ReadAllText(Path);
        return JsonSerializer.Deserialize<AppConfig>(Json) ?? new AppConfig();
    }

    public void SaveTo(string Path){
        var Dir = System.IO.Path.GetDirectoryName(Path);
        if(!string.IsNullOrEmpty(Dir)) System.IO.Directory.CreateDirectory(Dir);

        var Json = JsonSerializer.Serialize(this, new JsonSerializerOptions{ WriteIndented = true });
        System.IO.File.WriteAllText(Path, Json);
    }
}

internal static class NativeFileDialog{
    private const int OFN_PATHMUSTEXIST   = 0x00000800;
    private const int OFN_FILEMUSTEXIST   = 0x00001000;
    private const int OFN_OVERWRITEPROMPT = 0x00000002;
    private const int OFN_NOCHANGEDIR     = 0x00000008;

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

    [DllImport("comdlg32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetSaveFileNameW(ref OpenFileName Ofn);

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

    public static string? SaveFile(string FilterText, string DefExt, string SuggestedName, string? InitialDir = null){
        const int BufferChars = 1024;
        var Buffer = Marshal.AllocHGlobal(BufferChars * 2);

        var InitialChars = (SuggestedName + "\0").ToCharArray();
        Marshal.Copy(InitialChars, 0, Buffer, InitialChars.Length);

        try{
            var Ofn = new OpenFileName{
                StructSize = Marshal.SizeOf<OpenFileName>(),
                Filter     = FilterText,
                File       = Buffer,
                MaxFile    = BufferChars,
                DefExt     = DefExt,
                InitialDir = InitialDir,
                Flags      = OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR
            };

            return GetSaveFileNameW(ref Ofn) ? Marshal.PtrToStringUni(Buffer) : null;
        }finally{
            Marshal.FreeHGlobal(Buffer);
        }
    }
}

public class GitHubClient{
    private readonly HttpClient __Http;

    public int  RateLimitRemaining = 60;
    public int  RateLimitLimit     = 60;
    public long RateLimitReset     = 0;

    public event Action<string>? OnDebugLog;

    public GitHubClient(string? Token){
        __Http = new HttpClient();
        __Http.Timeout = TimeSpan.FromMinutes(5);
        __Http.DefaultRequestHeaders.UserAgent.ParseAdd("GitHubParser/1.0");
        __Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

        if(!string.IsNullOrWhiteSpace(Token)){
            __Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        }
    }

    private async Task<JsonDocument> __GetJson(string Url, CancellationToken Cancel){
        const int MaxAttempts = 3;

        for(var Attempt = 1; Attempt <= MaxAttempts; Attempt++){
            OnDebugLog?.Invoke($"→ GET {Url}" + (Attempt > 1 ? $" (попытка {Attempt}/{MaxAttempts})" : ""));
            var Timer = Stopwatch.StartNew();

            var RequestTask = __Http.GetAsync(Url, Cancel);

            while(true){
                var DelayTask     = Task.Delay(5000, CancellationToken.None);
                var CompletedTask = await Task.WhenAny(RequestTask, DelayTask);

                if(CompletedTask == RequestTask) break;

                Cancel.ThrowIfCancellationRequested();
                OnDebugLog?.Invoke($"...всё ещё жду ответ от {Url} ({Timer.Elapsed.TotalSeconds:F0} сек — если долго, проверьте интернет/VPN)");
            }

            using var Response = await RequestTask;
            Timer.Stop();

            if(Response.Headers.TryGetValues("X-RateLimit-Remaining", out var RemVals) && int.TryParse(RemVals.FirstOrDefault(), out var Rem)){
                RateLimitRemaining = Rem;
            }
            if(Response.Headers.TryGetValues("X-RateLimit-Limit", out var LimVals) && int.TryParse(LimVals.FirstOrDefault(), out var Lim)){
                RateLimitLimit = Lim;
            }
            if(Response.Headers.TryGetValues("X-RateLimit-Reset", out var ResetVals) && long.TryParse(ResetVals.FirstOrDefault(), out var Reset)){
                RateLimitReset = Reset;
            }

            var Text = await Response.Content.ReadAsStringAsync(Cancel);
            OnDebugLog?.Invoke($"← {(int)Response.StatusCode} {Response.ReasonPhrase} за {Timer.ElapsedMilliseconds} мс");

            if(Response.IsSuccessStatusCode) return JsonDocument.Parse(Text);

            var IsServerError = (int)Response.StatusCode >= 500;
            if(IsServerError && Attempt < MaxAttempts){
                var DelaySec = Attempt * 3;
                OnDebugLog?.Invoke($"Сервер вернул {(int)Response.StatusCode} — похоже на временный сбой, повторю через {DelaySec} сек...");
                await Task.Delay(DelaySec * 1000, Cancel);
                continue;
            }

            var Hint = Response.StatusCode switch{
                HttpStatusCode.NotFound     => " (проверьте правильность имени owner/repo и ветки)",
                HttpStatusCode.Unauthorized => " (проверьте корректность токена)",
                HttpStatusCode.Forbidden    => " (возможно, исчерпан лимит запросов GitHub API, либо токену не хватает прав)",
                _                           => ""
            };
            throw new Exception($"GitHub API вернул {(int)Response.StatusCode} {Response.ReasonPhrase}{Hint}. Ответ: {Text}");
        }

        throw new Exception("Не удалось выполнить запрос после нескольких попыток.");
    }

    public async Task<string> GetDefaultBranchAsync(string Owner, string Repo, CancellationToken Cancel){
        try{
            using var Doc = await __GetJson($"https://api.github.com/repos/{Owner}/{Repo}", Cancel);
            return Doc.RootElement.GetProperty("default_branch").GetString()!;
        }catch(OperationCanceledException){
            throw;
        }catch(Exception E){
            throw new Exception($"Не удалось получить ветку по умолчанию для {Owner}/{Repo}: {E.Message}", E);
        }
    }

    public async Task<string> GetCommitShaAsync(string Owner, string Repo, string Branch, CancellationToken Cancel){
        try{
            using var Doc = await __GetJson($"https://api.github.com/repos/{Owner}/{Repo}/commits/{Branch}", Cancel);
            return Doc.RootElement.GetProperty("sha").GetString()!;
        }catch(OperationCanceledException){
            throw;
        }catch(Exception E){
            throw new Exception($"Не удалось получить SHA коммита для ветки {Branch} ({Owner}/{Repo}): {E.Message}", E);
        }
    }

    public async Task<GitHubTreeResult> GetTreeAsync(string Owner, string Repo, string Sha, bool Recursive, CancellationToken Cancel){
        var Url = $"https://api.github.com/repos/{Owner}/{Repo}/git/trees/{Sha}" + (Recursive ? "?recursive=1" : "");

        try{
            using var Doc = await __GetJson(Url, Cancel);

            var Entries = new List<GitHubTreeEntry>();
            foreach(var Item in Doc.RootElement.GetProperty("tree").EnumerateArray()){
                Entries.Add(new GitHubTreeEntry(
                    Path : Item.GetProperty("path").GetString()!,
                    Type : Item.GetProperty("type").GetString()!,
                    Sha  : Item.GetProperty("sha").GetString()!,
                    Size : Item.TryGetProperty("size", out var SizeEl) ? SizeEl.GetInt64() : null
                ));
            }

            var Truncated = Doc.RootElement.TryGetProperty("truncated", out var TruncEl) && TruncEl.GetBoolean();
            return new GitHubTreeResult(Entries, Truncated);
        }catch(OperationCanceledException){
            throw;
        }catch(Exception E) when(Recursive && Regex.IsMatch(E.Message, @"вернул 5\d\d\b")){
            OnDebugLog?.Invoke($"Рекурсивный запрос дерева (sha {Sha[..Math.Min(7, Sha.Length)]}) провалился на стороне GitHub ({E.Message}) — поддерево слишком большое для recursive=1, буду обходить его по частям.");
            return new GitHubTreeResult(new List<GitHubTreeEntry>(), true);
        }catch(Exception E){
            throw new Exception($"Не удалось получить дерево файлов (sha {Sha[..Math.Min(7, Sha.Length)]}) для {Owner}/{Repo}: {E.Message}", E);
        }
    }

    public async Task<CompareResult> CompareAsync(string Owner, string Repo, string Base, string Head, CancellationToken Cancel){
        try{
            using var Doc = await __GetJson($"https://api.github.com/repos/{Owner}/{Repo}/compare/{Base}...{Head}", Cancel);

            return new CompareResult(
                Status      : Doc.RootElement.GetProperty("status").GetString()!,
                AheadBy     : Doc.RootElement.GetProperty("ahead_by").GetInt32(),
                BehindBy    : Doc.RootElement.GetProperty("behind_by").GetInt32(),
                TotalCommits: Doc.RootElement.TryGetProperty("total_commits", out var Tc) ? Tc.GetInt32() : 0
            );
        }catch(OperationCanceledException){
            throw;
        }catch(Exception E){
            throw new Exception($"Не удалось сравнить коммиты {Base[..Math.Min(7, Base.Length)]}...{Head[..Math.Min(7, Head.Length)]} для {Owner}/{Repo}: {E.Message}", E);
        }
    }

    public async Task<(int Remaining, int Limit, long Reset)> GetRateLimitAsync(CancellationToken Cancel){
        using var Doc = await __GetJson("https://api.github.com/rate_limit", Cancel);
        var Core = Doc.RootElement.GetProperty("resources").GetProperty("core");
        return (
            Core.GetProperty("remaining").GetInt32(),
            Core.GetProperty("limit").GetInt32(),
            Core.GetProperty("reset").GetInt64()
        );
    }
}

public class RepoScanner{
    private readonly Database     __DB;
    private readonly GitHubClient __Client;
    private readonly string       __Owner;
    private readonly string       __Name;

    public volatile bool CancelRequested;

    public event Action<ScanProgress>? OnProgress;
    public event Action<string>?       OnLog;

    public RepoScanner(Database DB, GitHubClient Client, string Owner, string Name){
        __DB     = DB;
        __Client = Client;
        __Owner  = Owner;
        __Name   = Name;

        __Client.OnDebugLog += Text => OnLog?.Invoke("[DEBUG] " + Text);

        __DB.EnsureTableCreated<RepoRow>();
        __DB.EnsureTableCreated<FileRow>();
        __DB.EnsureTableCreated<PendingFolderRow>();
    }

    public async Task<(RepoRow Repo, CommitDiffInfo? Diff, string LatestSha, string DefaultBranch)> PrepareAsync(CancellationToken Cancel){
        OnLog?.Invoke($"Определяю ветку по умолчанию для {__Owner}/{__Name}...");
        var DefaultBranch = await __Client.GetDefaultBranchAsync(__Owner, __Name, Cancel);
        OnLog?.Invoke($"Ветка по умолчанию: {DefaultBranch}");

        OnLog?.Invoke("Получаю SHA последнего коммита...");
        var LatestSha = await __Client.GetCommitShaAsync(__Owner, __Name, DefaultBranch, Cancel);
        OnLog?.Invoke($"Текущий коммит: {LatestSha[..7]}");

        var Existing = __DB.Table<RepoRow>().All().Take(1).ToList().FirstOrDefault();

        if(Existing != null && (Existing.Owner != __Owner || Existing.Name != __Name)){
            if(Existing.Status == ScanStatus.NotStarted){
                __DB.Execute("DELETE FROM RepoRow");
                Existing = null;
            }else{
                throw new Exception($"Этот файл результата уже содержит сканирование другого репозитория ({Existing.Owner}/{Existing.Name}). Выберите другой файл результата или продолжите с ним этот же репозиторий.");
            }
        }

        if(Existing == null){
            var NewRepo = new RepoRow{
                Owner         = __Owner,
                Name          = __Name,
                DefaultBranch = DefaultBranch,
                CommitSha     = LatestSha,
                Status        = ScanStatus.NotStarted,
                FilesFound    = 0,
                FoldersFound  = 0,
                TotalSize     = 0
            };

            __DB.Table<RepoRow>().Insert(NewRepo);
            Existing = __DB.Table<RepoRow>().All().Take(1).ToList().First();

            return (Existing, null, LatestSha, DefaultBranch);
        }

        if(Existing.Status == ScanStatus.NotStarted || Existing.CommitSha == LatestSha){
            __DB.Table<RepoRow>().Update(R => R.Id == Existing.Id, new{ CommitSha = LatestSha, DefaultBranch });
            Existing.CommitSha     = LatestSha;
            Existing.DefaultBranch = DefaultBranch;
            return (Existing, null, LatestSha, DefaultBranch);
        }

        OnLog?.Invoke($"Сохранённый коммит ({Existing.CommitSha[..7]}) отличается от текущего ({LatestSha[..7]}), сравниваю...");
        var Cmp  = await __Client.CompareAsync(__Owner, __Name, Existing.CommitSha, LatestSha, Cancel);
        var Diff = new CommitDiffInfo(Existing.CommitSha, LatestSha, Cmp.AheadBy, Cmp.BehindBy, Cmp.Status, Cmp.TotalCommits);

        return (Existing, Diff, LatestSha, DefaultBranch);
    }

    public void ApplyRescan(RepoRow Repo, string NewSha, string DefaultBranch){
        __DB.Execute("DELETE FROM FileRow");
        __DB.Execute("DELETE FROM PendingFolderRow");
        __DB.Table<RepoRow>().Update(R => R.Id == Repo.Id, new{ CommitSha = NewSha, DefaultBranch, Status = ScanStatus.NotStarted, FilesFound = 0, FoldersFound = 0, TotalSize = 0L });

        Repo.CommitSha     = NewSha;
        Repo.DefaultBranch = DefaultBranch;
        Repo.Status        = ScanStatus.NotStarted;
        Repo.FilesFound    = 0;
        Repo.FoldersFound  = 0;
        Repo.TotalSize     = 0;
    }

    public void ApplyContinueKeepOldSha(){ }

    public async Task RunAsync(RepoRow Repo, CancellationToken Cancel){
        OnLog?.Invoke($"Старт сканирования {__Owner}/{__Name} @ {Repo.CommitSha[..7]}");

        if(Repo.Status == ScanStatus.Done){
            __Report(Repo, "AlreadyDone", "", "", Cancel);
            return;
        }

        if(Repo.Status == ScanStatus.NotStarted){
            __DB.Table<RepoRow>().Update(R => R.Id == Repo.Id, new{ Status = ScanStatus.InProgress });
            Repo.Status = ScanStatus.InProgress;

            __Report(Repo, "SingleFetch", "/", "", Cancel);
            await __WaitForRateLimitIfNeeded(Repo, Cancel);

            OnLog?.Invoke("Пробую получить дерево файлов одним запросом (recursive=1)...");
            var Root = await __Client.GetTreeAsync(__Owner, __Name, Repo.CommitSha, Recursive: true, Cancel);

            if(!Root.Truncated){
                __SaveEntries(Repo, Root.Entries, "", "SingleFetch", Cancel);
                __MarkDone(Repo);
                return;
            }

            OnLog?.Invoke("Репозиторий слишком большой для одного запроса (truncated=true) — переходим на постепенный обход (BFS)");

            var Shallow = await __Client.GetTreeAsync(__Owner, __Name, Repo.CommitSha, Recursive: false, Cancel);
            __SaveEntries(Repo, Shallow.Entries.Where(E => E.Type == "blob"), "", "SingleFetch", Cancel);

            foreach(var Folder in Shallow.Entries.Where(E => E.Type == "tree")){
                var FolderPath = __BuildChildPath("", Folder.Path, IsFolder: true);
                __DB.Table<PendingFolderRow>().Insert(new PendingFolderRow{ Path = FolderPath, Sha = ShaCodec.ToCompact(Folder.Sha) });
            }
        }else{
            OnLog?.Invoke("Возобновление ранее прерванного сканирования из очереди папок");
        }

        await __RunBfsLoop(Repo, Cancel);
    }

    private async Task __RunBfsLoop(RepoRow Repo, CancellationToken Cancel){
        while(true){
            if(CancelRequested || Cancel.IsCancellationRequested){
                OnLog?.Invoke("Сканирование остановлено пользователем");
                __Report(Repo, "Cancelled", "", "", Cancel);
                return;
            }

            var Next = __DB.Table<PendingFolderRow>().All().Take(1).ToList().FirstOrDefault();
            if(Next == null){
                __MarkDone(Repo);
                return;
            }

            __Report(Repo, "BFS", Next.Path, "", Cancel);
            await __WaitForRateLimitIfNeeded(Repo, Cancel);

            var NextShaHex = ShaCodec.ToHex(Next.Sha);
            var SubTree = await __Client.GetTreeAsync(__Owner, __Name, NextShaHex, Recursive: true, Cancel);

            if(!SubTree.Truncated){
                __SaveEntries(Repo, SubTree.Entries, Next.Path, "BFS", Cancel);
                __DB.Table<PendingFolderRow>().DeleteWhere(P => P.Id == Next.Id);
            }else{
                OnLog?.Invoke($"Папка {Next.Path} сама по себе огромна — дробим дальше");

                var ShallowSub = await __Client.GetTreeAsync(__Owner, __Name, NextShaHex, Recursive: false, Cancel);
                __SaveEntries(Repo, ShallowSub.Entries.Where(E => E.Type == "blob"), Next.Path, "BFS", Cancel);

                foreach(var Folder in ShallowSub.Entries.Where(E => E.Type == "tree")){
                    var FolderPath = __BuildChildPath(Next.Path, Folder.Path, IsFolder: true);
                    __DB.Table<PendingFolderRow>().Insert(new PendingFolderRow{ Path = FolderPath, Sha = ShaCodec.ToCompact(Folder.Sha) });
                }

                __DB.Table<PendingFolderRow>().DeleteWhere(P => P.Id == Next.Id);
            }
        }
    }

    private static string __BuildChildPath(string ParentPath, string EntryPath, bool IsFolder){
        var Full = ParentPath.Length == 0 ? EntryPath : ParentPath + EntryPath;
        if(IsFolder && !Full.EndsWith("/")) Full += "/";
        return Full;
    }

    private static byte __MapType(string RawType) => RawType switch{
        "blob"   => EntryType.File,
        "tree"   => EntryType.Folder,
        "commit" => EntryType.Submodule,
        _        => EntryType.File
    };

    private void __SaveEntries(RepoRow Repo, IEnumerable<GitHubTreeEntry> Entries, string FolderPrefix, string Phase, CancellationToken Cancel){
        var EntryList = Entries.ToList();
        if(EntryList.Count == 0) return;

        var Rows = new List<FileRow>(EntryList.Count);
        long AddedSize = 0;
        var BaseFilesFound = Repo.FilesFound;

        for(var I = 0; I < EntryList.Count; I++){
            var Entry    = EntryList[I];
            var IsFolder = Entry.Type == "tree";
            var FullPath = __BuildChildPath(FolderPrefix, Entry.Path, IsFolder);

            var Row = new FileRow{
                Path = FullPath,
                Size = Entry.Size,
                Type = __MapType(Entry.Type)
            };

            Rows.Add(Row);
            if(Row.Type == EntryType.File && Row.Size.HasValue) AddedSize += Row.Size.Value;

            if(I % 2000 == 0){
                Cancel.ThrowIfCancellationRequested();
                __Report(Repo, Phase, FolderPrefix, FullPath, Cancel, BaseFilesFound + Rows.Count(R => R.Type == EntryType.File));
            }
        }

        __DB.Table<FileRow>().InsertMany(Rows);

        var AddedFiles   = Rows.Count(R => R.Type == EntryType.File);
        var AddedFolders = Rows.Count(R => R.Type == EntryType.Folder);
        var NewFiles     = Repo.FilesFound + AddedFiles;
        var NewFolders   = Repo.FoldersFound + AddedFolders;
        var NewSize      = Repo.TotalSize + AddedSize;

        __DB.Table<RepoRow>().Update(R => R.Id == Repo.Id, new{ FilesFound = NewFiles, FoldersFound = NewFolders, TotalSize = NewSize });
        Repo.FilesFound   = NewFiles;
        Repo.FoldersFound = NewFolders;
        Repo.TotalSize    = NewSize;

        OnLog?.Invoke($"+{AddedFiles} файлов, +{AddedFolders} папок (всего {NewFiles} файлов, {NewFolders} папок, {__FormatSize(NewSize)})");
    }

    private static string __FormatSize(long Bytes){
        string[] Units = ["Б", "КБ", "МБ", "ГБ", "ТБ"];
        double Value = Bytes;
        var UnitIndex = 0;
        while(Value >= 1024 && UnitIndex < Units.Length - 1){ Value /= 1024; UnitIndex++; }
        return $"{Value:F2} {Units[UnitIndex]}";
    }

    private void __MarkDone(RepoRow Repo){
        __DB.Table<RepoRow>().Update(R => R.Id == Repo.Id, new{ Status = ScanStatus.Done });
        Repo.Status = ScanStatus.Done;

        OnLog?.Invoke("Сканирование завершено");
        __Report(Repo, "Done", "", "", CancellationToken.None);
    }

    private async Task __WaitForRateLimitIfNeeded(RepoRow Repo, CancellationToken Cancel){
        if(__Client.RateLimitRemaining > 5) return;

        var Now         = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var WaitSeconds = (int)Math.Max(1, __Client.RateLimitReset - Now);

        OnLog?.Invoke($"Достигнут лимит запросов GitHub API — ждём {WaitSeconds} сек.");

        for(var I = WaitSeconds; I > 0 && !CancelRequested; I--){
            Cancel.ThrowIfCancellationRequested();
            OnProgress?.Invoke(new ScanProgress("RateLimitWait", Repo.FilesFound, Repo.FoldersFound, "", "", null, I, __PendingCount(), __Client.RateLimitRemaining, __Client.RateLimitLimit));
            await Task.Delay(1000, Cancel);
        }
    }

    private int __PendingCount() => __DB.Table<PendingFolderRow>().All().Count();

    private void __Report(RepoRow Repo, string Phase, string CurrentFolder, string CurrentFile, CancellationToken Cancel, int? FilesFoundOverride = null){
        OnProgress?.Invoke(new ScanProgress(
            Phase,
            FilesFoundOverride ?? Repo.FilesFound,
            Repo.FoldersFound,
            CurrentFolder,
            CurrentFile,
            null,
            0,
            __PendingCount(),
            __Client.RateLimitRemaining,
            __Client.RateLimitLimit
        ));
    }

    public (int FilesCount, int FoldersCount, long TotalSize) GetFinalStats(RepoRow Repo) => (Repo.FilesFound, Repo.FoldersFound, Repo.TotalSize);
}

public static class Bootstrap{
    private static WLO.Web.WebView2 Web    = null!;
    private static WLO.Window.GLFW  Window = null!;

    private static readonly ConcurrentQueue<string> __IncomingMessages = new();
    private static readonly ConcurrentQueue<string> __OutgoingMessages = new();

    private static Database?                __DB;
    private static GitHubClient?            __Client;
    private static RepoScanner?             __Scanner;
    private static CancellationTokenSource? __OperationCts;

    private static string? __CurrentOutputPath;
    private static string? __CurrentConfigPath;

    private static RepoRow? __PendingRepo;
    private static string?  __PendingNewSha;
    private static string?  __PendingBranch;

    private static readonly string __PointerFilePath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitHubParser", "lastconfigpath.txt"
    );

    private static readonly string __ConfigDir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GitHubParser"
    );

    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            AppDomain.CurrentDomain.UnhandledException += (_, E) => {
                WL.Logger.Fatal("Необработанное исключение в домене приложения", E.ExceptionObject as Exception);
            };
            TaskScheduler.UnobservedTaskException += (_, E) => {
                WL.Logger.Error("Необработанное исключение в фоновой задаче", E.Exception);
                E.SetObserved();
            };

            Web = WLO.Web.WebView2.Create();
            Web.OnMessage += Msg => __IncomingMessages.Enqueue(Msg);

            Window = new WLO.Window.GLFW(new Vector2I(1100, 700), "GitHub Parser", Web);
            Window.TODO_UseDarkMode();

            Web.NavigateCustom(__BuildHtml());

            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();

                while(__IncomingMessages.TryDequeue(out var Msg)){
                    _ = __HandleMessageAsync(Msg);
                }

                while(__OutgoingMessages.TryDequeue(out var OutJson)){
                    try{
                        Web.PostMessage(OutJson);
                    }catch(Exception E){
                        WL.Logger.Error("Не удалось отправить сообщение в UI", E);
                    }
                }
            }

            __OperationCts?.Cancel();
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

    private static void __SendLog(string Text){
        WL.Logger.Debug(Text);
        __Send("ScanLog", new{ Text, Time = DateTime.Now.ToString("HH:mm:ss") });
    }

    private static void __SavePointer(string ConfigPath){
        try{
            System.IO.Directory.CreateDirectory(__ConfigDir);
            System.IO.File.WriteAllText(__PointerFilePath, ConfigPath);
        }catch(Exception E){
            WL.Logger.Error("Не удалось сохранить указатель на последний конфиг", E);
        }
    }

    private static string? __LoadPointer(){
        try{
            if(System.IO.File.Exists(__PointerFilePath)) return System.IO.File.ReadAllText(__PointerFilePath).Trim();
        }catch(Exception E){
            WL.Logger.Error("Не удалось прочитать указатель на последний конфиг", E);
        }
        return null;
    }

    private static string __SanitizeFileName(string Input){
        var Invalid = System.IO.Path.GetInvalidFileNameChars();
        var Chars   = Input.Select(C => Invalid.Contains(C) ? '_' : C).ToArray();
        var Result  = new string(Chars).Trim();
        return Result.Length == 0 ? "repo" : Result;
    }

    private static string __SuggestConfigFileName(string RepoInput){
        var Parsed = __ParseRepoInput(RepoInput);
        var Base   = Parsed != null ? Parsed.Value.Name : RepoInput;
        return __SanitizeFileName(Base) + ".json";
    }

    private static async Task __CheckRateLimitAsync(string Token){
        try{
            var TempClient = new GitHubClient(Token);
            TempClient.OnDebugLog += Text => __SendLog("[DEBUG] " + Text);
            var (Remaining, Limit, Reset) = await TempClient.GetRateLimitAsync(CancellationToken.None);
            __Send("RateLimitInfo", new{ Remaining, Limit, Reset });
        }catch(Exception E){
            __SendLog("[DEBUG] Не удалось проверить лимит API: " + E.Message);
            __Send("RateLimitInfo", new{ Remaining = -1, Limit = -1, Reset = 0L });
        }
    }

    private static async Task __HandleMessageAsync(string Json){
        try{
            using var Doc  = JsonDocument.Parse(Json);
            var       Root = Doc.RootElement;
            var       Type = Root.GetProperty("Type").GetString() ?? "";
            var       Data = Root.TryGetProperty("Data", out var D) ? D : default;

            switch(Type){
                case "Ready":{
                    _ = __CheckRateLimitAsync("");

                    var PointerPath = __LoadPointer();
                    if(PointerPath != null && System.IO.File.Exists(PointerPath)){
                        try{
                            var Cfg = AppConfig.LoadFrom(PointerPath);
                            __CurrentConfigPath = PointerPath;
                            __Send("SavedConfig", new{ Cfg.Repo, Cfg.Token, Cfg.OutputPath, ConfigPath = PointerPath });
                            __SendLog($"Загружен конфиг: {PointerPath}");
                            if(!string.IsNullOrWhiteSpace(Cfg.Token)) _ = __CheckRateLimitAsync(Cfg.Token);
                        }catch(Exception E){
                            __SendError("Не удалось загрузить сохранённый конфиг", E);
                        }
                    }
                    break;
                }

                case "CheckRateLimit":{
                    var Token = Data.TryGetProperty("Token", out var TokenEl) ? TokenEl.GetString() ?? "" : "";
                    _ = __CheckRateLimitAsync(Token);
                    break;
                }

                case "BrowseConfigOpen":{
                    var Path = NativeFileDialog.OpenFile(
                        "Файлы конфигурации (*.json)\0*.json\0Все файлы (*.*)\0*.*\0\0", "json"
                    );
                    if(Path != null){
                        try{
                            var Cfg = AppConfig.LoadFrom(Path);
                            __CurrentConfigPath = Path;
                            __SavePointer(Path);
                            __Send("SavedConfig", new{ Cfg.Repo, Cfg.Token, Cfg.OutputPath, ConfigPath = Path });
                            __SendLog($"Загружен конфиг: {Path}");
                            if(!string.IsNullOrWhiteSpace(Cfg.Token)) _ = __CheckRateLimitAsync(Cfg.Token);
                        }catch(Exception E){
                            __SendError("Не удалось загрузить конфиг", E);
                        }
                    }
                    break;
                }

                case "BrowseConfigSaveAs":{
                    var Repo   = Data.GetProperty("Repo").GetString() ?? "";
                    var Token  = Data.GetProperty("Token").GetString() ?? "";
                    var Output = Data.GetProperty("OutputPath").GetString() ?? "";

                    var SuggestedName = __SuggestConfigFileName(Repo);
                    var InitialDir    = __CurrentConfigPath != null ? System.IO.Path.GetDirectoryName(__CurrentConfigPath) : null;

                    var Path = NativeFileDialog.SaveFile(
                        "Файлы конфигурации (*.json)\0*.json\0\0", "json", SuggestedName, InitialDir
                    );
                    if(Path != null){
                        try{
                            new AppConfig{ Repo = Repo, Token = Token, OutputPath = Output }.SaveTo(Path);
                            __CurrentConfigPath = Path;
                            __SavePointer(Path);
                            __Send("ConfigSaved", new{ ConfigPath = Path });
                            __SendLog($"Конфиг сохранён: {Path}");
                        }catch(Exception E){
                            __SendError("Не удалось сохранить конфиг", E);
                        }
                    }
                    break;
                }

                case "BrowseOutputPath":{
                    var Repo = Data.TryGetProperty("Repo", out var RepoEl) ? RepoEl.GetString() ?? "" : "";

                    string SuggestedName;
                    string? InitialDir = null;

                    if(__CurrentConfigPath != null){
                        SuggestedName = System.IO.Path.GetFileNameWithoutExtension(__CurrentConfigPath) + ".db";
                        InitialDir    = System.IO.Path.GetDirectoryName(__CurrentConfigPath);
                    }else{
                        var Parsed = __ParseRepoInput(Repo);
                        var Base   = Parsed != null ? Parsed.Value.Name : (Repo.Length > 0 ? Repo : "scan");
                        SuggestedName = __SanitizeFileName(Base) + ".db";
                    }

                    var Path = NativeFileDialog.SaveFile(
                        "База данных (*.db)\0*.db\0Все файлы (*.*)\0*.*\0\0", "db", SuggestedName, InitialDir
                    );
                    if(Path != null) __Send("OutputPathChosen", new{ Path });
                    break;
                }

                case "StartScan":{
                    var Repo   = Data.GetProperty("Repo").GetString() ?? "";
                    var Token  = Data.GetProperty("Token").GetString() ?? "";
                    var Output = Data.GetProperty("OutputPath").GetString() ?? "";
                    await __HandleStartScanAsync(Repo, Token, Output);
                    break;
                }

                case "ResolveCommitChange":{
                    var Action = Data.GetProperty("Action").GetString() ?? "Continue";
                    await __HandleResolveCommitChangeAsync(Action);
                    break;
                }

                case "CancelScan":{
                    __OperationCts?.Cancel();
                    if(__Scanner != null) __Scanner.CancelRequested = true;
                    __SendLog("Запрошена остановка...");
                    break;
                }

                case "OpenOutputFolder":{
                    var Path = Data.GetProperty("Path").GetString() ?? "";
                    __OpenOutputFolder(Path);
                    break;
                }
            }
        }catch(Exception E){
            __SendError("Ошибка обработки сообщения от интерфейса", E);
        }
    }

    private static async Task __HandleStartScanAsync(string RepoInput, string Token, string OutputPath){
        __OperationCts?.Cancel();
        __OperationCts = new CancellationTokenSource();
        var Cancel = __OperationCts.Token;

        try{
            var Parsed = __ParseRepoInput(RepoInput);
            if(Parsed == null){
                __Send("Error", new{ Title = "Некорректный репозиторий", Message = "Укажите в формате owner/repo или ссылку github.com/owner/repo", Details = "" });
                return;
            }

            var (Owner, Name) = Parsed.Value;

            string ConfigPathToUse;
            if(__CurrentConfigPath != null){
                ConfigPathToUse = __CurrentConfigPath;
            }else{
                System.IO.Directory.CreateDirectory(__ConfigDir);
                ConfigPathToUse = System.IO.Path.Combine(__ConfigDir, __SuggestConfigFileName(RepoInput));
            }

            new AppConfig{ Repo = RepoInput, Token = Token, OutputPath = OutputPath }.SaveTo(ConfigPathToUse);
            __CurrentConfigPath = ConfigPathToUse;
            __SavePointer(ConfigPathToUse);
            __SendLog($"Конфиг сохранён: {ConfigPathToUse}");

            __DB?.Dispose();
            __DB                = SQLite.Open(OutputPath);
            __CurrentOutputPath = OutputPath;

            __Client  = new GitHubClient(Token);
            __Scanner = new RepoScanner(__DB, __Client, Owner, Name);
            __Scanner.OnProgress += P => __Send("ScanProgress", P);
            __Scanner.OnLog      += Text => __Send("ScanLog", new{ Text, Time = DateTime.Now.ToString("HH:mm:ss") });

            __Send("ScanProgress", new ScanProgress("Resolving", 0, 0, "", "", null, 0, 0, 0, 0));

            var (Repo, Diff, LatestSha, DefaultBranch) = await __Scanner.PrepareAsync(Cancel);

            if(Diff != null){
                __PendingRepo   = Repo;
                __PendingNewSha = LatestSha;
                __PendingBranch = DefaultBranch;
                __Send("CommitChanged", Diff);
                return;
            }

            await __RunScanAndReportAsync(Repo, Cancel);
        }catch(OperationCanceledException){
            __SendLog("Сканирование остановлено пользователем");
            __Send("ScanCancelled");
        }catch(Exception E){
            __SendError("Ошибка сканирования", E);
        }
    }

    private static async Task __HandleResolveCommitChangeAsync(string Action){
        if(__PendingRepo == null || __Scanner == null || __OperationCts == null) return;

        var Repo   = __PendingRepo;
        var Cancel = __OperationCts.Token;
        __PendingRepo = null;

        try{
            if(Action == "Rescan"){
                __Scanner.ApplyRescan(Repo, __PendingNewSha!, __PendingBranch!);
                __SendLog("Выбрано: пересканировать заново, старые данные очищены");
            }else{
                __Scanner.ApplyContinueKeepOldSha();
                __SendLog($"Выбрано: продолжить со старой зафиксированной версии {Repo.CommitSha[..7]}");
            }

            await __RunScanAndReportAsync(Repo, Cancel);
        }catch(OperationCanceledException){
            __SendLog("Сканирование остановлено пользователем");
            __Send("ScanCancelled");
        }catch(Exception E){
            __SendError("Ошибка сканирования", E);
        }
    }

    private static async Task __RunScanAndReportAsync(RepoRow Repo, CancellationToken Cancel){
        await __Scanner!.RunAsync(Repo, Cancel);

        if(Repo.Status == ScanStatus.Done){
            var (FilesCount, FoldersCount, TotalSize) = __Scanner.GetFinalStats(Repo);
            __Send("ScanComplete", new{ FilesCount, FoldersCount, TotalSize, OutputPath = __CurrentOutputPath });
        }else if(__Scanner.CancelRequested || Cancel.IsCancellationRequested){
            __Send("ScanCancelled");
        }
    }

    private static void __OpenOutputFolder(string Path){
        try{
            if(System.IO.File.Exists(Path)){
                Process.Start("explorer.exe", $"/select,\"{Path}\"");
            }else{
                var Dir = System.IO.Path.GetDirectoryName(Path);
                if(!string.IsNullOrEmpty(Dir) && System.IO.Directory.Exists(Dir)){
                    Process.Start("explorer.exe", $"\"{Dir}\"");
                }
            }
        }catch(Exception E){
            WL.Logger.Error("Не удалось открыть папку результата", E);
        }
    }

    private static (string Owner, string Name)? __ParseRepoInput(string Input){
        Input = Input.Trim();

        var UrlMatch = Regex.Match(Input, @"github\.com[/:]([^/\s]+)/([^/\s]+?)(?:\.git)?/?$");
        if(UrlMatch.Success) return (UrlMatch.Groups[1].Value, UrlMatch.Groups[2].Value);

        var PlainMatch = Regex.Match(Input, @"^([^/\s]+)/([^/\s]+)$");
        if(PlainMatch.Success) return (PlainMatch.Groups[1].Value, PlainMatch.Groups[2].Value);

        return null;
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
            overflow-y: auto;
        }
        #Content{
            flex: 1;
            padding: 20px;
            display: flex;
            flex-direction: column;
            gap: 14px;
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
        .InlineButtons button{ flex: 1; padding: 6px; font-size: 12px; }
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

        #ConfigPathText{ font-size: 11px; color: #777; word-break: break-all; }

        #PhaseText{ font-size: 15px; font-weight: 600; }
        #ProgressBarOuter{
            height: 10px;
            background: #333;
            border-radius: 5px;
            overflow: hidden;
        }
        #ProgressBarInner{
            height: 100%;
            width: 0%;
            background: #0e639c;
            transition: width .3s;
        }
        #ProgressBarInner.Indeterminate{
            width: 30% !important;
            animation: Slide 1.2s infinite linear;
        }
        @keyframes Slide{
            0%{ margin-left: -30%; }
            100%{ margin-left: 100%; }
        }
        #StatsLine{ font-size: 12px; color: #999; display: flex; gap: 18px; flex-wrap: wrap; }
        #CurrentFileLine{ font-size: 12px; color: #6c9ecf; font-family: Consolas, monospace; min-height: 16px; }
        #LogPanel{
            flex: 1;
            background: #111;
            border: 1px solid #333;
            border-radius: 4px;
            padding: 10px;
            overflow-y: auto;
            font-family: Consolas, monospace;
            font-size: 12px;
            white-space: pre-wrap;
        }
        .DebugLine{ color: #6a8f6a; }
        #SummaryBlock{
            display: none;
            background: #1e3a1e;
            border: 1px solid #2d5a2d;
            border-radius: 4px;
            padding: 14px;
            font-size: 13px;
        }
        .Overlay{
            position: fixed;
            inset: 0;
            background: rgba(0,0,0,.6);
            display: none;
            align-items: center;
            justify-content: center;
        }
        .ModalBox{
            background: #252526;
            border: 1px solid #444;
            border-radius: 6px;
            padding: 20px;
            width: 520px;
            max-height: 80vh;
            overflow-y: auto;
            display: flex;
            flex-direction: column;
            gap: 10px;
        }
        .ModalBox h3{ margin: 0 0 4px 0; }
        .ModalButtons{ display: flex; gap: 10px; margin-top: 8px; }
        .ShaBadge{
            font-family: Consolas, monospace;
            background: #1b1b1b;
            padding: 2px 6px;
            border-radius: 3px;
        }
        #ErrorOverlay .ModalBox{ border-color: #823030; }
        #ErrorDetailsPre{
            display: none;
            background: #111;
            border: 1px solid #333;
            border-radius: 4px;
            padding: 10px;
            font-family: Consolas, monospace;
            font-size: 11px;
            white-space: pre-wrap;
            max-height: 240px;
            overflow-y: auto;
        }
        #ErrorToggleBtn{ font-size: 12px; align-self: flex-start; }
    </style>
    </head>
    <body>

    <div id="Sidebar">
        <div class="FieldRow">
            <label>Файл конфигурации</label>
            <input id="ConfigPathInput" placeholder="(не выбран, используется по умолчанию)" readonly />
            <div class="InlineButtons">
                <button id="OpenConfigBtn" class="Secondary">📂 Открыть конфиг</button>
                <button id="SaveConfigAsBtn" class="Secondary">💾 Сохранить как...</button>
            </div>
            <div id="ConfigPathText"></div>
        </div>

        <div class="FieldRow">
            <label>Репозиторий (owner/repo или ссылка)</label>
            <input id="RepoInput" placeholder="facebook/react" />
        </div>

        <div class="FieldRow">
            <label>GitHub токен</label>
            <input id="TokenInput" type="password" placeholder="ghp_..." />
        </div>

        <div class="FieldRow">
            <label>Файл результата (.db)</label>
            <input id="OutputInput" placeholder="C:\repos\scan.db" />
            <div class="InlineButtons">
                <button id="BrowseOutputBtn" class="Secondary">Обзор...</button>
            </div>
        </div>

        <button id="StartBtn">▶ Начать / Продолжить</button>
        <button id="StopBtn" class="Secondary" disabled>⏹ Остановить</button>
        <button id="OpenFolderBtn" class="Secondary">📂 Открыть папку результата</button>
    </div>

    <div id="Content">
        <div id="PhaseText">Готов к сканированию</div>
        <div id="ProgressBarOuter"><div id="ProgressBarInner"></div></div>
        <div id="StatsLine">
            <span>Найдено файлов: <b id="StatFiles">0</b></span>
            <span>Найдено папок: <b id="StatFoldersFound">0</b></span>
            <span>Осталось папок в очереди: <b id="StatFolders">0</b></span>
            <span>Лимит API: <b id="StatRate">—</b></span>
        </div>
        <div id="CurrentFileLine"></div>
        <div id="LogPanel"></div>
        <div id="SummaryBlock"></div>
    </div>

    <div class="Overlay" id="CommitOverlay">
        <div class="ModalBox">
            <h3>Репозиторий изменился</h3>
            <div>
                Сохранённый коммит: <span class="ShaBadge" id="OldShaText"></span><br/>
                Текущий коммит: <span class="ShaBadge" id="NewShaText"></span>
            </div>
            <div id="DiffText"></div>
            <div class="ModalButtons">
                <button id="ContinueBtn">Продолжить со старой версии</button>
                <button id="RescanBtn" class="Secondary">Пересканировать заново</button>
            </div>
        </div>
    </div>

    <div class="Overlay" id="ErrorOverlay">
        <div class="ModalBox">
            <h3 id="ErrorTitle">Ошибка</h3>
            <div id="ErrorMessage"></div>
            <button id="ErrorToggleBtn" class="Secondary">Показать технические детали ▾</button>
            <pre id="ErrorDetailsPre"></pre>
            <div class="ModalButtons">
                <button id="ErrorCopyBtn" class="Secondary">📋 Скопировать</button>
                <button id="ErrorCloseBtn">Закрыть</button>
            </div>
        </div>
    </div>

    <script>
    /* language=js */
    (function(){
        var PhaseLabels = {
            Resolving:     'Определяем ветку и коммит...',
            SingleFetch:   'Пробуем получить дерево за один запрос...',
            Resuming:      'Возобновляем сканирование...',
            BFS:           'Обход папок',
            RateLimitWait: 'Ожидание лимита GitHub API',
            Done:          'Готово',
            AlreadyDone:   'Уже полностью отсканировано ранее',
            Cancelled:     'Остановлено пользователем'
        };

        var LastErrorText = '';

        function FormatSize(Bytes){
            if(Bytes < 1024) return Bytes + ' Б';
            var Units = ['КБ','МБ','ГБ','ТБ'];
            var Value = Bytes;
            var UnitIndex = -1;
            do{ Value /= 1024; UnitIndex++; }while(Value >= 1024 && UnitIndex < Units.length - 1);
            return Value.toFixed(2) + ' ' + Units[UnitIndex];
        }

        function RateLimitColor(Remaining, Limit){
            if(!Limit || Limit < 0) return '#ddd';
            var Ratio = Remaining / Limit;
            if(Ratio > 0.5) return '#ddd';
            if(Ratio > 0.25) return '#e0c33e';
            if(Ratio > 0.1) return '#e08a3e';
            return '#e04f3e';
        }

        function UpdateRateLimitUI(Remaining, Limit){
            var El = document.getElementById('StatRate');
            if(Remaining < 0){ El.textContent = '—'; El.style.color = '#ddd'; return; }
            El.textContent = Remaining + ' / ' + Limit;
            El.style.color = RateLimitColor(Remaining, Limit);
        }

        function SetRunningState(IsRunning){
            document.getElementById('StartBtn').disabled = IsRunning;
            document.getElementById('StopBtn').disabled  = !IsRunning;
        }

        function UpdateProgressBar(P){
            var Bar = document.getElementById('ProgressBarInner');
            if(P.Phase === 'Done' || P.Phase === 'AlreadyDone'){
                Bar.classList.remove('Indeterminate');
                Bar.style.width = '100%';
                Bar.style.background = '#2d8a3e';
            }else if(P.Phase === 'Cancelled'){
                Bar.classList.remove('Indeterminate');
                Bar.style.width = '100%';
                Bar.style.background = '#777';
            }else if(P.TotalKnown){
                Bar.classList.remove('Indeterminate');
                Bar.style.background = '#0e639c';
                Bar.style.width = Math.min(100, (P.FilesFound / P.TotalKnown) * 100) + '%';
            }else{
                Bar.style.background = '#0e639c';
                Bar.classList.add('Indeterminate');
            }
        }

        function ResetProgressBar(){
            var Bar = document.getElementById('ProgressBarInner');
            Bar.classList.add('Indeterminate');
            Bar.style.width = '30%';
            Bar.style.background = '#0e639c';
        }

        function StopProgressBar(Color){
            var Bar = document.getElementById('ProgressBarInner');
            Bar.classList.remove('Indeterminate');
            Bar.style.width = '100%';
            Bar.style.background = Color;
        }

        function Send(Type, Data){
            window.chrome.webview.postMessage(JSON.stringify({ Type: Type, Data: Data }));
        }

        function AppendLog(Text, Time){
            var Panel = document.getElementById('LogPanel');
            var Line = document.createElement('div');
            if(Text.indexOf('[DEBUG]') === 0) Line.className = 'DebugLine';
            Line.textContent = '[' + (Time || '') + '] ' + Text;
            Panel.appendChild(Line);
            Panel.scrollTop = Panel.scrollHeight;
        }

        function ShowError(Title, Message, Details){
            document.getElementById('ErrorTitle').textContent = Title || 'Ошибка';
            document.getElementById('ErrorMessage').textContent = Message || '';
            document.getElementById('ErrorDetailsPre').textContent = Details || '';
            document.getElementById('ErrorDetailsPre').style.display = 'none';
            document.getElementById('ErrorToggleBtn').textContent = 'Показать технические детали ▾';
            LastErrorText = (Title || '') + '\n' + (Message || '') + '\n\n' + (Details || '');
            document.getElementById('ErrorOverlay').style.display = 'flex';
        }

        document.getElementById('ErrorCloseBtn').onclick = function(){
            document.getElementById('ErrorOverlay').style.display = 'none';
        };

        document.getElementById('ErrorToggleBtn').onclick = function(){
            var Pre = document.getElementById('ErrorDetailsPre');
            var Collapsed = Pre.style.display === 'none';
            Pre.style.display = Collapsed ? 'block' : 'none';
            this.textContent = Collapsed ? 'Скрыть технические детали ▴' : 'Показать технические детали ▾';
        };

        document.getElementById('ErrorCopyBtn').onclick = function(){
            if(navigator.clipboard && navigator.clipboard.writeText){
                navigator.clipboard.writeText(LastErrorText);
            }
        };

        var PendingDiff = null;
        var TokenDebounceHandle = null;

        document.getElementById('TokenInput').addEventListener('input', function(){
            clearTimeout(TokenDebounceHandle);
            TokenDebounceHandle = setTimeout(function(){
                Send('CheckRateLimit', { Token: document.getElementById('TokenInput').value });
            }, 600);
        });

        document.getElementById('OpenConfigBtn').onclick = function(){
            Send('BrowseConfigOpen', {});
        };

        document.getElementById('SaveConfigAsBtn').onclick = function(){
            Send('BrowseConfigSaveAs', {
                Repo: document.getElementById('RepoInput').value.trim(),
                Token: document.getElementById('TokenInput').value,
                OutputPath: document.getElementById('OutputInput').value.trim()
            });
        };

        document.getElementById('BrowseOutputBtn').onclick = function(){
            Send('BrowseOutputPath', { Repo: document.getElementById('RepoInput').value.trim() });
        };

        document.getElementById('StartBtn').onclick = function(){
            var Repo   = document.getElementById('RepoInput').value.trim();
            var Token  = document.getElementById('TokenInput').value;
            var Output = document.getElementById('OutputInput').value.trim();

            if(!Repo || !Output){
                ShowError('Заполните поля', 'Нужно указать репозиторий и путь к файлу результата', '');
                return;
            }

            document.getElementById('SummaryBlock').style.display = 'none';
            document.getElementById('LogPanel').innerHTML = '';
            SetRunningState(true);
            ResetProgressBar();

            Send('StartScan', { Repo: Repo, Token: Token, OutputPath: Output });
        };

        document.getElementById('StopBtn').onclick = function(){
            Send('CancelScan');
        };

        document.getElementById('OpenFolderBtn').onclick = function(){
            Send('OpenOutputFolder', { Path: document.getElementById('OutputInput').value.trim() });
        };

        document.getElementById('ContinueBtn').onclick = function(){
            document.getElementById('CommitOverlay').style.display = 'none';
            Send('ResolveCommitChange', { Action: 'Continue' });
        };

        document.getElementById('RescanBtn').onclick = function(){
            document.getElementById('CommitOverlay').style.display = 'none';
            Send('ResolveCommitChange', { Action: 'Rescan' });
        };

        if(window.chrome && window.chrome.webview){
            window.chrome.webview.addEventListener('message', function(Event){
                OnHostMessage({ data: Event.data });
            });
        }

        function OnHostMessage(Event){
            var Msg;
            try{ Msg = typeof Event.data === 'string' ? JSON.parse(Event.data) : Event.data; }
            catch(E){ return; }

            switch(Msg.Type){
                case 'SavedConfig':{
                    var C = Msg.Data || {};
                    if(C.Repo)       document.getElementById('RepoInput').value   = C.Repo;
                    if(C.Token)      document.getElementById('TokenInput').value  = C.Token;
                    if(C.OutputPath) document.getElementById('OutputInput').value = C.OutputPath;
                    if(C.ConfigPath){
                        document.getElementById('ConfigPathInput').value = C.ConfigPath;
                        document.getElementById('ConfigPathText').textContent = 'Загружено из: ' + C.ConfigPath;
                    }
                    break;
                }

                case 'ConfigSaved':{
                    var S = Msg.Data;
                    document.getElementById('ConfigPathInput').value = S.ConfigPath;
                    document.getElementById('ConfigPathText').textContent = 'Сохранено в: ' + S.ConfigPath;
                    break;
                }

                case 'OutputPathChosen':{
                    document.getElementById('OutputInput').value = Msg.Data.Path;
                    break;
                }

                case 'RateLimitInfo':{
                    UpdateRateLimitUI(Msg.Data.Remaining, Msg.Data.Limit);
                    break;
                }

                case 'ScanProgress':{
                    var P = Msg.Data;
                    document.getElementById('PhaseText').textContent = PhaseLabels[P.Phase] || P.Phase;
                    document.getElementById('StatFiles').textContent = P.FilesFound;
                    document.getElementById('StatFoldersFound').textContent = P.FoldersFound;
                    document.getElementById('StatFolders').textContent = P.FoldersRemaining;

                    UpdateRateLimitUI(P.RateLimitRemaining, P.RateLimitLimit);

                    var CurLine = document.getElementById('CurrentFileLine');
                    if(P.CurrentFile){
                        CurLine.textContent = 'Обрабатывается: /' + P.CurrentFile;
                    }else if(P.CurrentFolder){
                        CurLine.textContent = 'Папка: /' + P.CurrentFolder;
                    }else{
                        CurLine.textContent = '';
                    }

                    UpdateProgressBar(P);

                    if(P.Phase === 'Done' || P.Phase === 'AlreadyDone' || P.Phase === 'Cancelled'){
                        SetRunningState(false);
                    }
                    break;
                }

                case 'ScanLog':
                    AppendLog(Msg.Data.Text, Msg.Data.Time);
                    break;

                case 'CommitChanged':{
                    var D = Msg.Data;
                    PendingDiff = D;
                    document.getElementById('OldShaText').textContent = D.OldSha.substring(0, 10);
                    document.getElementById('NewShaText').textContent = D.NewSha.substring(0, 10);

                    var DiffText = 'Статус: ' + D.Status + '. ';
                    if(D.AheadBy > 0) DiffText += 'Новых коммитов: ' + D.AheadBy + '. ';
                    if(D.BehindBy > 0) DiffText += 'Отстаёт на: ' + D.BehindBy + '. ';

                    document.getElementById('DiffText').textContent = DiffText;
                    document.getElementById('CommitOverlay').style.display = 'flex';
                    break;
                }

                case 'ScanComplete':{
                    var Sm = Msg.Data;
                    SetRunningState(false);
                    StopProgressBar('#2d8a3e');

                    var Summary = document.getElementById('SummaryBlock');
                    Summary.style.display = 'block';
                    Summary.innerHTML =
                        '<b>Готово!</b><br/>' +
                        'Файлов: ' + Sm.FilesCount + '<br/>' +
                        'Папок: ' + Sm.FoldersCount + '<br/>' +
                        'Суммарный размер: ' + FormatSize(Sm.TotalSize) + '<br/>' +
                        'Файл: ' + Sm.OutputPath;
                    break;
                }

                case 'ScanCancelled':
                    SetRunningState(false);
                    StopProgressBar('#777');
                    break;

                case 'Error':
                    SetRunningState(false);
                    StopProgressBar('#823030');
                    ShowError(Msg.Data.Title, Msg.Data.Message, Msg.Data.Details);
                    break;
            }
        }

        window.onerror = function(Message, Source, Line, Col, Error){
            ShowError('Ошибка интерфейса (JS)', Message, (Error && Error.stack) ? Error.stack : (Source + ':' + Line + ':' + Col));
        };

        Send('Ready');
    })();
    </script>
    </body>
    </html>
    """;
}