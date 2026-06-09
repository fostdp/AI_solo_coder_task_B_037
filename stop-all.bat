@echo off
setlocal enabledelayedexpansion

echo ========================================
echo   5G Antenna Monitoring System Stopper
echo ========================================
echo.

set "BASE_DIR=%~dp0"
cd /d "%BASE_DIR%"

echo [1/5] Stopping Frontend...
docker-compose stop frontend
docker-compose rm -f frontend
echo [OK] Frontend stopped

echo [2/5] Stopping Backend...
docker-compose stop backend
docker-compose rm -f backend
echo [OK] Backend stopped

echo [3/5] Stopping Mosquitto MQTT Broker...
docker-compose stop mosquitto
docker-compose rm -f mosquitto
echo [OK] Mosquitto stopped

echo [4/5] Stopping InfluxDB...
docker-compose stop influxdb
docker-compose rm -f influxdb
echo [OK] InfluxDB stopped

echo [5/5] Stopping PostgreSQL...
docker-compose stop postgres
docker-compose rm -f postgres
echo [OK] PostgreSQL stopped

echo.
echo Stopping ECPRI Simulator...
taskkill /F /IM python.exe /T 2>nul
echo [OK] ECPRI Simulator stopped

echo.
echo ========================================
echo   All services stopped successfully!
echo ========================================
echo.
pause
