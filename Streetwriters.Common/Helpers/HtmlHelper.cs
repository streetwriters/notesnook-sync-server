using System.IO;
using WebMarkupMin.Core;
using WebMarkupMin.Core.Loggers;

namespace Streetwriters.Common.Helpers
{
    public static class HtmlHelper
    {
        public static string ReadMinifiedHtmlFile(string path)
        {
            var settings = new HtmlMinificationSettings()
            {
                WhitespaceMinificationMode = WhitespaceMinificationMode.Medium,
            };
            var cssMinifier = new KristensenCssMinifier();
            var jsMinifier = new CrockfordJsMinifier();

            var minifier = new HtmlMinifier(settings, cssMinifier, jsMinifier, new NullLogger());

            return minifier.Minify(File.ReadAllText(path), false).MinifiedContent;
        }
    }
}
