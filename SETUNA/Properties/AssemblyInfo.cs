using System;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("SETUNA")]
[assembly: AssemblyProduct("SETUNA")]
[assembly: AssemblyCopyright("Copyright (C) 2008 CLEARUP")]
[assembly: AssemblyTrademark("")]
[assembly: ComVisible(false)]
[assembly: Guid("4483e561-8b3e-427d-98a4-e0e821b7bf2f")]
[assembly: AssemblyInformationalVersion("3.0")]
[assembly: AssemblyDescription("")]
[assembly: AssemblyCompany("clearup")]
[assembly: NeutralResourcesLanguage("zh-CN")]
[assembly: AssemblyVersion("3.1.0")]

// 让测试项目能验证 internal 的内部机制（如 AutoStartup 的注册表值格式），
// 不必为了可测性把它们提升为 public。两个程序集都未签名，无需公钥。
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SETUNATests")]
