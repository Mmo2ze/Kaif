@echo off
cd /d "%~dp0"
rem StorePOS.exe starts StoreAPI automatically (port 5050 + LAN web app).
start "" /D "%~dp0" "%~dp0StorePOS.exe"
