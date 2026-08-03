@echo off
rem Build ZoteroPdfCleaner.exe with the csc.exe bundled in .NET Framework.
rem No SDK required; .NET Framework 4.x is preinstalled on Windows 10/11.
setlocal
set "DIR=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo ERROR: csc.exe not found. Please install .NET Framework 4.x.
  exit /b 1
)
"%CSC%" /nologo /target:winexe /optimize+ /out:"%DIR%ZoteroPdfCleaner.exe" "%DIR%Scanner.cs" "%DIR%ZoteroPdfCleaner.cs"
if errorlevel 1 (
  echo Build failed.
  exit /b 1
)
echo Build OK: %DIR%ZoteroPdfCleaner.exe
