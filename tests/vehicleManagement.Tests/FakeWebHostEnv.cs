using System;
using System.IO;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace vehicleManagement.Tests
{
    /// <summary>IWebHostEnvironment 桩：ContentRootPath 指向临时目录，测完可整体删除。</summary>
    internal sealed class FakeWebHostEnv : IWebHostEnvironment, IDisposable
    {
        public FakeWebHostEnv()
        {
            ContentRootPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(ContentRootPath);
        }

        public string ApplicationName { get; set; } = "vehicleManagement.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string FolderRoot => Path.Combine(ContentRootPath, "Folder");

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(ContentRootPath))
                    Directory.Delete(ContentRootPath, recursive: true);
            }
            catch
            {
                // 临时目录清理失败不影响测试结论
            }
        }
    }
}
