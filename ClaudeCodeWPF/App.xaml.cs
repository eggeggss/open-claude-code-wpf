using System.Globalization;
using System.Threading;
using System.Windows;
using OpenClaudeCodeWPF.Services;

namespace OpenClaudeCodeWPF
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var config = ConfigService.Instance;
            string lang = config.Language;
            if (!string.IsNullOrEmpty(lang))
            {
                try
                {
                    var culture = new CultureInfo(lang);
                    Thread.CurrentThread.CurrentCulture = culture;
                    Thread.CurrentThread.CurrentUICulture = culture;
                }
                catch { }
            }
        }
    }
}
