#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
5G eCPRI 数据模拟器
模拟200个5G Massive MIMO基站，每个基站64通道天线阵列
每5分钟通过HTTP/TCP/MQTT上报各通道的幅值、相位、驻波比、功放温度等数据
"""

import argparse
import json
import time
import random
import threading
import socket
import struct
from datetime import datetime, timezone
from typing import List, Dict, Tuple
import urllib.request
import urllib.error
import math

import paho.mqtt.client as mqtt


class StationConfig:
    """基站配置"""
    def __init__(self, station_id: str, station_code: str, name: str, 
                 longitude: float, latitude: float):
        self.station_id = station_id
        self.station_code = station_code
        self.name = name
        self.longitude = longitude
        self.latitude = latitude
        self.channel_count = 64
        self.array_rows = 8
        self.array_cols = 8
        self.faulty_channels = set()
        self.anomalous_channels = set()


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
        
        self._init_stations()
        self._init_protocol()
    
    def _init_stations(self):
        """初始化200个基站配置，分布在北京核心城区"""
        print(f"初始化 {self.config.station_count} 个基站...")
        
        center_lat = 39.9042
        center_lon = 116.4074
        
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
                latitude=round(lat, 8)
            )
            
            if self.config.inject_anomalies and random.random() < 0.1:
                num_faulty = random.randint(1, 3)
                for _ in range(num_faulty):
                    station.faulty_channels.add(random.randint(0, 63))
                num_anomalous = random.randint(2, 8)
                for _ in range(num_anomalous):
                    ch = random.randint(0, 63)
                    if ch not in station.faulty_channels:
                        station.anomalous_channels.add(ch)
            
            self.stations.append(station)
        
        print(f"已初始化 {len(self.stations)} 个基站")
        if self.config.inject_anomalies:
            fault_count = sum(1 for s in self.stations if s.faulty_channels)
            print(f"其中 {fault_count} 个基站模拟了故障通道")
    
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
            data.amplitude = 1.0 + random.uniform(-0.3, 0.3)
            data.phase = random.uniform(-0.5, 0.5)
            data.swr = random.uniform(1.5, 2.0)
            data.pa_temperature = random.uniform(55, 70)
            data.tx_power = random.uniform(35, 43)
            data.rx_power = random.uniform(-55, -45)
            data.ber = random.uniform(0.0001, 0.001)
        else:
            data.amplitude = 1.0 + random.uniform(-0.05, 0.05)
            data.phase = random.uniform(-0.1, 0.1)
            data.swr = random.uniform(1.05, 1.3)
            data.pa_temperature = random.uniform(35, 50)
            data.tx_power = random.uniform(40, 46)
            data.rx_power = random.uniform(-60, -50)
            data.ber = random.uniform(1e-8, 1e-5)
        
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
            64,
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
            return self._send_http(station)
        elif self.config.protocol == 'tcp':
            return self._send_tcp(station)
        elif self.config.protocol == 'mqtt':
            return self._send_mqtt(station)
        else:
            return False
    
    def _simulation_loop(self):
        """主模拟循环"""
        print(f"\n开始模拟，协议: {self.config.protocol}，间隔: {self.config.interval}秒")
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
                        print(f"  ✓ {station.station_code} 上报成功 "
                              f"(故障通道: {faulty}, 异常: {anomalous})")
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
    
    parser.add_argument(
        '--protocol', '-p',
        choices=['http', 'tcp', 'mqtt'],
        default='http',
        help='数据上报协议 (默认: http)'
    )
    
    parser.add_argument(
        '--api-base',
        default='http://localhost:5000',
        help='HTTP API基础地址 (默认: http://localhost:5000)'
    )
    
    parser.add_argument(
        '--tcp-host',
        default='localhost',
        help='TCP服务器地址 (默认: localhost)'
    )
    
    parser.add_argument(
        '--tcp-port',
        type=int,
        default=5001,
        help='TCP服务器端口 (默认: 5001)'
    )
    
    parser.add_argument(
        '--mqtt-host',
        default='localhost',
        help='MQTT服务器地址 (默认: localhost)'
    )
    
    parser.add_argument(
        '--mqtt-port',
        type=int,
        default=1883,
        help='MQTT服务器端口 (默认: 1883)'
    )
    
    parser.add_argument(
        '--mqtt-username',
        default=None,
        help='MQTT用户名'
    )
    
    parser.add_argument(
        '--mqtt-password',
        default=None,
        help='MQTT密码'
    )
    
    parser.add_argument(
        '--interval', '-i',
        type=int,
        default=300,
        help='上报间隔，单位秒 (默认: 300秒=5分钟)'
    )
    
    parser.add_argument(
        '--station-count', '-n',
        type=int,
        default=200,
        help='基站数量 (默认: 200)'
    )
    
    parser.add_argument(
        '--station-index',
        type=int,
        default=None,
        help='只模拟指定索引的基站 (默认: 所有基站)'
    )
    
    parser.add_argument(
        '--inject-anomalies',
        action='store_true',
        default=True,
        help='注入异常数据 (默认: True)'
    )
    
    parser.add_argument(
        '--no-anomalies',
        dest='inject_anomalies',
        action='store_false',
        help='不注入异常数据'
    )
    
    parser.add_argument(
        '--throttle',
        type=float,
        default=0.01,
        help='基站间发送延迟，单位秒 (默认: 0.01秒)'
    )
    
    parser.add_argument(
        '--once',
        action='store_true',
        help='只发送一次数据后退出'
    )
    
    parser.add_argument(
        '--verbose', '-v',
        action='store_true',
        help='详细输出模式'
    )
    
    args = parser.parse_args()
    
    print("=" * 60)
    print("5G Massive MIMO eCPRI 数据模拟器")
    print("=" * 60)
    print(f"基站数量: {args.station_count}")
    print(f"每基站通道数: 64 (8x8 阵列)")
    print(f"上报协议: {args.protocol}")
    print(f"上报间隔: {args.interval} 秒")
    print(f"注入异常: {args.inject_anomalies}")
    print("=" * 60)
    
    simulator = ECPRISimulator(args)
    simulator.run()


if __name__ == '__main__':
    main()
