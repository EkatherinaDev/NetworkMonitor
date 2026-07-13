namespace NetworkMonitor;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        if (AppInstaller.ShouldHandle(args))
        {
            AppInstaller.Handle(args);
            return;
        }

        Application.Run(new Form1());
    }
}
