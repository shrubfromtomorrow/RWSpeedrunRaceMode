@echo off
SETLOCAL
title Rain World Dev Symlink Setup

net session >nul 2>&1
if %errorlevel% NEQ 0 (
    echo [ERROR] This script must be run as administrator.
    pause
    exit /b
)

for %%I in ("%~dp0.") do (
    set "repo_dir=%%~fI"
    set "repo_name=%%~nI"
)
SET "rw_dir=C:\Program Files (x86)\Steam\steamapps\common\Rain World\"

cd "%rw_dir%RainWorld_Data\StreamingAssets\mods" || (
    echo [ERROR] Failed to find mod directory in Rain World. Your install drive might not be your C drive. Update the rw_dir path in the batch script.
    exit /b
)
IF EXIST %repo_name% (
    echo Removing existing plugins folder or symlink...
    rmdir /S /Q %repo_name%
)
mklink /D %repo_name% "%repo_dir%\mod" || (
    echo [ERROR] Failed to create symbolic link. Skill issue really
    pause
    exit /b
)
echo.
echo =============================
echo All operations complete :)
echo =============================
pause
goto :eof