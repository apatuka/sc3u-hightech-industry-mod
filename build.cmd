@echo off
rem Builds SC3U_HighTech_Industry.exe with the C# compiler included in Windows (.NET Framework 4).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
cd /d "%~dp0"
if not exist bin mkdir bin
"%CSC%" /nologo /codepage:65001 /optimize+ /target:winexe /win32manifest:src\app.manifest /out:bin\SC3U_HighTech_Industry.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll src\*.cs
endlocal
