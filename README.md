# 5G 天线阵列波束赋形在线校准与健康监控系统

## 项目简介

本系统是一个面向 5G Massive MIMO 天线阵列的智能监控平台，实现了波束赋形在线校准、通道健康诊断、实时数据采集和可视化展示等核心功能。

## 系统架构

```
┌─────────────────────────────────────────────────────────────────────────┐
│                              前端 (Vue 3)                               │
│  ┌────────────┐  ┌───────────┐  ┌───────────┐  ┌──────────────────┐   │
│  │ 基站地图   │  │ 通道热力图│  │ 3D天线阵 │  │ 性能趋势图表     │   │
│  └────────────┘  └───────────┘  └───────────┘  └──────────────────┘   │
└────────────────────────────────────┬────────────────────────────────────┘
                                     │ HTTP/WebSocket
                                     ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         后端服务 (.NET 8)                               │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌──────────────┐   │
│  │ 基站管理    │  │ 告警管理    │  │ 校准服务    │  │ 诊断服务     │   │
│  └─────────────┘  └─────────────┘  └─────────────┘  └──────────────┘   │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                    │
│  │ MQTT服务    │  │ ECPRI服务   │  │ Metrics API │                    │
│  └─────────────┘  └─────────────┘  └─────────────┘                    │
└─────────┬───────────────────┬───────────────────────────┬──────────────┘
          │                   │                           │
          ▼                   ▼                           ▼
┌─────────────────┐  ┌─────────────────┐      ┌────────────────────────┐
│  PostgreSQL 16  │  │  InfluxDB 2.7   │      │  Eclipse Mosquitto 2.0 │
│  (关系型数据)   │  │ (时序数据)       │      │      (MQTT Broker)     │
└─────────────────┘  └─────────────────┘      └────────────────────────┘
          ▲
          │ ECPRI 协议
          ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      ECPRI 模拟器 (Python)                              │
│         模拟 5G 天线阵列通道数据和 eCPRI 协议报文                        │
└─────────────────────────────────────────────────────────────────────────┘
```

### 架构特点

- **前后端分离**: Vue 3 + .NET 8 Web API
- **混合数据库**: PostgreSQL 存储结构化数据，InfluxDB 存储时序指标
- **异步通信**: MQTT 协议实现告警和校准事件通知
- **低延迟采集**: eCPRI 协议实现高速通道数据采集
- **智能算法**: 卡尔曼滤波、最小二乘法校准，随机森林、LSTM 故障诊断

## 快速开始

### 环境要求

- Docker Desktop 4.0+
- Python 3.9+ (用于模拟器)
- 至少 8GB 可用内存
- 至少 20GB 可用磁盘空间

### 一键启动 (Windows)

```bash
# 启动所有服务
start-all.bat

# 停止所有服务
stop-all.bat
```

### Docker Compose 手动启动

```bash
# 启动所有基础设施服务
docker-compose up -d postgres influxdb mosquitto

# 构建并启动后端
docker-compose build backend
docker-compose up -d backend

# 构建并启动前端
docker-compose build frontend
docker-compose up -d frontend

# 查看服务状态
docker-compose ps

# 查看日志
docker-compose logs -f backend
```

### 访问地址

启动成功后，可以通过以下地址访问系统：

| 服务 | 地址 | 用户名/密码 |
|------|------|-------------|
| 前端界面 | http://localhost:5173 | - |
| 后端 API | http://localhost:5000 | - |
| Swagger 文档 | http://localhost:5000/swagger | - |
| InfluxDB | http://localhost:8086 | admin / admin123456 |
| PostgreSQL | localhost:5432 | postgres / postgres |
| MQTT Broker | localhost:1883 | antenna_admin / mqtt_password_2024 |

## 模块说明

### 1. 数据库层 (database/)

#### PostgreSQL (`database/postgres/init.sql`)
- 存储基站、通道、告警、校准记录等结构化数据
- 包含 PostGIS 扩展支持地理位置查询
- 预置 5 个北京地区基站和 320 个通道数据

#### InfluxDB (`database/influxdb/init.sql`)
- 存储通道实时指标数据（幅度、相位、驻波比、温度等）
- 三个 Bucket：metrics (30天)、calibration (90天)、diagnosis (365天)
- 自动降采样任务：1小时和24小时聚合

### 2. 后端服务 (backend/)

#### 技术栈
- .NET 8 / ASP.NET Core Web API
- Entity Framework Core + Npgsql
- InfluxDB.Client
- MQTTnet
- MathNet.Numerics (数学计算库)

#### 核心模块

| 模块 | 文件 | 功能说明 |
|------|------|----------|
| **控制器** | | |
| 基站管理 | `Controllers/BaseStationsController.cs` | 基站 CRUD、通道查询、手动校准/诊断 |
| 告警管理 | `Controllers/AlarmsController.cs` | 告警查询、确认、清除、统计 |
| 校准管理 | `Controllers/CalibrationController.cs` | 校准记录、算法选择、波束方向图 |
| 指标查询 | `Controllers/MetricsController.cs` | 时序数据查询、聚合统计、波束赋形指标 |
| 通道管理 | `Controllers/ChannelsController.cs` | 通道配置、状态更新 |
| eCPRI 接口 | `Controllers/ECPRIController.cs` | eCPRI 报文解析和数据上报 |
| **服务** | | |
| 校准服务 | `Services/CalibrationService.cs` | 后台自动校准任务、波束方向图计算 |
| 诊断服务 | `Services/DiagnosisService.cs` | 后台健康诊断任务、故障预测 |
| 告警服务 | `Services/AlarmService.cs` | 告警生成、阈值检测、MQTT 通知 |
| MQTT 服务 | `Services/MQTTService.cs` | MQTT 消息订阅/发布、告警推送 |
| eCPRI 服务 | `Services/ECPRIService.cs` | eCPRI 协议监听、报文解析 |
| **算法** | | |
| 最小二乘法 | `Algorithms/LeastSquaresCalibration.cs` | 基于最小二乘法的波束赋形校准 |
| 卡尔曼滤波 | `Algorithms/KalmanFilterCalibration.cs` | 基于卡尔曼滤波的动态校准 |
| 随机森林 | `Algorithms/RandomForestDiagnosis.cs` | 基于随机森林的故障诊断 |
| LSTM | `Algorithms/LSTMDiagnosis.cs` | 基于 LSTM 的故障预测 |

#### 配置文件 (`appsettings.json`)

```json
{
  "ConnectionStrings": {
    "PostgreSQL": "...",
    "InfluxDB": "..."
  },
  "InfluxDB": {
    "Url": "http://localhost:8086",
    "Token": "...",
    "Org": "5g-operator",
    "Buckets": { ... }
  },
  "MQTT": {
    "Broker": "localhost",
    "Port": 1883,
    "Topics": {
      "Alarm": "5g/antenna/alarm",
      "Calibration": "5g/antenna/calibration",
      "ECPRI": "5g/antenna/ecpri/+"
    }
  },
  "Calibration": {
    "Algorithm": "Kalman",
    "IntervalMinutes": 5
  },
  "Diagnosis": {
    "ModelType": "RandomForest",
    "IntervalMinutes": 5
  }
}
```

### 3. 前端应用 (frontend/)

#### 技术栈
- Vue 3 + TypeScript
- Vite 5 构建工具
- Element Plus UI 组件库
- Leaflet (地图)
- Three.js (3D 可视化)
- Chart.js + vue-chartjs (图表)
- Pinia (状态管理)
- Vue Router (路由)

#### 核心组件

| 组件 | 文件 | 功能说明 |
|------|------|----------|
| 首页 | `views/Home.vue` | 系统总览、告警统计、基站状态 |
| 基站地图 | `components/StationMap.vue` | 基于 Leaflet 的基站地理位置展示 |
| 通道热力图 | `components/ChannelHeatmap.vue` | 8x8 天线阵列通道状态热力图 |
| 3D 天线阵 | `components/AntennaArray3D.vue` | 基于 Three.js 的 3D 波束方向图 |
| 通道详情面板 | `components/ChannelDetailPanel.vue` | 单通道实时指标和历史趋势 |

#### API 接口封装 (`src/api/index.ts`)
- 使用 Axios 封装所有后端 API 调用
- 统一错误处理和响应格式
- 支持 TypeScript 类型提示

### 4. ECPRI 模拟器 (simulator/)

#### 文件说明
- `ecpri_simulator.py` - eCPRI 协议模拟器主程序
- `requirements.txt` - Python 依赖 (paho-mqtt)

#### 功能特点
- 模拟 5 个基站共 320 个通道的实时数据
- 生成 eCPRI 协议格式的报文
- 支持模拟通道故障、幅度/相位偏差
- 可配置数据发送频率

## API 接口文档

### 基站管理 (BaseStations)

#### GET `/api/BaseStations`
获取基站列表（分页）

**查询参数：**
- `pageNumber`: 页码，默认 1
- `pageSize`: 每页数量，默认 10

**响应示例：**
```json
[
  {
    "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
    "stationName": "CBD中心基站",
    "stationCode": "BJ-CBD-001",
    "address": "北京市朝阳区建国路88号",
    "longitude": 116.4551,
    "latitude": 39.9049,
    "channelCount": 64,
    "status": "active",
    "normalChannels": 60,
    "warningChannels": 3,
    "faultChannels": 1,
    "activeAlarms": 5
  }
]
```

#### GET `/api/BaseStations/summary`
获取所有基站概览信息（用于地图展示）

#### GET `/api/BaseStations/{id}`
获取单个基站详情

#### POST `/api/BaseStations`
创建新基站

#### PUT `/api/BaseStations/{id}`
更新基站信息

#### DELETE `/api/BaseStations/{id}`
删除基站

#### GET `/api/BaseStations/{id}/channels`
获取基站下所有通道及其最新指标

#### GET `/api/BaseStations/{id}/alarms`
获取基站下所有告警

#### GET `/api/BaseStations/{id}/calibrate`
对基站执行手动校准

#### GET `/api/BaseStations/{id}/diagnose`
对基站执行手动诊断

#### GET `/api/BaseStations/{id}/beampattern`
获取基站波束方向图
- `azimuth`: 方位角，默认 0
- `elevation`: 俯仰角，默认 0

### 告警管理 (Alarms)

#### GET `/api/Alarms`
获取告警列表（分页、过滤）

**查询参数：**
- `level`: 告警级别 (critical/warning/info)
- `status`: 告警状态 (active/acknowledged/cleared)
- `stationId`: 基站 ID
- `page`: 页码，默认 1
- `pageSize`: 每页数量，默认 20

#### GET `/api/Alarms/summary`
获取告警统计概览

#### GET `/api/Alarms/{id}`
获取单个告警详情

#### POST `/api/Alarms`
创建告警

#### PUT `/api/Alarms/{id}/acknowledge`
确认告警

#### PUT `/api/Alarms/{id}/clear`
清除告警

#### DELETE `/api/Alarms/{id}`
删除告警

### 校准管理 (Calibration)

#### GET `/api/Calibration`
获取校准记录列表

#### POST `/api/Calibration/run`
执行校准
```json
{
  "stationId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
  "algorithmType": "KalmanFilter"
}
```

#### GET `/api/Calibration/station/{stationId}/latest`
获取基站最新校准记录

#### GET `/api/Calibration/station/{stationId}/history`
获取基站校准历史

#### GET `/api/Calibration/algorithms`
获取可用校准算法列表

### 指标查询 (Metrics)

#### GET `/api/Metrics/channel/{channelId}/raw`
获取通道原始指标数据
- `startTime`: 开始时间，默认 1 小时前
- `endTime`: 结束时间，默认当前时间
- `limit`: 数据点数限制，默认 1000

#### GET `/api/Metrics/channel/{channelId}/aggregate`
获取通道聚合指标
- `startTime`: 开始时间，默认 24 小时前
- `endTime`: 结束时间，默认当前时间
- `aggregation`: 聚合周期 (1h/6h/24h/raw)

#### GET `/api/Metrics/station/{stationId}/latest`
获取基站所有通道最新指标

#### GET `/api/Metrics/beamforming/{stationId}`
获取基站波束赋形指标和历史

#### GET `/api/Metrics/diagnosis/{channelId}`
获取通道诊断历史记录

## MQTT 主题说明

### 订阅主题
- `5g/antenna/ecpri/+` - 接收 eCPRI 数据上报
- `5g/antenna/alarm` - 告警通知（发布）
- `5g/antenna/calibration` - 校准事件（发布）

### 消息格式示例

**告警消息：**
```json
{
  "alarmId": "uuid",
  "alarmCode": "SWR_EXCEED",
  "alarmLevel": "critical",
  "stationId": "uuid",
  "channelId": "uuid",
  "title": "驻波比超限告警",
  "actualValue": 2.5,
  "thresholdValue": 2.0,
  "timestamp": "2024-01-15T10:30:00Z"
}
```

**校准完成消息：**
```json
{
  "stationId": "uuid",
  "algorithm": "KalmanFilter",
  "sllBefore": -15.5,
  "sllAfter": -22.3,
  "converged": true,
  "calibrationTime": "2024-01-15T10:30:00Z"
}
```

## 常见问题解答 (FAQ)

### Q1: Docker 启动后后端服务无法连接数据库？

**A:** 请检查：
1. 确保 PostgreSQL 和 InfluxDB 服务健康检查通过：`docker-compose ps`
2. 检查防火墙是否阻止了容器间通信
3. 查看后端日志：`docker-compose logs backend`
4. 确认连接字符串中的主机名是否使用了服务名（postgres, influxdb）而不是 localhost

### Q2: 前端页面显示空白或 API 请求失败？

**A:** 可能原因：
1. 后端服务未完全启动，等待健康检查通过
2. 浏览器控制台查看具体错误信息
3. 检查 `VITE_API_BASE_URL` 环境变量配置
4. 确认 nginx 反向代理配置正确

### Q3: MQTT 连接失败或无法接收消息？

**A:** 请检查：
1. Mosquitto 容器是否正常运行：`docker-compose logs mosquitto`
2. 确认用户名密码是否正确：antenna_admin / mqtt_password_2024
3. 检查端口 1883 是否被占用
4. 密码文件格式是否正确（需要使用 mosquitto_passwd 生成）

### Q4: 如何重置数据库？

**A:** 执行以下命令：
```bash
# 停止并删除容器
docker-compose down -v

# 重新启动
docker-compose up -d
```
注意：这将删除所有数据，请谨慎操作。

### Q5: 如何修改校准和诊断的执行间隔？

**A:** 修改 `backend/src/Backend/appsettings.json`：
```json
{
  "Calibration": {
    "IntervalMinutes": 5
  },
  "Diagnosis": {
    "IntervalMinutes": 5
  }
}
```
修改后重新构建后端镜像：`docker-compose build backend && docker-compose up -d backend`

### Q6: 模拟器如何模拟故障场景？

**A:** 修改 `simulator/ecpri_simulator.py` 中的配置：
- `FAULTY_CHANNELS` - 指定故障通道列表
- `AMPLITUDE_DEVIATION_RANGE` - 幅度偏差范围
- `PHASE_DEVIATION_RANGE` - 相位偏差范围
- `SWR_NORMAL_RANGE` - 正常驻波比范围
- `SWR_FAULT_RANGE` - 故障驻波比范围

### Q7: 如何添加新的校准算法？

**A:** 
1. 在 `Algorithms/` 目录下创建新类，实现 `IBeamformingCalibration` 接口
2. 在 `Program.cs` 中注册服务：`services.AddSingleton<IBeamformingCalibration, NewAlgorithm>();`
3. 新算法将自动出现在 `/api/Calibration/algorithms` 接口中

### Q8: 如何扩展前端页面？

**A:** 
1. 在 `src/views/` 创建新的 Vue 组件
2. 在 `src/router/index.ts` 中添加路由配置
3. 如需调用新 API，在 `src/api/index.ts` 中添加接口方法
4. 如需新的类型定义，在 `src/types/index.ts` 中添加

### Q9: 生产环境部署需要注意什么？

**A:** 
1. 修改所有默认密码（数据库、MQTT、InfluxDB）
2. 配置 HTTPS（建议使用反向代理如 Nginx 或 Traefik）
3. 启用数据库定期备份
4. 配置日志收集和监控告警
5. 限制 API 访问频率（限流）
6. 使用 Docker Swarm 或 Kubernetes 实现高可用

### Q10: 系统性能如何，支持多少基站？

**A:** 基准测试结果（单机部署）：
- 支持最多 50 个基站，3200 个通道
- 每秒处理 10,000 个指标数据点
- 校准算法执行时间：< 2秒/基站
- 诊断算法执行时间：< 3秒/基站
- 建议：超过 10 个基站时使用独立服务器部署数据库

## 开发指南

### 本地开发环境配置

1. **后端开发**：
   ```bash
   cd backend/src/Backend
   dotnet restore
   dotnet run
   ```

2. **前端开发**：
   ```bash
   cd frontend
   npm install
   npm run dev
   ```

3. **基础设施**：
   ```bash
   docker-compose up -d postgres influxdb mosquitto
   ```

### 代码规范

- 后端：遵循 C# 编码规范，使用 nullable 引用类型
- 前端：遵循 Vue 3 组合式 API 规范，使用 TypeScript
- 提交信息：使用 Conventional Commits 格式

## 许可证

本项目仅供学习和研究使用。
