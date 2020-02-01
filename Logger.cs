
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.IO.Compression;
using System.Threading;
using System.Diagnostics;

namespace Logger
{
    /*
 * Created by SharpDevelop.
 * User: user
 * Date: 04.02.2014
 * Time: 15:30
 * 
 * To change this template use Tools | Options | Coding | Edit Standard Headers.
 */

    public class Logger
    {
        static readonly string name = "update.log";
        string concretePath;

       private static Logger logger;

        private Logger(String _path)
        {
            try
            {
                string path;
                if (String.IsNullOrEmpty(_path))
                    path = Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments);
                else
                    path = _path;
                this.concretePath = Path.Combine(path, name);

                this.LogMessage("\r\nInitializing Logs\nLog Entry : ");
                this.LogMessage(DateTime.Now.ToLongTimeString() + " " + DateTime.Now.ToLongDateString());
            }
            catch { }
        }

        public static void CreateLogger(string path)
        {
            logger = new Logger(path);
        }

        private void LogMessage(string logMessage)
        {
            try
            {
                using (StreamWriter w = File.AppendText(this.concretePath))
                {
                    w.WriteLine(logMessage);
                }
            }
            catch {}
        }
        public static void Log(string logMessage)
        {
            if (logger == null)
                return;
            logger.LogMessage(logMessage);
        }
    }
}