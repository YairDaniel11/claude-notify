@echo off
setlocal
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll"
cd /d "%~dp0"
if not exist out mkdir out
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 %REFS% /out:out\ClaudeNotify.exe src\ClaudeNotify.cs || goto :err
out\ClaudeNotify.exe --make-icon out\app.ico
"%CSC%" /nologo /target:winexe /optimize+ /codepage:65001 /win32icon:out\app.ico %REFS% /out:out\ClaudeNotify.exe src\ClaudeNotify.cs || goto :err
echo Build OK: out\ClaudeNotify.exe
exit /b 0
:err
echo Build FAILED
exit /b 1
