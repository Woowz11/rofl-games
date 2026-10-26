using WLI;
using WLO;
using WLO.Database;

namespace GitHubParser;

public class FileRow{
    public int ID{ get; set; }
    public string Path{ get; set; } = "";
    public string? Extension{ get; set; }
    public long Size{ get; set; }
}

public static class test{
    public static void GO_TEST_SQL(){
        using Database DB = SQLite.OpenInMemory(); // не трогаем диск вообще, чисто для теста

        DBTable<FileRow> Files = DB.Table<FileRow>();

        WL.Logger.Info("1) Таблица создана: OK");

        // --- Insert ---
        Files.Insert(new FileRow{ Path = "src/logo.png", Extension = "png", Size = 15000 });
        Files.Insert(new FileRow{ Path = "src/readme.md", Extension = "md", Size = 500 });
        WL.Logger.Info("2) Insert прошёл: OK");

        // --- InsertMany + транзакция ---
        Files.InsertMany(new[]{
            new FileRow{ Path = "a.png", Extension = "png", Size = 100 },
            new FileRow{ Path = "b.png", Extension = "png", Size = 99999 },
            new FileRow{ Path = "c.txt", Extension = "txt", Size = 10 },
        });
        WL.Logger.Info("3) InsertMany прошёл: OK");

        // --- Where + OrderBy ---
        List<FileRow> Pngs = Files.Where(F => F.Extension == "png")
                                   .OrderByDescending(F => F.Size)
                                   .ToList();

        WL.Logger.Info($"4) Найдено png: {Pngs.Count} (ожидали 3)");
        foreach(FileRow F in Pngs){ WL.Logger.Info($"   - {F.Path} : {F.Size}"); }

        // --- Count ---
        int Total = Files.Where(F => F.Extension == "png").Count();
        WL.Logger.Info($"5) Count png: {Total} (ожидали 3)");

        // --- Update ---
        Files.Update(F => F.Path == "a.png", new{ Size = 777 });
        FileRow? Updated = Files.Where(F => F.Path == "a.png").FirstOrDefault();
        WL.Logger.Info($"6) После Update size={Updated?.Size} (ожидали 777)");

        // --- DeleteWhere ---
        Files.DeleteWhere(F => F.Extension == "txt");
        int AfterDelete = Files.All().Count();
        WL.Logger.Info($"7) После удаления txt осталось: {AfterDelete} (ожидали 4)");

        // --- Сырой SQL (дверь наружу) ---
        object? Max = DB.QueryScalar("SELECT MAX(Size) FROM FileRow");
        WL.Logger.Info($"8) Сырой SQL MAX(Size): {Max} (ожидали 99999)");

        WL.Logger.Info("Готово. Если везде ожидания совпали — библиотека работает.");
    }
}