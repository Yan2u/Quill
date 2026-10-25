using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;

namespace Quill
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
            _window.Activate();
        }

    }

}
