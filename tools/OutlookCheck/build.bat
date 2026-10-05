@echo off
rem Windows標準のC#コンパイラでビルドする（.NET SDK・Visual Studio不要）。
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
"%CSC%" /nologo /target:exe /out:OutlookCheck.exe /r:Microsoft.CSharp.dll /r:System.dll /r:System.Core.dll OutlookCheck.cs
if errorlevel 1 (echo ビルド失敗 & exit /b 1)
echo ビルド成功: OutlookCheck.exe
