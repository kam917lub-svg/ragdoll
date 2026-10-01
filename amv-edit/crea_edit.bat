@echo off
cd /d "%~dp0"
python "%~dp0make_edit.py" "%~dp0." %*
pause
