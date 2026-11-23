using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Polya {
    static class Program {
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
        [STAThread]
        static void Main(string[] args) {
            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException+=(s,e)=>Prompts.Alert(null,"Ошибка приложения",e.Exception.Message);
            Application.Run(new Workspace(args.Length>0?args[0]:null));
        }
    }
}
