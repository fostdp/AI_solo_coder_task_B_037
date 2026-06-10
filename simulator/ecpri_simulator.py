#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
5G eCPRI 数据模拟器
模拟N个5G Massive MIMO基站，每个基站64通道天线阵列
每5分钟通过HTTP/TCP/MQTT上报各通道的幅值、相位、驻波比、功放温度等数据
支持动态注入幅相偏差、通道故障
支持环境变量配置
暴露Prometheus指标
"""

import argparse
import json
import time
import random
import threading
import socket
import struct
import os
import sys
from datetime import datetime, timezone
from typing import List, Dict, Tuple, Optional
import urllib.request
import urllib.error
import math
from http.server import HTTPServer, BaseHTTPRequestHandler

import paho.mqtt.client as mqtt


# Prometheus指标
class Metrics:
    def __init__(self):
        self.packets_sent = 0
        self.packets_failed = 0
        self.stations_active = 0
        self.channels_faulty = 0
        self.channels_anomalous = 0
        self.last_sent_time = 0.0
        self._lock = threading.Lock()
    
    def inc_sent(self):
        with self._lock:
            self.packets_sent += 1
            self.last_sent_time = time.time()
    
    def inc_failed(self):
        with self._lock:
            self.packets_failed += 1
    
    def update_stats(self, stations, channels_faulty, channels_anomalous):
        with self._lock:
            self.stations_active = stations
            self.channels_faulty = channels_faulty
            self.channels_anomalous = channels_anomalous
    
    def to_prometheus(self) -> str:
        with self._lock:
            return f"""# HELP ecpri_packets_sent_total Total eCPRI packets sent
# TYPE ecpri_packets_sent_total counter
ecpri_packets_sent_total {self.packets_sent}
# HELP ecpri_packets_failed_total Total eCPRI packets failed
# TYPE ecpri_packets_failed_total counter
ecpri_packets_failed_total {self.packets_failed}
# HELP ecpri_stations_active Number of active stations
# TYPE ecpri_stations_active gauge
ecpri_stations_active {self.stations_active}
# HELP ecpri_channels_faulty Number of faulty channels
# TYPE ecpri_channels_faulty gauge
ecpri_channels_faulty {self.channels_faulty}
# HELP ecpri_channels_anomalous Number of anomalous channels
# TYPE ecpri_channels_anomalous gauge
ecpri_channels_anomalous {self.channels_anomalous}
# HELP ecpri_last_sent_timestamp Unix timestamp of last sent packet
# TYPE ecpri_last_sent_timestamp gauge
ecpri_last_sent_timestamp {self.last_sent_time}
"""

metrics = Metrics()


class MetricsHandler(BaseHTTPRequestHandler):
    def do_GET(self):
        if self.path == '/metrics':
            self.send_response(200)
            self.send_header('Content-Type', 'text/plain; version=0.0.4')
            self.end_headers()
            self.wfile.write(metrics.to_prometheus().encode('utf-8'))
        elif self.path == '/health':
            self.send_response(200)
            self.send_header('Content-Type', 'text/plain')
            self.end_headers()
            self.wfile.write(b'OK')
        else:
            self.send_response(404)
            self.end_headers()
    
    def log_message(self, format, *args):
        pass  # 静默日志


def env_bool(key: str, default: bool) -> bool:
    val = os.environ.get(key)
    if val is None:
        return default
    return val.lower() in ('1', 'true', 'yes', 'y', 'on')


def env_int(key: str, default: int) -> int:
    val = os.environ.get(key)
    if val is None or val == '':
        return default
    try:
        return int(val)
    except ValueError:
        return default


def env_float(key: str, default: float) -> float:
    val = os.environ.get(key)
    if val is None or val == '':
        return default
    try:
        return float(val)
    except ValueError:
        return default


class StationConfig:
    """基站配置"""
    def __init__(self, station_id: str, station_code: str, name: str, 
                 longitude: float, latitude: float,
                 channel_count: int = 64,
                 array_rows: int = 8,
                 array_cols: int = 8):
        self.station_id = station_id
        self.station_code = station_code
        self.name = name
        self.longitude = longitude
        self.latitude = latitude
        self.channel_count = channel_count
        self.array_rows = array_rows
        self.array_cols = array_cols
        self.faulty_channels = set()
        self.anomalous_channels = set()
        self.amplitude_bias = 0.0  # 全局幅相偏差
        self.phase_bias = 0.0


class ChannelData:
    """通道数据"""
    def __init__(self, channel_index: int, row: int, col: int):
        self.channel_index = channel_index
        self.row_index = row
        self.col_index = col
        self.amplitude = 0.0
        self.phase = 0.0
        self.swr = 0.0
        self.pa_temperature = 0.0
        self.tx_power = 0.0
        self.rx_power = 0.0
        self.ber = 0.0


class ECPRISimulator:
    """eCPRI数据模拟器"""
    
    def __init__(self, config: argparse.Namespace):
        self.config = config
        self.stations: List[StationConfig] = []
        self.sequence_number = 0
        self.stop_event = threading.Event()
        self.mqtt_client = None
        self.anomaly_injection_thread = None
        
        self._init_stations()
        self._init_protocol()
        self._start_metrics_server()
        self._start_anomaly_injection()
    
    def _init_stations(self):
        """初始化基站配置，分布在北京核心城区"""
        print(f"初始化 {self.config.station_count} 个基站，每基站 {self.config.channel_count} 通道...")
        
        center_lat = 39.9042
        center_lon = 116.4074
        
        array_rows = self.config.array_rows
        array_cols = self.config.array_cols
        channel_count = self.config.channel_count
        
        for i in range(self.config.station_count):
            angle = (i / self.config.station_count) * 2 * math.pi
            radius = 0.02 + (i % 5) * 0.015
            
            lat = center_lat + radius * math.sin(angle) + random.uniform(-0.005, 0.005)
            lon = center_lon + radius * math.cos(angle) + random.uniform(-0.005, 0.005)
            
            station = StationConfig(
                station_id=f"station-{i:04d}",
                station_code=f"BJ-5G-{i:04d}",
                name=f"5G基站-{i:04d}",
                longitude=round(lon, 8),
                latitude=round(lat, 8),
                channel_count=channel_count,
                array_rows=array_rows,
                array_cols=array_cols
            )
            
            if self.config.inject_anomalies and random.random() < 0.1:
                num_faulty = random.randint(1, 3)
                for _ in range(num_faulty):
                    station.faulty_channels.add(random.randint(0, channel_count - 1))
                num_anomalous = random.randint(2, 8)
                for _ in range(num_anomalous):
                    ch = random.randint(0, channel_count - 1)
                    if ch not in station.faulty_channels:
                        station.anomalous_channels.add(ch)
            
            self.stations.append(station)
        
        print(f"已初始化 {len(self.stations)} 个基站")
        if self.config.inject_anomalies:
            fault_count = sum(1 for s in self.stations if s.faulty_channels)
            print(f"其中 {fault_count} 个基站模拟了故障通道")
        self._update_metrics()
    
    def _update_metrics(self):
        total_faulty = sum(len(s.faulty_channels) for s in self.stations)
        total_anomalous = sum(len(s.anomalous_channels) for s in self.stations)
        metrics.update_stats(len(self.stations), total_faulty, total_anomalous)
    
    def _init_protocol(self):
        """初始化通信协议"""
        if self.config.protocol == 'mqtt':
            self._init_mqtt()
    
    def _init_mqtt(self):
        """初始化MQTT客户端"""
        try:
            self.mqtt_client = mqtt.Client(
                client_id=f"ecpri-simulator-{int(time.time())}",
                protocol=mqtt.MQTTv5
            )
            self.mqtt_client.on_connect = self._on_mqtt_connect
            self.mqtt_client.on_publish = self._on_mqtt_publish
            
            if self.config.mqtt_username:
                self.mqtt_client.username_pw_set(
                    self.config.mqtt_username, 
                    self.config.mqtt_password
                )
            
            self.mqtt_client.connect(
                self.config.mqtt_host,
                self.config.mqtt_port,
                keepalive=60
            )
            self.mqtt_client.loop_start()
            print(f"MQTT客户端已连接到 {self.config.mqtt_host}:{self.config.mqtt_port}")
        except Exception as e:
            print(f"MQTT连接失败: {e}")
            self.mqtt_client = None
    
    def _on_mqtt_connect(self, client, userdata, flags, rc, properties=None):
        """MQTT连接回调"""
        if rc == 0:
            print("MQTT连接成功")
        else:
            print(f"MQTT连接失败，错误码: {rc}")
    
    def _on_mqtt_publish(self, client, userdata, mid, reason_code, properties):
        """MQTT发布回调"""
        pass
    
    def _start_metrics_server(self):
        """启动Prometheus指标服务器"""
        def run_server():
            try:
                server = HTTPServer(('0.0.0.0', self.config.metrics_port), MetricsHandler)
                print(f"Prometheus指标服务器启动在端口 {self.config.metrics_port}")
                server.serve_forever()
            except Exception as e:
                print(f"指标服务器启动失败: {e}")
        
        thread = threading.Thread(target=run_server, daemon=True)
        thread.start()
    
    def _start_anomaly_injection(self):
        """启动动态异常注入线程"""
        if not self.config.dynamic_anomalies:
            return
        
        def inject_loop():
            while not self.stop_event.is_set():
                try:
                    time.sleep(self.config.anomaly_interval)
                    if self.stop_event.is_set():
                        break
                    
                    # 随机选择基站注入动态异常
                    if random.random() < 0.3:  # 30%概率
                        station = random.choice(self.stations)
                        self._inject_dynamic_anomaly(station)
                        self._update_metrics()
                except Exception as e:
                    print(f"动态异常注入失败: {e}")
        
        self.anomaly_injection_thread = threading.Thread(target=inject_loop, daemon=True)
        self.anomaly_injection_thread.start()
        print(f"动态异常注入已启动，间隔 {self.config.anomaly_interval} 秒")
    
    def _inject_dynamic_anomaly(self, station: StationConfig):
        """向指定基站注入动态异常"""
        anomaly_type = random.choice(['amplitude_bias', 'phase_bias', 'channel_fault', 'channel_anomaly'])
        
        if anomaly_type == 'amplitude_bias':
            station.amplitude_bias = random.uniform(
                self.config.amplitude_bias_min, 
                self.config.amplitude_bias_max
            )
            print(f"  [动态注入] 基站 {station.station_code} 注入幅值偏差: {station.amplitude_bias:.4f}")
        
        elif anomaly_type == 'phase_bias':
            station.phase_bias = random.uniform(
                self.config.phase_bias_min, 
                self.config.phase_bias_max
            )
            print(f"  [动态注入] 基站 {station.station_code} 注入相位偏差: {station.phase_bias:.4f} rad")
        
        elif anomaly_type == 'channel_fault':
            if len(station.faulty_channels) < station.channel_count:
                available = [c for c in range(station.channel_count) if c not in station.faulty_channels]
                if available:
                    ch = random.choice(available)
                    station.faulty_channels.add(ch)
                    print(f"  [动态注入] 基站 {station.station_code} 通道 {ch} 故障")
        
        elif anomaly_type == 'channel_anomaly':
            if len(station.anomalous_channels) < station.channel_count:
                available = [c for c in range(station.channel_count) 
                           if c not in station.faulty_channels and c not in station.anomalous_channels]
                if available:
                    ch = random.choice(available)
                    station.anomalous_channels.add(ch)
                    print(f"  [动态注入] 基站 {station.station_code} 通道 {ch} 异常")
    
    def _generate_channel_data(self, station: StationConfig, 
                                channel_index: int) -> ChannelData:
        """生成单个通道的模拟数据"""
        row = channel_index // station.array_cols
        col = channel_index % station.array_cols
        
        data = ChannelData(channel_index, row, col)
        
        is_faulty = channel_index in station.faulty_channels
        is_anomalous = channel_index in station.anomalous_channels
        
        if is_faulty:
            data.amplitude = random.uniform(0.1, 0.5)
            data.phase = random.uniform(-math.pi, math.pi)
            data.swr = random.uniform(2.5, 5.0)
            data.pa_temperature = random.uniform(75, 95)
            data.tx_power = random.uniform(10, 25)
            data.rx_power = random.uniform(-50, -35)
            data.ber = random.uniform(0.01, 0.1)
        elif is_anomalous:
            data.amplitude = 1.0 + random.uniform(-0.3, 0.3) + station.amplitude_bias
            data.phase = random.uniform(-0.5, 0.5) + station.phase_bias
            data.swr = random.uniform(1.5, 2.0)
            data.pa_temperature = random.uniform(55, 70)
            data.tx_power = random.uniform(35, 43)
            data.rx_power = random.uniform(-55, -45)
            data.ber = random.uniform(0.0001, 0.001)
        else:
            data.amplitude = 1.0 + random.uniform(-0.05, 0.05) + station.amplitude_bias
            data.phase = random.uniform(-0.1, 0.1) + station.phase_bias
            data.swr = random.uniform(1.05, 1.3)
            data.pa_temperature = random.uniform(35, 50)
            data.tx_power = random.uniform(40, 46)
            data.rx_power = random.uniform(-60, -50)
            data.ber = random.uniform(1e-8, 1e-5)
        
        # 确保幅值不为负
        data.amplitude = max(0.01, data.amplitude)
        
        return data
    
    def _build_http_packet(self, station: StationConfig) -> Dict:
        """构建HTTP格式的eCPRI数据包"""
        self.sequence_number += 1
        
        channels = []
        for i in range(station.channel_count):
            ch_data = self._generate_channel_data(station, i)
            channels.append({
                "channelIndex": ch_data.channel_index,
                "rowIndex": ch_data.row_index,
                "columnIndex": ch_data.col_index,
                "amplitude": round(ch_data.amplitude, 6),
                "phase": round(ch_data.phase, 6),
                "swr": round(ch_data.swr, 4),
                "paTemperature": round(ch_data.pa_temperature, 2),
                "txPower": round(ch_data.tx_power, 2),
                "rxPower": round(ch_data.rx_power, 2),
                "ber": ch_data.ber
            })
        
        return {
            "version": "1.0",
            "messageType": "channel_metrics",
            "stationId": station.station_id,
            "stationCode": station.station_code,
            "timestamp": int(time.time() * 1000),
            "sequenceNumber": self.sequence_number,
            "channels": channels
        }
    
    def _build_tcp_packet(self, station: StationConfig) -> bytes:
        """构建TCP二进制格式的eCPRI数据包"""
        self.sequence_number += 1
        
        header = struct.pack(
            '!BBHIQII',
            0x00,
            0x00,
            station.channel_count,
            self.sequence_number,
            int(time.time() * 1000),
            int(station.longitude * 1e7),
            int(station.latitude * 1e7)
        )
        
        payload = b''
        for i in range(station.channel_count):
            ch_data = self._generate_channel_data(station, i)
            ch_bytes = struct.pack(
                '!HHddddddd',
                ch_data.channel_index,
                ch_data.row_index * 100 + ch_data.col_index,
                ch_data.amplitude,
                ch_data.phase,
                ch_data.swr,
                ch_data.pa_temperature,
                ch_data.tx_power,
                ch_data.rx_power,
                ch_data.ber
            )
            payload += ch_bytes
        
        packet = header + payload
        return packet
    
    def _send_http(self, station: StationConfig) -> bool:
        """通过HTTP发送数据"""
        packet = self._build_http_packet(station)
        url = f"{self.config.api_base}/api/ecpri/data"
        
        try:
            json_data = json.dumps(packet).encode('utf-8')
            req = urllib.request.Request(
                url,
                data=json_data,
                headers={'Content-Type': 'application/json'},
                method='POST'
            )
            
            with urllib.request.urlopen(req, timeout=10) as response:
                if response.status == 200:
                    result = json.loads(response.read().decode('utf-8'))
                    return result.get('success', False)
                else:
                    print(f"HTTP错误: {response.status}")
                    return False
        except urllib.error.URLError as e:
            print(f"HTTP发送失败 (基站 {station.station_code}): {e.reason}")
            return False
        except Exception as e:
            print(f"HTTP发送异常 (基站 {station.station_code}): {e}")
            return False
    
    def _send_tcp(self, station: StationConfig) -> bool:
        """通过TCP Socket发送二进制数据"""
        packet = self._build_tcp_packet(station)
        
        try:
            with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
                sock.settimeout(5)
                sock.connect((self.config.tcp_host, self.config.tcp_port))
                sock.sendall(packet)
                return True
        except Exception as e:
            print(f"TCP发送失败 (基站 {station.station_code}): {e}")
            return False
    
    def _send_mqtt(self, station: StationConfig) -> bool:
        """通过MQTT发送数据"""
        if not self.mqtt_client:
            return False
        
        try:
            packet = self._build_http_packet(station)
            topic = f"ecpri/data/{station.station_code}"
            payload = json.dumps(packet)
            
            result = self.mqtt_client.publish(topic, payload, qos=1)
            return result.rc == mqtt.MQTT_ERR_SUCCESS
        except Exception as e:
            print(f"MQTT发送失败 (基站 {station.station_code}): {e}")
            return False
    
    def _send_data(self, station: StationConfig) -> bool:
        """根据配置的协议发送数据"""
        if self.config.protocol == 'http':
            success = self._send_http(station)
        elif self.config.protocol == 'tcp':
            success = self._send_tcp(station)
        elif self.config.protocol == 'mqtt':
            success = self._send_mqtt(station)
        else:
            success = False
        
        if success:
            metrics.inc_sent()
        else:
            metrics.inc_failed()
        
        return success
    
    def _simulation_loop(self):
        """主模拟循环"""
        print(f"\n开始模拟，协议: {self.config.protocol}，间隔: {self.config.interval}秒")
        print(f"基站数: {self.config.station_count}，每基站通道数: {self.config.channel_count}")
        print("按 Ctrl+C 停止模拟\n")
        
        cycle_count = 0
        
        while not self.stop_event.is_set():
            cycle_count += 1
            start_time = time.time()
            
            print(f"\n=== 第 {cycle_count} 轮数据上报开始 ({datetime.now().strftime('%Y-%m-%d %H:%M:%S')}) ===")
            
            success_count = 0
            for i, station in enumerate(self.stations):
                if self.stop_event.is_set():
                    break
                
                if self.config.station_index is not None:
                    if i != self.config.station_index:
                        continue
                
                if self._send_data(station):
                    success_count += 1
                    if self.config.verbose:
                        faulty = len(station.faulty_channels)
                        anomalous = len(station.anomalous_channels)
                        amp_bias = station.amplitude_bias
                        phase_bias = station.phase_bias
                        print(f"  ✓ {station.station_code} 上报成功 "
                              f"(故障: {faulty}, 异常: {anomalous}, "
                              f"幅偏: {amp_bias:+.4f}, 相偏: {phase_bias:+.4f})")
                else:
                    print(f"  ✗ {station.station_code} 上报失败")
                
                if self.config.throttle > 0:
                    time.sleep(self.config.throttle)
            
            elapsed = time.time() - start_time
            print(f"\n本轮完成: {success_count}/{len(self.stations)} 个基站上报成功，耗时: {elapsed:.2f}秒")
            
            if self.config.once:
                print("\n单次模式，模拟结束")
                break
            
            sleep_time = max(0, self.config.interval - elapsed)
            if sleep_time > 0:
                print(f"等待 {sleep_time:.1f} 秒后进行下一轮...")
                for _ in range(int(sleep_time)):
                    if self.stop_event.is_set():
                        break
                    time.sleep(1)
        
        print("\n模拟已停止")
    
    def run(self):
        """运行模拟器"""
        try:
            self._simulation_loop()
        except KeyboardInterrupt:
            print("\n收到停止信号...")
            self.stop_event.set()
        finally:
            if self.mqtt_client:
                self.mqtt_client.loop_stop()
                self.mqtt_client.disconnect()
                print("MQTT客户端已断开")


def main():
    parser = argparse.ArgumentParser(
        description='5G eCPRI 数据模拟器',
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    # 协议配置
    parser.add_argument(
        '--protocol', '-p',
        choices=['http', 'tcp', 'mqtt'],
        default=os.environ.get('SIM_PROTOCOL', 'http'),
        help='数据上报协议 (默认: http, 环境变量: SIM_PROTOCOL)'
    )
    
    parser.add_argument(
        '--api-base',
        default=os.environ.get('SIM_API_BASE', 'http://backend:5000'),
        help='HTTP API基础地址 (默认: http://backend:5000, 环境变量: SIM_API_BASE)'
    )
    
    parser.add_argument(
        '--tcp-host',
        default=os.environ.get('SIM_TCP_HOST', 'backend'),
        help='TCP服务器地址 (默认: backend, 环境变量: SIM_TCP_HOST)'
    )
    
    parser.add_argument(
        '--tcp-port',
        type=int,
        default=env_int('SIM_TCP_PORT', 5001),
        help='TCP服务器端口 (默认: 5001, 环境变量: SIM_TCP_PORT)'
    )
    
    parser.add_argument(
        '--mqtt-host',
        default=os.environ.get('SIM_MQTT_HOST', 'mosquitto'),
        help='MQTT服务器地址 (默认: mosquitto, 环境变量: SIM_MQTT_HOST)'
    )
    
    parser.add_argument(
        '--mqtt-port',
        type=int,
        default=env_int('SIM_MQTT_PORT', 1883),
        help='MQTT服务器端口 (默认: 1883, 环境变量: SIM_MQTT_PORT)'
    )
    
    parser.add_argument(
        '--mqtt-username',
        default=os.environ.get('SIM_MQTT_USERNAME', None),
        help='MQTT用户名 (环境变量: SIM_MQTT_USERNAME)'
    )
    
    parser.add_argument(
        '--mqtt-password',
        default=os.environ.get('SIM_MQTT_PASSWORD', None),
        help='MQTT密码 (环境变量: SIM_MQTT_PASSWORD)'
    )
    
    # 模拟配置
    parser.add_argument(
        '--interval', '-i',
        type=int,
        default=env_int('SIM_INTERVAL', 300),
        help='上报间隔，单位秒 (默认: 300秒=5分钟, 环境变量: SIM_INTERVAL)'
    )
    
    parser.add_argument(
        '--station-count', '-n',
        type=int,
        default=env_int('SIM_STATION_COUNT', 200),
        help='基站数量 (默认: 200, 环境变量: SIM_STATION_COUNT)'
    )
    
    parser.add_argument(
        '--channel-count', '-c',
        type=int,
        default=env_int('SIM_CHANNEL_COUNT', 64),
        help='每基站通道数 (默认: 64, 环境变量: SIM_CHANNEL_COUNT)'
    )
    
    parser.add_argument(
        '--array-rows',
        type=int,
        default=env_int('SIM_ARRAY_ROWS', 8),
        help='天线阵列行数 (默认: 8, 环境变量: SIM_ARRAY_ROWS)'
    )
    
    parser.add_argument(
        '--array-cols',
        type=int,
        default=env_int('SIM_ARRAY_COLS', 8),
        help='天线阵列列数 (默认: 8, 环境变量: SIM_ARRAY_COLS)'
    )
    
    parser.add_argument(
        '--station-index',
        type=int,
        default=None,
        help='只模拟指定索引的基站 (默认: 所有基站)'
    )
    
    # 异常配置
    parser.add_argument(
        '--inject-anomalies',
        action='store_true',
        default=env_bool('SIM_INJECT_ANOMALIES', True),
        help='初始化时注入异常数据 (默认: True, 环境变量: SIM_INJECT_ANOMALIES)'
    )
    
    parser.add_argument(
        '--no-anomalies',
        dest='inject_anomalies',
        action='store_false',
        help='不初始化注入异常数据'
    )
    
    parser.add_argument(
        '--dynamic-anomalies',
        action='store_true',
        default=env_bool('SIM_DYNAMIC_ANOMALIES', True),
        help='运行时动态注入异常 (默认: True, 环境变量: SIM_DYNAMIC_ANOMALIES)'
    )
    
    parser.add_argument(
        '--anomaly-interval',
        type=int,
        default=env_int('SIM_ANOMALY_INTERVAL', 60),
        help='动态异常注入间隔秒数 (默认: 60, 环境变量: SIM_ANOMALY_INTERVAL)'
    )
    
    # 幅相偏差配置
    parser.add_argument(
        '--amplitude-bias-min',
        type=float,
        default=env_float('SIM_AMP_BIAS_MIN', -0.3),
        help='幅值偏差最小值 (默认: -0.3, 环境变量: SIM_AMP_BIAS_MIN)'
    )
    
    parser.add_argument(
        '--amplitude-bias-max',
        type=float,
        default=env_float('SIM_AMP_BIAS_MAX', 0.3),
        help='幅值偏差最大值 (默认: 0.3, 环境变量: SIM_AMP_BIAS_MAX)'
    )
    
    parser.add_argument(
        '--phase-bias-min',
        type=float,
        default=env_float('SIM_PHASE_BIAS_MIN', -0.5),
        help='相位偏差最小值rad (默认: -0.5, 环境变量: SIM_PHASE_BIAS_MIN)'
    )
    
    parser.add_argument(
        '--phase-bias-max',
        type=float,
        default=env_float('SIM_PHASE_BIAS_MAX', 0.5),
        help='相位偏差最大值rad (默认: 0.5, 环境变量: SIM_PHASE_BIAS_MAX)'
    )
    
    # 性能配置
    parser.add_argument(
        '--throttle',
        type=float,
        default=env_float('SIM_THROTTLE', 0.01),
        help='基站间发送延迟秒数 (默认: 0.01, 环境变量: SIM_THROTTLE)'
    )
    
    parser.add_argument(
        '--metrics-port',
        type=int,
        default=env_int('SIM_METRICS_PORT', 8000),
        help='Prometheus指标端口 (默认: 8000, 环境变量: SIM_METRICS_PORT)'
    )
    
    # 模式配置
    parser.add_argument(
        '--once',
        action='store_true',
        default=env_bool('SIM_ONCE', False),
        help='只发送一次数据后退出 (默认: False, 环境变量: SIM_ONCE)'
    )
    
    parser.add_argument(
        '--verbose', '-v',
        action='store_true',
        default=env_bool('SIM_VERBOSE', False),
        help='详细输出模式 (默认: False, 环境变量: SIM_VERBOSE)'
    )
    
    args = parser.parse_args()
    
    # 验证参数
    if args.channel_count != args.array_rows * args.array_cols:
        print(f"警告: 通道数({args.channel_count}) != 行数×列数({args.array_rows}×{args.array_cols})")
    
    print("=" * 60)
    print("5G Massive MIMO eCPRI 数据模拟器")
    print("=" * 60)
    print(f"基站数量: {args.station_count}")
    print(f"每基站通道数: {args.channel_count} ({args.array_rows}x{args.array_cols} 阵列)")
    print(f"上报协议: {args.protocol}")
    print(f"上报间隔: {args.interval} 秒 ({args.interval/60:.1f} 分钟)")
    print(f"初始化注入异常: {args.inject_anomalies}")
    print(f"动态异常注入: {args.dynamic_anomalies}")
    if args.dynamic_anomalies:
        print(f"异常注入间隔: {args.anomaly_interval} 秒")
        print(f"幅值偏差范围: [{args.amplitude_bias_min}, {args.amplitude_bias_max}]")
        print(f"相位偏差范围: [{args.phase_bias_min}, {args.phase_bias_max}] rad")
    print(f"指标端口: {args.metrics_port}")
    print("=" * 60)
    
    simulator = ECPRISimulator(args)
    simulator.run()


if __name__ == '__main__':
    main()
