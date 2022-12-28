using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Streetwriters.Common.Enums;
using Streetwriters.Common.Interfaces;
using Streetwriters.Common.Models;

namespace Streetwriters.Common
{
    public class Slogger<T>
    {
        public static Task Info(string scope, params string[] messages)
        {
            return Write(Format("info", scope, messages));
        }

        public static Task Error(string scope, params string[] messages)
        {
            return Write(Format("error", scope, messages));
        }
        private static string Format(string level, string scope, params string[] messages)
        {
            var date = DateTime.UtcNow.ToString("MM-dd-yyyy HH:mm:ss");
            var messageText = string.Join(" ", messages);
            return $"[{date}] | {level} | <{scope}> {messageText}";
        }
        private static Task Write(string line)
        {
            var logDirectory = Path.GetFullPath("./logs");
            if (!Directory.Exists(logDirectory))
                Directory.CreateDirectory(logDirectory);
            var path = Path.Join(logDirectory, typeof(T).FullName + "-" + DateTime.UtcNow.ToString("MM-dd-yyyy") + ".log");
            return File.AppendAllLinesAsync(path, new string[1] { line });
        }
    }
}