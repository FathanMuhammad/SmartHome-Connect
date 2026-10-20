@echo off
echo ==============================================
echo Building Smart Home Controller (.EXE) ...
echo ==============================================

dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish

echo.
echo ==============================================
echo SELESAI! File EXE tersimpan di folder publish:
echo C:\DISK D\Personal Project\SmartHome\publish\SmartHomeAcController.exe
echo ==============================================
pause
