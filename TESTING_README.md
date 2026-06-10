# 测试套件使用说明

## 项目结构

```
├── backend/
│   └── tests/Backend.Tests/
│       ├── Backend.Tests.csproj          # 测试项目配置
│       ├── TestBase.cs                   # 测试基类
│       ├── UnitTests/
│       │   ├── DeformationMonitorTests.cs         # 阵面形变测试 (~45个用例)
│       │   ├── CoSiteInterferenceAnalyzerTests.cs # 共址干扰测试 (~35个用例)
│       │   ├── PaEfficiencyEvaluatorTests.cs      # 功放效率测试 (~40个用例)
│       │   └── SpectrumScannerTests.cs            # 频谱扫描测试 (~35个用例)
│       └── IntegrationTests/
│           └── FeatureModulesIntegrationTests.cs  # 集成测试 (~10个用例)
└── frontend/
    ├── vitest.config.ts                 # Vitest配置
    ├── package.json                     # 测试脚本配置
    └── tests/
        ├── setup.ts                     # 测试环境配置
        └── unit/
            ├── DeformationMonitor.spec.ts  # 形变组件测试 (~40个用例)
            ├── InterferenceAnalyzer.spec.ts # 共址干扰测试 (~35个用例)
            ├── PaEfficiencyPanel.spec.ts    # 功放效率测试 (~40个用例)
            ├── SpectrumScanner.spec.ts      # 频谱扫描测试 (~40个用例)
            └── color.spec.ts                # 颜色工具测试 (~60个用例)
```

## 后端测试 (.NET 8.0 + xUnit + Moq + FluentAssertions)

### 运行命令

```bash
# 构建测试项目
cd backend/tests/Backend.Tests
dotnet build

# 运行所有测试
dotnet test

# 运行特定类别的测试
dotnet test --filter "FullyQualifiedName~DeformationMonitor"
dotnet test --filter "FullyQualifiedName~SpectrumScanner"
dotnet test --filter "FullyQualifiedName~IntegrationTests"

# 查看详细测试输出
dotnet test --logger "console;verbosity=detailed"

# 生成测试报告
dotnet test --logger "trx;LogFileName=test-results.trx"
```

### 测试覆盖范围

| 模块 | 测试场景 | 用例数 | 覆盖范围 |
|------|---------|--------|----------|
| 阵面形变 | FEM计算精度、波束修正、告警触发、异常处理、性能 | ~45 | 正常/边界/异常 |
| 共址干扰 | 互耦模型、S参数偏差、调整建议、矢量可视化 | ~35 | 正常/边界/异常 |
| 功放效率 | 公式验证、衰减预测、更换建议、温度降额 | ~40 | 正常/边界/异常 |
| 频谱扫描 | 识别准确率、虚警率、零陷调整、渲染性能 | ~35 | 正常/边界/异常 |
| 集成测试 | 模块联动、数据流、事件流、并发、错误传播 | ~10 | 端到端流程 |

## 前端测试 (Vitest + Vue Test Utils + Happy DOM)

### 运行命令

```bash
# 安装测试依赖
cd frontend
npm install

# 运行所有测试
npm run test:unit

# 监听模式运行
npm run test:watch

# 生成覆盖率报告
npm run test:coverage

# 运行特定测试文件
npx vitest run tests/unit/SpectrumScanner.spec.ts

# 查看详细输出
npx vitest run --reporter=verbose
```

### 测试覆盖范围

| 组件/工具 | 测试场景 | 用例数 |
|-----------|---------|--------|
| DeformationMonitor | 计算属性、图标逻辑、阈值判断、图表数据、区域划分 | ~40 |
| InterferenceAnalyzer | 隔离度计算、耦合系数、等级判断、建议生成、矢量计算 | ~35 |
| PaEfficiencyPanel | 效率公式、温度降额、衰减预测、更换建议、趋势分析 | ~40 |
| SpectrumScanner | 频率点生成、噪声计算、峰值检测、DOA估计、零陷算法 | ~40 |
| color.ts | 所有颜色转换函数、插值计算、边界处理 | ~60 |

## 测试用例设计原则

### 1. 三层覆盖策略

- **正常场景**：标称参数下的正确性验证
- **边界场景**：阈值边界、极值输入的鲁棒性验证
- **异常场景**：错误输入、依赖失败的容错性验证

### 2. AAA模式 (Arrange-Act-Assert)

每个测试用例遵循：
```csharp
// Arrange - 准备测试数据和模拟对象
var sensors = CreateTestSensors(16, 2.5, 2.5, 2.5, 850, 25);

// Act - 执行待测方法
var result = await _monitor.RunDeformationAnalysisAsync(request, ct);

// Assert - 验证结果
result.MaxDisplacementMm.Should().BeGreaterThan(2.0);
result.OverallSeverity.Should().Be(SeverityLevel.Warning);
```

### 3. 依赖模拟

使用Moq模拟所有外部依赖：
- Repository层：数据访问
- Mediator：事件发布
- Logger：日志记录
- IOptions：配置选项

## 关键算法验证点

### 阵面形变
- ✅ FEM有限元板弯曲理论公式
- ✅ 温度补偿校正
- ✅ 相位梯度一致性
- ✅ 9区域划分逻辑

### 共址干扰
- ✅ 自由空间损耗公式
- ✅ 频率重叠耦合因子
- ✅ 干扰矢量归一化
- ✅ 方位角/仰角计算

### 功放效率
- ✅ 漏极效率公式
- ✅ 功率附加效率(PAE)
- ✅ 线性回归斜率计算
- ✅ 温度降额系数

### 频谱扫描
- ✅ 热噪声计算公式
- ✅ 峰值检测算法
- ✅ DOA方位角估计
- ✅ 零陷相位修正

## 性能指标要求

| 测试项 | 性能要求 |
|--------|---------|
| 形变监测(100传感器) | < 2秒 |
| 共址干扰(50天线) | < 2秒 |
| 功放效率(64通道) | < 5秒 |
| 频谱扫描(2000点) | < 2秒 |
| 高分辨率扫描(20000点) | < 5秒 |
| 全流程(4模块) | < 10秒 |

## 新增/修改的源文件

为支持测试，修复了以下源文件问题：

1. **FeatureDTOs.cs** - 新增 `ChannelMetric` 记录定义
2. **FeatureDTOs.cs** - 更新 `PaEfficiencyResult`，添加缺失字段：
   - PaTemperature, OutputPowerDbm, InputPowerDbm
   - DcCurrentA, DcVoltageV, DcPowerW, RfPowerW
3. **FeatureDTOs.cs** - 更新 `SpectrumScanResult`，添加缺失字段：
   - InterferenceDetails, SpuriousFreeDynamicRangeDb

## 注意事项

1. 运行测试前确保已安装：
   - .NET SDK 8.0+
   - Node.js 18+
   - npm 9+

2. 后端测试首次运行会自动还原NuGet包
3. 前端测试首次运行需要执行 `npm install`
4. 部分测试使用随机数，可能存在偶发失败，可重复运行验证
5. 所有单元测试均为隔离测试，不依赖外部服务
