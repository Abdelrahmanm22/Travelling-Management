using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Travel.Domain.Services
{
    public interface IFileStorageService
    {
        /// <summary>
        /// Saves <paramref name="content"/> under <paramref name="folder"/> with a GUID-prefixed
        /// name derived from <paramref name="originalFileName"/>; returns the stored file name.
        /// </summary>
        Task<string> SaveFileAsync(Stream content, string originalFileName, string folder);

        /// <summary>Deletes a stored file; no-op if it doesn't exist.</summary>
        void DeleteFile(string fileName, string folder);

        /// <summary>True when the file name has a raster-image extension (image vs generic file).</summary>
        bool IsImage(string fileName);
    }
}
