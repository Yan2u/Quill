using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Quill.Playground.Demos;
using Windows.Graphics;
using WinRT.Interop;

namespace Quill.Playground
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private LiveXamlView? _liveView;
        private StressTestView? _stressView;

        public MainWindow()
        {
            InitializeComponent();

            // set titlebar
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);
            AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

            // AppWindow.Resize takes physical pixels.
            double scale = GetDpiForWindow(WindowNative.GetWindowHandle(this)) / 96.0;
            AppWindow.Resize(new SizeInt32((int)(1400 * scale), (int)(880 * scale)));
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        private void OnPageHostLoaded(object sender, RoutedEventArgs e)
        {
            if (PageHost.Children.Count == 0)
            {
                ShowPage(PageSelector.SelectedItem?.Tag as string);
            }
        }

        private void OnPageSelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args) =>
            ShowPage(sender.SelectedItem?.Tag as string);

        private void ShowPage(string? tag)
        {
            UIElement page = tag == "stress" ? _stressView ??= new StressTestView() : _liveView ??= new LiveXamlView();
            foreach (UIElement child in PageHost.Children)
            {
                child.Visibility = Visibility.Collapsed;
            }

            if (!PageHost.Children.Contains(page))
            {
                PageHost.Children.Add(page);
            }

            page.Visibility = Visibility.Visible;
        }
    }
}
