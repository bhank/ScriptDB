﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using NDesk.Options;

namespace ScriptDb
{
    public class Parameters
    {
        public Parameters()
        {
            Filters = new Dictionary<FilterType, List<string>>();
        }

        public bool Help { get; private set; }
        public bool Examples { get; private set; }
        public bool Version { get; private set; }
        public string Database { get; private set; }
        public string Server { get; private set; }
        public string UserName { get; private set; }
        public string Password { get; private set; }
        public string OutputDirectory { get; private set; }
        public string OutputFileName { get; private set; }
        public DataScriptingFormat DataScriptingFormat { get; private set; }
        public bool Verbose { get; private set; }
        public bool ScriptProperties { get; private set; }
        public bool Purge { get; private set; }
        public bool ScriptAllDatabases { get; private set; }
        public Dictionary<FilterType, List<string>> Filters { get; set; }
        public string TableDataFilterFile { get; private set; }
        public bool TableOneFile { get; private set; }
        public bool ScriptAsCreate { get; private set; }
        public bool ScriptCreateOnly { get; private set; }
        public bool ScriptPermissions { get; private set; }
        public bool NoCollation { get; private set; }
        public bool ScriptStatistics { get; private set; }
        public bool ScriptDatabase { get; private set; }
        public bool IncludeSystemObjects { get; private set; }
        public string StartCommand { get; private set; }
        public string PreScriptingCommand { get; private set; }
        public string PostScriptingCommand { get; private set; }
        public string FinishCommand { get; private set; }

        public static bool TryParse(IList<string> args, out Parameters parameters)
        {
            parameters = null;

            var p = new Parameters
                {
                    Server = "localhost",
                };

            var optionSet = new OptionSet
                {
                    {"h|?|help", "Show this help.", v => p.Help = (v != null)},
                    {"x|examples", "Show examples of usage", v => p.Examples = (v!=null)},
                    {"V|version", "Show the version.", v => p.Version = (v != null)},
                    {"S|server=", "The SQL {SERVER} to which to connect (localhost if unspecified)", v => p.Server = v},
                    {"d|database=", "The {DATABASE} to script", v => p.Database = v},
                    {"A|scriptalldatabases", "Script all databases on the server, instead of just one specified database", v => p.ScriptAllDatabases = (v != null)},
                    {"U|login|uid|username=", "The SQL {LOGIN} ID (Use trusted auth if unspecified)", v => p.UserName = v},
                    {"P|pwd|password=", "The SQL {PASSWORD}", v => p.Password = v},
                    //{"E|trustedauth", "Use trusted authentication instead of a login and password", v => p.TrustedAuthentication = (v != null)},
                    //{"con|connstr|connectionstring=", "The {CONNECTIONSTRING} to connect to the server.\nThis can also specify a database.", v => p.ConnectionString = v},
                    {"outdir|outputpath|outputdirectory=", "The {DIRECTORY} under which to write script files.", v => p.OutputDirectory = v},
                    {"outfile|filename|outputfilename=", "The {FILENAME} to which to write scripts, or - for stdout.", v => p.OutputFileName = v},
                    {"v|verbose", "Show verbose messages.", v => p.Verbose = (v != null)},
                    {"purge", "Delete files from output directory before scripting.", v => p.Purge = (v != null)},
                    {"includedatabase|scriptdatabase", "Script the database itself.", v => p.ScriptDatabase = (v != null)},
                    {"dataformat=", "Specify the {FORMAT} for scripted table data: SQL (default), CSV, and/or BCP.", v =>
                        {
                                DataScriptingFormat d;
                                Enum.TryParse(v, true, out d);
                                p.DataScriptingFormat |= d; // You can specify multiple formats in separate parameters
                        }},
                    {"datatables:", "Script table data, optionally specifying {NAME}s of tables. (Default all)", v => p.AddFilter(FilterType.TableData, v)},
                    {"datatablefile=", "{FILENAME} containing tables for which to script data for each database name. File format:\ndatabase:table1,table2,table3", v => p.TableDataFilterFile = v},
                    {"tables:", "Script table schema, optionally specifying {NAME}s of tables. (Default all)", v => p.AddFilter(FilterType.Table, v)},
                    {"views:", "Script view schema, optionally specifying {NAME}s of views. (Default all)", v => p.AddFilter(FilterType.View, v)},
                    {"sps|storedprocs|storedprocedures:", "Script stored procedures, optionally specifying {NAME}s. (Default all)", v => p.AddFilter(FilterType.StoredProcedure, v)},
                    {"udfs|functions|userdefinedfunctions:", "Script user-defined functions, optionally specifying {NAME}s. (Default all)", v => p.AddFilter(FilterType.UserDefinedFunction, v)},
                    {"udts|types|userdefineddatatypes:", "Script user-defined data types, optionally specifying {NAME}s. (Default all)", v => p.AddFilter(FilterType.UserDefinedDataType, v)},
                    {"defaults:", "Script defaults, optionally specifying {NAME}s. (Default all)", v => p.AddFilter(FilterType.Default, v)},
                    {"rules:", "Script rules, optionally specifying {NAME}s. (Default all)", v => p.AddFilter(FilterType.Rule, v)},
                    {"all|scriptallobjects", "Script all objects. This is the default, but you can specify other filter parameters to limit certain objects.", v => p.AddAllFilters()},
                    {"tableonefile", "Script all parts of a table to a single file.", v => p.TableOneFile = (v != null)},
                    {"scriptascreate|scriptstoredproceduresascreate", "Script stored procedures as CREATE instead of ALTER.", v => p.ScriptAsCreate = (v != null)},
                    {"createonly", "Do not generate DROP statements.", v => p.ScriptCreateOnly = (v != null)},
                    {"p|scriptproperties", "Script extended properties.", v => p.ScriptProperties = (v != null)},
                    {"permissions|scriptpermissions", "Script permissions.", v => p.ScriptPermissions = (v != null)},
                    {"statistics|scriptstatistics", "Script statistics.", v => p.ScriptStatistics = (v != null)},
                    {"nocollation", "Skip scripting collation.", v => p.NoCollation = (v != null)}, // TODO: fix boolean... invert to make like normal options
                    {"includesystem","Include system objects (normally skipped).", v => p.IncludeSystemObjects = (v != null)},
                    {"startcommand=", "{COMMAND} to run on startup.", v => p.StartCommand = v},
                    {"prescriptingcommand=", "{COMMAND} to run before scripting each database.", v => p.PreScriptingCommand = v},
                    {"postscriptingcommand=", "{COMMAND} to run after scripting each database.", v => p.PostScriptingCommand = v},
                    {"finishcommand=", "{COMMAND} to run before shutdown.", v => p.FinishCommand = v},
                };
            var extraArgs = optionSet.Parse(args);

            string error = null;

            if (extraArgs.Count > 0)
            {
                error = "Unknown parameter: " + extraArgs[0];
            }
            else if (string.IsNullOrWhiteSpace(p.Database) != p.ScriptAllDatabases)
            {
                error = "You must specify either --database=DATABASENAME or --scriptalldatabases";
            }
            else if (string.IsNullOrWhiteSpace(p.OutputDirectory) && string.IsNullOrWhiteSpace(p.OutputFileName))
            {
                error = "You must specify either --outputdirectory or --outputfile";
            }
            else if (!string.IsNullOrWhiteSpace(p.TableDataFilterFile) && !File.Exists(p.TableDataFilterFile))
            {
                error = "Your --datatablefile was not found: " + p.TableDataFilterFile;
            }
            else if (string.IsNullOrWhiteSpace(p.OutputDirectory) && p.Purge)
            {
                error = "You must specify --outputdirectory in order to use --purge";
            }
            else if (string.IsNullOrWhiteSpace(p.OutputDirectory) && (string.Empty + p.StartCommand + p.PreScriptingCommand + p.PostScriptingCommand + p.FinishCommand).Contains("{path}"))
            {
                error = "You must specify --outputdirectory in order to use the {path} token in a command";
            }
            else if (!string.IsNullOrWhiteSpace(p.OutputFileName) && ((p.DataScriptingFormat & DataScriptingFormat.Bcp) == DataScriptingFormat.Bcp || (p.DataScriptingFormat & DataScriptingFormat.Csv) == DataScriptingFormat.Csv))
            {
                error = "When writing to a single output file, you can only script data in SQL format";
            } else if (!string.IsNullOrWhiteSpace(p.OutputFileName) && p.TableOneFile)
            {
                error = "When writing to a single output file, --tableonefile is redundant";
            }

            if (p.Help)
            {
                ShowUsage(optionSet);
                return false;
            }

            if (p.Examples)
            {
                ShowExamples();
                return false;
            }

            if (p.Version)
            {
                var assembly = typeof (Parameters).Assembly;
                var name = Path.GetFileNameWithoutExtension(assembly.Location).ToLowerInvariant();
                var version = assembly.GetCustomAttributes(typeof (AssemblyFileVersionAttribute), false).Cast<AssemblyFileVersionAttribute>().Select(a => a.Version).FirstOrDefault();
                Console.Error.WriteLine("{0} version {1}", name, version);
                return false;
            }

            if (error != null)
            {
                Console.Error.WriteLine(error);
                Console.Error.WriteLine("Pass --help for usage information.");
                return false;
            }

            if (p.DataScriptingFormat == DataScriptingFormat.None)
            {
                p.DataScriptingFormat = DataScriptingFormat.Sql;
            }

            parameters = p;
            return true;
        }

        private void AddFilter(FilterType filterType, string parameter)
        {
            List<string> filterList;
            if (!Filters.TryGetValue(filterType, out filterList))
            {
                filterList = new List<string>();
                Filters.Add(filterType, filterList);
            }
            AddFilter(filterList, parameter);
        }

        private static void AddFilter(List<string> filterList, string parameter)
        {
            if (parameter == null)
            {
                filterList.Add("*");
            }
            else
            {
                filterList.AddRange(parameter.Split(new[] {','}, StringSplitOptions.RemoveEmptyEntries));
            }
        }

        private void AddAllFilters()
        {
            foreach(FilterType filterType in Enum.GetValues(typeof(FilterType)))
            {
                if (filterType != FilterType.TableData) // TableData is separate, since it's data, not schema
                {
                    AddFilter(filterType, null);
                }
            }
        }

        private static void ShowUsage(OptionSet optionSet)
        {
            optionSet.WriteOptionDescriptions(Console.Error);
            Console.Error.WriteLine(@"

If you do not pass any of the filter parameters --tables, --datatables, --udfs,
--views, or --storedprocedures, then schema for all objects will be scripted.
(Data is only scripted if --datatables is passed.) If you do pass a filter
parameter, then you must specify all the objects you want scripted. For
example, passing only --tables will prevent any views or stored procedures
from being scripted.

You can also explicitly pass --all, and follow it with a more restrictive
filter. For example, passing --all --views=cust* will script all objects,
except only views starting with cust. Or passing --all --datatables=cust*
will script schema for all objects, plus data for tables starting with
cust.

Commands can include these tokens:
{path} - the output directory
{server} - the SQL server name
{serverclean} - same as above, but safe for use as a filename
{database} - the database name
{databaseclean} - same as above, but safe for use as a filename
The outDir parameter can also use all these tokens except {path}.
{database} is meaningful in StartCommand, FinishCommand and outDir only when
just a single database is specified in the connection string to be scripted.
");
        }

        private static void ShowExamples()
        {
            Console.Error.WriteLine(@"

1. Connect to the Northwind database on localhost using trusted auth,
and script the schema of all tables, views, and stored procedures
whose names start with ""cust"", and the data from all tables
regardless of their names, to files under the scripts\Northwind directory,
deleting its contents first:

  scriptdb.exe -d Northwind --tables=cust* --views=cust* --storedprocs=cust*
    --datatables --purge --outdir=scripts


2. Connect to the Orders database on DBSERVER using a SQL login,
and script all objects and data to a single file, AllScripts.sql:

  scriptdb.exe -S DBSERVER -U mylogin -P mypassword -d Orders
    --tables --views --sps --udfs --datatables --outfile=AllScripts.sql

3. Connect to DBSERVER using a SQL login, and script the data
for the SQL Agent job tables to CSV files under the testscripts\msdb directory,
after deleting existing files:

  scriptdb.exe -S DBSERVER -U mylogin -P mypassword -d msdb
    --datatables=sysjob* --dataformat=csv --includesystem
    --outdir=testscripts --purge

");
        }
    }
}
