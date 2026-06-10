#!/bin/bash
# 等待依赖服务就绪脚本

set -e

echo "============================================"
echo "等待依赖服务就绪..."
echo "============================================"

# 等待PostgreSQL
echo "等待PostgreSQL..."
until PGPASSWORD=${POSTGRES_PASSWORD:-postgres} psql -h postgres -U ${POSTGRES_USER:-postgres} -d ${POSTGRES_DB:-antenna_monitoring} -c "SELECT 1" > /dev/null 2>&1; do
    echo "  PostgreSQL 未就绪，等待中..."
    sleep 2
done
echo "  ✓ PostgreSQL 已就绪"

# 等待InfluxDB
echo "等待InfluxDB..."
until curl -f http://influxdb:8086/health > /dev/null 2>&1; do
    echo "  InfluxDB 未就绪，等待中..."
    sleep 2
done
echo "  ✓ InfluxDB 已就绪"

# 等待MQTT
echo "等待MQTT Broker..."
until mosquitto_pub -h mosquitto -p 1883 -t 'health/check' -m 'ready' -u ${MQTT_USER:-antenna_admin} -P ${MQTT_PASSWORD:-mqtt_password_2024} > /dev/null 2>&1; do
    echo "  MQTT Broker 未就绪，等待中..."
    sleep 2
done
echo "  ✓ MQTT Broker 已就绪"

echo "============================================"
echo "所有依赖服务已就绪！"
echo "============================================"
