using WLO.Math;

namespace GitHubParser;

public static class Bootstrap{
    private static WLO.Web.WebView2 Web    = null!;
    private static WLO.Window.GLFW  Window = null!;
    
    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            Web = WLO.Web.WebView2.Create();

            Window = new WLO.Window.GLFW(new Vector2I(720, 1280), "Github Parser", Web);
            Window.TODO_UseDarkMode();
            
            Web.Navigate("https://woowz11.github.io/woowzsite/quare.html");

            test.GO_TEST_SQL();
            
            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();
            }

            Window.Close();
        }catch(Exception e){
            WL.Logger.Fatal("Всё взорвалось?...", e);
            return WL.Core.Stop(1);
        }

        return WL.Core.Stop(0);
    }
}