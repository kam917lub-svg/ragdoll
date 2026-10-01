@echo off
cd /d "%~dp0"
echo Installo FFmpeg (se manca) e le librerie Python...
where ffmpeg >nul 2>nul || winget install -e --id Gyan.FFmpeg
python -m pip install --upgrade librosa soundfile faster-whisper yt-dlp
echo.
echo Fatto. Se FFmpeg e' stato appena installato, chiudi e riapri il terminale.
pause
