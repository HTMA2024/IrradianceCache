# IrradianceCache

基于八叉树和均匀网格的 Light Probe 存储与 GPU 查询系统，用于 Unity Built-in Render Pipeline。
![Overview](Images/Overview.jpg)
![IrradianceCache](Images/IrradianceCache.jpg)

## 概述

IrradianceCache 提供两种 Light Probe 数据存储模式，替代 Unity 默认的四面体插值方案：

- **Octree 模式**：层次化稀疏存储，BFS 序平铺数组，GPU 端从根到叶逐层遍历
- **Uniform Grid 模式**：单层均匀网格，O(1) 直接索引，无接缝问题

两种模式共享相同的 `OctreeNode` 数据结构和 SH 三线性插值逻辑，在 Shader 层面通过独立的 HLSL 文件实现零运行时分支。

## 特性

- L1 球谐系数（4 系数 × RGB）逐角点存储，GPU 端三线性插值
- Half 精度压缩格式（`OctreeNodeCompact`），内存减半
- 多 Volume 支持：优先级排序 + 边界混合
- 方向加权采样 / SDF 增强采样
- 支持 Volume 旋转（`worldToLocal` + `rotationMatrix`）
- Shader Graph 兼容（通过 Custom Function 节点）
- Editor 可视化工具：节点结构、SH 颜色、采样调试

## 数据结构

### Octree 模式

八叉树节点按 BFS 顺序存储在平铺数组中。每个非叶节点的 `childStartIndex` 指向其 8 个连续子节点的起始位置，叶节点标记为 -1。

```
Root (index 0)
├── childStartIndex → 1
│   ├── [1] child0  ├── [2] child1  ... ├── [8] child7
│   │   └── childStartIndex → 9 (下一层8个子节点)
│   ...
```

查询复杂度：O(maxDepth)，每层一次比较确定子节点象限。

### Uniform Grid 模式

所有节点处于同一层级，按 Row-major 线性排列：

```
linearIndex = iz * (nx * ny) + iy * nx + ix
```

查询复杂度：O(1)，通过归一化坐标直接计算 cell 索引。

## GPU 架构

Octree 和 Grid 使用独立的 HLSL 文件，编译时确定路径，无运行时分支：

```
LightProbeTypes.hlsl          ← 共享：结构体、Buffer 访问、SH 工具函数
├── IrradianceCache.hlsl     ← 八叉树查询 + 采样（单 Volume）
│   └── IrradianceCacheMulti.hlsl
│       └── IrradianceCacheFunctions.hlsl  ← Shader Graph 入口
├── UniformGridLightProbe.hlsl ← 网格查询 + 采样（单 Volume）
│   └── UniformGridLightProbeMulti.hlsl
│       └── UniformGridLightProbeFunctions.hlsl
```

Shader 中切换方式：

```hlsl
// 八叉树
#include "IrradianceCacheFunctions.hlsl"
SampleIrradianceCache_float(...)

// 均匀网格
#include "UniformGridLightProbeFunctions.hlsl"
SampleGridLightProbe_float(...)
```

## 模式对比

| 特性 | Octree | Uniform Grid |
|------|--------|-------------|
| 内存 | 稀疏区域节省 | 固定 nx×ny×nz |
| 查询 | O(depth) 树遍历 | O(1) 直接索引 |
| 接缝 | 需过渡混合 | 无 |
| 适用 | 密度不均匀场景 | 均匀密度 / 性能优先 |

## 项目结构

```
Assets/IrradianceCache/
├── Runtime/          C# 运行时（数据结构、构建、GPU 上传、采样）
├── Editor/           Inspector、Bake 工具、可视化窗口
├── Shaders/          HLSL 查询与插值、Shader Graph 入口、可视化 Shader
├── Materials/        可视化用材质
└── Scenes/           示例场景（Sponza）
```

## 使用方式

1. 场景中添加 `IrradianceCache` 组件，设置边界和参数
2. 在 Inspector 中选择存储模式（Octree / UniformGrid）
3. 点击 Bake 生成 `IrradianceCacheData` 资产
4. 材质中 include 对应的 HLSL 文件，或通过 Shader Graph Custom Function 节点接入

## 环境

- Unity 2022.3+ (Built-in Render Pipeline)
- Shader Model 4.5+
