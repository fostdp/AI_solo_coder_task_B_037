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

CREATE TABLE IF NOT EXISTS deformation_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    sensor_index INT NOT NULL,
    tilt_angle_x DECIMAL(12, 6),
    tilt_angle_y DECIMAL(12, 6),
    tilt_angle_z DECIMAL(12, 6),
    strain_value DECIMAL(15, 9),
    temperature DECIMAL(8, 2),
    calculated_displacement_mm DECIMAL(10, 6),
    stress_mpa DECIMAL(10, 4),
    deformation_zone VARCHAR(50),
    beam_correction_applied BOOLEAN DEFAULT FALSE,
    correction_angle_azimuth DECIMAL(10, 6),
    correction_angle_elevation DECIMAL(10, 6),
    wind_speed DECIMAL(8, 2),
    measurement_time TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS cosite_interference_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    interfering_operator VARCHAR(100),
    interfering_antenna_type VARCHAR(100),
    interfering_frequency_mhz DECIMAL(12, 3),
    interfering_power_dbm DECIMAL(10, 4),
    separation_distance_meters DECIMAL(10, 4),
    azimuth_angle_deg DECIMAL(10, 6),
    elevation_angle_deg DECIMAL(10, 6),
    isolation_db DECIMAL(10, 4),
    coupling_coefficient DECIMAL(15, 9),
    interference_margin_db DECIMAL(10, 4),
    is_isolation_sufficient BOOLEAN DEFAULT TRUE,
    recommendation TEXT,
    interference_vector_x DECIMAL(12, 6),
    interference_vector_y DECIMAL(12, 6),
    interference_vector_z DECIMAL(12, 6),
    affected_band_start_mhz DECIMAL(12, 3),
    affected_band_end_mhz DECIMAL(12, 3),
    measurement_time TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS pa_efficiency_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    channel_id UUID NOT NULL REFERENCES channels(id) ON DELETE CASCADE,
    channel_index INT NOT NULL,
    pa_temperature DECIMAL(8, 2),
    output_power_dbm DECIMAL(10, 4),
    input_power_dbm DECIMAL(10, 4),
    gain_db DECIMAL(8, 4),
    efficiency_percent DECIMAL(8, 4),
    power_added_efficiency_percent DECIMAL(8, 4),
    dc_current_a DECIMAL(10, 6),
    dc_voltage_v DECIMAL(8, 4),
    dc_power_w DECIMAL(10, 4),
    rf_power_w DECIMAL(10, 4),
    efficiency_decay_rate DECIMAL(12, 8),
    predicted_remaining_hours DECIMAL(12, 2),
    needs_replacement BOOLEAN DEFAULT FALSE,
    replacement_reason TEXT,
    efficiency_history DECIMAL(8, 4)[],
    history_timestamps TIMESTAMP[],
    measurement_time TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS spectrum_scan_records (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    start_frequency_mhz DECIMAL(12, 3),
    end_frequency_mhz DECIMAL(12, 3),
    resolution_bandwidth_khz DECIMAL(12, 3),
    frequency_points_mhz DECIMAL(12, 3)[],
    power_levels_dbm DECIMAL(10, 4)[],
    interference_count INT DEFAULT 0,
    interference_details TEXT,
    interference_frequencies_mhz DECIMAL(12, 3)[],
    interference_powers_dbm DECIMAL(10, 4)[],
    interference_directions_deg DECIMAL(10, 6)[],
    null_steering_applied BOOLEAN DEFAULT FALSE,
    null_angles_deg DECIMAL(10, 6)[],
    null_depths_db DECIMAL(8, 4)[],
    noise_floor_dbm DECIMAL(10, 4),
    spurious_free_dynamic_range_db DECIMAL(8, 4),
    scan_time TIMESTAMP NOT NULL,
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS co_site_antennas (
    id UUID PRIMARY KEY DEFAULT uuid_generate_v4(),
    station_id UUID NOT NULL REFERENCES base_stations(id) ON DELETE CASCADE,
    operator_name VARCHAR(100) NOT NULL,
    antenna_type VARCHAR(100),
    frequency_band_start_mhz DECIMAL(12, 3),
    frequency_band_end_mhz DECIMAL(12, 3),
    transmit_power_dbm DECIMAL(10, 4),
    separation_distance_meters DECIMAL(10, 4),
    azimuth_angle_deg DECIMAL(10, 6),
    elevation_angle_deg DECIMAL(10, 6),
    height_offset_meters DECIMAL(8, 2),
    status VARCHAR(20) DEFAULT 'active',
    created_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at TIMESTAMP NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS idx_deformation_station ON deformation_records(station_id, measurement_time DESC);
CREATE INDEX IF NOT EXISTS idx_deformation_zone ON deformation_records(deformation_zone);
CREATE INDEX IF NOT EXISTS idx_cosite_station ON cosite_interference_records(station_id, measurement_time DESC);
CREATE INDEX IF NOT EXISTS idx_cosite_isolation ON cosite_interference_records(is_isolation_sufficient);
CREATE INDEX IF NOT EXISTS idx_pa_efficiency_channel ON pa_efficiency_records(channel_id, measurement_time DESC);
CREATE INDEX IF NOT EXISTS idx_pa_efficiency_replace ON pa_efficiency_records(needs_replacement);
CREATE INDEX IF NOT EXISTS idx_spectrum_station ON spectrum_scan_records(station_id, scan_time DESC);
CREATE INDEX IF NOT EXISTS idx_spectrum_interference ON spectrum_scan_records(interference_count);
CREATE INDEX IF NOT EXISTS idx_co_site_antennas_station ON co_site_antennas(station_id);

INSERT INTO system_config (config_key, config_value, description) VALUES
('deformation_threshold_mm', '0.5', '天线阵面形变阈值(mm)'),
('deformation_interval_minutes', '5', '形变监测间隔(分钟)'),
('mem_sensor_count', '9', 'MEMS倾角传感器数量'),
('strain_gauge_count', '16', '应变片数量'),
('young_modulus_gpa', '70.0', '阵面材料杨氏模量(GPa)'),
('poisson_ratio', '0.33', '阵面材料泊松比'),
('plate_thickness_mm', '15.0', '阵面厚度(mm)'),
('isolation_threshold_db', '30.0', '共址隔离度阈值(dB)'),
('cosite_interference_interval_minutes', '10', '共址干扰分析间隔(分钟)'),
('pa_efficiency_threshold_percent', '40.0', '功放效率阈值(%)'),
('pa_efficiency_interval_minutes', '5', '功放效率评估间隔(分钟)'),
('pa_nominal_gain_db', '28.0', '功放标称增益(dB)'),
('pa_nominal_efficiency_percent', '45.0', '功放标称效率(%)'),
('spectrum_scan_interval_minutes', '15', '频谱扫描间隔(分钟)'),
('spectrum_start_mhz', '3400.0', '频谱扫描起始频率(MHz)'),
('spectrum_end_mhz', '3600.0', '频谱扫描终止频率(MHz)'),
('spectrum_rbw_khz', '100.0', '频谱扫描分辨率带宽(kHz)'),
('interference_power_threshold_dbm', '-80.0', '干扰功率阈值(dBm)'),
('null_depth_target_db', '25.0', '零陷目标深度(dB)'),
('max_null_count', '3', '最大零陷数量');

INSERT INTO co_site_antennas (station_id, operator_name, antenna_type, frequency_band_start_mhz, frequency_band_end_mhz, transmit_power_dbm, separation_distance_meters, azimuth_angle_deg, elevation_angle_deg, height_offset_meters, status)
SELECT bs.id, 
       CASE (random() * 3)::INT 
           WHEN 0 THEN '中国移动' 
           WHEN 1 THEN '中国联通' 
           ELSE '中国电信' 
       END,
       'Macro-Antenna-' || (random() * 3 + 1)::INT,
       1800 + (random() * 200),
       1900 + (random() * 200),
       43 + (random() * 5),
       2.0 + (random() * 3.0),
       random() * 360,
       random() * 20 - 5,
       random() * 2 - 1,
       'active'
FROM base_stations bs
CROSS JOIN generate_series(1, 2);
