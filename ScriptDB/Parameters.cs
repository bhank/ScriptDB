﻿using System;
using System.Collections.Generic;
using System.Linq;
using NDesk.Options;

namespace ScriptDb
{
    public class Parameters
    {
        public Parameters()
        {
            TableFilter = new List<string>();
            TableDataFilter = new List<string>();
            ViewFilter = new List<string>();
            StoredProcedureFilter = new List<string>();
        }

        public string Database { get; private set; }
        public string Server { get; private set; }
        public string UserName { get; private set; }
        public string Password { get; private set; }
        public bool TrustedAuthentication { get; private set; }
        //public string ConnectionString { get; private set; }
        public string OutputDirectory { get; private set; }
        public string OutputFileName { get; private set; }
        public DataScriptingFormat DataScriptingFormat { get; private set; }
        public bool Verbose { get; private set; }
        public bool ScriptProperties { get; private set; }
        public bool Purge { get; private set; }
        public bool ScriptAllDatabases { get; private set; }
        public List<string> TableFilter { get; private set; }
        public List<string> TableDataFilter { get; private set; }
        public string TableDataFilterFile { get; private set; }
        public List<string> ViewFilter { get; private set; }
        public List<string> StoredProcedureFilter { get; private set; }
        public bool TableOneFile { get; private set; }
        public bool ScriptAsCreate { get; private set; }
        public bool ScriptCreateOnly { get; private set; }
        public bool ScriptPermissions { get; private set; }
        public bool NoCollation { get; private set; }
        public bool ScriptStatistics { get; private set; }
        public bool ScriptDatabase { get; private set; }
        public string StartCommand { get; private set; }
        public string PreScriptingCommand { get; private set; }
        public string PostScriptingCommand { get; private set; }
        public string FinishCommand { get; private set; }

        public static bool TryParse(IList<string> args, out Parameters parameters)
        {
            parameters = null;

            var p = new Parameters();

            var optionSet = new OptionSet
                {
                    {"S|server=", "The SQL {SERVER} to which to connect", v => p.Server = v},
                    {"d|database=", "The {DATABASE} to script", v => p.Database = v},
                    {"scriptalldatabases", "Script all databases on the server, instead of just one specified database", v => p.ScriptAllDatabases = (v != null)},
                    {"U|login|username=", "The SQL {LOGIN} ID", v => p.UserName = v},
                    {"P|password=", "The SQL {PASSWORD}", v => p.Password = v},
                    {"E|trustedauth", "Use trusted authentication instead of a login and password", v => p.TrustedAuthentication = (v != null)},
                    //{"con|connstr|connectionstring=", "The {CONNECTIONSTRING} to connect to the server.\nThis can also specify a database.", v => p.ConnectionString = v},
                    {"outdir|outputpath|outputdirectory=", "The {DIRECTORY} under which to write script files.", v => p.OutputDirectory = v},
                    {"outfile|filename|outputfilename=", "The {FILENAME} to which to write scripts.", v => p.OutputFileName = v},
                    {"v|verbose", "Show verbose messages.", v => p.Verbose = (v != null)},
                    {"purge", "Delete files from output directory before scripting.", v => p.Purge = (v != null)},
                    {"includedatabase|scriptdatabase", "Script the database itself.", v => p.ScriptDatabase = (v != null)},
                    {"scriptdata:", "Script table data, optionally specifying the {FORMAT}: SQL (default), CSV, and/or BCP.", v =>
                        {
                            if (v == null)
                            {
                                p.DataScriptingFormat = DataScriptingFormat.Sql;
                            }
                            else
                            {
                                DataScriptingFormat d;
                                Enum.TryParse(v, true, out d);
                                p.DataScriptingFormat |= d; // Specify multiple formats in separate parameters
                            }
                        }},
                    {"tabledata=", "{NAME} of tables for which to script data. (Default all)", v => p.TableFilter.AddRange(v.SplitUpperCaseListParameter())},
                    {"tabledatafile=", "{FILENAME} containing tables for which to script data for each database name. File format:\ndatabase:table1,table2,table3", v => p.TableDataFilterFile = v},
                    {"table=", "{NAME} of tables for which to script schema. (Default all)", v => p.TableFilter.AddRange(v.SplitUpperCaseListParameter())},
                    {"view=", "{NAME} of views for which to script schema. (Default all)", v => p.ViewFilter.AddRange(v.SplitUpperCaseListParameter())},
                    {"sp|storedprocedure=", "{NAME} of stored procedures for which to script schema. (Default all)", v => p.StoredProcedureFilter.AddRange(v.SplitUpperCaseListParameter())},
                    {"tableonefile", "Script all parts of a table to a single file.", v => p.TableOneFile = (v != null)},
                    {"scriptascreate|scriptstoredproceduresascreate", "Script stored procedures as CREATE instead of ALTER.", v => p.ScriptAsCreate = (v != null)},
                    {"createonly", "Do not generate DROP statements.", v => p.ScriptCreateOnly = (v != null)},
                    {"p|scriptproperties", "Script extended properties.", v => p.ScriptProperties = (v != null)},
                    {"permissions|scriptpermissions", "Script permissions.", v => p.ScriptPermissions = (v != null)},
                    {"statistics|scriptstatistics", "Script statistics.", v => p.ScriptStatistics = (v != null)},
                    {"nocollation", "Skip scripting collation.", v => p.NoCollation = (v != null)}, // TODO: fix boolean... invert to make like normal options
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
            //else if (string.IsNullOrWhiteSpace(p.ConnectionString))
            //{
            //    error = "connectionstring is required";
            //}
            else if (string.IsNullOrWhiteSpace(p.Server))
            {
                error = "Specify a server";
            }
            //else if (string.IsNullOrWhiteSpace(p.Database) && !p.ScriptAllDatabases || !string.IsNullOrWhiteSpace(p.Database) && p.ScriptAllDatabases)
            else if (string.IsNullOrWhiteSpace(p.Database) != p.ScriptAllDatabases)
            {
                error = "Specify either a database name or scriptalldatabases";
            }
            //else if (!string.IsNullOrWhiteSpace(p.UserName) && p.TrustedAuthentication)
            else if (string.IsNullOrWhiteSpace(p.UserName) != p.TrustedAuthentication)
            {
                error = "Specify either a login and password or trusted auth";
            }
            else if (string.IsNullOrWhiteSpace(p.OutputDirectory) && string.IsNullOrWhiteSpace(p.OutputFileName))
            {
                error = "outputdirectory or outputfile is required";
            }

            if (error != null)
            {
                Console.Error.WriteLine(error);
                ShowUsage(optionSet);
                return false;
            }
            parameters = p;
            return true;
        }

        private static void ShowUsage(OptionSet optionSet)
        {
            //var commands = new List<string>(Enum.GetNames(typeof(Command)));
            //commands.RemoveAt(0);

            //var exeName = AppDomain.CurrentDomain.FriendlyName;
            //Console.Error.WriteLine(string.Format("{0} {{ {1} }}", exeName, string.Join(" | ", commands.ToArray())));
            //Console.Error.WriteLine(string.Format("{0} {1} PATH [options]", exeName, Command.CompleteSync));
            //Console.Error.WriteLine(string.Format("{0} {1} URL PATH [options]", exeName, Command.CheckoutUpdate));
            optionSet.WriteOptionDescriptions(Console.Error);
            Console.Error.WriteLine(@"
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
    }

    public static class ParameterExtensions
    {
        public static IEnumerable<string> SplitUpperCaseListParameter(this string s)
        {
            if (s == null)
            {
                return Enumerable.Empty<string>();
            }
            return s.ToUpperInvariant().Split(',');
        }
    }
}
