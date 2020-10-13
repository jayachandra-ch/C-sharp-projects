using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft;


namespace WindowsFormsApplication2
{
    static class Program
    {
       // [DllImport("kernel32.dll")]
        //public static extern Boolean AllocConsole();
        //[DllImport("kernel32.dll")]
       // public static extern Boolean FreeConsole();
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
                  
            // AllocConsole();
            //Stopwatch sw = Stopwatch.StartNew();
          //  Application.EnableVisualStyles();
           // Application.SetCompatibleTextRenderingDefault(false);
            Form1 f = new Form1();
            Application.Run(f);
        //    sw.Stop();
            //Console.WriteLine("the watch is " + Stopwatch.IsHighResolution);
          //  Console.WriteLine("Time used (float): {0} ms"+sw.Elapsed.TotalMilliseconds);
          //  Console.Read();
          //  FreeConsole();
        }
    }
}
