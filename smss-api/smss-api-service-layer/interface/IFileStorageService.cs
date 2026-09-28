using System;
using System.Collections.Generic;
using System.Text;

namespace smss_api_service_layer.@interface
{
    public interface IFileStorageService
    {
        // Saves a school logo and returns its relative public URL
        Task<string> SaveSchoolLogoAsync(string schoolCode, Stream content, string extension, CancellationToken ct);

        // Best-effort delete by the URL returned from Save. Never throws.
        void Delete(string relativeUrl);
    }
}
