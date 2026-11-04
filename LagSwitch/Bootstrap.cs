using WLO.Math;

namespace LagSwitch;

public static class Bootstrap{
    private static WLO.Window.GLFW  Window = null!;
    private static WLO.Web.WebView2 Web    = null!;
    
    [STAThread]
    public static int Main(string[] Args){
        try{
            WL.Core.Start(Args);

            Web = WLO.Web.WebView2.Create();

            Window = new WLO.Window.GLFW(new Vector2I(1100, 700), "LagSwitch", Web);
            Window.TODO_UseDarkMode();

            while(!Window.IsClosed){
                Window.PollEvents();
                Window.PollEvents2();
            }

            Window.Close();
        }catch(Exception e){
            WL.Logger.Fatal("Опять всё взорвалось...", e);
            return WL.Core.Stop(1);
        }

        return WL.Core.Stop(0);
    }
}