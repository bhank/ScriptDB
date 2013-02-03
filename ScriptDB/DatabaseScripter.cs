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
using System.Text;
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.IO;
using System.Diagnostics;

using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;
using ScriptDb;
using Utils;
using Rule = Microsoft.SqlServer.Management.Smo.Rule;

namespace Elsasoft.ScriptDb
{
    public class DatabaseScripter
    {

        private const string createSprocStub = @"
IF NOT EXISTS (SELECT * FROM sysobjects WHERE id = OBJECT_ID(N'[{0}].[{1}]') AND type in (N'P', N'PC'))
EXEC sp_executesql N'CREATE PROCEDURE [{0}].[{1}] AS SELECT ''this is a stub.  replace me with real code please.'''
GO
";

        #region Private Variables

        private string[] _TableFilter = new string[0];
        private string[] _TableDataFilter = new string[0];
        private string[] _RulesFilter = new string[0];
        private string[] _DefaultsFilter = new string[0];
        private string[] _UddtsFilter = new string[0];
        private string[] _UdfsFilter = new string[0];
        private string[] _ViewsFilter = new string[0];
        private string[] _SprocsFilter = new string[0];
        private string[] _UdtsFilter = new string[0];
        private string[] _SchemasFilter = new string[0];
        private string[] _DdlTriggersFilter = new string[0];

        private bool _TableOneFile = false;
        private bool _ScriptAsCreate = false;
        private bool _Permissions = false;
        private bool _NoCollation = false;
        private bool _IncludeDatabase;
        private bool _CreateOnly = false;
        private bool _ScriptProperties = false;
        private string _PostScriptingCommand = null;
        private string _PreScriptingCommand = null;

        private string _OutputFileName = null;
        #endregion

        private bool FilterExists()
        {
            return _TableFilter.Length > 0 || _RulesFilter.Length > 0 || _DefaultsFilter.Length > 0
                   || _UddtsFilter.Length > 0 || _UdfsFilter.Length > 0 || _ViewsFilter.Length > 0
                   || _SprocsFilter.Length > 0 || _UdtsFilter.Length > 0 || _SchemasFilter.Length > 0
                   || _DdlTriggersFilter.Length > 0;
        }

        /// <summary>
        /// does all the work.
        /// </summary>
        /// <param name="connStr"></param>
        /// <param name="outputDirectory"></param>
        /// <param name="verbose"></param>
        public void GenerateScripts(string connStr, string outputDirectory,
                                    bool scriptAllDatabases, bool purgeDirectory,
                                    DataScriptingFormat dataScriptingFormat, bool verbose, bool scriptProperties)
        {
            ConnectionString = connStr;
            SqlConnection connection = new SqlConnection(connStr);
            ServerConnection sc = new ServerConnection(connection);
            Server s = new Server(sc);

            s.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject", "IsEncrypted");
            s.SetDefaultInitFields(typeof(Table), "IsSystemObject");
            s.SetDefaultInitFields(typeof(View), "IsSystemObject", "IsEncrypted");
            s.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject", "IsEncrypted");
            s.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;

            RunCommand(StartCommand, verbose, outputDirectory, s.Name, null);

            // Purge at the Server level only when we're doing all databases
            if (purgeDirectory && scriptAllDatabases && Directory.Exists(outputDirectory))
            {
                if (verbose) Console.Write("Purging directory...");
                PurgeDirectory(outputDirectory, "*.sql");
                if (verbose) Console.WriteLine("Done");
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
                        Console.WriteLine("Exception: {0}", e.Message);
                    }
                }
            }
            else
            {
                var db = s.Databases[connection.Database];
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
            this._ScriptProperties = scriptProperties;

            // Output folder
            var databaseOutputDirectory = Path.Combine(outputDirectory, FixUpFileName(db.Name));
            if (Directory.Exists(databaseOutputDirectory))
            {
                if (purgeDirectory)
                {
                    if (verbose) Console.Write("Purging database directory...");
                    PurgeDirectory(databaseOutputDirectory, "*.sql");
                    if (verbose) Console.WriteLine("done.");
                }
            }
            else
            {
                Directory.CreateDirectory(databaseOutputDirectory);
            }

            ScriptingOptions so = new ScriptingOptions();
            so.Default = true;
            so.DriDefaults = true;
            so.DriUniqueKeys = true;
            so.Bindings = true;
            so.Permissions = _Permissions;
            so.NoCollation = _NoCollation;
            so.IncludeDatabaseContext = _IncludeDatabase;

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
            string indexes = Path.Combine(tablesOrViewsOutputDirectory, "Indexes");
            string primaryKeys = Path.Combine(tablesOrViewsOutputDirectory, "PrimaryKeys");
            string uniqueKeys = Path.Combine(tablesOrViewsOutputDirectory, "UniqueKeys");

            string FileName = Path.Combine(tablesOrViewsOutputDirectory, GetScriptFileName(tableOrView));

            foreach (Index smo in tableOrView.Indexes)
            {
                if (!smo.IsSystemObject)
                {
                    string dir =
                        (smo.IndexKeyType == IndexKeyType.DriPrimaryKey) ? primaryKeys :
                        (smo.IndexKeyType == IndexKeyType.DriUniqueKey) ? uniqueKeys : indexes;
                    if (!_TableOneFile)
                        FileName = Path.Combine(dir, GetScriptFileName(tableOrView, smo));
                    using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}.{2}", db.Name, tableOrView.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptTables(bool verbose, Database db, ScriptingOptions so, string outputDirectory, DataScriptingFormat dataScriptingFormat, Server server)
        {
            string data = Path.Combine(outputDirectory, "Data");
            string tables = Path.Combine(outputDirectory, "Tables");
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string constraints = Path.Combine(tables, "Constraints");
            string foreignKeys = Path.Combine(tables, "ForeignKeys");
            string triggers = Path.Combine(programmability, "Triggers");

            //            if (!Directory.Exists(tables)) Directory.CreateDirectory(tables);
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            //            if (!Directory.Exists(indexes)) Directory.CreateDirectory(indexes);
            //            if (!Directory.Exists(constraints)) Directory.CreateDirectory(constraints);
            //            if (!Directory.Exists(foreignKeys)) Directory.CreateDirectory(foreignKeys);
            //            if (!Directory.Exists(uniqueKeys)) Directory.CreateDirectory(uniqueKeys);
            //            if (!Directory.Exists(primaryKeys)) Directory.CreateDirectory(primaryKeys);
            //            if (!Directory.Exists(triggers)) Directory.CreateDirectory(triggers);
            //            if (!Directory.Exists(data)) Directory.CreateDirectory(data);

            foreach (Table table in db.Tables)
            {
                if (!table.IsSystemObject)
                {
                    if (!FilterExists() || Array.IndexOf(_TableFilter, table.Name) >= 0)
                    {
                        string FileName = Path.Combine(tables, GetScriptFileName(table));
                        #region Table Definition
                        using (StreamWriter sw = GetStreamWriter(FileName, false))
                        {
                            if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, table.Name);
                            if (!_CreateOnly)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(table.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(table.Script(so), sw);

                            if (_ScriptProperties && table is IExtendedProperties)
                            {
                                ScriptProperties((IExtendedProperties)table, sw);
                            }
                        }

                        #endregion

                        #region Triggers

                        foreach (Trigger smo in table.Triggers)
                        {
                            if (!smo.IsSystemObject && !smo.IsEncrypted)
                            {
                                if (!_TableOneFile)
                                    FileName = Path.Combine(triggers, GetScriptFileName(table, smo));
                                using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                                {
                                    if (verbose) Console.WriteLine("{0} Scripting {1}.{2}", db.Name, table.Name, smo.Name);
                                    if (!_CreateOnly)
                                    {
                                        so.ScriptDrops = so.IncludeIfNotExists = true;
                                        WriteScript(smo.Script(so), sw);
                                    }
                                    so.ScriptDrops = so.IncludeIfNotExists = false;
                                    WriteScript(smo.Script(so), sw);

                                    if (_ScriptProperties && smo is IExtendedProperties)
                                    {
                                        ScriptProperties((IExtendedProperties)smo, sw);
                                    }
                                }
                            }
                        }

                        #endregion

                        ScriptIndexes(table, verbose, db, so, tables);

                        #region Foreign Keys

                        foreach (ForeignKey smo in table.ForeignKeys)
                        {
                            if (!_TableOneFile)
                                FileName = Path.Combine(foreignKeys, GetScriptFileName(table, smo));
                            using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                            {
                                if (verbose) Console.WriteLine("{0} Scripting {1}.{2}", db.Name, table.Name, smo.Name);
                                if (!_CreateOnly)
                                {
                                    so.ScriptDrops = so.IncludeIfNotExists = true;
                                }
                                WriteScript(smo.Script(), sw);

                                if (_ScriptProperties && smo is IExtendedProperties)
                                {
                                    ScriptProperties((IExtendedProperties)smo, sw);
                                }
                            }
                        }

                        #endregion

                        #region Constraints

                        foreach (Check smo in table.Checks)
                        {
                            if (!_TableOneFile)
                                FileName = Path.Combine(constraints, GetScriptFileName(table, smo));
                            using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                            {
                                if (verbose) Console.WriteLine("{0} Scripting {1}.{2}", db.Name, table.Name, smo.Name);
                                WriteScript(smo.Script(), sw);
                                if (_ScriptProperties && smo is IExtendedProperties)
                                {
                                    ScriptProperties((IExtendedProperties)smo, sw);
                                }
                            }
                        }

                        #endregion

                        #region Script Data

                        if (dataScriptingFormat != DataScriptingFormat.None && MatchesTableDataFilters(db.Name, table.Name))
                        {
                            ScriptTableData(db, table, verbose, data, dataScriptingFormat, server);
                        }

                        #endregion
                    }
                }
                else
                {
                    //if (verbose) Console.WriteLine("skipping system object {0}", table.Name);
                }
            }
        }

        private void ScriptTableData(Database db, Table table, bool verbose, string dataDirectory, DataScriptingFormat dataScriptingFormat, Server server)
        {
            if ((dataScriptingFormat & DataScriptingFormat.Sql) == DataScriptingFormat.Sql)
            {
                ScriptTableDataNative(db, table, verbose, dataDirectory, server);
            }
            if ((dataScriptingFormat & DataScriptingFormat.Csv) == DataScriptingFormat.Csv)
            {
                ScriptTableDataToCsv(db, table, verbose, dataDirectory);
            }
            if ((dataScriptingFormat & DataScriptingFormat.Bcp) == DataScriptingFormat.Bcp)
            {
                ScriptTableDataWithBcp(db, table, verbose, dataDirectory);
            }
        }

        private void ScriptTableDataNative(Database db, Table table, bool verbose, string dataDirectory, Server server)
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
            using (TextWriter writer = new StreamWriter(fileName))
            {
                foreach (var script in scripter.EnumScript(new[] { table }))
                {
                    writer.WriteLine(script);
                }
            }
        }

        private void ScriptTableDataToCsv(Database db, Table table, bool verbose, string dataDirectory)
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

            using (Process p = new Process())
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
                p.StartInfo.WorkingDirectory = dataDirectory;
                p.StartInfo.UseShellExecute = false;
                p.StartInfo.RedirectStandardOutput = true;
                if (verbose) Console.WriteLine("bcp.exe {0}", p.StartInfo.Arguments);
                p.Start();
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (verbose) Console.WriteLine(output);
            }
        }

        private bool MatchesTableDataFilters(string databaseName, string tableName)
        {
            if(TableDataFilter.Length == 0 && DatabaseTableDataFilter == null)
            {
                return true;
            }

            databaseName = databaseName.ToUpperInvariant();
            tableName = tableName.ToUpperInvariant();

            if(Array.IndexOf(TableDataFilter, tableName) >= 0)
            {
                return true;
            }
            if (DatabaseTableDataFilter.ContainsKey(databaseName) && (DatabaseTableDataFilter[databaseName].Contains(tableName) || DatabaseTableDataFilter[databaseName].Contains("*")))
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
                if (!_CreateOnly)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(dropAssemblies, FixUpFileName(smo.Name) + ".DROP.sql"), false))
                    {
                        if (verbose) Console.WriteLine("Scripting Drop {0}", smo.Name);
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
                    if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                    so.ScriptDrops = so.IncludeIfNotExists = false;
                    WriteScript(smo.Script(so), sw);

                    if (_ScriptProperties && smo is IExtendedProperties)
                    {
                        ScriptProperties((IExtendedProperties)smo, sw);
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
                if (!smo.IsSystemObject && !smo.IsEncrypted)
                {
                    if (!FilterExists() || Array.IndexOf(_SprocsFilter, smo.Name) >= 0)
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(sprocs, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                            if (_ScriptAsCreate)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;

                            if (_ScriptAsCreate)
                            {
                                WriteScript(smo.Script(so), sw);
                            }
                            else
                            {
                                WriteScript(smo.Script(so), sw, "CREATE PROC", "ALTER PROC");
                            }

                            if (_ScriptProperties && smo is IExtendedProperties)
                            {
                                ScriptProperties((IExtendedProperties)smo, sw);
                            }
                        }
                    }
                }
                else
                {
                    //if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
                }
            }
        }

        private void ScriptViews(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string views = Path.Combine(outputDirectory, "Views");
            //            if (!Directory.Exists(views)) Directory.CreateDirectory(views);

            foreach (View smo in db.Views)
            {
                if (!smo.IsSystemObject && !smo.IsEncrypted)
                {
                    if (!FilterExists() || Array.IndexOf(_ViewsFilter, smo.Name) >= 0)
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(views, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                            if (!_CreateOnly)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);

                            if (_ScriptProperties && smo is IExtendedProperties)
                            {
                                ScriptProperties((IExtendedProperties)smo, sw);
                            }
                        }

                        if (db.Version >= 8 && db.CompatibilityLevel >= CompatibilityLevel.Version80)
                        {
                            ScriptIndexes(smo, verbose, db, so, views);
                        }
                    }
                }
                else
                {
                    //if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
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

                if (!smo.IsSystemObject && !smo.IsEncrypted)
                {
                    if (!FilterExists() || Array.IndexOf(_UdfsFilter, smo.Name) >= 0)
                    {
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(udfs, GetScriptFileName(smo)), false))
                        {
                            if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                            if (!_CreateOnly)
                            {
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(so), sw);
                            }
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);

                            if (_ScriptProperties && smo is IExtendedProperties)
                            {
                                ScriptProperties((IExtendedProperties)smo, sw);
                            }
                        }
                    }
                }
                else
                {
                    //if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
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
                if (!FilterExists() || Array.IndexOf(_UdtsFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
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
                if (!FilterExists() || Array.IndexOf(_UddtsFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
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
                if (!FilterExists() || Array.IndexOf(_RulesFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(rules, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
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
                if (!FilterExists() || Array.IndexOf(_DefaultsFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(defaults, GetScriptFileName(smo)), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
                        }
                    }
                }
            }
        }

        private void ScriptDdlTriggers(bool verbose, Database db, ScriptingOptions so, string outputDirectory)
        {
            string programmability = Path.Combine(outputDirectory, "Programmability");
            //            if (!Directory.Exists(programmability)) Directory.CreateDirectory(programmability);
            string triggers = Path.Combine(programmability, "Triggers");
            //            if (!Directory.Exists(triggers)) Directory.CreateDirectory(triggers);

            foreach (DatabaseDdlTrigger smo in db.Triggers)
            {
                if (!FilterExists() || Array.IndexOf(_DdlTriggersFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(triggers, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
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

                if (!FilterExists() || Array.IndexOf(_SchemasFilter, smo.Name) >= 0)
                {
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(schemas, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("{0} Scripting {1}", db.Name, smo.Name);
                        if (!_CreateOnly)
                        {
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                        }
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);

                        if (_ScriptProperties && smo is IExtendedProperties)
                        {
                            ScriptProperties((IExtendedProperties)smo, sw);
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
                if (verbose) Console.WriteLine("Running command: " + filename + " " + arguments);
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
            int count, position0, position1;
            count = position0 = position1 = 0;
            string upperString = original.ToUpper();
            string upperPattern = pattern.ToUpper();
            int inc = (original.Length / pattern.Length) * (replacement.Length - pattern.Length);
            char[] chars = new char[original.Length + Math.Max(0, inc)];
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
        /// <param name="Path"></param>
        /// <param name="Append"></param>
        /// <returns></returns>
        private StreamWriter GetStreamWriter(string Path, bool Append)
        {
            if (_OutputFileName != null)
            {
                Path = OutputFileName;
                Append = true;
            }
            if (OutputFileName == "-")
                return new StreamWriter(System.Console.OpenStandardOutput());

            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Path))) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            return new StreamWriter(Path, Append);
        }

        public static void PurgeDirectory(string DirName, string FileSpec)
        {
            string FullPath = Path.GetFullPath(DirName);
            try
            {
                var extensionsToPurge = new[] {".sql", ".csv", ".txt"};
                foreach (var s in Directory.EnumerateFiles(FullPath, "*", SearchOption.AllDirectories).Where(f => extensionsToPurge.Contains(Path.GetExtension(f).ToLowerInvariant())))
                {
                    // skip files inside .svn and .git folders (although these might be skipped regardless
                    // since they have a hidden attribute) 
                    if (!s.Contains(@"\.svn\") && !s.Contains(@"\.git\"))
                    {
                        FileInfo file = new FileInfo(s);
                        file.Attributes = FileAttributes.Normal;
                        file.Delete();
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine("Exception {0} : {1}", e.Message, FullPath);
            };
        }

        #endregion

        #region Public Properties

        public string[] TableFilter
        {
            get { return _TableFilter; }
            set { _TableFilter = value; }
        }

        public string[] TableDataFilter
        {
            get { return _TableDataFilter; }
            set { _TableDataFilter = value; }
        }

        public Dictionary<string, List<string>> DatabaseTableDataFilter { get; set; }

        public string[] RulesFilter
        {
            get { return _RulesFilter; }
            set { _RulesFilter = value; }
        }

        public string[] DefaultsFilter
        {
            get { return _DefaultsFilter; }
            set { _DefaultsFilter = value; }
        }

        public string[] UddtsFilter
        {
            get { return _UddtsFilter; }
            set { _UddtsFilter = value; }
        }

        public string[] UdfsFilter
        {
            get { return _UdfsFilter; }
            set { _UdfsFilter = value; }
        }

        public string[] ViewsFilter
        {
            get { return _ViewsFilter; }
            set { _ViewsFilter = value; }
        }

        public string[] SprocsFilter
        {
            get { return _SprocsFilter; }
            set { _SprocsFilter = value; }
        }

        public string[] UdtsFilter
        {
            get { return _UdtsFilter; }
            set { _UdtsFilter = value; }
        }

        public string[] SchemasFilter
        {
            get { return _SchemasFilter; }
            set { _SchemasFilter = value; }
        }

        public string[] DdlTriggersFilter
        {
            get { return _DdlTriggersFilter; }
            set { _DdlTriggersFilter = value; }
        }

        public bool TableOneFile
        {
            get { return _TableOneFile; }
            set { _TableOneFile = value; }
        }

        public bool ScriptAsCreate
        {
            get { return _ScriptAsCreate; }
            set { _ScriptAsCreate = value; }
        }

        public bool Permissions
        {
            get { return _Permissions; }
            set { _Permissions = value; }
        }

        public bool NoCollation
        {
            get { return _NoCollation; }
            set { _NoCollation = value; }
        }

        public bool CreateOnly
        {
            get { return _CreateOnly; }
            set { _CreateOnly = value; }
        }

        public string OutputFileName
        {
            get { return _OutputFileName; }
            set { _OutputFileName = value; }
        }
        public bool IncludeDatabase
        {
            get { return _IncludeDatabase; }
            set { _IncludeDatabase = value; }
        }
        public string PreScriptingCommand
        {
            get { return _PreScriptingCommand; }
            set { _PreScriptingCommand = value; }
        }
        public string PostScriptingCommand
        {
            get { return _PostScriptingCommand; }
            set { _PostScriptingCommand = value; }
        }

        public string StartCommand { get; set; }
        public string FinishCommand { get; set; }

        public string ConnectionString { get; set; }

        #endregion
    }
}
