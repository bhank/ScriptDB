/*
 * Copyright 2006 Jesse Hersch
 *
 * Permission to use, copy, modify, and distribute this software
 * and its documentation for any purpose is hereby granted without fee,
 * provided that the above copyright notice appears in all copies and that
 * both that copyright notice and this permission notice appear in
 * supporting documentation, and that the name of Jesse Hersch or
 * Elsasoft LLC not be used in advertising or publicity
 * pertaining to distribution of the software without specific, written
 * prior permission.  Jesse Hersch and Elsasoft LLC make no
 * representations about the suitability of this software for any
 * purpose.  It is provided "as is" without express or implied warranty.
 *
 * Jesse Hersch and Elsasoft LLC disclaim all warranties with
 * regard to this software, including all implied warranties of
 * merchantability and fitness, in no event shall Jesse Hersch or
 * Elsasoft LLC be liable for any special, indirect or
 * consequential damages or any damages whatsoever resulting from loss of
 * use, data or profits, whether in an action of contract, negligence or
 * other tortious action, arising out of or in connection with the use or
 * performance of this software.
 *
 * Author:
 *  Jesse Hersch
 *  Elsasoft LLC
 * 
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.Data.SqlClient;
using System.IO;
using Utility;

namespace Elsasoft.ScriptDb
{
    class Program
    {
        static void Main(string[] args)
        {
            Utility.Arguments arguments = new Arguments(args);
            try
            {
                if (arguments["?"] != null)
                {
                    PrintHelp();
                    return;
                }

                string connStr = arguments["con"];
                string outputDirectory = arguments["outDir"];
                bool scriptData = arguments["d"] != null;
                bool verbose = arguments["v"] != null;
                if (connStr == null || outputDirectory == null)
                {
                    PrintHelp();
                    return;
                }
                string database = null;
                using (SqlConnection sc = new SqlConnection(connStr))
                {
                    database = sc.Database;
                }

                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);
                outputDirectory = Path.Combine(outputDirectory, database);
                if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

                DatabaseScripter ds = new DatabaseScripter();

                if (arguments["table"] != null) 
                    ds.TableFilter= arguments["table"].Split(',');
                if(arguments["view"] != null )
                    ds.ViewsFilter = arguments["view"].Split(',');
                if(arguments["sp"] != null )
                    ds.SprocsFilter = arguments["sp"].Split(',');
                if (arguments["TableOneFile"] != null)
                    ds.TableOneFile = true;
                if(arguments["ScriptAsCreate"] != null )
                    ds.ScriptAsCreate =  true;
                if (arguments["Permissions"] != null)
                    ds.Permissions = true;
                if (arguments["NoCollation"] != null)
                    ds.NoCollation = true;
                if (arguments["IncludeDatabase"] != null)
                    ds.IncludeDatabase = true;
                if (arguments["CreateOnly"] != null)
                    ds.CreateOnly = false;
                if (arguments["filename"] != null)
                    ds.OutputFileName = arguments["filename"];
                ds.GenerateScript(connStr, outputDirectory, scriptData, verbose);
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception caught in Main()");
                Console.WriteLine("---------------------------------------");
                while (e != null)
                {
                    Console.WriteLine(e.Message);
                    Console.WriteLine();
                    Console.WriteLine(e.GetType().FullName);
                    Console.WriteLine();
                    Console.WriteLine(e.StackTrace);
                    Console.WriteLine("---------------------------------------");
                    e = e.InnerException;
                }
            }
        }

        private static void PrintHelp()
        {
            Console.WriteLine(
@"ScriptDb.exe usage:

ScriptDb.exe 
    ConnectionString 
    OutputDirectory
    [-d]
    [-v]
    [-table:table1,table2] [-TableOneFile] 
    [-view:view1,view2] 
    [-sp:sp1,sp2] 
    [-ScriptAsCreate] 
    [-Permissions] 
    [-NoCollation]
    [-CreateOnly]
    [-filename:<FileName> | -]

ConnectionString is a connection string to the db you want to 
generate scripts for.

OutputDirectory is a directory where you want the output placed.

-d to say that you want bcp files to be scripted.

-v to say whether you want me to be chatty or not.
Default is true because I am friendly and outgoing.

table - coma separated list of tables to script

TableOneFile - if specified table definition will be scripted into
one file instad of multiple scripts

view - coma separated list of views to script

sp - coma separated list of stored procedures to script

ScriptAsCreate - if specified then stored procedures will be scripted
as create instead of as alter statements

IncludeDatabase - Include Database Context in the script

CreateOnly - Do not generate DROP statements

filename - speicfy output filename. If file exists - script will be appended to the end of the file
           specify - to output to console

Example: 

ScriptDb.exe -con:server=(local);database=pubs;trusted_connection=yes -outDir:scripts [-d] [-v] [-table:table1,table2] [-TableOneFile] [-view:view1,view2] [-sp:sp1,sp2] [-ScriptAsCreate] [-Permissions] [-NoCollation] [-IncludeDatabase] -filename:-

");
        }

    }
}
