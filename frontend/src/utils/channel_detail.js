import {
  Chart as ChartJS,
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
} from 'chart.js'
import dayjs from 'dayjs'
import { rgba } from '@/utils/color'

ChartJS.register(
  CategoryScale,
  LinearScale,
  PointElement,
  LineElement,
  Title,
  Tooltip,
  Legend,
  Filler
)

export function createChannelDetailManager(options = {}) {
  const {
    mockDataGenerator,
    amplitudeRange = { min: 0.8, max: 1.2 },
    swrRange = { min: 0.8, max: 2.5 },
    swrThreshold = 1.8,
    amplitudeUpper = 1.1,
    amplitudeLower = 0.9
  } = options

  let amplitudeTrendData = []
  let swrTrendData = []

  const statusTextMap = {
    normal: '正常运行',
    warning: '状态警告',
    fault: '故障告警'
  }

  const getStatusText = (status) => {
    return statusTextMap[status] || status
  }

  const loadTrendData = async (channelId, hours = 24) => {
    if (mockDataGenerator) {
      amplitudeTrendData = mockDataGenerator(hours)
      swrTrendData = mockDataGenerator(hours)
      return { amplitude: amplitudeTrendData, swr: swrTrendData }
    }
    return { amplitude: [], swr: [] }
  }

  const createBaseChartOptions = (yMin, yMax, yLabel) => ({
    responsive: true,
    maintainAspectRatio: false,
    interaction: {
      mode: 'index',
      intersect: false
    },
    plugins: {
      legend: {
        position: 'top',
        labels: {
          usePointStyle: true,
          padding: 12,
          font: {
            size: 11
          }
        }
      },
      tooltip: {
        backgroundColor: 'rgba(0, 0, 0, 0.8)',
        padding: 10,
        titleFont: {
          size: 12
        },
        bodyFont: {
          size: 11
        }
      }
    },
    scales: {
      x: {
        grid: {
          display: false
        },
        ticks: {
          maxTicksLimit: 8,
          font: {
            size: 10
          }
        }
      },
      y: {
        min: yMin,
        max: yMax,
        grid: {
          color: 'rgba(0, 0, 0, 0.05)'
        },
        ticks: {
          font: {
            size: 10
          }
        }
      }
    }
  })

  const getAmplitudeChartData = () => {
    const labels = amplitudeTrendData.map(d => dayjs(d.timestamp).format('HH:mm'))
    const data = amplitudeTrendData.map(d => d.amplitude)

    return {
      labels,
      datasets: [
        {
          label: '幅值 (dB)',
          data,
          borderColor: '#409eff',
          backgroundColor: rgba('#409eff', 0.1),
          borderWidth: 2,
          pointRadius: 3,
          pointHoverRadius: 5,
          tension: 0.4,
          fill: false
        },
        {
          label: '正常上限',
          data: data.map(() => amplitudeUpper),
          borderColor: '#faad14',
          borderWidth: 1,
          borderDash: [5, 5],
          pointRadius: 0,
          fill: false
        },
        {
          label: '正常下限',
          data: data.map(() => amplitudeLower),
          borderColor: '#faad14',
          borderWidth: 1,
          borderDash: [5, 5],
          pointRadius: 0,
          fill: false
        }
      ]
    }
  }

  const getSwrChartData = () => {
    const labels = swrTrendData.map(d => dayjs(d.timestamp).format('HH:mm'))
    const data = swrTrendData.map(d => d.swr)

    return {
      labels,
      datasets: [
        {
          label: '驻波比',
          data,
          borderColor: '#67c23a',
          backgroundColor: rgba('#67c23a', 0.2),
          borderWidth: 2,
          pointRadius: 3,
          pointHoverRadius: 5,
          tension: 0.4,
          fill: true
        },
        {
          label: '告警阈值',
          data: data.map(() => swrThreshold),
          borderColor: '#ff4d4f',
          borderWidth: 1,
          borderDash: [5, 5],
          pointRadius: 0,
          fill: false
        }
      ]
    }
  }

  const amplitudeChartOptions = createBaseChartOptions(
    amplitudeRange.min,
    amplitudeRange.max,
    '幅值 (dB)'
  )

  const swrChartOptions = createBaseChartOptions(
    swrRange.min,
    swrRange.max,
    '驻波比'
  )

  const calculateChannelMetrics = (channel, trendData) => {
    if (!trendData || trendData.length === 0) {
      return {
        avgAmplitude: 0,
        maxAmplitude: 0,
        minAmplitude: 0,
        avgSwr: 0,
        maxSwr: 0,
        amplitudeDeviation: 0,
        swrDeviation: 0
      }
    }

    const amplitudes = trendData.map(d => d.amplitude)
    const swrs = trendData.map(d => d.swr)

    const avgAmplitude = amplitudes.reduce((a, b) => a + b, 0) / amplitudes.length
    const maxAmplitude = Math.max(...amplitudes)
    const minAmplitude = Math.min(...amplitudes)
    const avgSwr = swrs.reduce((a, b) => a + b, 0) / swrs.length
    const maxSwr = Math.max(...swrs)

    const amplitudeDeviation = Math.max(
      Math.abs(maxAmplitude - 1.0),
      Math.abs(minAmplitude - 1.0)
    ) * 100

    const swrDeviation = maxSwr > 1.5 ? (maxSwr - 1.5) * 100 : 0

    return {
      avgAmplitude,
      maxAmplitude,
      minAmplitude,
      avgSwr,
      maxSwr,
      amplitudeDeviation,
      swrDeviation
    }
  }

  const getRecommendations = (channel, metrics) => {
    const recommendations = []

    if (metrics.maxSwr >= swrThreshold) {
      recommendations.push({
        type: 'critical',
        text: `驻波比已达 ${metrics.maxSwr.toFixed(2)}，超过告警阈值 ${swrThreshold}，建议立即检修馈线连接`,
        action: '立即检修'
      })
    } else if (metrics.maxSwr >= 1.5) {
      recommendations.push({
        type: 'warning',
        text: `驻波比偏高 (${metrics.maxSwr.toFixed(2)})，建议检查天线端口连接状态`,
        action: '计划维护'
      })
    }

    if (metrics.amplitudeDeviation > 10) {
      recommendations.push({
        type: 'warning',
        text: `幅值偏差 ${metrics.amplitudeDeviation.toFixed(1)}%，建议执行幅相校准`,
        action: '触发校准'
      })
    }

    if (channel.failureProbability > 0.7) {
      recommendations.push({
        type: 'critical',
        text: `通道故障预测概率 ${(channel.failureProbability * 100).toFixed(1)}%，建议尽快更换功率放大器`,
        action: '备件更换'
      })
    } else if (channel.failureProbability > 0.4) {
      recommendations.push({
        type: 'warning',
        text: `通道故障预测概率 ${(channel.failureProbability * 100).toFixed(1)}%，建议密切关注`,
        action: '持续监控'
      })
    }

    if (recommendations.length === 0) {
      recommendations.push({
        type: 'success',
        text: '通道运行状态良好，各项指标正常',
        action: '继续监控'
      })
    }

    return recommendations
  }

  const setTrendData = (amplitudeData, swrData) => {
    amplitudeTrendData = amplitudeData || []
    swrTrendData = swrData || []
  }

  const getTrendData = () => ({
    amplitude: amplitudeTrendData,
    swr: swrTrendData
  })

  return {
    loadTrendData,
    setTrendData,
    getTrendData,
    getAmplitudeChartData,
    getSwrChartData,
    amplitudeChartOptions,
    swrChartOptions,
    calculateChannelMetrics,
    getRecommendations,
    getStatusText
  }
}

export { ChartJS }
