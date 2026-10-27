using Microsoft.UI.Xaml;
using Quill.Playground.SelfTest;

namespace Quill.Playground
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private Window? _window;

        public App()
        {
            InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            _window ??= new MainWindow();

            if (SelfTestRunner.TryGetReportPath(Environment.GetCommandLineArgs(), out string reportPath))
            {
                SelfTestRunner.Start(_window, reportPath);
                return;
            }

            _window.Activate();
        }

    }

}
