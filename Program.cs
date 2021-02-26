using System;
using System.Windows.Forms;

namespace AlphaTerm
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool ansi = false;
            bool crlf = false;
            bool cursor = true;
            bool echo = false;
            string host = null;
            int port = 23;
            int cols = 80;
            int rows = 25;
            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower()) {
                    case "-ansi":
                        ansi = true;
                        break;
                    case "-crlf":
                        crlf = true;
                        //crlf = (i + 1 < args.Length && (args[i + 1].ToLower() == "1" || args[i + 1].ToLower() == "on"));
                        break;
                    //case "-cursor":
                    //    cursor = (i + 1 < args.Length && (args[i + 1].ToLower() == "1" || args[i + 1].ToLower() == "on"));
                    //    break;
                    case "-nocursor":
                        cursor = false;
                        break;
                    case "-echo":
                        echo = true;
                        //echo = (i + 1 < args.Length && (args[i + 1].ToLower() == "1" || args[i + 1].ToLower() == "on"));
                        break;
                    case "-open":
                    case "-host":
                        if (i + 1 < args.Length && args[i + 1][0] != '-') host = args[i + 1].ToLower();
                        if (i + 2 < args.Length && args[i + 2][0] != '-') try { port = Convert.ToInt32(args[i + 2].ToLower()); } catch (Exception) { }
                        break;
                    case "-size":
                        if (i + 1 < args.Length && args[i + 1][0] != '-')
                        {
                            var size = args[i + 1].Split('x');
                            if (size.Length == 2)
                            {
                                try { cols = Convert.ToInt32(size[0]); } catch (Exception) { }
                                try { rows = Convert.ToInt32(size[1]); } catch (Exception) { }
                            }
                        }
                        break;
                }
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new AlphaTerm(ansi: ansi, crlf: crlf, cursor: cursor, echo: echo, rows: rows, cols: cols, host: host, port: port));
        }
    }
}
