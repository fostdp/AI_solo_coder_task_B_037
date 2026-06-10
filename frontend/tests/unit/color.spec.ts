import { describe, it, expect } from 'vitest'
import {
  amplitudeDeviationToColor,
  phaseDeviationToColor,
  swrToColor,
  temperatureToColor,
  failureProbabilityToColor,
  getHeatmapColor,
  getStatusColor,
  getAlarmLevelColor,
  rgba,
  getDeviationColor,
  getAmplitudePhaseColor,
  lerpColor
} from '@/utils/color'

const GREEN = '#67C23A'
const YELLOW = '#E6A23C'
const RED = '#F56C6C'

describe('color.ts 工具函数测试', () => {
  describe('lerpColor - 颜色插值', () => {
    it('t=0 返回第一个颜色', () => {
      expect(lerpColor('#000000', '#FFFFFF', 0)).toBe('#000000')
    })

    it('t=1 返回第二个颜色', () => {
      expect(lerpColor('#000000', '#FFFFFF', 1)).toBe('#ffffff')
    })

    it('t=0.5 返回中间色', () => {
      expect(lerpColor('#000000', '#FFFFFF', 0.5)).toBe('#808080')
    })

    it('正确计算RGB分量', () => {
      const result = lerpColor('#FF0000', '#00FF00', 0.5)
      expect(result).toBe('#808000')
    })
  })

  describe('amplitudeDeviationToColor - 幅度偏差颜色', () => {
    it('偏差0-3返回绿色', () => {
      expect(amplitudeDeviationToColor(0)).toBe(GREEN)
      expect(amplitudeDeviationToColor(2)).toBe(GREEN)
      expect(amplitudeDeviationToColor(3)).toBe(GREEN)
      expect(amplitudeDeviationToColor(-2)).toBe(GREEN)
    })

    it('偏差3-5返回绿色到黄色插值', () => {
      const midColor = amplitudeDeviationToColor(4)
      expect(midColor).not.toBe(GREEN)
      expect(midColor).not.toBe(YELLOW)
    })

    it('偏差5-10返回黄色到红色插值', () => {
      const midColor = amplitudeDeviationToColor(7)
      expect(midColor).not.toBe(YELLOW)
      expect(midColor).not.toBe(RED)
    })

    it('偏差>=10返回红色', () => {
      expect(amplitudeDeviationToColor(10)).toBe(RED)
      expect(amplitudeDeviationToColor(15)).toBe(RED)
    })
  })

  describe('phaseDeviationToColor - 相位偏差颜色', () => {
    it('偏差0-10返回绿色', () => {
      expect(phaseDeviationToColor(0)).toBe(GREEN)
      expect(phaseDeviationToColor(5)).toBe(GREEN)
      expect(phaseDeviationToColor(10)).toBe(GREEN)
    })

    it('偏差10-30返回绿色到黄色插值', () => {
      const midColor = phaseDeviationToColor(20)
      expect(midColor).not.toBe(GREEN)
      expect(midColor).not.toBe(YELLOW)
    })

    it('偏差30-60返回黄色到红色插值', () => {
      const midColor = phaseDeviationToColor(45)
      expect(midColor).not.toBe(YELLOW)
      expect(midColor).not.toBe(RED)
    })

    it('偏差>=60返回红色', () => {
      expect(phaseDeviationToColor(60)).toBe(RED)
      expect(phaseDeviationToColor(90)).toBe(RED)
    })
  })

  describe('swrToColor - 驻波比颜色', () => {
    it('SWR<=1.5返回绿色', () => {
      expect(swrToColor(1.0)).toBe(GREEN)
      expect(swrToColor(1.2)).toBe(GREEN)
      expect(swrToColor(1.5)).toBe(GREEN)
    })

    it('SWR 1.5-2.0返回绿色到黄色插值', () => {
      const midColor = swrToColor(1.75)
      expect(midColor).not.toBe(GREEN)
      expect(midColor).not.toBe(YELLOW)
    })

    it('SWR 2.0-3.0返回黄色到红色插值', () => {
      const midColor = swrToColor(2.5)
      expect(midColor).not.toBe(YELLOW)
      expect(midColor).not.toBe(RED)
    })

    it('SWR>=3.0返回红色', () => {
      expect(swrToColor(3.0)).toBe(RED)
      expect(swrToColor(4.0)).toBe(RED)
    })
  })

  describe('temperatureToColor - 温度颜色', () => {
    it('温度<=50返回绿色', () => {
      expect(temperatureToColor(25)).toBe(GREEN)
      expect(temperatureToColor(40)).toBe(GREEN)
      expect(temperatureToColor(50)).toBe(GREEN)
    })

    it('温度50-70返回绿色到黄色插值', () => {
      const midColor = temperatureToColor(60)
      expect(midColor).not.toBe(GREEN)
      expect(midColor).not.toBe(YELLOW)
    })

    it('温度70-90返回黄色到红色插值', () => {
      const midColor = temperatureToColor(80)
      expect(midColor).not.toBe(YELLOW)
      expect(midColor).not.toBe(RED)
    })

    it('温度>=90返回红色', () => {
      expect(temperatureToColor(90)).toBe(RED)
      expect(temperatureToColor(100)).toBe(RED)
    })
  })

  describe('failureProbabilityToColor - 故障概率颜色', () => {
    it('概率<0.5返回绿色', () => {
      expect(failureProbabilityToColor(0)).toBe(GREEN)
      expect(failureProbabilityToColor(0.3)).toBe(GREEN)
      expect(failureProbabilityToColor(0.49)).toBe(GREEN)
    })

    it('概率0.5-0.7返回绿色到黄色插值', () => {
      const midColor = failureProbabilityToColor(0.6)
      expect(midColor).not.toBe(GREEN)
      expect(midColor).not.toBe(YELLOW)
    })

    it('概率0.7-1.0返回黄色到红色插值', () => {
      const midColor = failureProbabilityToColor(0.85)
      expect(midColor).not.toBe(YELLOW)
      expect(midColor).not.toBe(RED)
    })

    it('概率>=1.0返回红色', () => {
      expect(failureProbabilityToColor(1.0)).toBe(RED)
    })
  })

  describe('getHeatmapColor - 热力图颜色', () => {
    it('min==max返回灰色', () => {
      expect(getHeatmapColor(50, 50, 50)).toBe('#E0E0E0')
    })

    it('最小值返回绿色', () => {
      const color = getHeatmapColor(0, 0, 100)
      expect(color).toContain('hsl(120')
    })

    it('最大值返回红色', () => {
      const color = getHeatmapColor(100, 0, 100)
      expect(color).toContain('hsl(0')
    })

    it('中间值返回黄色', () => {
      const color = getHeatmapColor(50, 0, 100)
      expect(color).toContain('hsl(60')
    })

    it('超出范围的值被限制', () => {
      const lowColor = getHeatmapColor(-10, 0, 100)
      const highColor = getHeatmapColor(110, 0, 100)
      expect(lowColor).toContain('hsl(120')
      expect(highColor).toContain('hsl(0')
    })
  })

  describe('getStatusColor - 状态颜色', () => {
    it('正常状态返回绿色', () => {
      expect(getStatusColor('normal')).toBe(GREEN)
      expect(getStatusColor('active')).toBe(GREEN)
      expect(getStatusColor('success')).toBe(GREEN)
    })

    it('警告状态返回黄色', () => {
      expect(getStatusColor('warning')).toBe(YELLOW)
      expect(getStatusColor('pending')).toBe(YELLOW)
    })

    it('错误状态返回红色', () => {
      expect(getStatusColor('fault')).toBe(RED)
      expect(getStatusColor('error')).toBe(RED)
      expect(getStatusColor('failed')).toBe(RED)
    })

    it('未知状态返回灰色', () => {
      expect(getStatusColor('unknown')).toBe('#909399')
      expect(getStatusColor('')).toBe('#909399')
      expect(getStatusColor(null as any)).toBe('#909399')
    })

    it('不区分大小写', () => {
      expect(getStatusColor('NORMAL')).toBe(GREEN)
      expect(getStatusColor('Warning')).toBe(YELLOW)
    })
  })

  describe('getAlarmLevelColor - 告警等级颜色', () => {
    it('严重告警返回红色', () => {
      expect(getAlarmLevelColor('critical')).toBe(RED)
    })

    it('警告告警返回黄色', () => {
      expect(getAlarmLevelColor('warning')).toBe(YELLOW)
    })

    it('信息告警返回蓝色', () => {
      expect(getAlarmLevelColor('info')).toBe('#409EFF')
    })

    it('未知等级返回灰色', () => {
      expect(getAlarmLevelColor('unknown')).toBe('#909399')
      expect(getAlarmLevelColor('')).toBe('#909399')
    })
  })

  describe('rgba - 十六进制转RGBA', () => {
    it('正确转换红色', () => {
      expect(rgba('#FF0000', 1)).toBe('rgba(255, 0, 0, 1)')
    })

    it('正确转换绿色', () => {
      expect(rgba('#00FF00', 0.5)).toBe('rgba(0, 255, 0, 0.5)')
    })

    it('正确转换蓝色', () => {
      expect(rgba('#0000FF', 0)).toBe('rgba(0, 0, 255, 0)')
    })

    it('正确转换白色', () => {
      expect(rgba('#FFFFFF', 1)).toBe('rgba(255, 255, 255, 1)')
    })

    it('正确转换黑色', () => {
      expect(rgba('#000000', 0.75)).toBe('rgba(0, 0, 0, 0.75)')
    })
  })

  describe('getDeviationColor - 通用偏差颜色', () => {
    it('低偏差返回绿色区间', () => {
      const color = getDeviationColor(1, 10)
      expect(color).not.toBe(RED)
    })

    it('中偏差返回黄色区间', () => {
      const color = getDeviationColor(5, 10)
      expect(color).not.toBe(GREEN)
      expect(color).not.toBe(RED)
    })

    it('高偏差返回红色', () => {
      expect(getDeviationColor(10, 10)).toBe(RED)
      expect(getDeviationColor(15, 10)).toBe(RED)
    })
  })

  describe('getAmplitudePhaseColor - 幅相结合颜色', () => {
    it('都正常返回绿色区间', () => {
      const color = getAmplitudePhaseColor(1, 5)
      expect(color).not.toBe(RED)
    })

    it('一个警告返回黄色区间', () => {
      const color = getAmplitudePhaseColor(4, 5)
      expect(color).not.toBe(GREEN)
      expect(color).not.toBe(RED)
    })

    it('都严重返回红色区间', () => {
      const color = getAmplitudePhaseColor(5, 50)
      expect(color).not.toBe(GREEN)
    })

    it('取两者中的较大值', () => {
      const color1 = getAmplitudePhaseColor(1, 50)
      const color2 = getAmplitudePhaseColor(5, 5)
      expect(color1).toBe(RED)
      expect(color2).not.toBe(RED)
    })
  })

  describe('边界条件测试', () => {
    it('负值正确处理（取绝对值）', () => {
      expect(amplitudeDeviationToColor(-5)).toBe(amplitudeDeviationToColor(5))
      expect(phaseDeviationToColor(-10)).toBe(phaseDeviationToColor(10))
    })

    it('零值处理', () => {
      expect(amplitudeDeviationToColor(0)).toBe(GREEN)
      expect(swrToColor(0)).toBe(GREEN)
      expect(temperatureToColor(0)).toBe(GREEN)
      expect(failureProbabilityToColor(0)).toBe(GREEN)
    })

    it('极值处理', () => {
      expect(amplitudeDeviationToColor(1000)).toBe(RED)
      expect(temperatureToColor(1000)).toBe(RED)
      expect(failureProbabilityToColor(1000)).toBe(RED)
    })
  })
})
