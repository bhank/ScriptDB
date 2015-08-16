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
using System.Diagnostics;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Microsoft.SqlServer.Management.Common;
using Microsoft.SqlServer.Management.Smo;
using ScriptDb;

namespace Elsasoft.ScriptDb
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Parameters parameters;
                if (!Parameters.TryParse(args, out parameters))
                {
                    return;
                }

                var outputDirectory = parameters.OutputDirectory;
                var connectionString = BuildConnectionString(parameters);

                using (var sc = new SqlConnection(connectionString))
                {
                    if (outputDirectory != null && (outputDirectory.Contains("{server}") || outputDirectory.Contains("{serverclean}")))
                    {
                        var serverConnection = new ServerConnection(sc);
                        var s = new Server(serverConnection);
                        // Get the proper case for the database name
                        //var database = s.Databases[sc.Database.ToLowerInvariant()].Name; // If I index into the collection, the .Name property gives me back the same case I put in!
                        var database = s.Databases.Cast<Database>().Single(d => d.Name.ToUpperInvariant() == sc.Database.ToUpperInvariant()).Name;

                        outputDirectory = outputDirectory.Replace("{server}", s.Name);
                        outputDirectory = outputDirectory.Replace("{serverclean}", DatabaseScripter.FixUpFileName(s.Name));
                        outputDirectory = outputDirectory.Replace("{database}", database);
                        outputDirectory = outputDirectory.Replace("{databaseclean}", DatabaseScripter.FixUpFileName(database));
                    }
                }

                if (outputDirectory != null && !Directory.Exists(outputDirectory))
                {
                    Directory.CreateDirectory(outputDirectory);
                }

                var ds = new DatabaseScripter();

                ds.ConnectionString = connectionString;
                ds.TableFilter = parameters.TableFilter.ToArray();
                ds.TableDataFilter = parameters.TableDataFilter.ToArray();
                var tableDataFile = parameters.TableDataFilterFile;
                if(tableDataFile != null)
                {
                    ds.DatabaseTableDataFilter = ReadTableDataFile(tableDataFile);
                }
                ds.ViewsFilter = parameters.ViewFilter.ToArray();
                ds.SprocsFilter = parameters.StoredProcedureFilter.ToArray();
                ds.TableOneFile = parameters.TableOneFile;
                ds.ScriptAsCreate = parameters.ScriptAsCreate;
                ds.Permissions = parameters.ScriptPermissions;
                ds.NoCollation = parameters.NoCollation;
                ds.Statistics = parameters.ScriptStatistics;
                ds.IncludeDatabase = parameters.ScriptDatabase;
                ds.IncludeSystemObjects = parameters.IncludeSystemObjects;
                ds.CreateOnly = parameters.ScriptCreateOnly;
                ds.OutputFileName = parameters.OutputFileName;
                ds.StartCommand = parameters.StartCommand;
                ds.PreScriptingCommand = parameters.PreScriptingCommand;
                ds.PostScriptingCommand = parameters.PostScriptingCommand;
                ds.FinishCommand = parameters.FinishCommand;
                var watch = new Stopwatch();
                watch.Start();
                ds.GenerateScripts(outputDirectory, parameters.ScriptAllDatabases, parameters.Purge, parameters.DataScriptingFormat, parameters.Verbose, parameters.ScriptProperties);
                Console.Error.WriteLine("Took {0} ms", watch.ElapsedMilliseconds);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception caught in Main()");
                Console.Error.WriteLine("---------------------------------------");
                while (e != null)
                {
                    Console.Error.WriteLine(e.Message);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(e.GetType().FullName);
                    Console.Error.WriteLine();
                    Console.Error.WriteLine(e.StackTrace);
                    Console.Error.WriteLine("---------------------------------------");
                    e = e.InnerException;
                }
            }
        }

        private static string BuildConnectionString(Parameters parameters)
        {
            var builder = new SqlConnectionStringBuilder
                {
                    ApplicationName = "ScriptDb",
                    DataSource = parameters.Server.ToLowerInvariant(),
                };
            if (!string.IsNullOrWhiteSpace(parameters.Database))
            {
                builder.InitialCatalog = parameters.Database;
            }
            if (string.IsNullOrWhiteSpace(parameters.UserName))
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.UserID = parameters.UserName;
                builder.Password = parameters.Password;
            }
            var connectionString = builder.ToString();
            return connectionString;
        }

        private static Dictionary<string, List<string>> ReadTableDataFile(string tableDataFile)
        {
            var tablesByDatabase = new Dictionary<string, List<string>>();
            var lines = File.ReadAllLines(tableDataFile);
            foreach(var line in lines)
            {
                if(string.IsNullOrEmpty(line) || line.IndexOf(':') == -1)
                {
                    continue;
                }
                var parts = line.Split(new[] {':'}, 2);
                var databaseName = parts[0].ToUpperInvariant();
                var tableNames = parts[1].ToUpperInvariant().Split(',');
                tablesByDatabase.Add(databaseName, new List<string>(tableNames));
            }
            return tablesByDatabase;
        }


        private static void PrintHelp()
        {
            Console.Error.WriteLine(
@"ScriptDb.exe usage:

ScriptDb.exe 
    -con:<ConnectionString>
    -outdir:<OutputDirectory>
    [-d[:<sql,csv,bcp>]]
    [-v]
    [-p]
    [-table:table1,table2] [-TableOneFile] 
    [-tableDataFile:<TableDataFileName>]
    [-view:view1,view2] 
    [-sp:sp1,sp2] 
    [-ScriptAsCreate] 
    [-ScriptAllDatabases]
    [-IncludeDatabase]
    [-Permissions] 
    [-Statistics]
    [-NoCollation]
    [-CreateOnly]
    [-Purge]
    [-filename:<FileName> | -]
    [-StartCommand:<command>]
    [-FinishCommand:<command>]
    [-PreScriptingCommand:<command>]
    [-PostScriptingCommand:<command>]

-con:<ConnectionString> is a connection string to the db.
-outDir:<OutputDirectory> is where the output scripts are placed.
-d script data to files in formats sql (default), csv, and/or bcp
-v for verbose output.
-p to script extended properties for each object.
-table - comma separated list of tables to script
-TableOneFile - script table definition into one file instad of multiple
-tableDataFile - text file of tables to script
    with lines of format databasename:{table1,table2|*}
-view - comma separated list of views to script
-sp - comma separated list of stored procedures to script
-ScriptAsCreate - script stored procedures as CREATE instead ALTER
-ScriptAllDatabases - script all databases on the current server
-IncludeDatabase - Include Database Context in scripted objects
-Permissions - script permissions
-Statistics - script statistics
-NoCollation - skip the collation clause in the script
-CreateOnly - Do not generate DROP statements
-Purge - delete files from output folder before generating scripts
-filename - specify output filename. If file exists,
    script will be appended to the end of the file
    specify '-' to output to console

Commands to run:
-StartCommand - at startup
-FinishCommand - before shutdown
-PreScriptingCommand - before scripting one database
-PostScriptingCommand - after scripting one database
Commands can include these tokens:
{path} - the output directory
{server} - the SQL server name
{serverclean} - same as above, but safe for use as a filename
{database} - the database name
{databaseclean} - same as above, but safe for use as a filename
The -outDir parameter can also use all these tokens except {path}.
{database} is meaningful in StartCommand, FinishCommand and outDir only when
just a single database is specified in the connection string to be scripted.

Example: 

ScriptDb.exe -con:server=(local);database=pubs;trusted_connection=yes -outDir:scripts [-d] [-v] [-p] [-table:table1,table2] [-TableOneFile] [-view:view1,view2] [-sp:sp1,sp2] [-ScriptAsCreate] [-Permissions] [-NoCollation] [-IncludeDatabase] -filename:-

");
        }

    }
}
