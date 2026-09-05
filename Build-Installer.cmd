@echo off
call "%~dp0Build-Exe.cmd" %*
exit /b %ERRORLEVEL%
