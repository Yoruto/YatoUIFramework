using System.Runtime.CompilerServices;

// 测试需要构造内部句柄，仅对测试程序集开放
[assembly: InternalsVisibleTo("Yato.Tests.EditMode")]
[assembly: InternalsVisibleTo("Yato.Tests.PlayMode")]
