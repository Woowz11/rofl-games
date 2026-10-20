using WLO.Math;

namespace YouTubePublish;

public static class Bootstrap{
    [STAThread]
    public static int Main(string[] Args){
        try{
            WLO.Web.WebView2 Web = WLO.Web.WebView2.Create();
            
            WLO.Window.GLFW Window = new WLO.Window.GLFW(new Vector2I(1280, 720), "Uploader", Web);

            Web.Navigate("https://woowz11.github.io/woowzsite/quare.html");

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