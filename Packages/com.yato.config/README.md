# Yato Config

为游戏定义 ConfigRecord 和 ConfigTable<T> 子类，并将表资产放入 ConfigCatalog。ConfigService.LoadAsync(resources, key) 加载目录并持有句柄；Get<T>/TryGet<T> 按精确、区分大小写的 ID 查询。

运行时行是源资产的 JSON 副本，不会回写 ScriptableObject。运行时将配置视为只读，角色或 UI 变化值使用独立数据。行的 Validate 方法可 Report 错误或 Require<T>(id) 校验引用。

目录 Validate、Yato/Config/Validate Catalogs 菜单、Player 构建前和 Bundle 构建前复用相同校验；自定义校验应无副作用。Dispose 释放目录句柄。默认序列化使用 JsonUtility，请采用可序列化字段。
