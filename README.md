# 5G Massive MIMO 天线阵列监控系统

## 概述

本项目是一套完整的5G Massive MIMO天线阵列监控系统，实现了eCPRI协议数据采集、幅相校准、通道故障诊断、告警推送等全链路功能。系统采用模块化架构设计，支持200个基站、每基站64通道的大规模监控，数据上报间隔5分钟。

## 架构设计

### 系统架构图

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              前端 (Vue3)                                │
│  ┌─────────────┐  ┌────────────────┐  ┌─────────────┐  ┌────────────┐ │
│  │ 3D天线视图  │  │ 通道详情面板  │  │  告警中心   │  │  趋势图表  │ │
│  └─────────────┘  └────────────────┘  └─────────────┘  └────────────┘ │
│                           Nginx (Gzip压缩)                              │
└─────────────────────────────────────┬───────────────────────────────────┘
                                      │ REST API / WebSocket
┌─────────────────────────────────────▼───────────────────────────────────┐
│                           C# 后端服务 (ASP.NET Core)                    │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐            │
│  │ ecpri_ingestor │  │calibration_eng │  │health_diagnoser│            │
│  │  数据采集模块  │  │   校准引擎     │  │  故障诊断引擎  │            │
│  └────────┬───────┘  └────────┬───────┘  └────────┬───────┘            │
│           │                   │                   │                    │
│           └───────────────────┼───────────────────┘                    │
│                               │                                        │
│                 ┌─────────────▼─────────────┐                          │
│                 │   MediatR / Channel       │                          │
│                 │    进程内消息总线         │                          │
│                 └─────────────┬─────────────┘                          │
│                               │                                        │
│                 ┌─────────────▼─────────────┐  ┌────────────────────┐  │
│                 │   alarm_forwarder         │  │ Serilog / Prometheus│  │
│                 │   告警推送模块            │  │  日志/指标         │  │
│                 └─────────────┬─────────────┘  └────────────────────┘  │
└───────────────────────────────┼─────────────────────────────────────────┘
                                │
        ┌───────────────────────┼───────────────────────┐
        │                       │                       │
┌───────▼───────┐    ┌──────────▼──────────┐    ┌──────▼───────┐
│  PostgreSQL   │    │     InfluxDB        │    │   Mosquitto   │
│  元数据存储   │    │   时序数据存储      │    │  MQTT Broker  │
│  (PostGIS)    │    │ (降采样/保留策略)   │    │               │
└───────────────┘    └─────────────────────┘    └──────────────┘
        ▲                       ▲                       ▲
        │                       │                       │
┌───────┴───────────────────────┴───────────────────────┴───────┐
│                    eCPRI 数据模拟器 (Python)                  │
│  200基站 × 64通道 | 5分钟间隔 | 幅相偏差/故障注入 | Prometheus │
└────────────────────────────────────────────────────────────────┘
```

### 核心模块说明

#### 1. ecpri_ingestor（数据采集模块）
- **职责**：负责eCPRI数据的采集、帧解析和协议转换
- **支持协议**：HTTP、TCP、MQTT
- **核心流程**：数据接收 → 帧解析 → 写InfluxDB → 发布MediatR事件 → 写入Channel
- **性能优化**：ArrayPool内存池、零拷贝解析

#### 2. calibration_engine（校准引擎）
- **职责**：幅相偏差计算、校准系数生成和应用
- **支持算法**：
  - 最小二乘法（Least Squares）：快速计算，适合稳态场景
  - 卡尔曼滤波（Kalman Filter）：自适应跟踪，适合动态场景
- **输出指标**：SLL（旁瓣抑制比）、校准系数矩阵

#### 3. health_diagnoser（故障诊断引擎）
- **职责**：通道健康评估、故障预测、异常检测
- **支持模型**：
  - LSTM神经网络：时序预测，适合长期趋势预测
  - 随机森林：特征重要性分析，适合故障根因定位
- **特征工程**：15维特征，包括幅值、相位、SWR、温度等

#### 4. alarm_forwarder（告警推送模块）
- **职责**：告警分级检查、MQTT推送、事件通知
- **告警级别**：
  - **一级告警**（Critical）：SWR > 2.0、温度 > 80°C、单通道故障
  - **二级告警**（Warning）：>10%通道故障、校准不收敛、SLL不达标

### 通信机制

#### MediatR 事件
| 事件 | 发布者 | 订阅者 | 说明 |
|------|--------|--------|------|
| `EcpriDataReceivedEvent` | EcpriIngestor | CalibrationEngine/HealthDiagnoser | eCPRI数据接收完成 |
| `CalibrationCompletedEvent` | CalibrationEngine | AlarmForwarder | 校准计算完成 |
| `DiagnosisCompletedEvent` | HealthDiagnoser | AlarmForwarder | 诊断计算完成 |
| `AlarmTriggeredEvent` | AlarmForwarder | MQTT Client | 告警触发 |

#### Channel 队列
- `EcpriDataChannel`：eCPRI原始数据队列
- `CalibrationRequestChannel`：校准请求队列
- `DiagnosisRequestChannel`：诊断请求队列
- `AlarmQueue`：告警消息队列

## 技术栈

### 后端
- **框架**：ASP.NET Core 8.0 / C# 12
- **ORM**：Entity Framework Core 8.0
- **消息总线**：MediatR 12.3
- **异步队列**：System.Threading.Channels
- **数据库**：PostgreSQL 16 + PostGIS
- **时序数据库**：InfluxDB 2.7 + Flux
- **日志**：Serilog + Console + File + MQTT
- **监控**：Prometheus + Grafana
- **MQTT客户端**：MQTTnet

### 前端
- **框架**：Vue 3 + TypeScript
- **3D渲染**：Three.js (WebGL 2.0)
- **图表**：Chart.js
- **状态管理**：Pinia
- **样式**：TailwindCSS

### 运维
- **容器化**：Docker + Docker Compose
- **反向代理**：Nginx
- **进程管理**：Systemd (可选)

## 部署指南

### 系统要求

- Docker ≥ 24.0
- Docker Compose ≥ 2.20
- 内存 ≥ 16GB
- 磁盘 ≥ 50GB SSD

### 快速部署

#### 1. 克隆仓库

```bash
git clone <repository-url>
cd 5g-antenna-monitoring
```

#### 2. 配置环境变量

编辑 `.env` 文件（已提供默认配置）：

```bash
# PostgreSQL
POSTGRES_USER=postgres
POSTGRES_PASSWORD=postgres_password
POSTGRES_DB=antenna_monitoring

# InfluxDB
DOCKER_INFLUXDB_INIT_ORG=5g-operator
DOCKER_INFLUXDB_INIT_ADMIN_TOKEN=5g-antenna-monitoring-token-2024
DOCKER_INFLUXDB_INIT_BUCKET=antenna_metrics_raw

# MQTT
MQTT_USER=antenna_admin
MQTT_PASSWORD=mqtt_password_2024

# 模拟器
SIM_STATION_COUNT=200
SIM_CHANNEL_COUNT=64
SIM_INTERVAL=300
SIM_PROTOCOL=http
```

#### 3. 启动服务

```bash
# 启动所有服务
docker-compose up -d

# 查看服务状态
docker-compose ps

# 查看日志
docker-compose logs -f backend
```

#### 4. 验证部署

```bash
# 运行健康检查脚本
./scripts/health-check-all.ps1
```

#### 5. 访问服务

| 服务 | 地址 | 用户名/密码 |
|------|------|------------|
| 前端 | http://localhost:5173 | - |
| 后端API | http://localhost:5000/swagger | - |
| Prometheus | http://localhost:9090 | - |
| Grafana | http://localhost:3000 | admin / admin |
| InfluxDB UI | http://localhost:8086 | - |

### 服务端口映射

| 服务 | 容器端口 | 主机端口 | 说明 |
|------|----------|----------|------|
| postgres | 5432 | 5432 | PostgreSQL数据库 |
| influxdb | 8086 | 8086 | InfluxDB时序数据库 |
| mosquitto | 1883 | 1883 | MQTT Broker |
| backend | 5000 | 5000 | ASP.NET Core后端 |
| frontend | 80 | 5173 | Vue前端 (Nginx) |
| ecpri-simulator | 8000 | 8000 | eCPRI模拟器指标 |
| prometheus | 9090 | 9090 | Prometheus监控 |
| grafana | 3000 | 3000 | Grafana可视化 |

### 常用运维命令

```bash
# 停止所有服务
docker-compose down

# 重启特定服务
docker-compose restart backend

# 查看服务日志
docker-compose logs -f ecpri-simulator

# 进入容器
docker-compose exec influxdb influx

# 清理所有数据（谨慎使用）
docker-compose down -v

# 更新镜像并重启
docker-compose pull
docker-compose up -d
```

## InfluxDB 数据分层

### Bucket 设计

| Bucket | 保留策略 | 用途 |
|--------|----------|------|
| `antenna_metrics_raw` | 7天 | 原始数据，1秒精度 |
| `antenna_metrics_1h` | 30天 | 1小时聚合，用于中期趋势分析 |
| `antenna_metrics_24h` | 365天 | 24小时聚合，用于长期报表 |
| `antenna_calibration` | 365天 | 校准结果数据 |
| `antenna_diagnosis` | 365天 | 诊断结果数据 |

### 降采样任务 (Flux Tasks)

1. **downsample_to_1h**（每小时执行）
   - 从 `antenna_metrics_raw` 聚合到 `antenna_metrics_1h`
   - 聚合函数：mean()

2. **downsample_to_24h**（每天执行）
   - 从 `antenna_metrics_1h` 聚合到 `antenna_metrics_24h`
   - 聚合函数：mean()

3. **cleanup_old_data**（每天执行）
   - 清理7天以上的原始数据
   - 清理30天以上的1小时聚合数据

### 自动创建

启动时通过 `database/influxdb/init.sh` 脚本自动创建Bucket和Tasks。

## eCPRI 模拟器使用指南

### 功能特性

- ✅ 支持 1-1000 个基站模拟
- ✅ 每基站支持 8-256 通道（建议为8的倍数）
- ✅ 支持 HTTP/TCP/MQTT 三种上报协议
- ✅ 可配置上报间隔（默认5分钟）
- ✅ 支持动态注入幅值偏差、相位偏差
- ✅ 支持动态注入通道故障、通道异常
- ✅ 暴露 Prometheus 指标
- ✅ 支持环境变量配置

### 环境变量配置

| 环境变量 | 默认值 | 说明 |
|----------|--------|------|
| `SIM_PROTOCOL` | http | 上报协议: http/tcp/mqtt |
| `SIM_API_BASE` | http://backend:5000 | HTTP API基础地址 |
| `SIM_TCP_HOST` | backend | TCP服务器地址 |
| `SIM_TCP_PORT` | 5001 | TCP服务器端口 |
| `SIM_MQTT_HOST` | mosquitto | MQTT服务器地址 |
| `SIM_MQTT_PORT` | 1883 | MQTT服务器端口 |
| `SIM_MQTT_USERNAME` | None | MQTT用户名 |
| `SIM_MQTT_PASSWORD` | None | MQTT密码 |
| `SIM_INTERVAL` | 300 | 上报间隔（秒） |
| `SIM_STATION_COUNT` | 200 | 基站数量 |
| `SIM_CHANNEL_COUNT` | 64 | 每基站通道数 |
| `SIM_ARRAY_ROWS` | 8 | 天线阵列行数 |
| `SIM_ARRAY_COLS` | 8 | 天线阵列列数 |
| `SIM_INJECT_ANOMALIES` | True | 初始化时注入异常 |
| `SIM_DYNAMIC_ANOMALIES` | True | 运行时动态注入异常 |
| `SIM_ANOMALY_INTERVAL` | 60 | 动态异常注入间隔（秒） |
| `SIM_AMP_BIAS_MIN` | -0.3 | 幅值偏差最小值 |
| `SIM_AMP_BIAS_MAX` | 0.3 | 幅值偏差最大值 |
| `SIM_PHASE_BIAS_MIN` | -0.5 | 相位偏差最小值（rad） |
| `SIM_PHASE_BIAS_MAX` | 0.5 | 相位偏差最大值（rad） |
| `SIM_THROTTLE` | 0.01 | 基站间发送延迟（秒） |
| `SIM_METRICS_PORT` | 8000 | Prometheus指标端口 |
| `SIM_ONCE` | False | 只发送一次后退出 |
| `SIM_VERBOSE` | False | 详细输出模式 |

### 命令行参数

```bash
# 查看帮助
docker-compose run --rm ecpri-simulator python ecpri_simulator.py --help

# 使用HTTP协议，200基站，5分钟间隔
docker-compose run --rm ecpri-simulator python ecpri_simulator.py \
  --protocol http \
  --station-count 200 \
  --interval 300

# 使用MQTT协议，只模拟单个基站
docker-compose run --rm ecpri-simulator python ecpri_simulator.py \
  --protocol mqtt \
  --station-index 0 \
  --mqtt-username antenna_admin \
  --mqtt-password mqtt_password_2024

# 详细输出模式，查看注入的异常
docker-compose run --rm ecpri-simulator python ecpri_simulator.py \
  --verbose \
  --dynamic-anomalies

# 只发送一次，用于测试
docker-compose run --rm ecpri-simulator python ecpri_simulator.py --once

# 自定义幅相偏差范围
docker-compose run --rm ecpri-simulator python ecpri_simulator.py \
  --amplitude-bias-min -0.5 \
  --amplitude-bias-max 0.5 \
  --phase-bias-min -1.0 \
  --phase-bias-max 1.0
```

### 数据格式

#### HTTP/JSON 格式

```json
{
  "version": "1.0",
  "messageType": "channel_metrics",
  "stationId": "station-0000",
  "stationCode": "BJ-5G-0000",
  "timestamp": 1717234800000,
  "sequenceNumber": 1,
  "channels": [
    {
      "channelIndex": 0,
      "rowIndex": 0,
      "columnIndex": 0,
      "amplitude": 0.987654,
      "phase": 0.012345,
      "swr": 1.1234,
      "paTemperature": 42.5,
      "txPower": 43.2,
      "rxPower": -55.3,
      "ber": 1e-7
    }
  ]
}
```

### 模拟器独立运行

```bash
# 不依赖docker-compose，单独运行模拟器
cd simulator
pip install -r requirements.txt

# 使用默认配置运行
python ecpri_simulator.py

# 指定后端地址
python ecpri_simulator.py --api-base http://192.168.1.100:5000
```

## 可观测性

### Serilog 日志配置

```csharp
builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithMachineName()
        .Enrich.WithProcessId()
        .Enrich.WithThreadId()
        .WriteTo.Console(
            theme: AnsiConsoleTheme.Code,
            outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.File(
            path: "logs/antenna-monitoring-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        )
        .WriteTo.MQTT(
            clientOptions: mqttOptions,
            topic: "5g/antenna/logs",
            restrictedToMinimumLevel: LogEventLevel.Warning
        );
});
```

#### 日志主题

- `5g/antenna/logs`：Warning+级别日志推送到MQTT

### Prometheus 指标

#### 自定义指标（后端）

| 指标名称 | 类型 | 标签 | 说明 |
|----------|------|------|------|
| `ecpri_packets_received_total` | Counter | protocol, station_code | 接收的eCPRI数据包总数 |
| `ecpri_packets_failed_total` | Counter | protocol, station_code | 处理失败的数据包总数 |
| `alarms_triggered_total` | Counter | severity, alarm_type | 触发的告警总数 |
| `alarms_active_total` | Gauge | severity | 当前活跃告警数 |
| `channels_health_ratio` | Gauge | station_id | 通道健康率（0-1） |
| `calibration_sll_before_db` | Gauge | station_id, algorithm | 校准前SLL（dB） |
| `calibration_sll_after_db` | Gauge | station_id, algorithm | 校准后SLL（dB） |
| `calibration_duration_seconds` | Histogram | algorithm | 校准耗时分布 |
| `diagnosis_duration_seconds` | Histogram | model_type | 诊断耗时分布 |
| `ecpri_processing_latency_ms` | Histogram | protocol | 数据处理延迟分布 |
| `diagnosis_avg_failure_probability` | Gauge | station_id, model_type | 平均故障概率 |

#### HTTP 指标

```csharp
app.UseHttpMetrics();  // HTTP请求持续时间、请求量等
```

#### 系统指标

```csharp
app.UseMetricServer();
```

### Prometheus 告警规则

详见 `monitoring/prometheus/rules/alerts.yml`

## 前端 Nginx Gzip 配置

```nginx
gzip on;
gzip_vary on;
gzip_min_length 1024;
gzip_comp_level 6;
gzip_types
    text/plain
    text/css
    text/xml
    text/javascript
    application/json
    application/javascript
    application/xml+rss
    application/xml
    application/xhtml+xml
    application/x-font-ttf
    application/x-font-opentype
    application/vnd.ms-fontobject
    image/svg+xml
    image/x-icon
    application/atom+xml
    application/rdf+xml
    application/wasm;
gzip_proxied any;
gzip_disable "msie6";
```

### 缓存策略

- 静态资源（JS/CSS/图片/字体）：缓存30天
- HTML入口：不缓存（每次请求最新）
- API响应：根据业务需求配置

## API 文档

### eCPRI 数据上报

```http
POST /api/ecpri/data
Content-Type: application/json

{
  "version": "1.0",
  "messageType": "channel_metrics",
  "stationId": "station-0000",
  "stationCode": "BJ-5G-0000",
  "timestamp": 1717234800000,
  "sequenceNumber": 1,
  "channels": [...]
}

Response:
{
  "success": true,
  "message": "Data received and processed"
}
```

### 校准请求

```http
POST /api/calibration/execute
Content-Type: application/json

{
  "stationId": "station-0000",
  "algorithmType": "KalmanFilter"
}

Response:
{
  "success": true,
  "sllBefore": -18.5,
  "sllAfter": -25.3,
  "converged": true
}
```

### 诊断请求

```http
POST /api/diagnosis/analyze
Content-Type: application/json

{
  "stationId": "station-0000",
  "modelType": "LSTM",
  "channelIndex": 0
}

Response:
{
  "channelId": "channel-0000",
  "failureProbability": 0.023,
  "healthScore": 0.95,
  "predictedFailureHours": 720,
  "anomalyScore": 0.12
}
```

### 健康检查

```http
GET /health

Response: "Healthy"
```

### Prometheus 指标

```http
GET /metrics

# HELP ecpri_packets_received_total Total eCPRI packets
# TYPE ecpri_packets_received_total counter
ecpri_packets_received_total{protocol="http",station_code="BJ-5G-0000"} 42
...
```

## 性能指标

### 处理能力

| 指标 | 数值 |
|------|------|
| 单基站数据处理延迟 | < 50ms |
| 200基站处理周期 | < 10s |
| 单包处理内存 | < 10KB |
| InfluxDB写入吞吐 | > 10,000 points/s |

### 存储估算（200基站×64通道）

| 数据类型 | 日写入量 | 月存储量 | 年存储量 |
|----------|----------|----------|----------|
| 原始数据 | ~110GB | ~3.3TB | -（保留7天） |
| 1小时聚合 | ~4.5GB | ~135GB | ~1.6TB |
| 24小时聚合 | ~188MB | ~5.6GB | ~68GB |
| 校准结果 | ~50MB | ~1.5GB | ~18GB |
| 诊断结果 | ~100MB | ~3GB | ~36GB |
| **合计** | ~115GB | **~145GB** | **~1.7TB** |

## 开发指南

### 项目结构

```
5g-antenna-monitoring/
├── backend/                          # C# 后端
│   ├── src/Backend/
│   │   ├── Modules/                  # 业务模块
│   │   │   ├── EcpriIngestor/        # eCPRI数据采集
│   │   │   ├── CalibrationEngine/    # 校准引擎
│   │   │   ├── HealthDiagnoser/      # 故障诊断
│   │   │   └── AlarmForwarder/       # 告警推送
│   │   ├── Messages/                 # MediatR消息契约
│   │   ├── Extensions/               # DI扩展方法
│   │   ├── Models/                   # 数据模型
│   │   ├── Repositories/             # 数据访问
│   │   └── Controllers/              # API控制器
│   └── Dockerfile
├── frontend/                         # Vue3 前端
│   ├── src/
│   │   ├── utils/
│   │   │   ├── array_3d_viewer.js    # 3D天线视图
│   │   │   └── channel_detail.js     # 通道详情
│   │   └── ...
│   ├── nginx.conf                    # Nginx配置
│   └── Dockerfile
├── simulator/                        # eCPRI模拟器 (Python)
│   ├── ecpri_simulator.py
│   ├── requirements.txt
│   └── Dockerfile
├── database/
│   ├── postgres/                     # PostgreSQL初始化脚本
│   └── influxdb/                     # InfluxDB初始化
│       ├── init.sh
│       └── tasks/                    # Flux降采样任务
├── monitoring/
│   ├── prometheus/                   # Prometheus配置
│   │   ├── prometheus.yml
│   │   └── rules/
│   └── grafana/                      # Grafana配置
│       ├── datasources.yml
│       ├── dashboards.yml
│       └── dashboards/
├── scripts/                          # 运维脚本
│   ├── wait-for-services.sh
│   └── health-check-all.ps1
├── docker-compose.yml                # 服务编排
├── .env                              # 环境变量
└── README.md
```

### 本地开发

#### 后端开发

```bash
cd backend/src/Backend
dotnet restore
dotnet build

# 运行（需要先启动PostgreSQL、InfluxDB、MQTT）
dotnet run
```

#### 前端开发

```bash
cd frontend
npm install
npm run dev
```

#### 模拟器开发

```bash
cd simulator
pip install -r requirements.txt
python ecpri_simulator.py --once
```

### 运行测试

```bash
# 后端单元测试
cd backend
dotnet test

# 端到端测试
cd tests
dotnet run --project E2ETests
```

## 故障排查

### 常见问题

**Q: 后端服务启动失败，提示数据库连接失败**
- 检查 `.env` 中的数据库配置
- 确认 PostgreSQL 容器是否正常运行: `docker-compose ps postgres`
- 查看 PostgreSQL 日志: `docker-compose logs postgres`

**Q: eCPRI模拟器无法连接到后端**
- 检查后端容器是否健康: `docker-compose ps backend`
- 确认协议和地址配置: `SIM_PROTOCOL`, `SIM_API_BASE`
- 查看模拟器日志: `docker-compose logs ecpri-simulator`

**Q: InfluxDB 写入失败**
- 检查 InfluxDB Token 是否正确
- 确认 Bucket 是否已创建
- 查看后端日志中的 InfluxDB 错误

**Q: Prometheus 无法抓取指标**
- 检查 Prometheus 配置文件
- 确认目标服务的 `/metrics` 端点可访问
- 查看 Prometheus UI 中的 Targets 页面

### 日志排查

```bash
# 查看所有服务日志
docker-compose logs -f

# 只看错误日志
docker-compose logs backend | grep -i error

# 查看最近100行
docker-compose logs --tail=100 ecpri-simulator
```

## 安全建议

1. **修改默认密码**：更改 `.env` 中的所有默认密码
2. **启用HTTPS**：在生产环境使用 Let's Encrypt 配置 SSL
3. **网络隔离**：使用 Docker 网络隔离数据库和内部服务
4. **访问控制**：配置 MQTT 用户名密码认证
5. **API鉴权**：在生产环境启用 JWT 或 OAuth2.0 认证
6. **日志脱敏**：确保日志中不包含敏感信息
7. **定期更新**：及时更新基础镜像和依赖包

## License

MIT License

## 贡献

欢迎提交 Issue 和 Pull Request。

## 联系方式

- 项目地址：[GitHub Repository]
- 问题反馈：[Issues]
