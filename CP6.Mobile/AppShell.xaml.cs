namespace CP6.Mobile;

public partial class AppShell : Shell
{
    public AppShell(LoginPage loginPage)
    {
        InitializeComponent();
        Items.Add(new ShellContent
        {
            Route = "login",
            Title = "Sign in",
            Content = loginPage,
        });
        Routing.RegisterRoute("tasks", typeof(TaskListPage));
        Routing.RegisterRoute("task-detail", typeof(TaskDetailPage));
        Routing.RegisterRoute("move-scan", typeof(MoveScanPage));
        Routing.RegisterRoute("upgrade", typeof(UpgradePage));
        Routing.RegisterRoute("device-activation", typeof(DeviceActivationPage));
    }
}
