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
using System.Collections.Specialized;
using System.Data.SqlClient;
using System.IO;
using System.Diagnostics;

using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;

namespace Elsasoft.ScriptDb
{
    public class DatabaseScripter
    {

        private const string createSprocStub = @"
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[{0}].[{1}]') AND type in (N'P', N'PC'))
EXEC sys.sp_executesql N'CREATE PROCEDURE [{0}].[{1}] AS SELECT ''this is a stub.  replace me with real code please.'''
GO
";

        private string[] _TableFilter = new string[0];
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
        public void GenerateScript(string connStr, string outputDirectory, bool scriptData, bool verbose)
        {
            //            if (!Directory.Exists(outputDirectory)) Directory.CreateDirectory(outputDirectory);

            using (SqlConnection connection = new SqlConnection(connStr))
            {
                ServerConnection sc = new ServerConnection(connection);
                Server s = new Server(sc);
                s.SetDefaultInitFields(typeof(StoredProcedure), "IsSystemObject", "IsEncrypted");
                s.SetDefaultInitFields(typeof(Table), "IsSystemObject");
                s.SetDefaultInitFields(typeof(View), "IsSystemObject", "IsEncrypted");
                s.SetDefaultInitFields(typeof(UserDefinedFunction), "IsSystemObject", "IsEncrypted");
                s.ConnectionContext.SqlExecutionModes = SqlExecutionModes.CaptureSql;

                Database db = s.Databases[connection.Database];

                ScriptingOptions so = new ScriptingOptions();
                so.Default = true;
                so.DriDefaults = true;
                so.DriUniqueKeys = true;
                so.Bindings = true;

                ScriptTables(verbose, db, so, outputDirectory, scriptData);
                ScriptDefaults(verbose, db, so, outputDirectory);
                ScriptRules(verbose, db, so, outputDirectory);
                ScriptUddts(verbose, db, so, outputDirectory);
                ScriptUdfs(verbose, db, so, outputDirectory);
                ScriptViews(verbose, db, so, outputDirectory);
                ScriptSprocs(verbose, db, so, outputDirectory);

                if (s.Information.Version.Major >= 9 &&
                    db.CompatibilityLevel >= CompatibilityLevel.Version90)
                {
                    ScriptUdts(verbose, db, so, outputDirectory);
                    ScriptSchemas(verbose, db, so, outputDirectory);
                    ScriptDdlTriggers(verbose, db, so, outputDirectory);
                    ScriptAssemblies(verbose, db, so, outputDirectory);
                }
            }
        }

        private void ScriptTables(bool verbose, Database db, ScriptingOptions so, string outputDirectory, bool scriptData)
        {
            string data = Path.Combine(outputDirectory, "Data");
            string tables = Path.Combine(outputDirectory, "Tables");
            string programmability = Path.Combine(outputDirectory, "Programmability");
            string indexes = Path.Combine(tables, "Indexes");
            string constraints = Path.Combine(tables, "Constraints");
            string foreignKeys = Path.Combine(tables, "ForeignKeys");
            string primaryKeys = Path.Combine(tables, "PrimaryKeys");
            string uniqueKeys = Path.Combine(tables, "UniqueKeys");
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
                        string FileName = Path.Combine(tables, FixUpFileName(table.Name) + ".sql");
                        #region Table Definition
                        using (StreamWriter sw = GetStreamWriter(FileName, false))
                        {
                            if (verbose) Console.WriteLine("Scripting {0}", table.Name);
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(table.Script(so), sw);
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(table.Script(so), sw);
                        }

                        #endregion

                        #region Triggers

                        foreach (Trigger smo in table.Triggers)
                        {
                            if (!smo.IsSystemObject && !smo.IsEncrypted)
                            {
                                if (!_TableOneFile)
                                    FileName =
                                        Path.Combine(triggers,
                                                     FixUpFileName(string.Format("{0}.{1}.sql", table.Name, smo.Name)));
                                using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                                {
                                    if (verbose) Console.WriteLine("Scripting {0}.{1}", table.Name, smo.Name);
                                    so.ScriptDrops = so.IncludeIfNotExists = true;
                                    WriteScript(smo.Script(so), sw);
                                    so.ScriptDrops = so.IncludeIfNotExists = false;
                                    WriteScript(smo.Script(so), sw);
                                }
                            }
                        }

                        #endregion

                        #region Indexes

                        foreach (Index smo in table.Indexes)
                        {
                            if (!smo.IsSystemObject)
                            {
                                string dir =
                                    (smo.IndexKeyType == IndexKeyType.DriPrimaryKey) ? primaryKeys :
                                    (smo.IndexKeyType == IndexKeyType.DriUniqueKey) ? uniqueKeys : indexes;
                                if (!_TableOneFile)
                                    FileName =
                                        Path.Combine(dir,
                                                     FixUpFileName(string.Format("{0}.{1}.sql", table.Name, smo.Name)));
                                using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                                {
                                    if (verbose) Console.WriteLine("Scripting {0}.{1}", table.Name, smo.Name);
                                    so.ScriptDrops = so.IncludeIfNotExists = true;
                                    WriteScript(smo.Script(so), sw);
                                    so.ScriptDrops = so.IncludeIfNotExists = false;
                                    WriteScript(smo.Script(so), sw);
                                }
                            }
                        }

                        #endregion

                        #region Foreign Keys

                        foreach (ForeignKey smo in table.ForeignKeys)
                        {
                            if (!_TableOneFile)
                                FileName =
                                    Path.Combine(foreignKeys,
                                                 FixUpFileName(string.Format("{0}.{1}.sql", table.Name, smo.Name)));
                            using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                            {
                                if (verbose) Console.WriteLine("Scripting {0}.{1}", table.Name, smo.Name);
                                so.ScriptDrops = so.IncludeIfNotExists = true;
                                WriteScript(smo.Script(), sw);
                            }
                        }

                        #endregion

                        #region Constraints

                        foreach (Check smo in table.Checks)
                        {
                            if (!_TableOneFile)
                                FileName =
                                    Path.Combine(constraints,
                                                 FixUpFileName(string.Format("{0}.{1}.sql", table.Name, smo.Name)));
                            using (StreamWriter sw = GetStreamWriter(FileName, _TableOneFile))
                            {
                                if (verbose) Console.WriteLine("Scripting {0}.{1}", table.Name, smo.Name);
                                WriteScript(smo.Script(), sw);
                            }
                        }

                        #endregion

                        #region Script Data

                        if (scriptData)
                        {
                            using (Process p = new Process())
                            {
                                //
                                // makes more sense to pass this cmd line as an arg to scriptdb.exe, 
                                // but I am too lazy to do that now...
                                // besides, we have to leave some work for others!
                                //
                                p.StartInfo.Arguments = string.Format("\"{0}.{1}.{2}\" out {2}.txt -c -T -S{3}",
                                                                      db.Name,
                                                                      table.Schema,
                                                                      table.Name,
                                                                      db.Parent.Name);

                                p.StartInfo.FileName = "bcp.exe";
                                p.StartInfo.WorkingDirectory = data;
                                p.StartInfo.UseShellExecute = false;
                                p.StartInfo.RedirectStandardOutput = true;
                                if (verbose) Console.WriteLine("bcp.exe {0}", p.StartInfo.Arguments);
                                p.Start();
                                string output = p.StandardOutput.ReadToEnd();
                                p.WaitForExit();
                                if (verbose) Console.WriteLine(output);
                            }
                        }

                        #endregion
                    }
                }
                else
                {
                    if (verbose) Console.WriteLine("skipping system object {0}", table.Name);
                }
            }
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

                using (StreamWriter sw = GetStreamWriter(Path.Combine(assemblies, FixUpFileName(smo.Name) + ".sql"), false))
                {
                    if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                    so.ScriptDrops = so.IncludeIfNotExists = false;
                    WriteScript(smo.Script(so), sw);
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
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(sprocs, FixUpFileName(smo.Name) + ".sql"), false))
                        {
                            if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            sw.WriteLine(string.Format(createSprocStub, smo.Schema, smo.Name));
                            if (_ScriptAsCreate)
                                WriteScript(smo.Script(so), sw);
                            else
                                WriteScript(smo.Script(so), sw, "CREATE PROC", "ALTER PROC");
                        }
                    }
                }
                else
                {
                    if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
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
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(views, FixUpFileName(smo.Name) + ".sql"), false))
                        {
                            if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);
                        }
                    }
                }
                else
                {
                    if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
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
                        using (StreamWriter sw = GetStreamWriter(Path.Combine(udfs, FixUpFileName(smo.Name) + ".sql"), false))
                        {
                            if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                            so.ScriptDrops = so.IncludeIfNotExists = true;
                            WriteScript(smo.Script(so), sw);
                            so.ScriptDrops = so.IncludeIfNotExists = false;
                            WriteScript(smo.Script(so), sw);
                        }
                    }
                }
                else
                {
                    if (verbose) Console.WriteLine("skipping system object {0}", smo.Name);
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
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
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
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(types, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
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
                    using (StreamWriter sw = GetStreamWriter(Path.Combine(rules, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
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
                    using (
                        StreamWriter sw =
                            GetStreamWriter(Path.Combine(defaults, FixUpFileName(smo.Name) + ".sql"), false))
                    {
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
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
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
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
                        if (verbose) Console.WriteLine("Scripting {0}", smo.Name);
                        so.ScriptDrops = so.IncludeIfNotExists = true;
                        WriteScript(smo.Script(so), sw);
                        so.ScriptDrops = so.IncludeIfNotExists = false;
                        WriteScript(smo.Script(so), sw);
                    }
                }
            }
        }

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

        private string FixUpFileName(string name)
        {
            return name
                .Replace("[", ".")
                .Replace("]", ".")
                .Replace(" ", ".")
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
        private StreamWriter GetStreamWriter(string Path, bool Append)
        {
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Path))) Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
            return new StreamWriter(Path, Append);
        }


        public string[] TableFilter
        {
            get { return _TableFilter; }
            set { _TableFilter = value; }
        }

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
    }
}
