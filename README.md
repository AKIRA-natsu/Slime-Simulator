# Slime Simulator · v9 — Unity 6 / HDRP 透明体积渲染

**制作署名：GPT-6 astar（项目所有者指定署名）。** 项目代码实现、迭代优化、测试及演示录像由 Codex AI 助手完成；需求与验收反馈由项目所有者提供。参考作品与第三方素材的作者见文末。

Unity 6.3 LTS（6000.3.27f1）/ HDRP 17.3.0 / Windows / DirectX 11。项目位于 `D:\Unity\Project\Slime-Simulator`。

Unity Hub 打开该目录，等待依赖解析，打开 `Assets/Slime/Scenes/Laboratory.unity` 并 Play。也可双击成品目录中的 `启动史莱姆.cmd`。

## 实机演示录像

下方为本项目 Unity 6 / HDRP 原生程序生成的固定步长验收回放：1440 × 900、18 秒、15 fps，用于观察材质、穿栏杆、分裂、跟随与融合效果。

<video controls preload="metadata" width="960" poster="docs/images/slime-hdrp.png">
  <source src="Slime-Simulator-motion.mp4" type="video/mp4">
  当前阅读器不支持内嵌视频，请使用下方播放链接。
</video>

[播放或下载演示录像](Slime-Simulator-motion.mp4) · [查看录像封面](docs/images/slime-hdrp.png)

| 时间 | 演示内容 |
| --- | --- |
| 00:00–00:04 | TPS 肩后视角、竖栏杆挤压与自动断团 |
| 00:04–00:06 | 连续六次分裂，形成七个独立个体 |
| 00:06–00:09 | 小史莱姆独立移动与侧翼跟随 |
| 00:09–00:18 | 停步集合、接触融合与稳定静置 |

录像保存在项目根目录 `Slime-Simulator-motion.mp4`，封面保存在 `docs/images/slime-hdrp.png`。复制或分享文档时保留这两个相对位置；不支持 HTML 视频的 Markdown 阅读器可使用上面的播放链接。

## 操作

- 点击画面捕获鼠标；移动鼠标直接环视，右键拉近瞄准，X 切换左右肩，滚轮调距离。Esc 释放鼠标，Tab 打开设置。
- WASD 按镜头方向移动；Shift 加速；空格跳跃。碰到可攀爬墙面或开口时自动识别攀附、挤压，不需要 E/C。
- Q 可连续分裂，每次细分最大的可分个体。G 召回；停步也会自动集合，真实接触后融合。
- 1 静置与提拉、2 实体墙、3 爬墙、4 窄门、5 高窗、6 街区、7 横栏杆、8 竖栏杆自动断团。
- V 切换固定观察模式，观察模式下左键可提拉史莱姆；F 轻触，R 重置。

## 本版实现

TPS 使用官方 Cinemachine 2.10.7 的 3rd Person Follow。肩后偏移、较宽视野、直接鼠标朝向；平移目标过滤软体起伏。相机不再自动 LookAt 质心，也不对玩家转向施加旧的 75°/s 限速。`SlimeTpsObstacles` 扫掠相机实际运动路径，在狭窄处收拢肩位；没有随机噪声、自动转向或分裂自动缩放。

分裂取消了一次操作和两只跟随者的配额。每个个体都有独立求解器、运动控制、环境感知和体积场。每 0.1 秒检查实际液体连通性；障碍隔断的部分自动成为独立个体，不保留跨个体拉力。连通的液体仍可整体挤过栏杆。分身沿已走路径追赶，通路允许时展开侧翼队形；集合时只有接触且无实体阻隔才合并。

**分裂保持总质量，不会无限复制液体。** 当前使用 512 个离散粒子，最终仍有单粒子的分辨率下限；没有额外的分身数量和冷却限制。回归覆盖连续 32 团细分，原生性能测试包含 16 团同时活动。

街区可走范围约 98 × 62 米，有交叉街道、8 栋可攀爬建筑、屋顶、台阶、两类栏杆和门窗练习区。主体建筑、花池、路灯与地图边界共用显示和液体碰撞数据。高空求解范围也已扩展，屋顶起跳不会被原有 7 米上限压扁。

透明体积着色器已接入 HDRP 的相机深度、折射颜色金字塔和曝光体系。厚处的绿松石色随实际光路长度增加，薄层透出路面；表面采用菲涅耳反射、屏幕空间追踪与局部环境立方体回退，保留内部气泡和眼睛。表面密度核随粒子相对流速轻微拉长，整团平移和静置不会触发假波纹。地面湿痕、透射接触阴影在折射采样前绘制。

摄影纯天空 HDRI 经上半球照度积分归一化；路面和砖墙使用 CC0 扫描颜色、法线与粗糙度，转换成 HDRP Mask Map。后处理为固定曝光、ACES、轻量 Bloom、SMAA、环境遮蔽与接触阴影；关闭运动模糊、景深和胶片颗粒。

静态几何合并、小个体体积场自适应分辨率继续保留。行进中环境立方体每帧更新一个面，六面完成后整体交换；开场和切换场景仍会完整刷新。已清理旧 URP 管线、后处理资产及被 HDRP/Lit 替代的旧地面、砖墙着色器。

本机 RTX 3050 OEM、1440 × 900：标准静态视角 40–54 batches；六面反射集中刷新的压力用例为 239，实际行进按面分帧刷新。同步帧成本平均：静置 7.71 ms、行走 18.88 ms、奔跑 19.07 ms、16 个体 18.84 ms（含 GPU 同步，非实时 FPS）。HDRP 的成本高于上一版 URP，具体数据见报告。

## 验证与复跑

- Unity 菜单 `Slime Lab → Run regression checks`：54 项物理、输入和自动识别检查；`Run colony checks`：17 项分裂、隔墙、真实栏杆断团、跟随融合、32 团质量守恒、扩大地图与屋顶跳跃检查。
- `Camera-validation.json`：原生 Cinemachine/PhysX 轨迹，包含走停、门窗、爬墙、分裂、自动断团、跳跃、快速转向、瞄准与换肩。验证镜头不进入实体和主角，非输入事件不改变瞄准方向。
- `ThirdPerson-validation.json`：540 帧原生回放；`Unity-visual-validation.json`：12 个实际捕获时刻及静置像素比较；GPU 密度检查与粒子连续碰撞检查分别记录。
- `Batch-validation.json` / `Performance-validation.json`：渲染计数与同步帧成本，后者包含 GPU 回读，**不能当作实时 FPS**。新版街区与旧场景工作量不同。
- [Slime-Simulator-motion.mp4](Slime-Simulator-motion.mp4)：18 秒、15 fps 的固定步长回放，展示竖栏杆、连续分裂、侧翼跟随与融合，不能用来衡量实时帧率。

命令行：`-slimeCamera <绝对目录>`、`-slimeColony <绝对目录>`、`-slimeCapture <绝对目录>`、`-slimeBatches <绝对路径/results.json>`、`-slimeBenchmark <绝对路径/results.json>`。这些模式结束后自动退出。

## 版本管理

项目根目录的 `.gitignore` 忽略 Unity 缓存、临时文件、构建目录、自动截图、IDE 本地配置及生成的解决方案文件。`Assets` 与 `.meta`、`Packages`、`ProjectSettings`、README、验收报告和本页演示录像保留在版本管理范围内。

## 当前边界

街景仍是程序化简化几何，透明效果使用屏幕空间折射与环境立方体，尚不能等同参考视频的资产与流体细节。没有完整淋浴入流、液膜破裂、泡沫或真实光线追踪焦散。次级飞溅属于视觉层。跟随依赖路径记忆和局部感知，不是任意复杂迷宫的全局寻路。新可交互模型需配置轴对齐 SolidBox 液体碰撞数据。

键鼠入口与自动回放共用控制代码；本次原生验收不代替用户手动评价 TPS 手感。

## 代码与参考

`SlimeSimulation / SlimeMotor / SlimePerception`：物理与感知；`SlimeColony`：断团、分裂、跟随、融合；`SlimeFollowCamera / SlimeTpsObstacles`：TPS；`Arena / SlimeStreet`：街区；`SlimeSurface / SlimeVolume.compute / SlimeGlass.shader`：体积与透明渲染；`SlimeRendering / SlimeContactPass / SlimeHDRPProject`：HDRP 渲染、资源生成与构建。

[参考视频](https://www.bilibili.com/video/BV16w411h7XQ/) · [原作者 Asher Zhu](https://vuth.artstation.com/) · [Cinemachine 官方第三人称说明](https://docs.unity.cn/Packages/com.unity.cinemachine@2.9/manual/Cinemachine3rdPersonFollow.html)。项目不包含原作者游戏资产或视频。

外部资源均为 CC0：[Kloofendal 48d Partly Cloudy Pure Sky](https://polyhaven.com/a/kloofendal_48d_partly_cloudy_puresky)（Greg Zaal、Jarod Guest）、[Asphalt 01](https://polyhaven.com/a/asphalt_01)（Charlotte Baglioni、Dario Barresi）、[Brick Wall 001](https://polyhaven.com/a/brick_wall_001)（Dimitrios Savva、Rob Tuytel），[Poly Haven 许可证](https://polyhaven.com/license)。
