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
using System.Data;
using System.Linq;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.IO;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using ScriptDb;
using Utils;
using Rule = Microsoft.SqlServer.Management.Smo.Rule;

namespace Elsasoft.ScriptDb
{
    public class DatabaseScripter
    {
        private bool FilterExists()
        {
            return Filters.Count > 0;
        }

        /// <summary>
        /// does all the work.
        /// </summary>
        /// <param name="connStr"></param>
        /// <param name="outputDirectory"></param>
        /// <param name="dataScriptingFormat"></param>
        /// <param name="verbose"></param>
        /// <param name="scriptAllDatabases"></param>
        /// <param name="purgeDirectory"></param>
        /// <param name="scriptProperties"></param>
        public void GenerateScripts(string outputDirectory,
                                    bool scriptAllDatabases, bool purgeDirectory,
                                    DataScriptingFormat dataScriptingFormat, bool verbose, bool scriptProperties)
        {
            var connection = new SqlConnection(ConnectionString);
            var sc = new ServerConnection(connection);
            var s = new Server(sc);

            s.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject", "IsEncrypted");
            s.SetDefaultInitFields(typeof(Table), "IsSystemObject");
            s.SetDefaultInitFields(typeof(View), "IsSystemObject", "IsEncrypted");
            s.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject", "IsEncrypted");
            s.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;

            RunCommand(StartCommand, verbose, outputDirectory, s.Name, null);

            // Purge at the Server level only when we're doing all databases
            if (purgeDirectory && scriptAllDatabases && outputDirectory != null && Directory.Exists(outputDirectory))
            {
                if (verbose) Console.Error.Write("Purging directory...");
                PurgeDirectory(outputDirectory, "*.sql");
                if (verbose) Console.Error.WriteLine("Done");
            }

            if (!string.IsNullOrEmpty(OutputFileName) && File.Exists(OutputFileName))
            {
                try
                {
                    File.Delete(OutputFileName);
                }
                catch (Exception e)
                {
                    Console.Error.Write("Error deleting output file {0}: {1}", OutputFileName, e.Message);
                }
            }

            if (scriptAllDatabases)
            {
                foreach (Database db in s.Databases)
                {
                    try
                    {
                        RunCommand(PreScriptingCommand, verbose, outputDirectory, s.Name, db.Name);
                        GenerateDatabaseScript(db, outputDirectory, purgeDirectory, dataScriptingFormat, verbose, scriptProperties, s);
                        RunCommand(PostScriptingCommand, verbose, outputDirectory, s.Name, db.Name);
                    }
                    catch (Exception e)
                    {
                        Console.Error.WriteLine("Exception: {0}", e.Message);
                    }
                }
            }
            else
            {
                //var db = s.Databases[connection.Database]; // Doesn't fix the case of the database name.
                var db = s.Databases.Cast<Database>().Single(d => d.Name.ToUpperInvariant() == connection.Database.ToUpperInvariant());
                if(db == null)
                {
                    throw new Exception(string.Format("Database '{0}' was not found", connection.Database));
                }
                RunCommand(PreScriptingCommand, verbose, outputDirectory, s.Name, db.Name);
                GenerateDatabaseScript(db, outputDirectory, purgeDirectory,
                    dataScriptingFormat, verbose, scriptProperties, s);
                RunCommand(PostScriptingCommand, verbose, outputDirectory, s.Name, db.Name);
            }

            RunCommand(FinishCommand, verbose, outputDirectory, s.Name, null);
        }

        // TODO: maybe pass in the databaseOutputDirectory instead of calculating it in here?
        private void GenerateDatabaseScript(Database db, string outputDirectory, bool purgeDirectory,
                           DataScriptingFormat dataScriptingFormat, bool verbose, bool scriptProperties, Server server)
        {
            Properties = scriptProperties;

            // Output folder
            var databaseOutputDirectory = string.Empty;
            if (outputDirectory != null)
            {
                databaseOutputDirectory = Path.Combine(outputDirectory, FixUpFileName(db.Name));
                if (Directory.Exists(databaseOutputDirectory))
                {
                    if (purgeDirectory)
                    {
                        if (verbose) Console.Error.Write("Purging database directory...");
                        PurgeDirectory(databaseOutputDirectory, "*.sql");
                        if (verbose) Console.Error.WriteLine("done.");
                    }
                }
                else
                {
                    Directory.CreateDirectory(databaseOutputDirectory);
                }
            }

            var so = new ScriptingOptions
                {
                    Default = true,
                    DriDefaults = true,
                    DriUniqueKeys = true,
                    Bindings = true,
                    Permissions = Permissions,
                    NoCollation = NoCollation,
                    Statistics = Statistics,
                    IncludeDatabaseContext = IncludeDatabase
                };

            ScriptTables(verbose, db, so, databaseOutputDirectory, dataScriptingFormat, server);
            ScriptDefaults(verbose, db, so, databaseOutputDirectory);
            ScriptRules(verbose, db, so, databaseOutputDirectory);
            ScriptUddts(verbose, db, so, databaseOutputDirectory);
            ScriptUdfs(verbose, db, so, databaseOutputDirectory);
            ScriptViews(verbose, db, so, databaseOutputDirectory);
            ScriptSprocs(verbose, db, so, databaseOutputDirectory);

            if (db.Version >= 9 &&
                db.CompatibilityLevel >= CompatibilityLevel.Version90)
            {
                ScriptUdts(verbose, db, so, databaseOutputDirectory);
                ScriptSchemas(verbose, db, so, databaseOutputDirectory);
                ScriptDdlTriggers(verbose, db, so, databaseOutputDirectory);
                //ScriptAssemblies(verbose, db, so, databaseOutputDirectory);
            }
        }

        #region Private Script Functions

        private void ScriptIndexes(TableViewBase tableOrView, bool verbose, Database db, ScriptingOptions so, string tablesOrViewsOutputDirectory)
        {
            var indexes = Path.Combine(tablesOrViewsOutputDirectory, "Indexes");
            var primaryKeys = Path.Combine(tablesOrViewsOutputDirectory, "PrimaryKeys");
            var uniqueKeys = Path.Combine(tablesOrViewsOutputDirectory, "UniqueKeys");

            var fileName = Path.Combine(tablesOrViewsOutputDirectory, GetScriptFileName(tableOrView));

            foreach (Index smo in tableOrView.Indexes)
            {
                if (IncludeSystemObjects || !smo.IsSystemObject)
                {
                    string dir =
                        (smo.IndexKeyType == IndexKeyType.DriPrimaryKey) ? primaryKeys :
                        (smo.IndexKeyType == IndexKeyType.DriUniqueKey) ? uniqueKeys : indexes;
                    if (!TableOneFile)
                        fileName = Path.Combine(dir, GetScriptFileName(tableOrView, smo));
                    using (StreamWriter sw = GetStreamWriter(fileName, TableOneFile))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]: [{3}]", db.Name, tableOrView.Schema, tableOrView.Name, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptTables(bool verbose, Database db, ScriptingOptions so, string outputDirectory, DataScriptingFormat dataScriptingFormat, Server server)
        {
            string data = Path.Combine(outputDirectory, "Data");
            string tables = Path.Combine(outputDirectory, "Tables");
            string constraints = Path.Combine(tables, "Constraints");
            string foreignKeys = Path.Combine(tables, "ForeignKeys");
            string fullTextIndexes = Path.Combine(tables, "FullTextIndexes");
            string triggers = Path.Combine(tables, "Triggers");

            foreach (Table table in db.Tables)
            {
                if (IncludeSystemObjects || !table.IsSystemObject)
                {
                    if (!FilterExists() || MatchesFilter(FilterType.Table, table.Name))
                    {
                        ScriptTable(verbose, db, so, tables, table, triggers, fullTextIndexes, foreignKeys, constraints);
                    }

                    #region Script Data

                    if (MatchesTableDataFilters(db.Name, table.Name))
                    {
                        ScriptTableData(db, table, verbose, data, dataScriptingFormat, server);
                    }

                    #endregion
                }
            }
        }

        private void ScriptTable(bool verbose, Database db, ScriptingOptions so, string tables, Table table, string triggers, string fullTextIndexes, string foreignKeys, string constraints)
        {
            string fileName = Path.Combine(tables, GetScriptFileName(table));

            #region Table Definition

            using (StreamWriter sw = GetStreamWriter(fileName, false))
            {
                if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, table.Schema, table.Name);
                if (!CreateOnly)
                {
                    so.ScriptDrops = so.IncludeIfNotExists = true;
                    WriteScript(table.Script(so), sw);
                }
                so.ScriptDrops = so.IncludeIfNotExists = false;
                WriteScript(table.Script(so), sw);

                if (Properties)
                {
                    ScriptProperties(table, sw);
                }
            }

            #endregion

            #region Triggers

            foreach (Trigger smo in table.Triggers)
            {
                if ((IncludeSystemObjects || !smo.IsSystemObject) && !smo.IsEncrypted)
                {
                    if (!TableOneFile)
                        fileName = Path.Combine(triggers, GetScriptFileName(table, smo));
                    using (StreamWriter sw = GetStreamWriter(fileName, TableOneFile))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]: [{3}]", db.Name, table.Schema, table.Name, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }

            #endregion

            ScriptIndexes(table, verbose, db, so, tables);

            #region Full Text Indexes

            if (table.FullTextIndex != null)
            {
                if (!TableOneFile)
                    fileName = Path.Combine(fullTextIndexes, GetScriptFileName(table));
                using (StreamWriter sw = GetStreamWriter(fileName, TableOneFile))
                {
                    if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]: full-text index", db.Name, table.Schema, table.Name);
                    if (!CreateOnly)
                    {
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(table.FullTextIndex.Script(so), sw);
                    }
                    so.ScriptDrops = so.IncludeIfNotExists = false;
                    WriteScript(table.FullTextIndex.Script(so), sw);
                }
            }

            #endregion

            #region Foreign Keys

            foreach (ForeignKey smo in table.ForeignKeys)
            {
                if (!TableOneFile)
                    fileName = Path.Combine(foreignKeys, GetScriptFileName(table, smo));
                using (StreamWriter sw = GetStreamWriter(fileName, TableOneFile))
                {
                    if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]: [{3}]", db.Name, table.Schema, table.Name, smo.Name);
                    if (!CreateOnly)
                    {
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                    }
                    WriteScript(smo.Script(), sw);

                    if (Properties)
                    {
                        ScriptProperties(smo, sw);
                    }
                }
            }

            #endregion

            #region Constraints

            foreach (Check smo in table.Checks)
            {
                if (!TableOneFile)
                    fileName = Path.Combine(constraints, GetScriptFileName(table, smo));
                using (StreamWriter sw = GetStreamWriter(fileName, TableOneFile))
                {
                    if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]: [{3}]", db.Name, table.Schema, table.Name, smo.Name);
                    WriteScript(smo.Script(), sw);
                    if (Properties)
                    {
                        ScriptProperties(smo, sw);
                    }
                }
            }

            #endregion
        }

        private bool MatchesFilter(FilterType filterType, string name)
        {
            List<string> filterList;
            if (Filters.TryGetValue(filterType, out filterList))
            {
                return MatchesFilter(filterList, name);
            }
            return false;
        }

        private static bool MatchesFilter(IEnumerable<string> filter, string name)
        {
            return filter.Any(filterItem => MatchesWildcard(name, filterItem));
        }

        public static bool MatchesWildcard(string testString, string pattern)
        {
            var regex = "^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
            return Regex.IsMatch(testString, regex, RegexOptions.IgnoreCase);
        }

        private void ScriptTableData(Database db, Table table, bool verbose, string dataDirectory, DataScriptingFormat dataScriptingFormat, Server server)
        {
            if ((dataScriptingFormat & DataScriptingFormat.Sql) == DataScriptingFormat.Sql)
            {
                ScriptTableDataNative(table, dataDirectory, server);
            }
            if ((dataScriptingFormat & DataScriptingFormat.Csv) == DataScriptingFormat.Csv)
            {
                ScriptTableDataToCsv(db, table, dataDirectory);
            }
            if ((dataScriptingFormat & DataScriptingFormat.Bcp) == DataScriptingFormat.Bcp)
            {
                ScriptTableDataWithBcp(db, table, verbose, dataDirectory);
            }
        }

        private void ScriptTableDataNative(Table table, string dataDirectory, Server server)
        {
            if (!Directory.Exists(dataDirectory)) Directory.CreateDirectory(dataDirectory);
            var fileName = Path.ChangeExtension(Path.Combine(dataDirectory, GetScriptFileName(table)), "sql");

            var scripter = new Scripter(server)
            {
                Options =
                {
                    ScriptData = true,
                    ScriptSchema = false
                }
            };
            using (TextWriter writer = GetStreamWriter(fileName, false))
            {
                foreach (var script in scripter.EnumScript(new[] { table }))
                {
                    writer.WriteLine(script);
                }
            }
        }

        private void ScriptTableDataToCsv(Database db, Table table, string dataDirectory)
        {
            if (!Directory.Exists(dataDirectory)) Directory.CreateDirectory(dataDirectory);
            var fileName = Path.ChangeExtension(Path.Combine(dataDirectory, GetScriptFileName(table)), "csv");
            using(var csvWriter = new CsvWriter(fileName))
            {
                var fieldNames = new string[table.Columns.Count];
                for(var i = 0; i < table.Columns.Count; i++)
                {
                    fieldNames[i] = table.Columns[i].Name;
                }
                csvWriter.WriteFields(fieldNames);

                var sqlConnection = new SqlConnection(ConnectionString);
                sqlConnection.Open();
                var command = sqlConnection.CreateCommand();
                command.CommandText = string.Format("SELECT * FROM [{0}].[{1}].[{2}]", db.Name, table.Schema, table.Name);
                command.CommandType = CommandType.Text;
                var reader = command.ExecuteReader();

                while(reader.Read())
                {
                    var values = new object[table.Columns.Count];
                    reader.GetValues(values);
                    csvWriter.WriteFields(values);
                }
            }
        }

        private void ScriptTableDataWithBcp(Database db, Table table, bool verbose, string dataDirectory)
        {
            if (!Directory.Exists(dataDirectory)) Directory.CreateDirectory(dataDirectory);
            var fileName = Path.ChangeExtension(Path.Combine(dataDirectory, GetScriptFileName(table)), "txt");

            using (var p = new Process())
            {
                var credentials = "-T";

                var builder = new SqlConnectionStringBuilder(ConnectionString);
                if (!builder.IntegratedSecurity)
                {
                    credentials = string.Format("-U \"{0}\" -P \"{1}\"", builder.UserID, builder.Password);
                }

                p.StartInfo.Arguments = string.Format("\"[{0}].[{1}].[{2}]\" out \"{3}\" -c -S\"{4}\" {5}",
                                                      db.Name,
                                                      table.Schema,
                                                      table.Name,
                                                      fileName,
                                                      db.Parent.Name,
                                                      credentials);

                p.StartInfo.FileName = "bcp.exe";
                //p.StartInfo.WorkingDirectory = dataDirectory;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                if (verbose) Console.Error.WriteLine("{0} {1}", p.StartInfo.FileName, p.StartInfo.Arguments);
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (verbose) Console.Error.WriteLine(output);
            }
        }

        private bool MatchesTableDataFilters(string databaseName, string tableName)
        {
            if (MatchesFilter(FilterType.TableData, tableName))
            {
                return true;
            }
            if (DatabaseTableDataFilter != null && DatabaseTableDataFilter.ContainsKey(databaseName.ToUpperInvariant()) && MatchesFilter(DatabaseTableDataFilter[databaseName.ToUpperInvariant()], tableName))
            {
                return true;
            }
            return false;
        }

        private void ScriptAssemblies(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string assemblies = Path.Combine(programmability, "Assemblies");
            string dropAssemblies = Path.Combine(assemblies, "Drop");
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //            if (!Directory.Exists(assemblies)) Directory.CreateDirectory(assemblies);
            //            if (!Directory.Exists(dropAssemblies)) Directory.CreateDirectory(dropAssemblies);

            foreach (SqlAssembly smo in db.Assemblies)
            {
                if (!CreateOnly)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(dropAssemblies, FixUpFileName(smo.Name) + ".DROP.sql"), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}]: DROP [{1}]", db.Name, smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;

                        //
                        // need to drop any objects that depend on 
                        // this assembly before dropping the assembly!
                        //
                        foreach (UserDefinedFunction ss in db.UserDefinedFunctions)
                        {
                            if (ss.AssemblyName == smo.Name)
                            {
                                WriteScript(ss.Script(so), sw);
                            }
                        }

                        foreach (StoredProcedure ss in db.StoredProcedures)
                        {
                            if (ss.AssemblyName == smo.Name)
                            {
                                WriteScript(ss.Script(so), sw);
                            }
                        }

                        foreach (UserDefinedType ss in db.UserDefinedTypes)
                        {
                            if (ss.AssemblyName == smo.Name)
                            {
                                WriteScript(ss.Script(so), sw);
                            }
                        }

                        WriteScript(smo.Script(so), sw);
                    }
                }
                using (StreamWriter sw = GetStreamWriter(Path.Combine(assemblies, FixUpFileName(smo.Name) + ".sql"), false))
                {
                    if (verbose) Console.Error.WriteLine("[{0}]: [{1}]", db.Name, smo.Name);
                    so.ScriptDrops = so.IncludeIfNotExists = false;
                    WriteScript(smo.Script(so), sw);

                    if (Properties)
                    {
                        ScriptProperties(smo, sw);
                    }
                }
            }
        }

        private void ScriptSprocs(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string sprocs = Path.Combine(programmability, "StoredProcedures");
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //            if (!Directory.Exists(sprocs)) Directory.CreateDirectory(sprocs);


            foreach (StoredProcedure smo in db.StoredProcedures)
            {
                if ((IncludeSystemObjects || !smo.IsSystemObject) && !smo.IsEncrypted)
                {
                    if (!FilterExists() || MatchesFilter(FilterType.StoredProcedure, smo.Name))
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(sprocs, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                            if (ScriptAsCreate)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;

                            if (ScriptAsCreate)
                            {
                                WriteScript(smo.Script(so), sw);
                            }
                            else
                            {
                                WriteScript(smo.Script(so), sw, "CREATE PROC", "ALTER PROC");
                            }

                            if (Properties)
                            {
                                ScriptProperties(smo, sw);
                            }
                        }
                    }
                }
            }
        }

        private void ScriptViews(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string views = Path.Combine(outputDirectory, "Views");
            //            if (!Directory.Exists(views)) Directory.CreateDirectory(views);

            foreach (View smo in db.Views)
            {
                if ((IncludeSystemObjects || !smo.IsSystemObject) && !smo.IsEncrypted)
                {
                    if (!FilterExists() || MatchesFilter(FilterType.View, smo.Name))
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(views, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                            if (!CreateOnly)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);

                            if (Properties)
                            {
                                ScriptProperties(smo, sw);
                            }
                        }

                        if (db.Version >= 8 && db.CompatibilityLevel >= CompatibilityLevel.Version80)
                        {
                            ScriptIndexes(smo, verbose, db, so, views);
                        }
                    }
                }
            }
        }

        private void ScriptUdfs(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string udfs = Path.Combine(programmability, "Functions");
            //if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //if (!Directory.Exists(udfs)) Directory.CreateDirectory(udfs);


            foreach (UserDefinedFunction smo in db.UserDefinedFunctions)
            {

                if ((IncludeSystemObjects || !smo.IsSystemObject) && !smo.IsEncrypted)
                {
                    if (!FilterExists() || MatchesFilter(FilterType.UserDefinedFunction, smo.Name))
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(udfs, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                            if (!CreateOnly)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);

                            if (Properties)
                            {
                                ScriptProperties(smo, sw);
                            }
                        }
                    }
                }
            }
        }

        private void ScriptUdts(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string types = Path.Combine(programmability, "Types");
            //if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //if (!Directory.Exists(types)) Directory.CreateDirectory(types);

            foreach (UserDefinedType smo in db.UserDefinedTypes)
            {
                if (!FilterExists() || MatchesFilter(FilterType.UserDefinedType, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptUddts(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string types = Path.Combine(programmability, "Types");
            //if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //if (!Directory.Exists(types)) Directory.CreateDirectory(types);

            foreach (UserDefinedDataType smo in db.UserDefinedDataTypes)
            {
                if (!FilterExists() || MatchesFilter(FilterType.UserDefinedDataType, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptRules(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string rules = Path.Combine(programmability, "Rules");
            //if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //if (!Directory.Exists(rules)) Directory.CreateDirectory(rules);


            foreach (Rule smo in db.Rules)
            {
                if (!FilterExists() || MatchesFilter(FilterType.Rule, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(rules, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptDefaults(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            string defaults = Path.Combine(programmability, "Defaults");
            //            if (!Directory.Exists(defaults)) Directory.CreateDirectory(defaults);

            foreach (Default smo in db.Defaults)
            {
                if (!FilterExists() || MatchesFilter(FilterType.Default, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(defaults, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}].[{1}].[{2}]", db.Name, smo.Schema, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptDdlTriggers(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            string triggers = Path.Combine(programmability, "Database Triggers");
            //            if (!Directory.Exists(triggers)) Directory.CreateDirectory(triggers);

            foreach (DatabaseDdlTrigger smo in db.Triggers)
            {
                if (!FilterExists() || MatchesFilter(FilterType.DdlTrigger, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(triggers, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}]: [{1}]", db.Name, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptSchemas(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string schemas = Path.Combine(outputDirectory, "Schemas");
            //            if (!Directory.Exists(schemas)) Directory.CreateDirectory(schemas);

            foreach (Schema smo in db.Schemas)
            {
                // IsSystemObject doesn't exist for schemas.  Bad Cip!!!
                if (smo.Name == "sys" ||
                    smo.Name == "dbo" ||
                    smo.Name == "db_accessadmin" ||
                    smo.Name == "db_backupoperator" ||
                    smo.Name == "db_datareader" ||
                    smo.Name == "db_datawriter" ||
                    smo.Name == "db_ddladmin" ||
                    smo.Name == "db_denydatawriter" ||
                    smo.Name == "db_denydatareader" ||
                    smo.Name == "db_owner" ||
                    smo.Name == "db_securityadmin" ||
                    smo.Name == "INFORMATION_SCHEMA" ||
                    smo.Name == "guest") continue;

                if (!FilterExists() || MatchesFilter(FilterType.Schema, smo.Name))
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(schemas, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.Error.WriteLine("[{0}]: [{1}]", db.Name, smo.Name);
                        if (!CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (Properties)
                        {
                            ScriptProperties(smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptProperties(IExtendedProperties obj, StreamWriter sw)
        {
            if (obj == null) throw new ArgumentNullException("obj");
            if (sw == null) throw new ArgumentNullException("sw");

            foreach (ExtendedProperty ep in obj.ExtendedProperties)
            {
                WriteScript(ep.Script(), sw);
            }
        }

        // TODO:
        // Add a timeout?
        // Do something special on command failure?
        private static void RunCommand(string command, bool verbose, string outputDirectory, string serverName, string databaseName)
        {
            string filename, arguments;
            if (ParseCommand(command, outputDirectory, serverName, databaseName, out filename, out arguments))
            {
                if (verbose) Console.Error.WriteLine("Running command: " + filename + " " + arguments);
                RunAndWait(filename, arguments);
            }
        }

        private static bool ParseCommand(string postScriptingCommand, string outputDirectory, string serverName, string databaseName, out string filename, out string arguments)
        {
            filename = string.Empty;
            arguments = string.Empty;

            if(postScriptingCommand == null)
            {
                return false;
            }
            var command = postScriptingCommand.Trim();
            if(string.IsNullOrEmpty(command))
            {
                return false;
            }

            command = command.Replace("{path}", outputDirectory);
            command = command.Replace("{server}", serverName);
            command = command.Replace("{database}", databaseName);
            command = command.Replace("{serverclean}", FixUpFileName(serverName));
            command = command.Replace("{databaseclean}", FixUpFileName(databaseName));

            if (command.StartsWith("\""))
            {
                var secondQuotePosition = command.IndexOf('"', 1);
                if(secondQuotePosition > -1)
                {
                    filename = command.Substring(1, secondQuotePosition - 1);
                    arguments = command.Substring(secondQuotePosition + 1);
                }
                else
                {
                    filename = command;
                }
            }
            if(string.IsNullOrEmpty(filename))
            {
                var spacePosition = command.IndexOf(' ');
                if(spacePosition > -1)
                {
                    filename = command.Substring(0, spacePosition);
                    arguments = command.Substring(spacePosition + 1);
                }
                else
                {
                    filename = command;
                }
            }
            return true;
        }

        private static void RunAndWait(string filename, string arguments)
        {
            var process = new Process
            {
                StartInfo =
                {
                    FileName = filename,
                    Arguments = arguments,
                    //CreateNoWindow = true,
                    UseShellExecute = false, // Share parent console -- http://stackoverflow.com/questions/5094003/net-windowstyle-hidden-vs-createnowindow-true
                }
            };
            process.Start();
            process.WaitForExit();
            if(process.ExitCode != 0)
            {
                throw new Exception("Command returned error " + process.ExitCode);
            }
        }

        #endregion

        #region Private Utility Functions

        private void WriteScript(StringCollection script, StreamWriter sw, string replaceMe, string replaceWith)
        {
            foreach (string ss in script)
            {
                if (ss == "SET QUOTED_IDENTIFIER ON" ||
                    ss == "SET QUOTED_IDENTIFIER OFF" ||
                    ss == "SET ANSI_NULLS ON" ||
                    ss == "SET ANSI_NULLS OFF")
                {
                    continue;
                }

                string sss = ReplaceEx(ss, replaceMe, replaceWith);
                sw.WriteLine(sss);
                sw.WriteLine("GO\r\n");
            }
        }

        private void WriteScript(StringCollection script, StreamWriter sw)
        {
            foreach (string ss in script)
            {
                if (ss == "SET QUOTED_IDENTIFIER ON" ||
                    ss == "SET QUOTED_IDENTIFIER OFF" ||
                    ss == "SET ANSI_NULLS ON" ||
                    ss == "SET ANSI_NULLS OFF")
                {
                    continue;
                }

                sw.WriteLine(ss);
                sw.WriteLine("GO\r\n");
            }
        }

        /// <summary>
        /// for case-insensitive string replace.  from www.codeproject.com
        /// </summary>
        /// <param name="original"></param>
        /// <param name="pattern"></param>
        /// <param name="replacement"></param>
        /// <returns></returns>
        private string ReplaceEx(string original, string pattern, string replacement)
        {
            int position0, position1;
            int count = position0 = 0;
            string upperString = original.ToUpper();
            string upperPattern = pattern.ToUpper();
            int inc = (original.Length / pattern.Length) * (replacement.Length - pattern.Length);
            var chars = new char[original.Length + Math.Max(0, inc)];
            while ((position1 = upperString.IndexOf(upperPattern, position0)) != -1)
            {
                for (int i = position0; i < position1; ++i) chars[count++] = original[i];
                for (int i = 0; i < replacement.Length; ++i) chars[count++] = replacement[i];
                position0 = position1 + pattern.Length;
            }
            if (position0 == 0) return original;
            for (int i = position0; i < original.Length; ++i) chars[count++] = original[i];
            return new string(chars, 0, count);
        }

        public static string GetScriptFileName(ScriptSchemaObjectBase parentObject, NamedSmoObject subObject = null)
        {
            var scriptFileName = FixUpFileName(parentObject.Schema) + "." + FixUpFileName(parentObject.Name);
            if (subObject != null)
            {
                scriptFileName += "." + FixUpFileName(subObject.Name);
            }
            scriptFileName += ".sql";
            return scriptFileName;
        }

        public static string FixUpFileName(string name)
        {
            if(string.IsNullOrEmpty(name))
            {
                return name;
            }

            return name
                .Replace("[", ".")
                .Replace("]", ".")
                //.Replace(" ", ".")
                .Replace("&", ".")
                .Replace("'", ".")
                .Replace("\"", ".")
                .Replace(">", ".")
                .Replace("<", ".")
                .Replace("!", ".")
                .Replace("@", ".")
                .Replace("#", ".")
                .Replace("$", ".")
                .Replace("%", ".")
                .Replace("^", ".")
                .Replace("*", ".")
                .Replace("(", ".")
                .Replace(")", ".")
                .Replace("+", ".")
                .Replace("{", ".")
                .Replace("}", ".")
                .Replace("|", ".")
                .Replace("\\", ".")
                .Replace("?", ".")
                .Replace(",", ".")
                .Replace("/", ".")
                .Replace(";", ".")
                .Replace(":", ".")
                .Replace("-", ".")
                .Replace("=", ".")
                .Replace("`", ".")
                .Replace("~", ".");
        }

        /// <summary>
        /// THIS FUNCTION HAS A SIDEEFFECT.
        /// If OutputFileName is set, it will always open the filename
        /// </summary>
        /// <param name="path"></param>
        /// <param name="append"></param>
        /// <returns></returns>
        private StreamWriter GetStreamWriter(string path, bool append)
        {
            if (OutputFileName != null)
            {
                path = OutputFileName;
                append = true;
            }
            if (OutputFileName == "-")
                return new StreamWriter(Console.OpenStandardOutput());

            if (!string.IsNullOrEmpty(Path.GetDirectoryName(path)) && !Directory.Exists(Path.GetDirectoryName(path)))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
            }
            return new StreamWriter(path, append);
        }

        public static void PurgeDirectory(string dirName, string fileSpec)
        {
            string fullPath = Path.GetFullPath(dirName);
            try
            {
                var extensionsToPurge = new[] {".sql", ".csv", ".txt"};
                foreach (var s in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories).Where(f => extensionsToPurge.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    // skip files inside .svn and .git folders (although these might be skipped regardless
                    // since they have a hidden attribute) 
                    if (!s.Contains(@"\.svn\") && !s.Contains(@"\.git\"))
                    {
                        var file = new FileInfo(s)
                            {
                                Attributes = FileAttributes.Normal
                            };
                        file.Delete();
                    }
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("Exception {0} : {1}", e.Message, fullPath);
            }
        }

        #endregion

        #region Public Properties

        public Dictionary<FilterType, List<string>> Filters { get; set; }
        public Dictionary<string, List<string>> DatabaseTableDataFilter { get; set; }
        public bool TableOneFile { get; set; }
        public bool ScriptAsCreate { get; set; }
        public bool Properties { get; set; }
        public bool Permissions { get; set; }
        public bool NoCollation { get; set; }
        public bool Statistics { get; set; }
        public bool CreateOnly { get; set; }
        public string OutputFileName { get; set; }
        public bool IncludeDatabase { get; set; }
        public bool IncludeSystemObjects { get; set; }
        public string PreScriptingCommand { get; set; }
        public string PostScriptingCommand { get; set; }
        public string StartCommand { get; set; }
        public string FinishCommand { get; set; }

        public string ConnectionString { get; set; }

        #endregion
    }
}
