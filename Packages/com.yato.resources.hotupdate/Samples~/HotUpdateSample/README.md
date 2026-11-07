# Resource Hot Update Sample

从 Yato Resources Hot Update 包单独导入此示例，在场景中新建 GameObject 并添加 `HotUpdateSampleEntry`。输入已经发布的 Bundle 根 URL 后手动运行更新；示例不会自动联网。

本地 file URL 或 loopback HTTP 仅在勾选测试选项后允许。正式更新使用 HTTPS。资源更新结束后再创建 Bundle Provider；旧 Provider 继续使用旧快照。

此示例的程序集显式引用 Bundle 和 HotUpdate；不导入它时，基础 FullSample 可以在无这两个包的离线工程中运行。
