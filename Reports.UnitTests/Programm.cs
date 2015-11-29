using System;
using System.Collections.Generic;
using System.Text;

namespace Reports.UnitTests
{
    class Programm
    {
        public static void Main(string[] args) 
        {
            string settingFile = "Settings.xml";
            //string settingFile = "<?xml version=\"1.0\" encoding=\"utf-8\" ?><Settings><ReportServer Path=\"http://win-bqugakuf90e/ReportServer_SQL2008/\" Mode=\"Native\" ParameterLanguage=\"en-AU\"><Folder Name=\"Test\"><Report Name=\"Sales Order Detail SQL2008R2\" ExportFormat=\"PDF\" ExportPath=\"C:\\Temp\\test.pdf\"><Params><Param Name=\"SalesOrderNumber\" Value=\"SO50750\" /></Report></Folder></ReportServer></Settings>";
            //string settingFile = "<?xml </Settings>"; //Error check
            string resultFile = string.Empty;
            if (args.Length > 0) 
            {
                settingFile = args[0];
                if (args.Length > 1) 
                {
                    resultFile = args[1];
                }
            }

            ReportHelper rh = new ReportHelper();
            rh.Load(settingFile, resultFile);
        }
    }
}
