@echo off

REM
REM Copyright 2006 Jesse Hersch
REM
REM Permission to use, copy, modify, and distribute this software
REM and its documentation for any purpose is hereby granted without fee,
REM provided that the above copyright notice appears in all copies and that
REM both that copyright notice and this permission notice appear in
REM supporting documentation, and that the name of Jesse Hersch or
REM Elsasoft LLC not be used in advertising or publicity
REM pertaining to distribution of the software without specific, written
REM prior permission.  Jesse Hersch and Elsasoft LLC make no
REM representations about the suitability of this software for any
REM purpose.  It is provided "as is" without express or implied warranty.
REM
REM Jesse Hersch and Elsasoft LLC disclaim all warranties with
REM regard to this software, including all implied warranties of
REM merchantability and fitness, in no event shall Jesse Hersch or
REM Elsasoft LLC be liable for any special, indirect or
REM consequential damages or any damages whatsoever resulting from loss of
REM use, data or profits, whether in an action of contract, negligence or
REM other tortious action, arising out of or in connection with the use or
REM performance of this software.
REM
REM Author:
REM  Jesse Hersch
REM  Elsasoft LLC
REM 

set db=%1
set dbserver=%2
set script_directory=%3

if "%db%"=="" goto syntax
if "%dbserver%"=="" goto syntax
if "%script_directory%"=="" set root=%CD%

REM use windows auth
SET auth=-E

REM make sure we can find sqlcmd first...
SQLCMD /? > nul
IF ERRORLEVEL 1 echo could not find sqlcmd on this machine.  exiting. & goto Done

IF NOT EXIST "%script_directory%" echo Directory %script_directory% not found.  exiting. & goto Done

call :BuildDirectory "%script_directory%\Schemas\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Assemblies\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Assemblies\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Types\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Defaults\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Rules\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Triggers\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Views\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\Functions\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Programmability\StoredProcedures\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\Constraints\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\PrimaryKeys\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\UniqueKeys\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\ForeignKeys\*.sql" || goto Done
call :BuildDirectory "%script_directory%\Tables\Indexes\*.sql" || goto Done
goto Success

:BuildFile
ECHO executing: %1
sqlcmd.exe -b -i %1 -S %dbserver% -d %db% %auth% 	
IF ERRORLEVEL 1 ECHO failed to execute script %1
goto :EOF

:BuildDirectory
FOR %%i in ("%1") do call :BuildFile %%i || goto Done
goto :EOF

:Success
echo ==================================
echo all scripts executed successfully!
echo ==================================
goto Done

:Syntax
echo.
echo builddb.bat syntax:
echo.
echo builddb.bat database server directory
echo.
echo where:
echo.
echo    database:  is the name of the database you want to build (it must exist already)
echo    server:    is the server where said database lives.  
echo    directory: is where the Tables,Views,etc folders are located.  
echo               if directory is not provided then the current directory is assumed.
goto Done

:Done