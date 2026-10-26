using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GitHubExplorer
{
    public static class GlobMatcher
    {
        public static bool MatchesExpression(string? Pattern, string Target)
        {
            if (string.IsNullOrWhiteSpace(Pattern)) return true;
            var OrParts = Pattern.Split(new[] { "||" }, StringSplitOptions.None);
            foreach (var OrPart in OrParts)
            {
                var AndParts = OrPart.Split(new[] { "&&" }, StringSplitOptions.None);
                bool AllMatch = true;
                foreach (var Part in AndParts)
                {
                    var Trimmed = Part.Trim();
                    if (Trimmed.Length == 0) continue;
                    if (!MatchesSingle(Trimmed, Target)) { AllMatch = false; break; }
                }
                if (AllMatch) return true;
            }
            return false;
        }

        public static bool MatchesSingle(string Term, string Target)
        {
            if (string.IsNullOrEmpty(Term)) return true;
            if (!HasWildcard(Term))
                return Target.IndexOf(Term, StringComparison.OrdinalIgnoreCase) >= 0;

            var Pattern = "^" + WildcardToRegexBody(Term) + "$";
            return Regex.IsMatch(Target, Pattern, RegexOptions.IgnoreCase);
        }

        public static bool MatchesPathAnchored(string Pattern, string RelativePath)
        {
            if (string.IsNullOrEmpty(Pattern)) return false;
            var RegexPattern = "^" + WildcardToRegexBody(Pattern) + "$";
            return Regex.IsMatch(RelativePath, RegexPattern, RegexOptions.IgnoreCase);
        }

        private static bool HasWildcard(string Term) => Term.IndexOf('*') >= 0 || Term.IndexOf('?') >= 0;

        private static string WildcardToRegexBody(string Term)
        {
            var Sb = new StringBuilder();
            foreach (var Ch in Term)
            {
                if (Ch == '*') Sb.Append(".*");
                else if (Ch == '?') Sb.Append('.');
                else Sb.Append(Regex.Escape(Ch.ToString()));
            }
            return Sb.ToString();
        }
    }

    public sealed class ClassificationEntry
    {
        public string RelativePath { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsFolder { get; set; }
        public long Size { get; set; }
        public int Depth => RelativePath.Count(c => c == '/');
    }

    public sealed class ClassificationContext
    {
        public string FolderPath { get; set; } = "";
        public List<ClassificationEntry> Entries { get; set; } = new();

        public IEnumerable<ClassificationEntry> Files => Entries.Where(E => !E.IsFolder);
        public IEnumerable<ClassificationEntry> Folders => Entries.Where(E => E.IsFolder);

        public bool AnyMatches(string Pattern, bool FoldersOnly = false, bool FilesOnly = false, int MaxDepth = int.MaxValue)
        {
            var Source = Entries.AsEnumerable();
            if (FoldersOnly) Source = Source.Where(E => E.IsFolder);
            if (FilesOnly) Source = Source.Where(E => !E.IsFolder);
            if (MaxDepth != int.MaxValue) Source = Source.Where(E => E.Depth <= MaxDepth);
            return Source.Any(E => GlobMatcher.MatchesPathAnchored(Pattern, E.RelativePath));
        }

        public int CountMatches(string Pattern, bool FoldersOnly = false, bool FilesOnly = false, int MaxDepth = int.MaxValue)
        {
            var Source = Entries.AsEnumerable();
            if (FoldersOnly) Source = Source.Where(E => E.IsFolder);
            if (FilesOnly) Source = Source.Where(E => !E.IsFolder);
            if (MaxDepth != int.MaxValue) Source = Source.Where(E => E.Depth <= MaxDepth);
            return Source.Count(E => GlobMatcher.MatchesPathAnchored(Pattern, E.RelativePath));
        }
    }

    public sealed class ClassificationCondition
    {
        private readonly Func<ClassificationContext, bool> _Eval;
        public string Description { get; }

        public ClassificationCondition(Func<ClassificationContext, bool> Eval, string Description)
        {
            _Eval = Eval;
            this.Description = Description;
        }

        public bool Evaluate(ClassificationContext Ctx) => _Eval(Ctx);
        public override string ToString() => Description;
    }

    public static class Cond
    {
        public static ClassificationCondition HasFile(string Pattern) =>
            new ClassificationCondition(Ctx => Ctx.AnyMatches(Pattern, FilesOnly: true), $"Есть файл: {Pattern}");

        public static ClassificationCondition HasFolder(string Pattern) =>
            new ClassificationCondition(Ctx => Ctx.AnyMatches(Pattern, FoldersOnly: true), $"Есть папка: {Pattern}");

        public static ClassificationCondition HasAny(string Pattern) =>
            new ClassificationCondition(Ctx => Ctx.AnyMatches(Pattern), $"Есть: {Pattern}");

        public static ClassificationCondition CountAtLeast(string Pattern, int Min) =>
            new ClassificationCondition(Ctx => Ctx.CountMatches(Pattern) >= Min, $"Минимум {Min} совпадений: {Pattern}");

        public static ClassificationCondition HasExtension(string Ext, string? WithinFolder = null) =>
            new ClassificationCondition(Ctx => Ctx.Entries.Any(E =>
                    !E.IsFolder &&
                    E.Name.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) &&
                    (WithinFolder == null || E.RelativePath.StartsWith(WithinFolder + "/", StringComparison.OrdinalIgnoreCase))),
                WithinFolder == null ? $"Есть файл с расширением {Ext}" : $"Есть файл с расширением {Ext} внутри {WithinFolder}/");

        public static ClassificationCondition ExtensionCountAtLeast(string Ext, int Min, string? WithinFolder = null) =>
            new ClassificationCondition(Ctx => Ctx.Entries.Count(E =>
                    !E.IsFolder &&
                    E.Name.EndsWith(Ext, StringComparison.OrdinalIgnoreCase) &&
                    (WithinFolder == null || E.RelativePath.StartsWith(WithinFolder + "/", StringComparison.OrdinalIgnoreCase))) >= Min,
                WithinFolder == null ? $"Минимум {Min} файлов с расширением {Ext}" : $"Минимум {Min} файлов с расширением {Ext} внутри {WithinFolder}/");

        public static ClassificationCondition PathContains(string Substr) =>
            new ClassificationCondition(Ctx => Ctx.Entries.Any(E => E.RelativePath.IndexOf(Substr, StringComparison.OrdinalIgnoreCase) >= 0), $"Путь содержит: {Substr}");

        public static ClassificationCondition HasFileAnyOf(params string[] Patterns) =>
            new ClassificationCondition(Ctx => Patterns.Any(P => Ctx.AnyMatches(P, FilesOnly: true)),
                "Есть один из файлов: " + string.Join(", ", Patterns));

        public static ClassificationCondition HasFolderAnyOf(params string[] Patterns) =>
            new ClassificationCondition(Ctx => Patterns.Any(P => Ctx.AnyMatches(P, FoldersOnly: true)),
                "Есть одна из папок: " + string.Join(", ", Patterns));

        public static ClassificationCondition HasAllFiles(params string[] Patterns) =>
            new ClassificationCondition(Ctx => Patterns.All(P => Ctx.AnyMatches(P, FilesOnly: true)),
                "Есть все файлы: " + string.Join(", ", Patterns));

        public static ClassificationCondition HasAllFolders(params string[] Patterns) =>
            new ClassificationCondition(Ctx => Patterns.All(P => Ctx.AnyMatches(P, FoldersOnly: true)),
                "Есть все папки: " + string.Join(", ", Patterns));

        public static ClassificationCondition FolderNameIs(string Name) =>
            new ClassificationCondition(Ctx => string.Equals(GetFolderName(Ctx.FolderPath), Name, StringComparison.OrdinalIgnoreCase),
                $"Название папки: {Name}");

        public static ClassificationCondition FolderNameContains(string Substr) =>
            new ClassificationCondition(Ctx => GetFolderName(Ctx.FolderPath).IndexOf(Substr, StringComparison.OrdinalIgnoreCase) >= 0,
                $"Название папки содержит: {Substr}");

        public static ClassificationCondition Not(ClassificationCondition C) =>
            new ClassificationCondition(Ctx => !C.Evaluate(Ctx), $"НЕ ({C.Description})");

        public static ClassificationCondition And(params ClassificationCondition[] Conditions) =>
            new ClassificationCondition(Ctx => Conditions.All(C => C.Evaluate(Ctx)), string.Join(" И ", Conditions.Select(C => C.Description)));

        public static ClassificationCondition Or(params ClassificationCondition[] Conditions) =>
            new ClassificationCondition(Ctx => Conditions.Any(C => C.Evaluate(Ctx)), string.Join(" ИЛИ ", Conditions.Select(C => C.Description)));

        private static string GetFolderName(string FolderPath)
        {
            if (string.IsNullOrEmpty(FolderPath)) return "";
            var Trimmed = FolderPath.TrimEnd('/');
            var Idx = Trimmed.LastIndexOf('/');
            return Idx < 0 ? Trimmed : Trimmed.Substring(Idx + 1);
        }
    }

    public sealed class ClassificationRule
    {
        public ClassificationCondition Condition { get; }
        public double Weight { get; }
        public string? Note { get; }

        public ClassificationRule(ClassificationCondition Condition, double Weight, string? Note = null)
        {
            this.Condition = Condition;
            this.Weight = Weight;
            this.Note = Note;
        }
    }

    public sealed class ClassificationProfile
    {
        public string Key { get; }
        public string DisplayName { get; }
        public string IconUrl { get; }
        public double Threshold { get; }
        public List<ClassificationRule> Rules { get; } = new();
        public List<ClassificationRule> RequiredRules { get; } = new();

        public ClassificationProfile(string Key, string DisplayName, string IconUrl, double Threshold)
        {
            this.Key = Key;
            this.DisplayName = DisplayName;
            this.IconUrl = IconUrl;
            this.Threshold = Threshold;
        }

        public ClassificationProfile Add(ClassificationCondition Condition, double Weight, string? Note = null)
        {
            Rules.Add(new ClassificationRule(Condition, Weight, Note));
            return this;
        }

        public ClassificationProfile Require(ClassificationCondition Condition, string? Note = null)
        {
            RequiredRules.Add(new ClassificationRule(Condition, 0, Note ?? ("Обязательно: " + Condition.Description)));
            return this;
        }
    }

    public sealed class ClassificationResult
    {
        public ClassificationProfile Profile { get; set; } = null!;
        public double Score { get; set; }
    }

    public sealed class ClassificationRuleMatch
    {
        public ClassificationRule Rule { get; set; } = null!;
        public bool Matched { get; set; }
        public double WeightContribution => Matched ? Rule.Weight : 0;
    }

    public sealed class ClassificationBreakdown
    {
        public ClassificationProfile Profile { get; set; } = null!;
        public double Score { get; set; }
        public double Percentage { get; set; }
        public List<ClassificationRuleMatch> RuleMatches { get; set; } = new();
        public bool IsMatch => Score >= Profile.Threshold;

        public ClassificationResult ToResult() => new ClassificationResult { Profile = Profile, Score = Score };
    }

    public sealed class EntryClassifier
    {
        private readonly List<ClassificationProfile> _Profiles = new();

        public IReadOnlyList<ClassificationProfile> Profiles => _Profiles;

        public EntryClassifier Register(ClassificationProfile Profile)
        {
            _Profiles.Add(Profile);
            return this;
        }

        public List<ClassificationBreakdown> ClassifyDetailed(ClassificationContext Ctx)
        {
            var Results = new List<ClassificationBreakdown>();
            foreach (var Profile in _Profiles)
            {
                var Matches = new List<ClassificationRuleMatch>();
                bool RequiredOk = true;

                foreach (var Req in Profile.RequiredRules)
                {
                    bool Matched = Req.Condition.Evaluate(Ctx);
                    if (!Matched) RequiredOk = false;
                    Matches.Add(new ClassificationRuleMatch { Rule = Req, Matched = Matched });
                }

                double Score = 0;

                if (RequiredOk)
                {
                    foreach (var Rule in Profile.Rules)
                    {
                        bool Matched = false;
                        if (Score < Profile.Threshold)
                        {
                            Matched = Rule.Condition.Evaluate(Ctx);
                            if (Matched) Score += Rule.Weight;
                        }
                        Matches.Add(new ClassificationRuleMatch { Rule = Rule, Matched = Matched });
                    }
                }
                else
                {
                    foreach (var Rule in Profile.Rules)
                        Matches.Add(new ClassificationRuleMatch { Rule = Rule, Matched = false });
                }

                Results.Add(new ClassificationBreakdown { Profile = Profile, Score = RequiredOk ? Score : 0, RuleMatches = Matches });
            }

            double TotalPositive = Results.Where(R => R.Score > 0).Sum(R => R.Score);
            foreach (var R in Results)
                R.Percentage = TotalPositive > 0 && R.Score > 0 ? (R.Score / TotalPositive) * 100.0 : 0.0;

            return Results.OrderByDescending(R => R.Score).ToList();
        }

        public ClassificationBreakdown? ClassifyBest(ClassificationContext Ctx) =>
            ClassifyDetailed(Ctx).FirstOrDefault(R => R.IsMatch);

        public List<ClassificationResult> ClassifyAll(ClassificationContext Ctx) =>
            ClassifyDetailed(Ctx).Where(R => R.IsMatch).Select(R => R.ToResult()).ToList();
    }

    public sealed class FolderClassification
    {
        public ClassificationResult? Best { get; set; }
        public List<ClassificationBreakdown> Breakdown { get; set; } = new();
    }

    public sealed class FileClassification
    {
        public ClassificationResult? InheritedBest { get; set; }
        public string? SourceFolderPath { get; set; }
        public List<ClassificationBreakdown>? SourceBreakdown { get; set; }
    }

    public sealed class CachedEntryClassifier
    {
        private readonly EntryClassifier _Classifier;
        private readonly ConcurrentDictionary<string, FolderClassification> _FolderCache =
            new(StringComparer.OrdinalIgnoreCase);

        public CachedEntryClassifier(EntryClassifier Classifier)
        {
            _Classifier = Classifier;
        }

        public IReadOnlyList<ClassificationProfile> Profiles => _Classifier.Profiles;

        public FolderClassification ClassifyFolder(string FolderPath, Func<string, ClassificationContext> BuildContext)
        {
            return _FolderCache.GetOrAdd(FolderPath, Key =>
            {
                var Ctx = BuildContext(Key);
                var Breakdown = _Classifier.ClassifyDetailed(Ctx);
                var Best = Breakdown.FirstOrDefault(B => B.IsMatch)?.ToResult();
                return new FolderClassification { Best = Best, Breakdown = Breakdown };
            });
        }

        public FileClassification ClassifyFile(string FileFolderPath, Func<string, ClassificationContext> BuildContext, int MaxLevelsUp = 8)
        {
            var Current = FileFolderPath;
            for (int Level = 0; Level <= MaxLevelsUp; Level++)
            {
                if (string.IsNullOrEmpty(Current)) break;
                var Classification = ClassifyFolder(Current, BuildContext);
                if (Classification.Best != null)
                {
                    return new FileClassification
                    {
                        InheritedBest = Classification.Best,
                        SourceFolderPath = Current,
                        SourceBreakdown = Classification.Breakdown
                    };
                }
                Current = GetParentFolderPath(Current);
            }
            return new FileClassification { InheritedBest = null, SourceFolderPath = null, SourceBreakdown = null };
        }

        public void Invalidate(string FolderPath) => _FolderCache.TryRemove(FolderPath, out _);
        public void InvalidateAll() => _FolderCache.Clear();

        private static string GetParentFolderPath(string FolderPath)
        {
            var Trimmed = FolderPath.TrimEnd('/');
            var Idx = Trimmed.LastIndexOf('/');
            if (Idx < 0) return "";
            return Trimmed.Substring(0, Idx);
        }
    }

    public static class KnownClassificationProfiles
    {
        private static string ToSourceIcon(string Name) =>
            $"https://github.com/Woowz11/woowzsite/raw/refs/heads/main/source/GitHubExplorer/{Name}.svg";

        public static ClassificationProfile GModAddon()
        {
            var P = new ClassificationProfile("gmod_addon", "Garry's Mod аддон", ToSourceIcon("gmod-addon"), 10);
            P.Add(Cond.HasFile("addon.json"), 10, "Манифест аддона");
            P.Add(Cond.HasFolder("lua/*"), 4, "Папка с Lua-кодом");
            P.Add(Cond.HasFolder("gamemodes/*"), 3, "Похоже на gamemode");
            P.Add(Cond.HasFolder("materials/*"), 2, "Папка с материалами");
            P.Add(Cond.HasFolder("models/*"), 2, "Папка с моделями");
            P.Add(Cond.HasFolder("sound/*"), 1, "Папка со звуками");
            P.Add(Cond.HasFolder("particles/*"), 1, "Папка с частицами");
            P.Add(Cond.HasExtension(".lua", "lua"), 1, "Есть .lua файлы внутри lua/");
            return P;
        }

        public static ClassificationProfile UnityProject()
        {
            var P = new ClassificationProfile("unity_project", "Unity проект", ToSourceIcon("unity-project"), 10);

            P.Require(Cond.HasFolder("Assets"), "Обязательно: папка Assets");
            P.Require(Cond.HasFolder("ProjectSettings"), "Обязательно: папка ProjectSettings");

            P.Add(Cond.HasFile("ProjectSettings/ProjectVersion.txt"), 10, "Файл версии редактора Unity");
            P.Add(Cond.HasFile("Assembly-CSharp.csproj"), 4, "Сгенерированный C#-проект для скриптов");
            P.Add(Cond.HasFile("*.sln"), 3, "Файл решения Visual Studio");
            P.Add(Cond.HasFolder("Packages"), 3, "Папка пакетов");
            P.Add(Cond.HasFile("Unity.RenderPipelines.HighDefinition.Config.Runtime.csproj"), 2, "Проект конфигурации HDRP");
            P.Add(Cond.HasFile("obj/Debug/Assembly-CSharp.csprojAssemblyReference.cache"), 2, "Кэш ссылок сборки Assembly-CSharp");
            P.Add(Cond.HasFile("Packages/manifest.json"), 2, "Манифест пакетов");
            P.Add(Cond.HasFile("UserSettings/EditorUserSettings.asset"), 2, "Пользовательские настройки редактора");
            P.Add(Cond.HasFolder("Assets/Scenes"), 2, "Папка сцен");
            P.Add(Cond.HasExtension(".unity", "Assets"), 2, "Есть файлы сцен .unity внутри Assets/");
            P.Add(Cond.HasFile(".vsconfig"), 1, "Конфигурация Visual Studio");
            P.Add(Cond.HasFile("Packages/packages-lock.json"), 1, "Файл блокировки версий пакетов");
            P.Add(Cond.HasFolder("LocalPackages"), 1, "Папка локальных пакетов");
            P.Add(Cond.HasFolder("Logs"), 1, "Папка логов редактора");
            P.Add(Cond.HasFolder("UserSettings"), 1, "Пользовательские настройки редактора");
            P.Add(Cond.HasFile("UserSettings/Search.index"), 1, "Индекс поиска редактора");
            P.Add(Cond.HasFile("UserSettings/Search.settings"), 1, "Настройки поиска редактора");
            P.Add(Cond.HasFile("ProjectSettings/AudioManager.asset"), 1, "Настройки звука проекта");
            P.Add(Cond.HasFile("ProjectSettings/EditorSettings.asset"), 1, "Настройки редактора проекта");
            P.Add(Cond.HasFile("ProjectSettings/GraphicsSettings.asset"), 1, "Настройки графики проекта");
            P.Add(Cond.HasFile("ProjectSettings/InputManager.asset"), 1, "Настройки ввода проекта");
            P.Add(Cond.HasFile("ProjectSettings/Physics2DSettings.asset"), 1, "Настройки физики 2D");
            P.Add(Cond.HasFile("ProjectSettings/QualitySettings.asset"), 1, "Настройки качества графики");
            P.Add(Cond.HasFile("ProjectSettings/TimeManager.asset"), 1, "Настройки времени проекта");
            P.Add(Cond.HasFile("ProjectSettings/VersionControlSettings.asset"), 1, "Настройки системы контроля версий");
            P.Add(Cond.HasFolder("obj"), 1, "Промежуточная папка сборки");
            P.Add(Cond.HasFolder("Temp"), 1, "Временная папка редактора");
            P.Add(Cond.HasExtension(".meta", "Assets"), 1, "Есть .meta файлы внутри Assets/");
            P.Add(Cond.HasExtension(".asset", "Assets"), 1, "Есть .asset файлы внутри Assets/");
            P.Add(Cond.HasExtension(".prefab", "Assets"), 1, "Есть .prefab файлы внутри Assets/");
            P.Add(Cond.FolderNameContains("unity"), 1, "Название папки содержит \"unity\"");
            P.Add(Cond.HasFile("*.log"), 0.5, "Есть лог-файлы (слабый доп. признак)");
            P.Add(Cond.HasFile("*.json"), 0.5, "Есть json-файлы (слабый доп. признак)");

            return P;
        }

        public static ClassificationProfile MCreatorProject()
        {
            var P = new ClassificationProfile("mcreator_project", "MCreator проект", ToSourceIcon("mcreator-project"), 8);

            P.Require(Cond.HasFile("*.mcreator"), "Обязательно: файл проекта MCreator (*.mcreator)");

            P.Add(Cond.HasFile("*.mcreator"), 8, "Файл проекта MCreator");
            P.Add(Cond.HasFolder("elements"), 3, "Папка элементов мода");
            P.Add(Cond.HasFile("build.gradle"), 2, "Файл сборки Gradle");
            P.Add(Cond.HasFile("mcreator.gradle"), 2, "Gradle-конфигурация MCreator");
            P.Add(Cond.HasFolder("gradle"), 2, "Папка Gradle wrapper");
            P.Add(Cond.HasFolder("models"), 1, "Папка моделей");
            P.Add(Cond.HasFolder("run"), 1, "Папка тестового запуска Minecraft");
            P.Add(Cond.HasFolder("src"), 1, "Папка исходного кода мода");
            P.Add(Cond.HasFile(".classpath"), 1, "Файл classpath Eclipse");
            P.Add(Cond.HasFile(".project"), 1, "Файл проекта Eclipse");
            P.Add(Cond.HasFile("gradlew"), 1, "Gradle wrapper (Unix)");
            P.Add(Cond.HasFile("gradlew.bat"), 1, "Gradle wrapper (Windows)");
            P.Add(Cond.HasFile("run/options.txt"), 1, "Внутри run/ есть установленный клиент Minecraft");
            P.Add(Cond.HasFolder("run/saves"), 1, "Внутри run/ есть сохранения Minecraft");

            return P;
        }

        public static ClassificationProfile MinecraftGame()
        {
            var P = new ClassificationProfile("minecraft_game", "Minecraft (игра/инстанс)", ToSourceIcon("minecraft-game"), 5);

            P.Add(Cond.HasFile("options.txt"), 4, "Файл настроек игрока Minecraft");
            P.Add(Cond.HasFile("server.properties"), 4, "Настройки сервера Minecraft");
            P.Add(Cond.HasFolder("saves"), 3, "Папка сохранений миров");
            P.Add(Cond.HasFolder("config"), 1, "Папка конфигурации модов/игры");
            P.Add(Cond.HasFolder("crash-reports"), 1, "Папка отчётов о крашах");
            P.Add(Cond.HasFolder("screenshots"), 1, "Папка скриншотов");
            P.Add(Cond.HasFile("usercache.json"), 1, "Кэш пользователей сервера");
            P.Add(Cond.HasFile("usernamecache.json"), 1, "Кэш никнеймов сервера");

            return P;
        }

        public static EntryClassifier BuildDefault() =>
            new EntryClassifier()
                .Register(GModAddon())
                .Register(UnityProject())
                .Register(MCreatorProject())
                .Register(MinecraftGame());
    }
}