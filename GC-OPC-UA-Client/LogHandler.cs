using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Net.Mail;

namespace GC_OPC_UA_Client
{
    class LogHandler
    {
        /// <summary>
        /// Static method for adding an entry to a log file including a time stamp
        /// </summary>
        /// <param name="logMessage">The message to be added to the logfile</param>
        public static void WriteLogFile(string logMessage)
        {
            try
            {
                System.Console.WriteLine(logMessage);
                CultureInfo ci = Thread.CurrentThread.CurrentCulture;
                Thread.CurrentThread.CurrentCulture = CultureInfo.CreateSpecificCulture("sv-SE");

                string fileName = "SEOPCLog" + DateTime.Now.ToShortDateString() + ".txt";


                string logFolder = settings.CloudLogFolder;

                FileStream w = File.Open(logFolder + System.IO.Path.DirectorySeparatorChar + fileName, System.IO.FileMode.Append, System.IO.FileAccess.Write, System.IO.FileShare.Write);
                StreamWriter sw = new StreamWriter(w, System.Text.Encoding.Default);
                sw.Write("{0} {1}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    DateTime.Now.ToLongDateString());
                sw.Write("\r\n");
                sw.Write("{0}", logMessage);
                sw.Write("\r\n-------------------------------\r\n\r\n");
                sw.Close();

                Thread.CurrentThread.CurrentCulture = ci;

            }
            catch (Exception e)
            {
                System.Console.WriteLine("Log file exception:" + e.Message);
            }
        }
    }
}
