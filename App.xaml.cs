using System.Globalization;
using System.Text;
using System.Windows;

namespace SimpleRollCall
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // One time setup, before any window or page is created:
            // - gb18030 has to come from the code pages provider
            // - the UI culture has to be set first so that the {x:Static} resource
            //   lookups in XAML already resolve to the configured language
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { /* falls back to utf-8 if gb18030 is unavailable */ }

            CultureInfo.CurrentUICulture = AppConfig.Current.GetCulture();

            base.OnStartup(e);
        }
    }
}
