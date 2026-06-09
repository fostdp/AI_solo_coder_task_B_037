@echo off
setlocal enabledelayedexpansion

echo ========================================
echo   5G Antenna Monitoring System Starter
echo ========================================
echo.

set "BASE_DIR=%~dp0"
cd /d "%BASE_DIR%"

echo [1/6] Starting PostgreSQL...
docker-compose up -d postgres
echo Waiting for PostgreSQL to be ready...
:wait_postgres
for /f "delims=" %%i in ('docker inspect --format="{{.State.Health.Status}}" 5g-antenna-postgres 2^>nul') do set "STATUS=%%i"
if /i not "!STATUS!"=="healthy" (
    timeout /t 5 >nul
    goto wait_postgres
)
echo [OK] PostgreSQL is running on port 5432
echo.

echo [2/6] Starting InfluxDB...
docker-compose up -d influxdb
echo Waiting for InfluxDB to be ready...
:wait_influxdb
for /f "delims=" %%i in ('docker inspect --format="{{.State.Health.Status}}" 5g-antenna-influxdb 2^>nul') do set "STATUS=%%i"
if /i not "!STATUS!"=="healthy" (
    timeout /t 5 >nul
    goto wait_influxdb
)
echo [OK] InfluxDB is running on port 8086
echo.

echo [3/6] Starting Mosquitto MQTT Broker...
docker-compose up -d mosquitto
echo Waiting for Mosquitto to be ready...
:wait_mosquitto
for /f "delims=" %%i in ('docker inspect --format="{{.State.Health.Status}}" 5g-antenna-mosquitto 2^>nul') do set "STATUS=%%i"
if /i not "!STATUS!"=="healthy" (
    timeout /t 5 >nul
    goto wait_mosquitto
)
echo [OK] Mosquitto is running on port 1883 (MQTT) and 9001 (WebSocket)
echo.

echo [4/6] Building and Starting Backend...
docker-compose build backend
docker-compose up -d backend
echo Waiting for Backend to be ready...
:wait_backend
for /f "delims=" %%i in ('docker inspect --format="{{.State.Health.Status}}" 5g-antenna-backend 2^>nul') do set "STATUS=%%i"
if /i not "!STATUS!"=="healthy" (
    timeout /t 10 >nul
    goto wait_backend
)
echo [OK] Backend is running on port 5000
echo.

echo [5/6] Building and Starting Frontend...
docker-compose build frontend
docker-compose up -d frontend
echo Waiting for Frontend to be ready...
:wait_frontend
for /f "delims=" %%i in ('docker inspect --format="{{.State.Health.Status}}" 5g-antenna-frontend 2^>nul') do set "STATUS=%%i"
if /i not "!STATUS!"=="healthy" (
    timeout /t 5 >nul
    goto wait_frontend
)
echo [OK] Frontend is running on port 5173
echo.

echo [6/6] Starting ECPRI Simulator...
cd /d "%BASE_DIR%\simulator"
if not exist "venv" (
    echo Creating Python virtual environment...
    python -m venv venv
    call venv\Scripts\activate.bat
    pip install -r requirements.txt
) else (
    call venv\Scripts\activate.bat
)

start "ECPRI Simulator" cmd /k "python ecpri_simulator.py"
echo [OK] ECPRI Simulator started
cd /d "%BASE_DIR%"
echo.

echo ========================================
echo   All services started successfully!
echo ========================================
echo.
echo Access URLs:
echo   - Frontend:     http://localhost:5173
echo   - Backend API:  http://localhost:5000
echo   - Swagger:      http://localhost:5000/swagger
echo   - InfluxDB:     http://localhost:8086
echo   - pgAdmin:      http://localhost:5050 (if configured)
echo.
echo MQTT Broker:
echo   - Host: localhost
echo   - Port: 1883 (MQTT), 9001 (WebSocket)
echo   - User: antenna_admin
echo   - Pass: mqtt_password_2024
echo.
echo To stop all services, run: stop-all.bat
echo ========================================
echo.
pause
