using WLO;
using WLO.Math;

namespace YouTubePublish;

public static class Bootstrap{
    [STAThread]
    public static int Main(string[] Args){
        try{
            WLO.Web.WebView2 Web = WLO.Web.WebView2.Create();
            
            WLO.Window.GLFW Window = new WLO.Window.GLFW(new Vector2I(1280, 720), "Uploader", Web);

            Web.Navigate("https://woowz11.github.io/woowzsite/quare.html");

            // ----------------------------------------------------------------------
            
            YouTube YouTube = new YouTube(File.ReadAllText(@"W:\Other\rofl-games\YouTubePublish\client_secret.json"), @"W:\Other\rofl-games\YouTubePublish\UserLogin", "YoutubePublisher");

            WL.Logger.Info($"Канал: {YouTube.MyChannel.Title}");
            WL.Logger.Info($"ID: {YouTube.MyChannel.ID}");
            WL.Logger.Info($"Подписчиков: {YouTube.MyChannel.SubscriberCount}");
            WL.Logger.Info($"Видео: {YouTube.MyChannel.VideoCount}");
            WL.Logger.Info($"Просмотров: {YouTube.MyChannel.ViewCount}");
            
            WL.Logger.Info("");
            
            foreach(var V in YouTube.ListUploads(10)){
                WL.Logger.Info($"[{V.ID}] {V.Title} ({V.PublishedAt})");
            }
            
            // ----------------------------------------------------------------------
            
            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();
            }

            Window.Close();
        }catch(Exception e){
            WL.Logger.Fatal("Всё взорвалось...", e);
            return 1;
        }
        
        return 0;
    }
}