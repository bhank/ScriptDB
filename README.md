ScriptDB
========

**Download the [latest release][1] from GitHub!**

ScriptDb is a C# console app that uses SQL Management Objects (SMO) to script database objects. It was [originally written][2] by [Jesse Hersch][3] (see his copyright information below).


Highlights of my version:

* script table data to sql (INSERT statements), csv (using http://www.heikniemi.fi/jhlib/), or bcp
* specify tables for which to script data on the command line or in a file
* run commands on startup, shutdown, and before and after scripting a database
* script view indexes, full-text indexes, and statistics
* use safe output filenames
* improved command line interface

The most significant change is the ability to run commands. This allows you to do things like scripting databases directly into source control with a single command.

### Requirements
ScriptDb requires SMO, SQL Management Objects. You can download this as part of the Microsoft SQL Server Feature Pack. To do the minimal install, go to [the download page][4] and download the files ENU\x64\SharedManagementObjects.msi and ENU\x64\SQLSysClrTypes.msi (substitute x86 if necessary). Install SQLSysClrTypes.msi first.

### Examples

Here's a simple command to script the schema of tables, views, and stored procedures into a single file for the Northwind database on localhost, using trusted auth:

    scriptdb.exe --database=Northwind --outputfile=Northwind.sql 

Here's a really complicated command to use ScriptDB along with https://github.com/bhank/SVNCompleteSync to script all databases on a server to SVN:

    scriptdb.exe --server=DBSERVER --username=SQLLOGIN --password=SQLPASSWORD --scriptalldatabases --outputdirectory=scripts\{serverclean} --purge --datatablefile=ScriptDataTables.txt --tableonefile --scriptdatabase  --startcommand="SvnClient.exe" checkoutupdate \"https://svnserver/svn/svnrepo/trunk/Databases/{serverclean}\" \"{path}\" --mkdir --message=\"Adding server {server}\" --cleanworkingcopy --verbose --trust-server-cert" --prescriptingcommand="cmd /c echo Scripting {database} at %TIME%" --postscriptingcommand="SvnClient.exe completesync --message=\"Updating {database} on {server}\" \"{path}\{databaseclean}\" --verbose --trust-server-cert"

And here's a breakdown of what it's doing:

`scriptdb.exe`

* `--server=DBSERVER` - connect to DBSERVER (omit this for localhost)
* `--username=SQLLOGIN --password=SQLPASSWORD` - use this SQL login (omit this for trusted auth)
* `--scriptalldatabases` - script all the databases on the server (specify --database=DatabaseName to script just one)
* `--outputdirectory=scripts\{serverclean}` - put the output in a subdirectory under scripts, named after the SQL server, but with any illegal characters cleaned out
* `--purge` - delete existing files from this directory before scripting (so that when we commit, any database objects which no longer exist will be removed from SVN)
* `--datatablefile=ScriptDataTables.txt` - use this file to determine the tables in each database for which data should be scripted, rather than just schema. The lines of this file look like "DatabaseName:Table,OtherTable,WildcardTables*,YetAnotherTable"
* `--tableonefile` - put indexes, triggers, etc. for a table all in the same file as the table itself, rather than in separate files
* `--scriptdatabase` - script the database itself too -- but I don't think this works yet
* `--startcommand="SvnClient.exe" checkoutupdate \"https://svnserver/svn/svnrepo/trunk/Databases/{serverclean}\" \"{path}\" --mkdir --message=\"Adding server {server}\" --cleanworkingcopy --verbose --trust-server-cert"` - on startup, ensure that a clean up-to-date working copy for the SVN URL for this SQL server exists in the output directory, creating it in SVN if necessary, checking it out if it isn't already checked out, reverting any changes, and updating it
* `--prescriptingcommand="cmd /c echo Scripting {database} at %TIME%"` - print the time before starting to script each database (not really necessary)
* `--postscriptingcommand="SvnClient.exe completesync --message=\"Updating {database} on {server}\" \"{path}\{databaseclean}\" --verbose --trust-server-cert"` - commit changes (if any) to SVN after scripting each database, adding any new files and deleting any missing files

-Adam Coyne <github@mail2.coyne.nu>

```
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
```

[1]: https://github.com/bhank/ScriptDB/releases
[2]: http://scriptdb.codeplex.com/
[3]: http://www.elsasoft.org
[4]: https://www.microsoft.com/en-us/download/details.aspx?id=42295
