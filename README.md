ScriptDB
========

ScriptDb is a C# console app that uses SQL Management Objects (SMO) to script database objects. It was originally written by Jesse Hersch of http://www.elsasoft.org (see his copyright information below). His version is hosted at http://scriptdb.codeplex.com/ .

A binary is available at http://www.coyne.nu/tools/scriptdb.html .

Highlights of my version:
* script table data to sql (INSERT statements), csv (using http://www.heikniemi.fi/jhlib/), or bcp (with fixed authentication)
* specify tables for which to script data on the command line or in a file
* run commands on startup, shutdown, and before and after scripting a database
* script view indexes, full-text indexes, and statistics
* use safe output filenames
* improved command line interface

The most significant change is the ability to run commands. This allows you to do things like scripting databases directly into source control with a single command.

Scripting a single database:

    scriptdb.exe --server=localhost --trustedauth --database=Northwind --purge --outputdirectory=scripts --scriptdata --tableonefile --scriptpermissions --scriptdatabase

Using it along with https://github.com/bhank/SVNCompleteSync to script all databases on a server to SVN:

    scriptdb.exe --server=localhost --trustedauth --scriptalldatabases --purge --scriptdata --tabledatafile=ScriptDataTables.txt --tableonefile --scriptdatabase --outputdirectory=scripts\{serverclean} --startcommand="svnclient.exe checkoutupdate \"https://svnserver/svn/Databases/{serverclean}\" \"{path}\" --mkdir --message=\"Adding server {server}\" --cleanworkingcopy --verbose --username=svnuser --password=svnpass --trust-server-cert" --prescriptingcommand="cmd /c now.exe Scripting {database} & exit /b 0" --postscriptingcommand="svnclient.exe completesync --message=\"Updating {database} on {server}\" \"{path}\{databaseclean}\" --verbose --username=svnuser --password=svnpass --trust-server-cert"


-Adam Coyne

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
