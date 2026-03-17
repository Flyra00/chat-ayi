using ChatAyi.Pages;

namespace ChatAyi;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        Routing.RegisterRoute("chat", typeof(ChatPage));
    }
}
