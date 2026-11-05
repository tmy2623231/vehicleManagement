using System;
using System.IO;
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
            return await SaveFileAsync(file.FileName, file.OpenReadStream(), departmentName, licensePlate, personnelName);
        }

        public async Task<string> UploadFileAsync(IBrowserFile file, string departmentName, string licensePlate, string personnelName)
        {
            if (file == null || file.Size == 0)
                throw new ArgumentException("文件不能为空");

            ValidateFile(file.Name, file.Size);
            var maxSize = IsVideoFile(file.Name) ? MaxVideoSize : MaxImageSize;
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

            var fullPath = Path.Combine(_folderRoot, clean.Replace('/', Path.DirectorySeparatorChar));
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
            var relativeFolder = Path.Combine(departmentName ?? "未知部门", licensePlate ?? "未知车牌", personnelName ?? "未知人员", dateFolder);
            var uploadsFolder = Path.Combine(_folderRoot, relativeFolder);

            if (!Directory.Exists(uploadsFolder))
                Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{Guid.NewGuid()}_{Path.GetFileName(originalFileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var fileStream = new FileStream(filePath, FileMode.Create))
            {
                await stream.CopyToAsync(fileStream);
            }

            return $"/Folder/{relativeFolder.Replace(Path.DirectorySeparatorChar, '/')}/{fileName}";
        }
    }
}
