using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using vehicleManagement.BLL.Common;
using Xunit;

namespace vehicleManagement.Tests
{
    /// <summary>P1-4 回归：目录/文件名四段全部来自可编辑主数据，必须逐段净化，落盘不得越出 Folder 根。</summary>
    public class FileUploadServiceTests
    {
        /// <summary>最小 IFormFile 桩：只提供 FileUploadService 实际用到的成员。</summary>
        private sealed class StubFormFile : IFormFile
        {
            private readonly byte[] _content;

            public StubFormFile(string fileName, byte[] content)
            {
                FileName = fileName;
                _content = content;
            }

            public string ContentType => "application/octet-stream";
            public string ContentDisposition => "";
            public IHeaderDictionary Headers => new HeaderDictionary();
            public long Length => _content.Length;
            public string Name => "file";
            public string FileName { get; }

            public void CopyTo(Stream target) => target.Write(_content, 0, _content.Length);

            public Task CopyToAsync(Stream target, CancellationToken cancellationToken = default)
                => target.WriteAsync(_content, 0, _content.Length, cancellationToken);

            public Stream OpenReadStream() => new MemoryStream(_content);
        }

        [Fact]
        public async Task UploadFileAsync_TraversalInSegments_StaysUnderFolderRoot()
        {
            using var env = new FakeWebHostEnv();
            var service = new FileUploadService(env);

            // 无明显文件头的任意内容：嗅探识别不出，按「不误杀」放行到落盘逻辑
            var content = System.Text.Encoding.UTF8.GetBytes("not a real jpeg, no magic header");
            var file = new StubFormFile("..\\..\\evil.jpg", content);

            var relative = await service.UploadFileAsync(
                file,
                departmentName: "..\\..\\wwwroot",     // 越界尝试 1
                licensePlate: "../../../x:y|z",        // 越界尝试 2 + 非法字符
                personnelName: "..");                  // 越界尝试 3

            Assert.StartsWith("/Folder/", relative);

            var relativeOnDisk = relative.Substring("/Folder/".Length)
                .Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(env.FolderRoot, relativeOnDisk));

            // 1) 落盘位置仍在 Folder 根之下
            Assert.StartsWith(env.FolderRoot + Path.DirectorySeparatorChar, fullPath);
            Assert.True(File.Exists(fullPath));

            // 2) Folder 根之外没有新增任何条目（内容根下只应有 Folder 一个）
            var entries = Directory.GetFileSystemEntries(env.ContentRootPath);
            Assert.Single(entries);
            Assert.Equal(env.FolderRoot, entries[0]);
        }

        [Fact]
        public async Task UploadFileAsync_IllegalExtension_Throws()
        {
            using var env = new FakeWebHostEnv();
            var service = new FileUploadService(env);
            var file = new StubFormFile("payload.txt", new byte[] { 1, 2, 3 });

            await Assert.ThrowsAsync<ArgumentException>(() =>
                service.UploadFileAsync(file, "部门", "京A1", "张三"));
        }
    }
}
