using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Travel.Domain.Services;

namespace Travel.Repository.Storage
{
    public class FileStorageService : IFileStorageService
    {
        private const string RootFolder = "Files";

        private readonly string _basePath;

        public FileStorageService(string basePath)
        {
            _basePath = basePath;
        }

        public async Task<string> SaveFileAsync(Stream content, string originalFileName, string folder)
        {
            var folderPath = Path.Combine(_basePath, RootFolder, folder);
            Directory.CreateDirectory(folderPath);

            var fileName = $"{Guid.NewGuid()}{originalFileName}";
            var filePath = Path.Combine(folderPath, fileName);

            await using var fs = new FileStream(filePath, FileMode.Create);
            await content.CopyToAsync(fs);

            return fileName;
        }

        public void DeleteFile(string fileName, string folder)
        {
            var filePath = Path.Combine(_basePath, RootFolder, folder, fileName);
            if (File.Exists(filePath))
                File.Delete(filePath);
        }

        public bool IsImage(string fileName)
        {
            var ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp";
        }
    }
}
