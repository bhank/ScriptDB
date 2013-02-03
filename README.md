ScriptDB
========

ScriptDb is a C# console app that uses SQL Management Objects (SMO) to script database objects. It was originally written by Jesse Hersch of http://www.elsasoft.org (see his copyright information below). His version is hosted at http://scriptdb.codeplex.com/ .

Highlights of my version:
* script table data to sql (INSERT statements), csv (using http://www.heikniemi.fi/jhlib/), or bcp (with fixed authentication)
* specify tables for which to script data on the command line or in a file
* run commands on startup, shutdown, and before and after scripting a database
* script view indexes
* use safe output filenames

The most significant change is the ability to run commands. This allows you to do things like scripting databases directly into source control with a single command.

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
