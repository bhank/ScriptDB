using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

        public string ConnectionString { get; private set; }
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

            var p = new Parameters
                {
                    // set defaults?
                    //Message = "Committed by SvnClient",
                };

            var optionSet = new OptionSet
                {
                    {"con|connstr|connectionstring=", "The {CONNECTIONSTRING} to connect to the server.\nThis can also specify a database.", v => p.ConnectionString = v},
                    {"outdir|outputpath|outputdirectory=", "The {DIRECTORY} under which to write script files.", v => p.OutputDirectory = v},
                    {"outfile|filename|outputfilename=", "The {FILENAME} to which to write scripts.", v => p.OutputFileName = v},
                    {"v|verbose", "Show verbose messages.", v => p.Verbose = (v != null)},
                    {"purge", "Delete files from output directory before scripting.", v => p.Purge = (v != null)},
                    {"scriptalldatabases", "Script all databases, instead of just the one specified by the connection string (if any)", v => p.ScriptAllDatabases = (v != null)},
                    {"includedatabase|scriptdatabase", "Script the database itself.", v => p.ScriptDatabase = (v != null)},
                    {"d|datascriptingformat|tabledataformat=", "The {FORMAT} in which to script data to files. SQL (default), CSV, and/or BCP.", v =>
                        {
                            DataScriptingFormat d;
                            Enum.TryParse(v, true, out d);
                            p.DataScriptingFormat |= d; // Specify multiple formats in separate parameters
                        }},
                    {"tabledata=", "{NAME} of tables for which to script data in formats specified by -d. (Default all)", v => p.TableFilter.AddRange(v.SplitListParameter())},
                    {"tabledatafile=", "{FILENAME} containing tables for which to script data for each database name. File format:\ndatabase:table1,table2,table3", v => p.TableDataFilterFile = v},
                    {"table=", "{NAME} of tables for which to script schema. (Default all)", v => p.TableFilter.AddRange(v.SplitListParameter())},
                    {"view=", "{NAME} of views for which to script schema. (Default all)", v => p.ViewFilter.AddRange(v.SplitListParameter())},
                    {"sp|storedprocedure=", "{NAME} of stored procedures for which to script schema. (Default all)", v => p.StoredProcedureFilter.AddRange(v.SplitListParameter())},
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
            else if (string.IsNullOrWhiteSpace(p.ConnectionString))
            {
                error = "connectionstring is required";
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
            // TODO: describe substitution tokens

            //var commands = new List<string>(Enum.GetNames(typeof(Command)));
            //commands.RemoveAt(0);

            //var exeName = AppDomain.CurrentDomain.FriendlyName;
            //Console.Error.WriteLine(string.Format("{0} {{ {1} }}", exeName, string.Join(" | ", commands.ToArray())));
            //Console.Error.WriteLine(string.Format("{0} {1} PATH [options]", exeName, Command.CompleteSync));
            //Console.Error.WriteLine(string.Format("{0} {1} URL PATH [options]", exeName, Command.CheckoutUpdate));
            optionSet.WriteOptionDescriptions(Console.Error);
        }
    }

    public static class ParameterExtensions
    {
        public static IEnumerable<string> SplitListParameter(this string s)
        {
            if (s == null)
            {
                return Enumerable.Empty<string>();
            }
            return s.Split(',');
        }
    }
}
