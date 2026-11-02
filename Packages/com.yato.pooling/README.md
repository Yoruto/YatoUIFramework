# Yato Pooling

ResourceInstancePool.CreateAsync(resources, key, capacity, parent, ct) 持有一份预制体句柄。Prewarm 创建实例；TryRent(out PooledInstance) 达到总容量时返回 false。关闭时销毁全部实例并释放句柄。请将池注册进 SceneContext.Resources，池不自行推断场景归属。

PooledInstance 是带代次的 readonly struct；直接 Dispose 为零分配热路径，避免装箱为 IDisposable。复制或过期句柄无法归还新一轮租用。外部 Destroy 的实例可在容量内重新创建。预热、外部销毁后的补建及用户回调可能分配；IPoolCallbacks 的状态重置由游戏定义。

所有操作在 Unity 主线程执行。建议预制体初始 inactive，避免创建期间先触发一次 OnEnable。池关闭会使所有租用失效；不要在取还回调中递归租用/关闭同一个池。
