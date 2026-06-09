CREATE EXTENSION IF NOT EXISTS "uuid-ossp";
CREATE EXTENSION IF NOT EXISTS "postgis";

CREATE TABLE IF NOT EXISTS base_stations (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_name VARCHAR(100) NOT NULL,
    station_code VARCHAR(50) UNIQUE NOT NULL,
    address VARCHAR(200),
    longitude DECIMAL(12, 9) NOT NULL,
    latitude DECIMAL(12, 9) NOT NULL,
    altitude DECIMAL(8, 2),
    antenna_model VARCHAR(100),
    channel_count INT NOT NULL DEFAULT 64,
    array_rows INT NOT NULL DEFAULT 8,
    array_columns INT NOT NULL DEFAULT 8,
    frequency_band DECIMAL(10, 2),
    installation_date DATE,
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS channels (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    channel_index INT NOT NULL,
    row_index INT NOT NULL,
    column_index INT NOT NULL,
    tx_power DECIMAL(8, 4),
    nominal_amplitude DECIMAL(8, 4) NOT NULL DEFAULT 1.0,
    nominal_phase DECIMAL(8, 4) NOT NULL DEFAULT 0.0,
    calibration_coeff_amplitude DECIMAL(10, 6) DEFAULT 1.0,
    calibration_coeff_phase DECIMAL(10, 6) DEFAULT 0.0,
    last_calibration_time TIMESTAMP,
    status VARCHAR(20) NOT NULL DEFAULT 'normal',
    failure_probability DECIMAL(6, 3) DEFAULT 0.0,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    UNIQUE(station_id, channel_index)
);

CREATE TABLE IF NOT EXISTS calibration_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    channel_id UUID NOT NULL REFERENCES channels(id) ON DELETE CASCADE,
    calibration_time TIMESTAMP NOT NULL,
    amplitude_deviation DECIMAL(10, 6),
    phase_deviation DECIMAL(10, 6),
    calibration_coeff_amplitude DECIMAL(10, 6),
    calibration_coeff_phase DECIMAL(10, 6),
    sll_before DECIMAL(8, 4),
    sll_after DECIMAL(8, 4),
    algorithm VARCHAR(50),
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS alarms (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    alarm_code VARCHAR(50) NOT NULL,
    alarm_type VARCHAR(20) NOT NULL,
    alarm_level VARCHAR(20) NOT NULL,
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    channel_id UUID REFERENCES channels(id) ON DELETE CASCADE,
    title VARCHAR(200) NOT NULL,
    description TEXT,
    threshold_value DECIMAL(10, 4),
    actual_value DECIMAL(10, 4),
    status VARCHAR(20) NOT NULL DEFAULT 'active',
    acknowledged BOOLEAN NOT NULL DEFAULT FALSE,
    acknowledged_by VARCHAR(100),
    acknowledged_at TIMESTAMP,
    cleared_at TIMESTAMP,
    mqtt_published BOOLEAN NOT NULL DEFAULT FALSE,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS diagnosis_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    channel_id UUID NOT NULL REFERENCES channels(id) ON DELETE CASCADE,
    diagnosis_time TIMESTAMP NOT NULL,
    swr_value DECIMAL(10, 6),
    temperature_value DECIMAL(10, 6),
    failure_probability DECIMAL(6, 3),
    model_type VARCHAR(50),
    prediction_horizon_hours INT,
    recommendation TEXT,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS system_config (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    config_key VARCHAR(100) UNIQUE NOT NULL,
    config_value TEXT,
    description VARCHAR(500),
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_channels_station_id ON channels(station_id);
CREATE INDEX IF NOT EXISTS idx_channels_status ON channels(status);
CREATE INDEX IF NOT EXISTS idx_alarms_station_id ON alarms(station_id);
CREATE INDEX IF NOT EXISTS idx_alarms_level ON alarms(alarm_level);
CREATE INDEX IF NOT EXISTS idx_alarms_status ON alarms(status);
CREATE INDEX IF NOT EXISTS idx_calibration_station ON calibration_records(station_id, calibration_time DESC);
CREATE INDEX IF NOT EXISTS idx_diagnosis_channel ON diagnosis_records(channel_id, diagnosis_time DESC);
CREATE INDEX IF NOT EXISTS idx_base_stations_location ON base_stations USING GIST(
    ST_SetSRID(ST_MakePoint(longitude, latitude), 4326)
);

INSERT INTO system_config (config_key, config_value, description) VALUES
('swr_warning_threshold', '1.5', '驻波比预警阈值'),
('swr_alarm_threshold', '2.0', '驻波比告警阈值'),
('sll_required', '-20.0', '旁瓣抑制比要求(dB)'),
('failure_probability_threshold', '0.7', '故障概率预警阈值'),
('sector_failure_channel_ratio', '0.1', '扇区失效通道比例阈值'),
('calibration_interval_minutes', '5', '校准间隔(分钟)'),
('diagnosis_interval_minutes', '5', '诊断间隔(分钟)'),
('mqtt_broker', 'localhost:1883', 'MQTT Broker地址'),
('mqtt_topic_alarm', '5g/antenna/alarm', '告警MQTT主题'),
('mqtt_topic_calibration', '5g/antenna/calibration', '校准MQTT主题');

INSERT INTO base_stations (station_name, station_code, address, longitude, latitude, altitude, antenna_model, channel_count, array_rows, array_columns, frequency_band, status) VALUES
('CBD中心基站', 'BJ-CBD-001', '北京市朝阳区建国路88号', 116.4551, 39.9049, 350.5, 'Huawei-Antenna-M64', 64, 8, 8, 3.5, 'active'),
('金融街基站', 'BJ-FIN-002', '北京市西城区金融街15号', 116.3559, 39.9139, 320.0, 'Huawei-Antenna-M64', 64, 8, 8, 3.5, 'active'),
('中关村基站', 'BJ-ZGC-003', '北京市海淀区中关村大街1号', 116.3058, 39.9842, 290.0, 'Huawei-Antenna-M64', 64, 8, 8, 3.5, 'active'),
('望京基站', 'BJ-WJ-004', '北京市朝阳区望京街9号', 116.4701, 39.9867, 280.0, 'Huawei-Antenna-M64', 64, 8, 8, 3.5, 'active'),
('国贸基站', 'BJ-GM-005', '北京市朝阳区国贸三期', 116.4621, 39.9088, 330.0, 'Huawei-Antenna-M64', 64, 8, 8, 3.5, 'active');

INSERT INTO channels (station_id, channel_index, row_index, column_index, tx_power, nominal_amplitude, nominal_phase)
SELECT 
    bs.id,
    (r - 1) * 8 + (c - 1),
    r - 1,
    c - 1,
    43.0 + (random() * 2.0 - 1.0),
    1.0,
    0.0
FROM base_stations bs
CROSS JOIN generate_series(1, 8) r
CROSS JOIN generate_series(1, 8) c;
