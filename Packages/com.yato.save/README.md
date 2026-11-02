# Yato Save

独立可选包，依赖 Core/Persistence，不加入聚合包默认依赖。new SaveService<T>(store, dataVersion, validate, migrator) 接受游戏 DTO、校验器及可选迁移器。默认 JSON 记录有 formatVersion、dataVersion、savedAtUtc、payload；payload 是 DTO 的 JSON 文本。

LoadAsync 不创建文件、不自动恢复或自动写回迁移结果。SaveAsync 先快照校验数据，再将有效旧主存档提交到备份，最后提交新主存档。主存档损坏时禁止直接覆盖，游戏必须明确恢复或删除。RecoverBackupAsync 是显式回退，备份保留。

根目录默认 saves；槽位 ID 为 1–64 个 ASCII 字母/数字/_/-，目录使用 slot- 前缀。相同根及槽位跨服务互斥，冲突立即 FileBusyException；不同槽位可并发。DeleteAsync 是显式删除两个文件，不承诺跨文件原子性。取消在最终提交前有效，提交成功后返回实际结果。

迁移器接收旧版本原始 payload，返回当前 DTO。未知新版本返回 Incompatible；迁移失败报告 Corrupt 并保留文件。默认 JsonUtility 不支持的类型可替换 ISerializer。所有异步方法应被 await；验证和序列化在调用者上下文执行。

Samples 中的 CounterSample 仅演示计数器槽位、保存、加载、恢复与删除，没有游戏玩法。
