using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using vehicleManagement.DAL.Common;

namespace vehicleManagement.BLL.Common
{
    public class FileUploadService
    {
        private readonly IWebHostEnvironment _environment;
        private readonly string[] _allowedImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".webp" };
        private readonly string[] _allowedVideoExtensions = { ".mp4", ".avi", ".mov", ".wmv" };
        private const long MaxImageSize = 10 * 1024 * 1024; // 10MB
        private const long MaxVideoSize = 100 * 1024 * 1024; // 100MB
        private readonly string _folderRoot;

        /// <summary>文件头指纹 → 允许的扩展名（空数组 = 拒绝）；无法识别的编码放行，避免误杀。</summary>
        private static readonly Dictionary<string, string[]> MagicMap = new()
        {
            ["jpeg"] = new[] { ".jpg", ".jpeg" },
            ["png"] = new[] { ".png" },
            ["gif"] = new[] { ".gif" },
            ["webp"] = new[] { ".webp" },
            ["mp4"] = new[] { ".mp4", ".mov" },   // ftyp 容器，mov 与 mp4 同族
            ["avi"] = new[] { ".avi" },
            ["wmv"] = new[] { ".wmv" },
            ["exe"] = Array.Empty<string>(),        // 可执行/压缩/文档/脚本一律不允许
            ["zip"] = Array.Empty<string>(),
            ["pdf"] = Array.Empty<string>(),
            ["elf"] = Array.Empty<string>(),
            ["script"] = Array.Empty<string>()
        };

        public FileUploadService(IWebHostEnvironment environment)
        {
            _environment = environment;
            _folderRoot = Path.Combine(_environment.ContentRootPath, "Folder");
            if (!Directory.Exists(_folderRoot))
                Directory.CreateDirectory(_folderRoot);
        }

        public async Task<string> UploadFileAsync(IFormFile file, string departmentName, string licensePlate, string personnelName)
        {
            if (file == null || file.Length == 0)
                throw new ArgumentException("文件不能为空");

            ValidateFile(file.FileName, file.Length);
            // 嗅探与拷贝各开一次流（OpenReadStream 可多次调用），嗅探完即释放
            using (var probe = file.OpenReadStream())
            {
                await ValidateContentAsync(file.FileName, probe);
            }
            return await SaveFileAsync(file.FileName, file.OpenReadStream(), departmentName, licensePlate, personnelName);
        }

        public async Task<string> UploadFileAsync(IBrowserFile file, string departmentName, string licensePlate, string personnelName)
        {
            if (file == null || file.Size == 0)
                throw new ArgumentException("文件不能为空");

            ValidateFile(file.Name, file.Size);
            var maxSize = IsVideoFile(file.Name) ? MaxVideoSize : MaxImageSize;
            using (var probe = file.OpenReadStream(maxSize))
            {
                await ValidateContentAsync(file.Name, probe);
            }
            return await SaveFileAsync(file.Name, file.OpenReadStream(maxSize), departmentName, licensePlate, personnelName);
        }

        public bool DeleteFile(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;

            // 去掉 SaveFileAsync 返回路径中自带的 /Folder/ 前缀，避免与 _folderRoot 拼接重复
            var clean = relativePath.Replace('\\', '/');
            if (clean.StartsWith("/Folder/", StringComparison.OrdinalIgnoreCase))
                clean = clean[8..]; // "/Folder/" = 8 chars
            else if (clean.StartsWith("Folder/", StringComparison.OrdinalIgnoreCase))
                clean = clean[7..]; // "Folder/" = 7 chars

            var fullPath = Path.GetFullPath(Path.Combine(_folderRoot, clean.Replace('/', Path.DirectorySeparatorChar)));

            // 前缀终校验：库里的相对路径是历史数据，不能假定它没被改过
            if (!fullPath.StartsWith(_folderRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
                return true;
            }
            return false;
        }

        private void ValidateFile(string fileName, long fileSize)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var isImage = Array.IndexOf(_allowedImageExtensions, extension) != -1;
            var isVideo = Array.IndexOf(_allowedVideoExtensions, extension) != -1;

            if (!isImage && !isVideo)
                throw new ArgumentException("不支持的文件格式");

            var maxSize = isVideo ? MaxVideoSize : MaxImageSize;
            var typeName = isVideo ? "视频" : "图片";
            var sizeLimit = isVideo ? "100MB" : "10MB";

            if (fileSize > maxSize)
                throw new ArgumentException($"{typeName}文件大小不能超过{sizeLimit}");
        }

        private bool IsVideoFile(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return Array.IndexOf(_allowedVideoExtensions, extension) != -1;
        }

        private async Task<string> SaveFileAsync(string originalFileName, Stream stream, string departmentName, string licensePlate, string personnelName)
        {
            var dateFolder = ChinaTime.Now.ToString("yyyy-MM-dd");

            // 四段目录都来自用户可编辑主数据，必须逐段净化防 ".." 越界写盘
            var relativeFolder = Path.Combine(
                Safe(departmentName), Safe(licensePlate), Safe(personnelName), Safe(dateFolder));
            var uploadsFolder = Path.Combine(_folderRoot, relativeFolder);

            // 落盘前的最终事实：解析后的绝对路径必须还在 _folderRoot 下
            var fullFolder = Path.GetFullPath(uploadsFolder);
            if (!fullFolder.StartsWith(_folderRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("非法文件路径");

            if (!Directory.Exists(fullFolder))
                Directory.CreateDirectory(fullFolder);

            // 扩展名单独保留，避免净化阶段把后缀截掉（否则文件变成无后缀）
            var safeBaseName = Safe(Path.GetFileNameWithoutExtension(originalFileName));
            var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
            var fileName = $"{Guid.NewGuid():N}_{safeBaseName}{extension}";
            var filePath = Path.Combine(fullFolder, fileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            return $"/Folder/{relativeFolder.Replace(Path.DirectorySeparatorChar, '/')}/{fileName}";
        }

        /// <summary>目录段/文件名净化：非法字符换下划线，禁掉上跳，限长 40。</summary>
        private static string Safe(string s)
        {
            s = (s ?? "").Trim();
            foreach (var c in Path.GetInvalidFileNameChars())
                s = s.Replace(c, '_');
            s = s.Replace("..", "_").Trim('.', ' ');
            if (string.IsNullOrWhiteSpace(s)) s = "未知";
            return s.Length > 40 ? s[..40] : s;
        }

        /// <summary>内容嗅探：读前 12 字节识别真实类型，与扩展名不符则拒（防止改名落盘）；空内容放行。</summary>
        private static async Task ValidateContentAsync(string fileName, Stream stream)
        {
            var buffer = new byte[12];
            var read = await stream.ReadAsync(buffer, 0, buffer.Length);

            var header = new byte[read];
            Array.Copy(buffer, header, Math.Min(read, buffer.Length));

            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            var kind = DetectKind(header);

            // 识别不出来就放行（冷门编码不能因为嗅探而误杀业务文件）
            if (kind == null) return;

            if (MagicMap.TryGetValue(kind, out var allowed) && Array.IndexOf(allowed, extension) >= 0)
                return;

            throw new ArgumentException("文件内容与扩展名不符");
        }

        /// <summary>根据文件头指纹识别真实类型，识别不出返回 null。</summary>
        private static string DetectKind(byte[] h)
        {
            if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpeg";
            if (h.Length >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return "png";
            if (h.Length >= 4 && h[0] == 0x47 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x38) return "gif";

            if (h.Length >= 12 && h[0] == 0x52 && h[1] == 0x49 && h[2] == 0x46 && h[3] == 0x46)
            {
                var riffType = Encoding.ASCII.GetString(h, 8, 4);
                if (riffType == "WEBP") return "webp";
                if (riffType == "AVI ") return "avi";
                return null;
            }

            if (h.Length >= 8 && Encoding.ASCII.GetString(h, 4, 4) == "ftyp") return "mp4";   // mp4 / mov
            if (h.Length >= 4 && h[0] == 0x30 && h[1] == 0x26 && h[2] == 0xB2 && h[3] == 0x75) return "wmv";  // ASF
            if (h.Length >= 2 && h[0] == 0x4D && h[1] == 0x5A) return "exe";                   // MZ
            if (h.Length >= 2 && h[0] == 0x50 && h[1] == 0x4B) return "zip";                   // PK
            if (h.Length >= 4 && h[0] == 0x25 && h[1] == 0x50 && h[2] == 0x44 && h[3] == 0x46) return "pdf";  // %PDF
            if (h.Length >= 4 && h[0] == 0x7F && h[1] == 0x45 && h[2] == 0x4C && h[3] == 0x46) return "elf";  // \x7FELF
            if (h.Length >= 2 && h[0] == 0x23 && h[1] == 0x21) return "script";                 // #!

            return null;
        }
    }
}
